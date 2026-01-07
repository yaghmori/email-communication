using System.Linq;
using EmailCommunication.Models;
using EmailCommunication.Services.Microservices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Email;

/// <summary>
/// Email-specific Kafka publisher implementation
/// </summary>
public class EmailKafkaPublisher : KafkaPublisherBase<EmailPayload>
{
    public EmailKafkaPublisher(
        IConfiguration configuration,
        ILogger<EmailKafkaPublisher> logger
    ) : base(configuration, logger, "Kafka")
    {
    }

    protected override string GetEventType() => "evt.email.message.send.v1";

    protected override string? GetMessageKey(EmailPayload payload)
    {
        // Use recipient email as message key for partitioning
        // This ensures ordering per recipient
        if (payload.To is string singleEmail)
        {
            return singleEmail.ToLowerInvariant();
        }

        return null;
    }

    protected override EmailPayload TransformPayload(EmailPayload payload)
    {
        // Normalize email addresses to lowercase
        if (payload.To is string email)
        {
            payload.To = email.ToLowerInvariant();
        }
        else if (payload.To is string[] emails)
        {
            payload.To = emails.Select(e => e.ToLowerInvariant()).ToArray();
        }

        return payload;
    }
}
