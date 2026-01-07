using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EmailCommunication.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services;

/// <summary>
/// TCP client for communicating with email service
/// </summary>
public interface IEmailTcpClient
{
    Task<bool> SendEmailAsync(EmailPayload payload, string? tenantId = null);
    Task<bool> ConnectAsync();
    void Disconnect();
    bool IsConnected { get; }
}

public class EmailTcpClient : IEmailTcpClient, IDisposable
{
    private readonly ILogger<EmailTcpClient> _logger;
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly int _connectionTimeout;
    private readonly int _receiveTimeout;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    public bool IsConnected => _tcpClient?.Connected ?? false;

    /// <summary>
    /// NestJS TCP uses a simple text-based protocol: [LENGTH]#[JSON]
    /// This is NOT a binary length prefix - it's a decimal string followed by '#'
    /// Example: "123#{"pattern":"...","data":{...}}"
    /// </summary>
    private const char LENGTH_DELIMITER = '#';

    public EmailTcpClient(IConfiguration configuration, ILogger<EmailTcpClient> logger)
    {
        _logger = logger;
        _host = configuration["Tcp:Host"] ?? "localhost";
        _port = int.Parse(configuration["Tcp:Port"] ?? "4003");
        _connectionTimeout = int.Parse(configuration["Tcp:ConnectionTimeout"] ?? "5000");
        _receiveTimeout = int.Parse(configuration["Tcp:ReceiveTimeout"] ?? "30000");
    }

    /// <summary>
    /// Connect to the TCP email service
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            if (IsConnected)
            {
                _logger.LogInformation("Already connected to TCP server");
                return true;
            }

            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = _receiveTimeout;
            _tcpClient.SendTimeout = _connectionTimeout;

            _logger.LogInformation("Connecting to TCP server at {Host}:{Port}", _host, _port);

            var connectTask = _tcpClient.ConnectAsync(_host, _port);
            var timeoutTask = Task.Delay(_connectionTimeout);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("Connection timeout after {Timeout}ms", _connectionTimeout);
                _tcpClient?.Dispose();
                _tcpClient = null;
                return false;
            }

            if (_tcpClient.Connected)
            {
                _stream = _tcpClient.GetStream();
                _logger.LogInformation("Successfully connected to TCP server");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to TCP server at {Host}:{Port}", _host, _port);
            _tcpClient?.Dispose();
            _tcpClient = null;
            return false;
        }
    }

    /// <summary>
    /// Send email via TCP connection (creates fresh connection for each message)
    /// This method ensures clean state for each message and avoids connection reuse issues
    /// </summary>
    public async Task<bool> SendEmailAsync(EmailPayload payload, string? tenantId = null)
    {
        // Create a fresh connection for each message to avoid state issues
        // This is more reliable than reusing connections
        TcpClient? tempClient = null;
        NetworkStream? tempStream = null;

        try
        {
            // Create new connection
            tempClient = new TcpClient();
            tempClient.ReceiveTimeout = _receiveTimeout;
            tempClient.SendTimeout = _connectionTimeout;

            _logger.LogInformation("Creating new TCP connection to {Host}:{Port}", _host, _port);

            var connectTask = tempClient.ConnectAsync(_host, _port);
            var timeoutTask = Task.Delay(_connectionTimeout);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("Connection timeout after {Timeout}ms", _connectionTimeout);
                return false;
            }

            if (!tempClient.Connected)
            {
                _logger.LogError("Failed to connect to TCP server");
                return false;
            }

            tempStream = tempClient.GetStream();
            _logger.LogDebug("Successfully connected to TCP server");

            // Use the temporary stream for this message
            return await SendEmailToStreamAsync(tempStream, payload, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via TCP");
            return false;
        }
        finally
        {
            // Always close the temporary connection
            try
            {
                tempStream?.Close();
                tempClient?.Close();
            }
            catch { }
            finally
            {
                tempStream?.Dispose();
                tempClient?.Dispose();
            }
        }
    }

    /// <summary>
    /// Send email to a specific stream (internal method)
    /// </summary>
    private async Task<bool> SendEmailToStreamAsync(
      NetworkStream stream,
      EmailPayload payload,
      string? tenantId
    )
    {
        // Use semaphore to ensure only one send operation at a time
        await _sendLock.WaitAsync();
        try
        {
            if (stream == null)
            {
                throw new InvalidOperationException("Stream is null");
            }

            // IMPORTANT: NestJS expects the payload directly in the "data" field,
            // not wrapped in an envelope. The controller receives SendEmailRequest directly.
            // Map your EmailPayload to match what SendEmailRequest expects
            var nestjsData = new
            {
                to = payload.To,
                subject = payload.Subject,
                text = payload.Text,
                html = payload.Html,
                from = payload.From,
                fromName = payload.FromName,
                requestId = Guid.NewGuid().ToString(),
                // Add any other fields that SendEmailRequest expects
            };

            // NestJS TCP microservices require a specific message format:
            // { "pattern": "email.send_email", "data": { /* payload */ }, "id": "unique-id" }
            var nestjsMessage = new
            {
                pattern = "email.send_email",
                data = nestjsData,
                id = Guid.NewGuid().ToString(),
            };

            // Serialize to JSON
            var json = JsonSerializer.Serialize(
              nestjsMessage,
              new JsonSerializerOptions
              {
                  PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                  WriteIndented = false,
              }
            );

            // NestJS TCP protocol: [LENGTH]#[JSON]
            // The length is the byte length of the JSON string, sent as decimal text followed by '#'
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var messageLength = jsonBytes.Length;

            // Validate message length (NestJS has max buffer size: ~512MB)
            if (messageLength > 10 * 1024 * 1024) // 10MB max
            {
                throw new InvalidOperationException($"Message too large: {messageLength} bytes");
            }

            // Build the complete message: "123#{"pattern":"...","data":{...}}"
            var lengthPrefix = $"{messageLength}{LENGTH_DELIMITER}";
            var lengthPrefixBytes = Encoding.UTF8.GetBytes(lengthPrefix);

            // Combine length prefix and JSON into a single buffer
            var completeMessage = new byte[lengthPrefixBytes.Length + jsonBytes.Length];
            Buffer.BlockCopy(lengthPrefixBytes, 0, completeMessage, 0, lengthPrefixBytes.Length);
            Buffer.BlockCopy(jsonBytes, 0, completeMessage, lengthPrefixBytes.Length, jsonBytes.Length);

            // Log detailed information for debugging
            _logger.LogDebug(
              "Sending email to TCP server. JSON Length: {Length} bytes",
              messageLength
            );
            _logger.LogDebug("Length prefix: {Prefix}", lengthPrefix);
            _logger.LogDebug("Complete message size: {Size} bytes", completeMessage.Length);
            _logger.LogDebug("Email JSON: {EmailJson}", json);

            // Send everything in one write operation
            await stream.WriteAsync(completeMessage, 0, completeMessage.Length);
            await stream.FlushAsync();

            _logger.LogInformation(
              "Email sent to TCP server successfully. Message length: {Length} bytes",
              messageLength
            );

            // Read response from NestJS
            // NestJS sends response in same format: [LENGTH]#[JSON]
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                // Read until we find the '#' delimiter
                var lengthBuffer = new List<byte>();
                var readBuffer = new byte[1];

                while (true)
                {
                    var bytesRead = await stream.ReadAsync(readBuffer, 0, 1, cts.Token);
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Server closed connection without sending response");
                        return false;
                    }

                    var currentByte = readBuffer[0];
                    if ((char)currentByte == LENGTH_DELIMITER)
                    {
                        // Found delimiter, parse the length
                        break;
                    }

                    lengthBuffer.Add(currentByte);

                    // Prevent infinite loop with reasonable limit (length string should be < 20 chars)
                    if (lengthBuffer.Count > 20)
                    {
                        _logger.LogError("Response length prefix too long");
                        return false;
                    }
                }

                // Parse the length from the text we read
                var lengthString = Encoding.UTF8.GetString(lengthBuffer.ToArray());
                if (!int.TryParse(lengthString, out var responseLength))
                {
                    _logger.LogError("Invalid response length: {Length}", lengthString);
                    return false;
                }

                if (responseLength == 0)
                {
                    _logger.LogWarning("Server sent empty response");
                    return false;
                }

                // Validate response length (prevent buffer overflow)
                if (responseLength > 10 * 1024 * 1024) // 10MB max
                {
                    _logger.LogError("Response length too large: {Length} bytes", responseLength);
                    return false;
                }

                // Read the response payload
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
                        _logger.LogWarning("Server closed connection while reading response");
                        return false;
                    }
                    totalBytesRead += bytesRead;
                }

                var responseJson = Encoding.UTF8.GetString(responseBuffer, 0, responseLength);
                _logger.LogInformation("Server response: {Response}", responseJson);

                // Parse response to check for success
                try
                {
                    using var doc = JsonDocument.Parse(responseJson);
                    if (doc.RootElement.TryGetProperty("success", out var successElement))
                    {
                        var success = successElement.GetBoolean();
                        if (success)
                        {
                            _logger.LogInformation("Email sent successfully");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning("Server returned success=false: {Response}", responseJson);
                            return false;
                        }
                    }
                    // If no success field, assume success if we got a response
                    return true;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(
                      ex,
                      "Failed to parse server response as JSON: {Response}",
                      responseJson
                    );
                    // Still return true if we got a response, as the message was sent
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Timeout waiting for server response");
                return false;
            }
            catch (IOException ex) when (ex.Message.Contains("closed") || ex.Message.Contains("reset"))
            {
                _logger.LogError("Connection closed by server: {Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading server response");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via TCP");
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Disconnect from TCP server
    /// </summary>
    public void Disconnect()
    {
        try
        {
            _stream?.Close();
            _tcpClient?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during disconnect");
        }
        finally
        {
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _stream = null;
            _tcpClient = null;
            _logger.LogInformation("Disconnected from TCP server");
        }
    }

    public void Dispose()
    {
        _sendLock?.Dispose();
        Disconnect();
    }
}
