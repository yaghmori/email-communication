# Microservice Client Architecture

This project provides a **reusable, generic architecture** for communicating with NestJS microservices via **TCP** and **Kafka**. The architecture is designed to be easily extended for multiple services (Email, Storage, etc.).

## 🏗️ Architecture Overview

```
Services/
├── Microservices/              # Generic base classes (reusable)
│   ├── NestJsTcpClient.cs      # Generic TCP client for NestJS
│   └── KafkaPublisherBase.cs   # Generic Kafka publisher
│
├── Email/                      # Email service implementation
│   ├── EmailTcpClient.cs       # Email-specific TCP client
│   ├── EmailKafkaPublisher.cs  # Email-specific Kafka publisher
│   └── EmailService.cs         # Email service with retry logic
│
└── Storage/                    # Storage service implementation
    ├── StorageTcpClient.cs     # Storage-specific TCP clients
    └── StorageKafkaPublisher.cs # Storage-specific Kafka publishers
```

## 📦 Generic Base Classes

### 1. NestJsTcpClient<TRequest, TResponse>

Generic TCP client that implements the **NestJS TCP protocol** (`[LENGTH]#[JSON]`).

**Features:**
- ✅ Automatic connection management per request
- ✅ Thread-safe sending with semaphore
- ✅ Configurable timeouts
- ✅ Request/response transformation hooks
- ✅ Comprehensive error handling and logging

**Usage Example:**
```csharp
public class EmailTcpClient : NestJsTcpClient<EmailPayload, EmailServiceResponse>
{
    public EmailTcpClient(IConfiguration config, ILogger<EmailTcpClient> logger)
        : base(config, logger, "Tcp") // Config section name
    {
    }

    protected override string GetMessagePattern() => "email.send_email";

    // Optional: Transform request before sending
    protected override object TransformRequest(EmailPayload request)
    {
        return new { /* custom format */ };
    }

    // Optional: Parse custom response format
    protected override EmailServiceResponse ParseResponse(string json)
    {
        // Custom parsing logic
    }
}
```

**Configuration:**
```json
{
  "Tcp": {
    "Host": "localhost",
    "Port": "4003",
    "ConnectionTimeout": "5000",
    "ReceiveTimeout": "30000"
  }
}
```

### 2. KafkaPublisherBase<TPayload>

Generic Kafka publisher with event envelope wrapping.

**Features:**
- ✅ Automatic event envelope creation
- ✅ Configurable message keys for partitioning
- ✅ Payload transformation hooks
- ✅ Idempotent producer settings
- ✅ Retry logic built-in

**Usage Example:**
```csharp
public class EmailKafkaPublisher : KafkaPublisherBase<EmailPayload>
{
    public EmailKafkaPublisher(IConfiguration config, ILogger<EmailKafkaPublisher> logger)
        : base(config, logger, "Kafka") // Config section name
    {
    }

    protected override string GetEventType() => "evt.email.message.send.v1";

    // Optional: Custom message key for partitioning
    protected override string? GetMessageKey(EmailPayload payload)
    {
        return payload.To?.ToString()?.ToLowerInvariant();
    }

    // Optional: Transform payload before publishing
    protected override EmailPayload TransformPayload(EmailPayload payload)
    {
        // Normalization, validation, etc.
        return payload;
    }
}
```

**Configuration:**
```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ClientId": "csharp-email-client",
    "Topic": "email.send.requested"
  }
}
```

### 3. EventEnvelope<TPayload>

Generic event envelope for Kafka messages with metadata.

**Structure:**
```json
{
  "messageId": "uuid",
  "timestamp": "2026-01-07T12:00:00Z",
  "eventType": "evt.email.message.send.v1",
  "eventVersion": 1,
  "source": "csharp-email-client",
  "tenantId": "tenant-123",
  "payload": { /* your data */ }
}
```

## 🚀 Creating a New Service

### Step 1: Define Your Models

```csharp
// Models/YourServiceModels.cs
public class YourServiceRequest
{
    [JsonPropertyName("field1")]
    public string Field1 { get; set; } = string.Empty;

    [JsonPropertyName("field2")]
    public string Field2 { get; set; } = string.Empty;
}

public class YourServiceResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
```

### Step 2: Create TCP Client

```csharp
// Services/YourService/YourServiceTcpClient.cs
using EmailCommunication.Services.Microservices;

public class YourServiceTcpClient : NestJsTcpClient<YourServiceRequest, YourServiceResponse>
{
    public YourServiceTcpClient(
        IConfiguration configuration,
        ILogger<YourServiceTcpClient> logger
    ) : base(configuration, logger, "YourServiceTcp")
    {
    }

    protected override string GetMessagePattern() => "your-service.action_name";
}
```

### Step 3: Create Kafka Publisher

```csharp
// Services/YourService/YourServiceKafkaPublisher.cs
using EmailCommunication.Services.Microservices;

public class YourServiceKafkaPublisher : KafkaPublisherBase<YourServiceRequest>
{
    public YourServiceKafkaPublisher(
        IConfiguration configuration,
        ILogger<YourServiceKafkaPublisher> logger
    ) : base(configuration, logger, "YourServiceKafka")
    {
    }

    protected override string GetEventType() => "evt.your-service.action.v1";
}
```

### Step 4: Add Configuration

```json
{
  "YourServiceTcp": {
    "Host": "localhost",
    "Port": "4005",
    "ConnectionTimeout": "5000",
    "ReceiveTimeout": "30000"
  },
  "YourServiceKafka": {
    "BootstrapServers": "localhost:9092",
    "ClientId": "csharp-yourservice-client",
    "Topic": "yourservice.events"
  }
}
```

### Step 5: Register in DI

```csharp
// Program.cs
services.AddSingleton<YourServiceTcpClient>();
services.AddSingleton<YourServiceKafkaPublisher>();
```

### Step 6: Use Your Client

```csharp
var tcpClient = serviceProvider.GetRequiredService<YourServiceTcpClient>();
var response = await tcpClient.SendAsync(new YourServiceRequest
{
    Field1 = "value1",
    Field2 = "value2"
});

if (response?.Success == true)
{
    Console.WriteLine("Success!");
}
```

## 📋 Real-World Examples

### Example 1: Email Service (Included)

**TCP Client:** [Services/Email/EmailTcpClient.cs](Services/Email/EmailTcpClient.cs)
- Pattern: `email.send_email`
- Custom request transformation
- Custom response parsing

**Kafka Publisher:** [Services/Email/EmailKafkaPublisher.cs](Services/Email/EmailKafkaPublisher.cs)
- Event: `evt.email.message.send.v1`
- Partitioning by recipient email

### Example 2: Storage Service (Included)

**TCP Clients:** [Services/Storage/StorageTcpClient.cs](Services/Storage/StorageTcpClient.cs)
- `StorageUploadTcpClient` - Pattern: `storage.upload_file`
- `StorageDeleteTcpClient` - Pattern: `storage.delete_file`

**Kafka Publishers:** [Services/Storage/StorageKafkaPublisher.cs](Services/Storage/StorageKafkaPublisher.cs)
- `StorageUploadKafkaPublisher` - Event: `evt.storage.file.upload.v1`
- `StorageDeleteKafkaPublisher` - Event: `evt.storage.file.delete.v1`

## 🔧 Advanced Features

### Custom Request Transformation

Override `TransformRequest` to customize the data sent to the server:

```csharp
protected override object TransformRequest(EmailPayload request)
{
    return new
    {
        to = request.To,
        subject = request.Subject,
        requestId = Guid.NewGuid().ToString(),
        // Add custom fields
        customField = "value"
    };
}
```

### Custom Response Parsing

Override `ParseResponse` to handle different response formats:

```csharp
protected override EmailServiceResponse ParseResponse(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        return new EmailServiceResponse
        {
            Success = doc.RootElement.GetProperty("success").GetBoolean(),
            Message = doc.RootElement.GetProperty("message").GetString()
        };
    }
    catch
    {
        return new EmailServiceResponse { Success = false };
    }
}
```

### Custom Message Partitioning

Override `GetMessageKey` to control Kafka partitioning:

```csharp
protected override string? GetMessageKey(FileUploadPayload payload)
{
    // Partition by file name
    return payload.FileName;
}
```

## 🎯 Benefits

1. **Reusability**: Write once, use for all services
2. **Consistency**: All services follow the same patterns
3. **Type Safety**: Generic types ensure compile-time checks
4. **Maintainability**: Changes to protocol affect all clients
5. **Testability**: Easy to mock base classes
6. **Extensibility**: Override methods for custom behavior

## 🔌 NestJS TCP Protocol

The base TCP client implements the NestJS microservice protocol:

**Message Format:**
```
[DECIMAL_LENGTH]#[JSON_MESSAGE]
```

**Example:**
```
123#{"pattern":"email.send_email","data":{"to":"test@example.com"},"id":"uuid"}
```

**Key Points:**
- Length is sent as **decimal text** (not binary)
- `#` delimiter separates length from JSON
- Message includes: `pattern`, `data`, and `id` fields

## 📊 Configuration Reference

### TCP Configuration
```json
{
  "ServiceName": {
    "Host": "localhost",           // Server hostname
    "Port": "4003",                // Server port
    "ConnectionTimeout": "5000",   // Connection timeout (ms)
    "ReceiveTimeout": "30000"      // Response timeout (ms)
  }
}
```

### Kafka Configuration
```json
{
  "ServiceNameKafka": {
    "BootstrapServers": "localhost:9092",
    "ClientId": "unique-client-id",
    "Topic": "topic.name"
  }
}
```

## 🛠️ Testing

To test the refactored code:

```bash
# Build the project
dotnet build

# Run the interactive client
dotnet run

# Select TCP or Kafka mode
# Send test messages
```

## 📝 Migration Guide

If you have existing service-specific clients:

1. Create new service folder under `Services/`
2. Inherit from `NestJsTcpClient<TRequest, TResponse>`
3. Inherit from `KafkaPublisherBase<TPayload>`
4. Implement required abstract methods
5. Add configuration for the new service
6. Update DI registrations
7. Test thoroughly

## 🎓 Best Practices

1. **Naming Convention**: Use `{Service}{Action}TcpClient` format
2. **Config Sections**: Use clear, service-specific section names
3. **Logging**: Let base classes handle logging, add context in overrides
4. **Error Handling**: Base classes handle common errors, add service-specific handling
5. **Versioning**: Include version in event types (e.g., `.v1`, `.v2`)

---

**Happy Coding!** 🚀

For questions or issues, refer to:
- [NestJsTcpClient.cs](Services/Microservices/NestJsTcpClient.cs) - TCP implementation
- [KafkaPublisherBase.cs](Services/Microservices/KafkaPublisherBase.cs) - Kafka implementation
