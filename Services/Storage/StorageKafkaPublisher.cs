using EmailCommunication.Models;
using EmailCommunication.Services.Microservices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Storage;

/// <summary>
/// Storage service Kafka publisher for file upload events
/// </summary>
public class StorageUploadKafkaPublisher : KafkaPublisherBase<FileUploadPayload>
{
    public StorageUploadKafkaPublisher(
        IConfiguration configuration,
        ILogger<StorageUploadKafkaPublisher> logger
    ) : base(configuration, logger, "StorageKafka")
    {
    }

    protected override string GetEventType() => "evt.storage.file.upload.v1";

    protected override string? GetMessageKey(FileUploadPayload payload)
    {
        // Use file name as key for partitioning
        return payload.FileName;
    }
}

/// <summary>
/// Storage service Kafka publisher for file delete events
/// </summary>
public class StorageDeleteKafkaPublisher : KafkaPublisherBase<FileDeletePayload>
{
    public StorageDeleteKafkaPublisher(
        IConfiguration configuration,
        ILogger<StorageDeleteKafkaPublisher> logger
    ) : base(configuration, logger, "StorageKafka")
    {
    }

    protected override string GetEventType() => "evt.storage.file.delete.v1";

    protected override string? GetMessageKey(FileDeletePayload payload)
    {
        // Use file ID as key for partitioning
        return payload.FileId;
    }
}
