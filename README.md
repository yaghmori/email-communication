# Email Communication Client

A simple C# console application that communicates with the Email Service via Kafka, following best practices for event-driven email communication.

## Features

- ✅ **Kafka Integration** - Uses Confluent.Kafka client for reliable message delivery
- ✅ **Event Envelope Format** - Properly formatted events matching email service requirements
- ✅ **Retry Logic** - Automatic retry with exponential backoff
- ✅ **Error Handling** - Comprehensive error handling and logging
- ✅ **Template Support** - Send emails using templates stored in email service
- ✅ **Multi-Recipient** - Support for single or multiple recipients
- ✅ **Metadata Tracking** - Custom metadata for tracking and debugging

## Prerequisites

- .NET 10.0 SDK or later
- Running Kafka cluster (accessible at `localhost:9092` by default)
- Email service running and configured to consume from `email.send.requested` topic

## Setup

1. **Clone or navigate to the project directory**

2. **Update configuration** in `appsettings.json`:
   ```json
   {
     "Kafka": {
       "BootstrapServers": "localhost:9092",
       "ClientId": "csharp-email-client",
       "Topic": "email.send.requested"
     }
   }
   ```

3. **Restore dependencies and build**:
   ```bash
   dotnet restore
   dotnet build
   ```

4. **Run the application**:
   ```bash
   dotnet run
   ```

## Configuration

### Environment Variables

You can override configuration using environment variables:

```bash
# Windows PowerShell
$env:Kafka__BootstrapServers="your-kafka-server:9092"
$env:Kafka__Topic="email.send.requested"

# Linux/Mac
export Kafka__BootstrapServers="your-kafka-server:9092"
export Kafka__Topic="email.send.requested"
```

### appsettings.json

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ClientId": "csharp-email-client",
    "Topic": "email.send.requested"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

## Usage Examples

### 1. Simple Email (HTML + Text)

```csharp
var payload = new EmailPayload
{
    To = "user@example.com",
    Subject = "Welcome!",
    Html = "<h1>Welcome!</h1><p>Thank you for joining.</p>",
    Text = "Welcome! Thank you for joining.",
    Priority = "normal"
};

await emailService.SendEmailWithRetryAsync(payload);
```

### 2. Template Email

```csharp
var payload = new EmailPayload
{
    To = "customer@example.com",
    Template = "order-confirmation",
    Locale = "en",
    TenantId = "tenant-123",
    Data = new Dictionary<string, object>
    {
        { "orderId", "ORD-12345" },
        { "customerName", "John Doe" },
        { "orderTotal", 99.99 }
    }
};

await emailService.SendEmailWithRetryAsync(payload, payload.TenantId);
```

### 3. Multiple Recipients

```csharp
var payload = new EmailPayload
{
    To = new[] { "user1@example.com", "user2@example.com" },
    Subject = "Announcement",
    Html = "<h1>Important Update</h1>",
    Priority = "high"
};

await emailService.SendEmailWithRetryAsync(payload);
```

### 4. Email with Metadata

```csharp
var payload = new EmailPayload
{
    To = "user@example.com",
    Subject = "Password Reset",
    Html = "<p>Reset your password here...</p>",
    Metadata = new Dictionary<string, object>
    {
        { "userId", "user-123" },
        { "type", "password-reset" }
    }
};

await emailService.SendEmailWithRetryAsync(payload);
```

## Project Structure

```
EmailCommunication/
├── Models/
│   └── EmailEventEnvelope.cs      # Event envelope and payload models
├── Services/
│   ├── EmailKafkaPublisher.cs     # Kafka publisher implementation
│   └── EmailService.cs             # Email service with retry logic
├── Program.cs                      # Main program with examples
├── appsettings.json                # Configuration
└── EmailCommunication.csproj       # Project file
```

## Key Components

### EmailEventEnvelope
- Wraps email payload with required event metadata
- Includes `messageId`, `timestamp`, `eventType`, `source`, etc.
- Automatically generated when publishing

### EmailKafkaPublisher
- Handles Kafka connection and message publishing
- Implements idempotent producer for exactly-once semantics
- Uses message keys for proper partitioning
- Normalizes email addresses to lowercase

### EmailService
- High-level service wrapper
- Implements retry logic with exponential backoff
- Provides simple interface for sending emails

## Best Practices Implemented

1. ✅ **Email Normalization** - All email addresses converted to lowercase
2. ✅ **Message Keys** - Recipient email used as key for partitioning
3. ✅ **Idempotent Producer** - Prevents duplicate messages
4. ✅ **Retry Logic** - Exponential backoff for transient failures
5. ✅ **Error Handling** - Comprehensive logging and error handling
6. ✅ **Event Envelope** - Proper format matching email service requirements
7. ✅ **Configuration** - Externalized configuration for flexibility

## Event Format

The application publishes messages in the following format:

```json
{
  "messageId": "uuid",
  "timestamp": "2025-01-01T00:00:00.000Z",
  "eventType": "evt.email.message.send.v1",
  "eventVersion": 1,
  "source": "csharp-email-client",
  "tenantId": "optional-tenant-id",
  "payload": {
    "to": "user@example.com",
    "subject": "Email Subject",
    "html": "<html>...</html>",
    "text": "Plain text version",
    "priority": "normal"
  }
}
```

## Troubleshooting

### Connection Issues

If you can't connect to Kafka:
1. Verify Kafka is running: `kafka-broker-api-versions --bootstrap-server localhost:9092`
2. Check firewall/network settings
3. Verify `BootstrapServers` in `appsettings.json`

### Messages Not Being Consumed

1. Check email service logs
2. Verify topic name matches: `email.send.requested`
3. Ensure email service is running and connected to Kafka

### Validation Errors

If emails fail validation:
1. Ensure required fields are provided (`to`, `subject` OR `template`)
2. Check template exists and is active (for template emails)
3. Verify schema matches template requirements

## References

- [Email Communication Guide](../doc/email-service/EMAIL_COMMUNICATION_GUIDE.md)
- [Email Service Integration Guide](../doc/email-service/EMAIL_SERVICE_INTEGRATION_GUIDE.md)
- [Email Template Guide](../doc/email-service/EMAIL_TEMPLATE_GUIDE.md)

## License

This is an internal tool for communicating with the email service.

