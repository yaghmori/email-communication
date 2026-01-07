using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Microservices;

/// <summary>
/// Generic NestJS TCP microservice client base class
/// Implements the NestJS TCP protocol: [LENGTH]#[JSON]
/// </summary>
/// <typeparam name="TRequest">The request payload type</typeparam>
/// <typeparam name="TResponse">The response type</typeparam>
public abstract class NestJsTcpClient<TRequest, TResponse> : IDisposable
{
    protected readonly ILogger Logger;
    private readonly string _host;
    private readonly int _port;
    private readonly int _connectionTimeout;
    private readonly int _receiveTimeout;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private const char LENGTH_DELIMITER = '#';

    protected NestJsTcpClient(
        IConfiguration configuration,
        ILogger logger,
        string configSection
    )
    {
        Logger = logger;
        _host = configuration[$"{configSection}:Host"] ?? "localhost";
        _port = int.Parse(configuration[$"{configSection}:Port"] ?? "4003");
        _connectionTimeout = int.Parse(configuration[$"{configSection}:ConnectionTimeout"] ?? "5000");
        _receiveTimeout = int.Parse(configuration[$"{configSection}:ReceiveTimeout"] ?? "30000");
    }

    /// <summary>
    /// Get the NestJS message pattern for this service
    /// Example: "email.send_email", "storage.upload_file"
    /// </summary>
    protected abstract string GetMessagePattern();

    /// <summary>
    /// Transform the request payload before sending (optional override)
    /// </summary>
    protected virtual object TransformRequest(TRequest request) => request!;

    /// <summary>
    /// Parse the response from the server (optional override)
    /// </summary>
    protected virtual TResponse ParseResponse(string json)
    {
        return JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })!;
    }

    /// <summary>
    /// Send a request to the NestJS TCP microservice
    /// </summary>
    public async Task<TResponse?> SendAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        TcpClient? client = null;
        NetworkStream? stream = null;

        try
        {
            // Create new TCP connection
            client = new TcpClient
            {
                ReceiveTimeout = _receiveTimeout,
                SendTimeout = _connectionTimeout
            };

            Logger.LogDebug("Connecting to {Service} TCP server at {Host}:{Port}",
                GetMessagePattern(), _host, _port);

            var connectTask = client.ConnectAsync(_host, _port);
            var timeoutTask = Task.Delay(_connectionTimeout, cancellationToken);
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Logger.LogError("Connection timeout after {Timeout}ms", _connectionTimeout);
                return default;
            }

            if (!client.Connected)
            {
                Logger.LogError("Failed to connect to TCP server");
                return default;
            }

            stream = client.GetStream();
            Logger.LogDebug("Successfully connected to TCP server");

            // Send request and get response
            return await SendRequestAsync(stream, request, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send request via TCP");
            return default;
        }
        finally
        {
            try
            {
                stream?.Close();
                client?.Close();
            }
            catch { }
            finally
            {
                stream?.Dispose();
                client?.Dispose();
            }
        }
    }

    private async Task<TResponse?> SendRequestAsync(
        NetworkStream stream,
        TRequest request,
        CancellationToken cancellationToken
    )
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (stream == null)
            {
                throw new InvalidOperationException("Stream is null");
            }

            // Transform request data
            var transformedData = TransformRequest(request);

            // Build NestJS message format
            var nestjsMessage = new
            {
                pattern = GetMessagePattern(),
                data = transformedData,
                id = Guid.NewGuid().ToString()
            };

            // Serialize to JSON
            var json = JsonSerializer.Serialize(
                nestjsMessage,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                }
            );

            // Build message with NestJS TCP protocol: [LENGTH]#[JSON]
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var messageLength = jsonBytes.Length;

            if (messageLength > 10 * 1024 * 1024) // 10MB max
            {
                throw new InvalidOperationException($"Message too large: {messageLength} bytes");
            }

            var lengthPrefix = $"{messageLength}{LENGTH_DELIMITER}";
            var lengthPrefixBytes = Encoding.UTF8.GetBytes(lengthPrefix);

            // Combine length prefix and JSON
            var completeMessage = new byte[lengthPrefixBytes.Length + jsonBytes.Length];
            Buffer.BlockCopy(lengthPrefixBytes, 0, completeMessage, 0, lengthPrefixBytes.Length);
            Buffer.BlockCopy(jsonBytes, 0, completeMessage, lengthPrefixBytes.Length, jsonBytes.Length);

            Logger.LogDebug("Sending request. Pattern: {Pattern}, Length: {Length} bytes",
                GetMessagePattern(), messageLength);

            // Send message
            await stream.WriteAsync(completeMessage, 0, completeMessage.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            Logger.LogInformation("Request sent successfully. Pattern: {Pattern}, Length: {Length} bytes",
                GetMessagePattern(), messageLength);

            // Read response
            return await ReadResponseAsync(stream, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<TResponse?> ReadResponseAsync(
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            // Read length prefix until '#' delimiter
            var lengthBuffer = new List<byte>();
            var readBuffer = new byte[1];

            while (true)
            {
                var bytesRead = await stream.ReadAsync(readBuffer, 0, 1, cts.Token);
                if (bytesRead == 0)
                {
                    Logger.LogWarning("Server closed connection without sending response");
                    return default;
                }

                var currentByte = readBuffer[0];
                if ((char)currentByte == LENGTH_DELIMITER)
                {
                    break;
                }

                lengthBuffer.Add(currentByte);

                if (lengthBuffer.Count > 20)
                {
                    Logger.LogError("Response length prefix too long");
                    return default;
                }
            }

            // Parse length
            var lengthString = Encoding.UTF8.GetString(lengthBuffer.ToArray());
            if (!int.TryParse(lengthString, out var responseLength))
            {
                Logger.LogError("Invalid response length: {Length}", lengthString);
                return default;
            }

            if (responseLength == 0)
            {
                Logger.LogWarning("Server sent empty response");
                return default;
            }

            if (responseLength > 10 * 1024 * 1024)
            {
                Logger.LogError("Response length too large: {Length} bytes", responseLength);
                return default;
            }

            // Read response payload
            var responseBuffer = new byte[responseLength];
            var totalBytesRead = 0;
            while (totalBytesRead < responseLength)
            {
                var bytesRead = await stream.ReadAsync(
                    responseBuffer,
                    totalBytesRead,
                    responseLength - totalBytesRead,
                    cts.Token
                );
                if (bytesRead == 0)
                {
                    Logger.LogWarning("Server closed connection while reading response");
                    return default;
                }
                totalBytesRead += bytesRead;
            }

            var responseJson = Encoding.UTF8.GetString(responseBuffer, 0, responseLength);
            Logger.LogInformation("Received response: {Response}", responseJson);

            // Parse response
            return ParseResponse(responseJson);
        }
        catch (OperationCanceledException)
        {
            Logger.LogError("Timeout waiting for server response");
            return default;
        }
        catch (IOException ex) when (ex.Message.Contains("closed") || ex.Message.Contains("reset"))
        {
            Logger.LogError("Connection closed by server: {Message}", ex.Message);
            return default;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reading server response");
            return default;
        }
    }

    public void Dispose()
    {
        _sendLock?.Dispose();
        GC.SuppressFinalize(this);
    }
}
