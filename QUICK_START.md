# Quick Start Guide: Adding a New Microservice Client

This guide shows you how to add a new microservice client in **5 minutes**.

## 🚀 Quick Example: Creating a Notification Service Client

### 1. Create Your Models (1 minute)

Create `Models/NotificationModels.cs`:

```csharp
using System.Text.Json.Serialization;

namespace EmailCommunication.Models;

public class NotificationPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "info"; // info, warning, error
}

public class NotificationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("notificationId")]
    public string? NotificationId { get; set; }
}
```

### 2. Create TCP Client (1 minute)

Create `Services/Notification/NotificationTcpClient.cs`:

```csharp
using EmailCommunication.Models;
using EmailCommunication.Services.Microservices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Notification;

public class NotificationTcpClient : NestJsTcpClient<NotificationPayload, NotificationResponse>
{
    public NotificationTcpClient(
        IConfiguration configuration,
        ILogger<NotificationTcpClient> logger
    ) : base(configuration, logger, "NotificationTcp")
    {
    }

    protected override string GetMessagePattern() => "notification.send";
}
```

### 3. Create Kafka Publisher (1 minute)

Create `Services/Notification/NotificationKafkaPublisher.cs`:

```csharp
using EmailCommunication.Models;
using EmailCommunication.Services.Microservices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Notification;

public class NotificationKafkaPublisher : KafkaPublisherBase<NotificationPayload>
{
    public NotificationKafkaPublisher(
        IConfiguration configuration,
        ILogger<NotificationKafkaPublisher> logger
    ) : base(configuration, logger, "NotificationKafka")
    {
    }

    protected override string GetEventType() => "evt.notification.send.v1";

    protected override string? GetMessageKey(NotificationPayload payload)
    {
        return payload.UserId; // Partition by user
    }
}
```

### 4. Add Configuration (1 minute)

Update `appsettings.json`:

```json
{
  "NotificationTcp": {
    "Host": "localhost",
    "Port": "4005",
    "ConnectionTimeout": "5000",
    "ReceiveTimeout": "30000"
  },
  "NotificationKafka": {
    "BootstrapServers": "localhost:9092",
    "ClientId": "csharp-notification-client",
    "Topic": "notification.send.requested"
  }
}
```

### 5. Use Your Client (1 minute)

In your code:

```csharp
// Register in DI (Program.cs)
services.AddSingleton<NotificationTcpClient>();
services.AddSingleton<NotificationKafkaPublisher>();

// Use TCP
var tcpClient = serviceProvider.GetRequiredService<NotificationTcpClient>();
var response = await tcpClient.SendAsync(new NotificationPayload
{
    UserId = "user-123",
    Title = "Welcome!",
    Message = "Your account has been created",
    Type = "info"
});

if (response?.Success == true)
{
    Console.WriteLine($"Notification sent: {response.NotificationId}");
}

// Use Kafka
var kafkaPublisher = serviceProvider.GetRequiredService<NotificationKafkaPublisher>();
var published = await kafkaPublisher.PublishAsync(new NotificationPayload
{
    UserId = "user-123",
    Title = "Welcome!",
    Message = "Your account has been created",
    Type = "info"
}, tenantId: "tenant-1");

if (published)
{
    Console.WriteLine("Event published to Kafka!");
}
```

## 🎯 That's It!

You now have a fully functional microservice client with:
- ✅ TCP communication
- ✅ Kafka event publishing
- ✅ Automatic connection management
- ✅ Error handling and retries
- ✅ Logging
- ✅ Type safety

## 🔄 Common Patterns

### Pattern 1: Multiple Operations for Same Service

```csharp
// Upload file
public class StorageUploadTcpClient : NestJsTcpClient<FileUploadPayload, StorageResponse>
{
    protected override string GetMessagePattern() => "storage.upload";
}

// Delete file
public class StorageDeleteTcpClient : NestJsTcpClient<FileDeletePayload, StorageResponse>
{
    protected override string GetMessagePattern() => "storage.delete";
}
```

### Pattern 2: Custom Request Transformation

```csharp
protected override object TransformRequest(NotificationPayload request)
{
    return new
    {
        userId = request.UserId,
        title = request.Title,
        message = request.Message,
        type = request.Type,
        timestamp = DateTime.UtcNow.ToString("O"),
        requestId = Guid.NewGuid().ToString()
    };
}
```

### Pattern 3: Custom Response Handling

```csharp
protected override NotificationResponse ParseResponse(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new NotificationResponse
        {
            Success = root.GetProperty("success").GetBoolean(),
            NotificationId = root.TryGetProperty("id", out var id)
                ? id.GetString()
                : null
        };
    }
    catch
    {
        return new NotificationResponse { Success = false };
    }
}
```

## 📦 Project Structure After Adding New Service

```
email-communication/
├── Services/
│   ├── Microservices/          # Base classes (don't modify)
│   │   ├── NestJsTcpClient.cs
│   │   └── KafkaPublisherBase.cs
│   ├── Email/                   # Email service
│   ├── Storage/                 # Storage service
│   └── Notification/            # Your new service ✨
│       ├── NotificationTcpClient.cs
│       └── NotificationKafkaPublisher.cs
├── Models/
│   ├── EmailEventEnvelope.cs
│   ├── StorageModels.cs
│   └── NotificationModels.cs    # Your new models ✨
└── appsettings.json             # Updated config ✨
```

## 🐛 Troubleshooting

### Client can't connect?
Check:
1. Service is running on correct host:port
2. Firewall allows connection
3. Config section name matches constructor parameter

### Messages not arriving?
Check:
1. Message pattern matches server expectation
2. Request format matches server DTO
3. Server logs for errors

### Build errors?
1. Ensure using statements include namespaces
2. Check generic type parameters match
3. Verify all required methods are implemented

## 📚 Next Steps

- Read [ARCHITECTURE.md](ARCHITECTURE.md) for detailed architecture info
- Check existing implementations in `Services/Email/` and `Services/Storage/`
- Review base classes in `Services/Microservices/`

---

**Questions?** Check the documentation or source code comments!
