using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Microservices;

/// <summary>
/// Generic event envelope for Kafka messages
/// </summary>
/// <typeparam name="TPayload">The payload type</typeparam>
public class EventEnvelope<TPayload>
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("eventVersion")]
    public int EventVersion { get; set; } = 1;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("payload")]
    public TPayload Payload { get; set; } = default!;
}

/// <summary>
/// Generic Kafka publisher base class for microservices
/// </summary>
/// <typeparam name="TPayload">The payload type to publish</typeparam>
public abstract class KafkaPublisherBase<TPayload> : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly string _source;
    protected readonly ILogger Logger;
    private readonly JsonSerializerOptions _jsonOptions;

    protected KafkaPublisherBase(
        IConfiguration configuration,
        ILogger logger,
        string configSection
    )
    {
        Logger = logger;
        _source = configuration[$"{configSection}:ClientId"] ?? "csharp-client";
        _topic = configuration[$"{configSection}:Topic"] ?? "default.topic";

        var config = new ProducerConfig
        {
            BootstrapServers = configuration[$"{configSection}:BootstrapServers"] ?? "localhost:9092",
            ClientId = _source,
            Partitioner = Partitioner.Murmur2Random,
            EnableIdempotence = true,
            MaxInFlight = 1,
            Acks = Acks.All,
            RetryBackoffMs = 100,
            MessageSendMaxRetries = 3
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetKeySerializer(Serializers.Utf8)
            .SetValueSerializer(Serializers.Utf8)
            .Build();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Logger.LogInformation(
            "{Service} KafkaPublisher initialized. Topic: {Topic}, BootstrapServers: {BootstrapServers}",
            GetType().Name,
            _topic,
            config.BootstrapServers
        );
    }

    /// <summary>
    /// Get the event type for this publisher
    /// Example: "evt.email.message.send.v1", "evt.storage.file.upload.v1"
    /// </summary>
    protected abstract string GetEventType();

    /// <summary>
    /// Get the message key for partitioning (optional override)
    /// </summary>
    protected virtual string? GetMessageKey(TPayload payload) => null;

    /// <summary>
    /// Transform the payload before publishing (optional override)
    /// </summary>
    protected virtual TPayload TransformPayload(TPayload payload) => payload;

    /// <summary>
    /// Publish a message to Kafka
    /// </summary>
    public async Task<bool> PublishAsync(
        TPayload payload,
        string? tenantId = null,
        string? messageKey = null
    )
    {
        try
        {
            // Transform payload
            var transformedPayload = TransformPayload(payload);

            // Create event envelope
            var envelope = new EventEnvelope<TPayload>
            {
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("O"),
                EventType = GetEventType(),
                EventVersion = 1,
                Source = _source,
                TenantId = tenantId,
                Payload = transformedPayload
            };

            var json = JsonSerializer.Serialize(envelope, _jsonOptions);

            // Use provided key or get from payload
            var key = messageKey ?? GetMessageKey(transformedPayload);

            var message = new Message<string, string>
            {
                Key = key,
                Value = json,
                Headers = new Headers
                {
                    { "event-type", Encoding.UTF8.GetBytes(envelope.EventType) }
                }
            };

            var deliveryResult = await _producer.ProduceAsync(_topic, message);

            Logger.LogInformation(
                "Message published to Kafka. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}, MessageId: {MessageId}",
                deliveryResult.Topic,
                deliveryResult.Partition,
                deliveryResult.Offset,
                envelope.MessageId
            );

            return true;
        }
        catch (ProduceException<string, string> ex)
        {
            Logger.LogError(
                ex,
                "Failed to publish to Kafka. Error: {Error}, IsFatal: {IsFatal}",
                ex.Error.Reason,
                ex.Error.IsFatal
            );
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error publishing to Kafka");
            return false;
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
