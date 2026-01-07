using EmailCommunication.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services;

/// <summary>
/// Email service with retry logic and error handling
/// </summary>
public interface IEmailService
{
    Task<bool> SendEmailAsync(EmailPayload payload, string? tenantId = null);
    Task<bool> SendEmailWithRetryAsync(
        EmailPayload payload,
        string? tenantId = null,
        int maxRetries = 3
    );
    Task<bool> SendEmailAsync(EmailPayload payload, string mode, string? tenantId = null);
    Task<bool> SendEmailWithRetryAsync(
        EmailPayload payload,
        string mode,
        string? tenantId = null,
        int maxRetries = 3
    );
}

public class EmailService : IEmailService
{
    private readonly EmailKafkaPublisher? _kafkaPublisher;
    private readonly IEmailTcpClient? _tcpClient;
    private readonly ILogger<EmailService> _logger;
    private readonly string _mode;

    public EmailService(
        IConfiguration configuration,
        EmailKafkaPublisher? kafkaPublisher,
        IEmailTcpClient? tcpClient,
        ILogger<EmailService> logger
    )
    {
        _kafkaPublisher = kafkaPublisher;
        _tcpClient = tcpClient;
        _logger = logger;
        _mode = configuration["EmailService:Mode"] ?? "Kafka";
    }

    /// <summary>
    /// Send email via configured mode (TCP or Kafka) - single attempt
    /// </summary>
    public async Task<bool> SendEmailAsync(EmailPayload payload, string? tenantId = null)
    {
        return _mode.ToLowerInvariant() switch
        {
            "tcp" => await SendViaTcpAsync(payload, tenantId),
            "kafka" => await SendViaKafkaAsync(payload, tenantId),
            _ => throw new InvalidOperationException($"Unsupported email service mode: {_mode}")
        };
    }

    /// <summary>
    /// Send email with retry logic and exponential backoff
    /// </summary>
    public async Task<bool> SendEmailWithRetryAsync(
        EmailPayload payload,
        string? tenantId = null,
        int maxRetries = 3
    )
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var success = await SendEmailAsync(payload, tenantId);

            if (success)
            {
                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "Email sent successfully after {Attempt} attempts",
                        attempt
                    );
                }
                return true;
            }

            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100);
                _logger.LogWarning(
                    "Email send failed, retry {Attempt}/{MaxRetries} after {Delay}ms",
                    attempt,
                    maxRetries,
                    delay.TotalMilliseconds
                );
                await Task.Delay(delay);
            }
        }

        _logger.LogError(
            "Failed to send email after {MaxRetries} attempts",
            maxRetries
        );
        return false;
    }

    /// <summary>
    /// Send email via specified mode (TCP or Kafka) - single attempt
    /// </summary>
    public async Task<bool> SendEmailAsync(EmailPayload payload, string mode, string? tenantId = null)
    {
        return mode.ToLowerInvariant() switch
        {
            "tcp" => await SendViaTcpAsync(payload, tenantId),
            "kafka" => await SendViaKafkaAsync(payload, tenantId),
            _ => throw new InvalidOperationException($"Unsupported email service mode: {mode}")
        };
    }

    /// <summary>
    /// Send email via specified mode with retry logic and exponential backoff
    /// </summary>
    public async Task<bool> SendEmailWithRetryAsync(
        EmailPayload payload,
        string mode,
        string? tenantId = null,
        int maxRetries = 3
    )
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var success = await SendEmailAsync(payload, mode, tenantId);

            if (success)
            {
                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "Email sent successfully after {Attempt} attempts",
                        attempt
                    );
                }
                return true;
            }

            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100);
                _logger.LogWarning(
                    "Email send failed, retry {Attempt}/{MaxRetries} after {Delay}ms",
                    attempt,
                    maxRetries,
                    delay.TotalMilliseconds
                );
                await Task.Delay(delay);
            }
        }

        _logger.LogError(
            "Failed to send email after {MaxRetries} attempts",
            maxRetries
        );
        return false;
    }

    private async Task<bool> SendViaKafkaAsync(EmailPayload payload, string? tenantId)
    {
        if (_kafkaPublisher == null)
        {
            _logger.LogError("Kafka publisher is not configured");
            return false;
        }
        return await _kafkaPublisher.PublishEmailRequestAsync(payload, tenantId);
    }

    private async Task<bool> SendViaTcpAsync(EmailPayload payload, string? tenantId)
    {
        if (_tcpClient == null)
        {
            _logger.LogError("TCP client is not configured");
            return false;
        }
        return await _tcpClient.SendEmailAsync(payload, tenantId);
    }
}
