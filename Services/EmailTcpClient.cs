using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
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

    public bool IsConnected => _tcpClient?.Connected ?? false;

    public EmailTcpClient(IConfiguration configuration, ILogger<EmailTcpClient> logger)
    {
        _logger = logger;
        _host = configuration["Tcp:Host"] ?? "localhost";
        _port = int.Parse(configuration["Tcp:Port"] ?? "5000");
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
    /// Send email via TCP connection
    /// </summary>
    public async Task<bool> SendEmailAsync(EmailPayload payload, string? tenantId = null)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Not connected to TCP server. Attempting to connect...");
            var connected = await ConnectAsync();
            if (!connected)
            {
                _logger.LogError("Failed to connect to TCP server");
                return false;
            }
        }

        try
        {
            // Create email event envelope
            var envelope = new EmailEventEnvelope { Payload = payload, TenantId = tenantId };

            // Serialize to JSON
            var json = JsonSerializer.Serialize(
                envelope,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false,
                }
            );

            // Prepare message: length prefix + JSON data
            // NestJS microservices expects a 4-byte unsigned integer (uint32) in network byte order (big-endian)
            var messageLength = (uint)Encoding.UTF8.GetByteCount(json);
            var lengthBytes = BitConverter.GetBytes(messageLength);

            // Convert to network byte order (big-endian)
            // BitConverter produces little-endian on Windows, so we need to reverse for network byte order
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }

            _logger.LogDebug("Sending email to TCP server: {EmailJson}", json);

            // Send length prefix
            if (_stream == null)
            {
                throw new InvalidOperationException("Stream is null");
            }

            await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);

            // Send JSON data
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            await _stream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
            await _stream.FlushAsync();

            _logger.LogInformation(
                "Email sent to TCP server successfully. Message length: {Length} bytes",
                messageLength
            );

            // Wait for response (optional acknowledgment)
            // NestJS sends responses with a 4-byte length prefix (big-endian) followed by JSON
            // Note: Some NestJS handlers may not send a response, which is acceptable
            try
            {
                // Set a short timeout for reading response to avoid hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

                // Read the 4-byte length prefix
                var lengthPrefixBuffer = new byte[4];
                var totalBytesRead = 0;
                while (totalBytesRead < 4)
                {
                    var bytesRead = await _stream.ReadAsync(
                        lengthPrefixBuffer,
                        totalBytesRead,
                        4 - totalBytesRead,
                        cts.Token
                    );
                    if (bytesRead == 0)
                    {
                        // Server closed connection without sending response - this is acceptable
                        _logger.LogDebug(
                            "Server closed connection without response, message was sent successfully"
                        );
                        return true;
                    }
                    totalBytesRead += bytesRead;
                }

                // Convert from network byte order (big-endian) to host byte order
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthPrefixBuffer);
                }
                var responseLength = BitConverter.ToUInt32(lengthPrefixBuffer, 0);

                if (responseLength > 0)
                {
                    // Read the response payload
                    var responseBuffer = new byte[responseLength];
                    totalBytesRead = 0;
                    while (totalBytesRead < responseLength)
                    {
                        var bytesRead = await _stream.ReadAsync(
                            responseBuffer,
                            totalBytesRead,
                            (int)responseLength - totalBytesRead,
                            cts.Token
                        );
                        if (bytesRead == 0)
                        {
                            // Server closed connection mid-response - still consider it success
                            _logger.LogDebug(
                                "Server closed connection while reading response, message was sent successfully"
                            );
                            return true;
                        }
                        totalBytesRead += bytesRead;
                    }

                    var response = Encoding.UTF8.GetString(responseBuffer, 0, (int)responseLength);
                    _logger.LogInformation("Server response: {Response}", response);

                    // Check if response indicates success
                    if (
                        response.Contains("success", StringComparison.OrdinalIgnoreCase)
                        || response.Contains("ok", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout waiting for response - message was already sent, so consider it success
                _logger.LogDebug("Response read timeout, message was sent successfully");
            }
            catch (IOException ex)
                when (ex.Message.Contains("closed") || ex.Message.Contains("reset"))
            {
                // Connection closed by server is acceptable if message was already sent
                _logger.LogDebug("Connection closed by server, message was sent successfully");
            }
            catch (Exception ex)
            {
                // Log at debug level since this is non-critical
                _logger.LogDebug(
                    ex,
                    "Could not read server response, message was sent successfully"
                );
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via TCP");

            // Close connection on error to force reconnect on next attempt
            Disconnect();
            return false;
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
        Disconnect();
    }
}
