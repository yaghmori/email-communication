using System.Text.Json.Serialization;

namespace EmailCommunication.Models;

/// <summary>
/// Complete event envelope for Kafka messages
/// </summary>
public class EmailEventEnvelope
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "evt.email.message.send.v1";

    [JsonPropertyName("eventVersion")]
    public int EventVersion { get; set; } = 1;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "csharp-email-client";

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("payload")]
    public EmailPayload Payload { get; set; } = new();
}

/// <summary>
/// Email payload data
/// </summary>
public class EmailPayload
{
    [JsonPropertyName("to")]
    public object To { get; set; } = string.Empty; // string or string[]

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("html")]
    public string? Html { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("template")]
    public string? Template { get; set; }

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "en";

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("fromName")]
    public string? FromName { get; set; }

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "normal";

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
