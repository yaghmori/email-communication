using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using EmailCommunication.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services;

/// <summary>
/// Kafka publisher for email send requests
/// </summary>
public class EmailKafkaPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly string _source;
    private readonly ILogger<EmailKafkaPublisher> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public EmailKafkaPublisher(IConfiguration configuration, ILogger<EmailKafkaPublisher> logger)
    {
        _logger = logger;
        _source = configuration["Kafka:ClientId"] ?? "csharp-email-client";
        _topic = configuration["Kafka:Topic"] ?? "email.send.requested";

        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            ClientId = configuration["Kafka:ClientId"] ?? "csharp-email-client",
            // Important: Use Murmur2Random to match Node.js behavior
            Partitioner = Partitioner.Murmur2Random,
            // Enable idempotent producer for exactly-once semantics
            EnableIdempotence = true,
            MaxInFlight = 1, // Required when idempotence is enabled
            Acks = Acks.All, // Wait for all replicas
            RetryBackoffMs = 100,
            MessageSendMaxRetries = 3,
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetKeySerializer(Serializers.Utf8)
            .SetValueSerializer(Serializers.Utf8)
            .Build();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System
                .Text
                .Json
                .Serialization
                .JsonIgnoreCondition
                .WhenWritingNull,
        };

        _logger.LogInformation(
            "EmailKafkaPublisher initialized. Topic: {Topic}, BootstrapServers: {BootstrapServers}",
            _topic,
            config.BootstrapServers
        );
    }

    /// <summary>
    /// Publish email send request to Kafka
    /// </summary>
    /// <param name="payload">Email payload data</param>
    /// <param name="tenantId">Optional tenant ID for multi-tenant apps</param>
    /// <param name="messageKey">Optional message key (defaults to recipient email)</param>
    /// <returns>True if published successfully, false otherwise</returns>
    public async Task<bool> PublishEmailRequestAsync(
        EmailPayload payload,
        string? tenantId = null,
        string? messageKey = null
    )
    {
        try
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

            var envelope = new EmailEventEnvelope
            {
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("O"), // ISO 8601 format
                EventType = "evt.email.message.send.v1",
                EventVersion = 1,
                Source = _source,
                TenantId = tenantId,
                Payload = payload,
            };

            var json = JsonSerializer.Serialize(envelope, _jsonOptions);

            // Use recipient email as message key for partitioning (ensures ordering per recipient)
            var key =
                messageKey
                ?? (payload.To is string singleEmail ? singleEmail.ToLowerInvariant() : null);

            var message = new Message<string, string>
            {
                Key = key!, // Null is valid for Kafka keys - it means no key/random partition
                Value = json,
                Headers = new Headers
                {
                    { "event-type", Encoding.UTF8.GetBytes(envelope.EventType) },
                },
            };

            var deliveryResult = await _producer.ProduceAsync(_topic, message);

            _logger.LogInformation(
                "Email request published to Kafka. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}, MessageId: {MessageId}",
                deliveryResult.Topic,
                deliveryResult.Partition,
                deliveryResult.Offset,
                envelope.MessageId
            );

            return true;
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish email request to Kafka. Error: {Error}, IsFatal: {IsFatal}",
                ex.Error.Reason,
                ex.Error.IsFatal
            );
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error publishing email request to Kafka");
            return false;
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
    }
}
