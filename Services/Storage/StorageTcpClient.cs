using System;
using EmailCommunication.Models;
using EmailCommunication.Services.Microservices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Storage;

/// <summary>
/// Storage service TCP client for file upload operations
/// </summary>
public class StorageUploadTcpClient : NestJsTcpClient<FileUploadPayload, StorageServiceResponse>
{
    public StorageUploadTcpClient(
        IConfiguration configuration,
        ILogger<StorageUploadTcpClient> logger
    ) : base(configuration, logger, "Storage")
    {
    }

    protected override string GetMessagePattern() => "storage.upload_file";
}

/// <summary>
/// Storage service TCP client for file delete operations
/// </summary>
public class StorageDeleteTcpClient : NestJsTcpClient<FileDeletePayload, StorageServiceResponse>
{
    public StorageDeleteTcpClient(
        IConfiguration configuration,
        ILogger<StorageDeleteTcpClient> logger
    ) : base(configuration, logger, "Storage")
    {
    }

    protected override string GetMessagePattern() => "storage.delete_file";
}
