using System;
using System.Text.Json;
using EmailCommunication.Models;
using EmailCommunication.Services.Microservices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailCommunication.Services.Email;

/// <summary>
/// Email service response
/// </summary>
public class EmailServiceResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? MessageId { get; set; }
}

/// <summary>
/// Email-specific TCP client implementation
/// </summary>
public class EmailTcpClient : NestJsTcpClient<EmailPayload, EmailServiceResponse>
{
    public EmailTcpClient(
        IConfiguration configuration,
        ILogger<EmailTcpClient> logger
    ) : base(configuration, logger, "Tcp")
    {
    }

    protected override string GetMessagePattern() => "email.send_email";

    protected override object TransformRequest(EmailPayload request)
    {
        // Transform EmailPayload to match NestJS SendEmailRequest format
        return new
        {
            to = request.To,
            subject = request.Subject,
            text = request.Text,
            html = request.Html,
            from = request.From,
            fromName = request.FromName,
            template = request.Template,
            locale = request.Locale,
            data = request.Data,
            priority = request.Priority,
            metadata = request.Metadata,
            requestId = Guid.NewGuid().ToString()
        };
    }

    protected override EmailServiceResponse ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try to extract success field
            var success = root.TryGetProperty("success", out var successElement)
                ? successElement.GetBoolean()
                : true; // Assume success if field not present

            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;

            var messageId = root.TryGetProperty("messageId", out var messageIdElement)
                ? messageIdElement.GetString()
                : null;

            return new EmailServiceResponse
            {
                Success = success,
                Message = message,
                MessageId = messageId
            };
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to parse response as JSON: {Response}", json);
            // Still return success if we got a response
            return new EmailServiceResponse { Success = true, Message = json };
        }
    }
}
