using System.Text.Json.Serialization;

namespace EmailCommunication.Models;

/// <summary>
/// Storage service file upload payload
/// </summary>
public class FileUploadPayload
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("fileContent")]
    public string FileContent { get; set; } = string.Empty; // Base64 encoded

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "application/octet-stream";

    [JsonPropertyName("folder")]
    public string? Folder { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("overwrite")]
    public bool Overwrite { get; set; } = false;
}

/// <summary>
/// Storage service file delete payload
/// </summary>
public class FileDeletePayload
{
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("permanent")]
    public bool Permanent { get; set; } = false;
}

/// <summary>
/// Storage service response
/// </summary>
public class StorageServiceResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("fileId")]
    public string? FileId { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
