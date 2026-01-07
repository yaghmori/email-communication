using EmailCommunication.Models;
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
}

public class EmailService : IEmailService
{
    private readonly EmailKafkaPublisher _kafkaPublisher;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailKafkaPublisher kafkaPublisher, ILogger<EmailService> logger)
    {
        _kafkaPublisher = kafkaPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Send email via Kafka (single attempt)
    /// </summary>
    public async Task<bool> SendEmailAsync(EmailPayload payload, string? tenantId = null)
    {
        return await _kafkaPublisher.PublishEmailRequestAsync(payload, tenantId);
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
            var success = await _kafkaPublisher.PublishEmailRequestAsync(payload, tenantId);

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
                    "Kafka publish failed, retry {Attempt}/{MaxRetries} after {Delay}ms",
                    attempt,
                    maxRetries,
                    delay.TotalMilliseconds
                );
                await Task.Delay(delay);
            }
        }

        _logger.LogError(
            "Failed to publish email to Kafka after {MaxRetries} attempts",
            maxRetries
        );
        return false;
    }
}
