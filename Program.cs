using EmailCommunication.Models;
using EmailCommunication.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmailCommunication;

class Program
{
    static async Task Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<EmailKafkaPublisher>();
        services.AddSingleton<IEmailService, EmailService>();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var emailService = serviceProvider.GetRequiredService<IEmailService>();

        logger.LogInformation("Email Communication Client Started");
        logger.LogInformation(
            "Kafka Bootstrap Servers: {BootstrapServers}",
            configuration["Kafka:BootstrapServers"]
        );

        try
        {
            // Example 1: Send a simple email with HTML and text
            await SendSimpleEmailAsync(emailService, logger);

            // Example 2: Send email using a template
            await SendTemplateEmailAsync(emailService, logger);

            // Example 3: Send email to multiple recipients
            await SendBulkEmailAsync(emailService, logger);

            // Example 4: Send email with custom metadata
            await SendEmailWithMetadataAsync(emailService, logger);

            logger.LogInformation("\nAll examples completed successfully!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while running examples");
        }
        finally
        {
            // Cleanup
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    /// <summary>
    /// Example 1: Send a simple email with HTML and text content
    /// </summary>
    static async Task SendSimpleEmailAsync(IEmailService emailService, ILogger logger)
    {
        logger.LogInformation("\n=== Example 1: Simple Email ===");

        var payload = new EmailPayload
        {
            To = "user@example.com",
            Subject = "Welcome to Our Service!",
            Html =
                @"
                <html>
                <body>
                    <h1>Welcome!</h1>
                    <p>Thank you for joining our service.</p>
                    <p>We're excited to have you on board.</p>
                </body>
                </html>",
            Text =
                "Welcome! Thank you for joining our service. We're excited to have you on board.",
            Priority = "normal",
        };

        var success = await emailService.SendEmailWithRetryAsync(payload);

        if (success)
        {
            logger.LogInformation("✅ Simple email queued successfully");
        }
        else
        {
            logger.LogError("❌ Failed to queue simple email");
        }
    }

    /// <summary>
    /// Example 2: Send email using a template
    /// </summary>
    static async Task SendTemplateEmailAsync(IEmailService emailService, ILogger logger)
    {
        logger.LogInformation("\n=== Example 2: Template Email ===");

        var payload = new EmailPayload
        {
            To = "customer@example.com",
            Template = "order-confirmation", // Template key from email service
            Locale = "en",
            Data = new Dictionary<string, object>
            {
                { "orderId", "ORD-12345" },
                { "customerName", "John Doe" },
                { "orderTotal", 99.99 },
                {
                    "items",
                    new[]
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "Product A" },
                            { "quantity", 2 },
                            { "price", 49.99 },
                        },
                    }
                },
                { "orderUrl", "https://app.example.com/orders/ORD-12345" },
            },
            Priority = "normal",
        };

        var success = await emailService.SendEmailWithRetryAsync(payload);

        if (success)
        {
            logger.LogInformation("✅ Template email queued successfully");
        }
        else
        {
            logger.LogError("❌ Failed to queue template email");
        }
    }

    /// <summary>
    /// Example 3: Send email to multiple recipients
    /// </summary>
    static async Task SendBulkEmailAsync(IEmailService emailService, ILogger logger)
    {
        logger.LogInformation("\n=== Example 3: Bulk Email ===");

        var payload = new EmailPayload
        {
            To = new[] { "user1@example.com", "user2@example.com", "user3@example.com" },
            Subject = "Important Announcement",
            Html =
                "<h1>Important Update</h1><p>This is an important announcement for all users.</p>",
            Text = "Important Update: This is an important announcement for all users.",
            Priority = "high",
        };

        var success = await emailService.SendEmailWithRetryAsync(payload);

        if (success)
        {
            logger.LogInformation("✅ Bulk email queued successfully");
        }
        else
        {
            logger.LogError("❌ Failed to queue bulk email");
        }
    }

    /// <summary>
    /// Example 4: Send email with custom metadata for tracking
    /// </summary>
    static async Task SendEmailWithMetadataAsync(IEmailService emailService, ILogger logger)
    {
        logger.LogInformation("\n=== Example 4: Email with Metadata ===");

        var payload = new EmailPayload
        {
            To = "user@example.com",
            Subject = "Password Reset Request",
            Html =
                @"
                <html>
                <body>
                    <h1>Password Reset</h1>
                    <p>Click the link below to reset your password:</p>
                    <a href=""https://app.example.com/reset?token=abc123"">Reset Password</a>
                </body>
                </html>",
            Text = "Password Reset: Visit https://app.example.com/reset?token=abc123",
            Priority = "high",
            Metadata = new Dictionary<string, object>
            {
                { "userId", "user-123" },
                { "type", "password-reset" },
                { "requestId", Guid.NewGuid().ToString() },
                { "timestamp", DateTime.UtcNow.ToString("O") },
            },
        };

        var success = await emailService.SendEmailWithRetryAsync(payload);

        if (success)
        {
            logger.LogInformation("✅ Email with metadata queued successfully");
        }
        else
        {
            logger.LogError("❌ Failed to queue email with metadata");
        }
    }
}
