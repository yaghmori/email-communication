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
        services.AddSingleton<IEmailTcpClient, EmailTcpClient>();
        services.AddSingleton<IEmailService, EmailService>();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var emailService = serviceProvider.GetRequiredService<IEmailService>();
        var tcpClient = serviceProvider.GetRequiredService<IEmailTcpClient>();

        logger.LogInformation("Email Communication Client Started");
        logger.LogInformation(
            "TCP Server: {Host}:{Port}",
            configuration["Tcp:Host"],
            configuration["Tcp:Port"]
        );
        logger.LogInformation(
            "Kafka Bootstrap Servers: {BootstrapServers}",
            configuration["Kafka:BootstrapServers"]
        );

        try
        {
            // Interactive email sending
            var defaultRecipient =
                configuration["EmailService:DefaultRecipient"] ?? "test@example.com";
            await RunInteractiveModeAsync(emailService, logger, tcpClient, defaultRecipient);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred");
        }
        finally
        {
            // Cleanup
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    static async Task RunInteractiveModeAsync(
        IEmailService emailService,
        ILogger logger,
        IEmailTcpClient tcpClient,
        string defaultRecipient
    )
    {
        Console.WriteLine("\n=== Interactive Email Communication Client ===");
        Console.WriteLine("You can choose TCP or Kafka for each email.");
        Console.WriteLine($"Default recipient: {defaultRecipient}");
        Console.WriteLine("(Press Enter to use default recipient)\n");

        while (true)
        {
            try
            {
                // Display menu
                Console.WriteLine("=== Email Menu ===");
                Console.WriteLine("1. Welcome Email");
                Console.WriteLine("2. Order Confirmation Email");
                Console.WriteLine("3. Password Reset Email");
                Console.WriteLine("4. Newsletter Email");
                Console.WriteLine("5. Bulk Announcement Email");
                Console.WriteLine("6. Custom Email (Interactive)");
                Console.WriteLine();
                Console.WriteLine("Press any key to refresh menu, or select an option (1-6): ");

                var keyInfo = Console.ReadKey(true);
                var choice = keyInfo.KeyChar.ToString();
                Console.WriteLine(choice);
                Console.WriteLine();

                // If not a valid menu option (1-6), just refresh the menu
                if (
                    string.IsNullOrWhiteSpace(choice)
                    || (
                        choice != "1"
                        && choice != "2"
                        && choice != "3"
                        && choice != "4"
                        && choice != "5"
                        && choice != "6"
                    )
                )
                {
                    continue;
                }

                EmailPayload? payload = null;
                bool shouldContinue = true;

                switch (choice)
                {
                    case "1":
                        (payload, shouldContinue) = await GetWelcomeEmailAsync(defaultRecipient);
                        break;
                    case "2":
                        (payload, shouldContinue) = await GetOrderConfirmationEmailAsync(
                            defaultRecipient
                        );
                        break;
                    case "3":
                        (payload, shouldContinue) = await GetPasswordResetEmailAsync(
                            defaultRecipient
                        );
                        break;
                    case "4":
                        (payload, shouldContinue) = await GetNewsletterEmailAsync(defaultRecipient);
                        break;
                    case "5":
                        (payload, shouldContinue) = await GetBulkAnnouncementEmailAsync(
                            defaultRecipient
                        );
                        break;
                    case "6":
                        (payload, shouldContinue) = await GetCustomEmailAsync(defaultRecipient);
                        break;
                    default:
                        Console.WriteLine("Invalid option. Please select 0-6.\n");
                        continue;
                }

                if (!shouldContinue)
                {
                    Console.WriteLine("Returning to menu...\n");
                    continue;
                }

                if (payload == null)
                {
                    Console.WriteLine("Email creation cancelled.\n");
                    continue;
                }

                // Ask for transport method (TCP or Kafka)
                Console.WriteLine("\n--- Select Transport Method ---");
                Console.WriteLine("1. TCP");
                Console.WriteLine("2. Kafka");
                Console.WriteLine("0. Back to Menu");
                Console.Write("Select transport method (0-2, default: 1): ");
                var transportChoice = Console.ReadLine()?.Trim().ToLowerInvariant();

                if (transportChoice == "0" || transportChoice == "back")
                {
                    Console.WriteLine("Returning to menu...\n");
                    continue;
                }

                string selectedMode;
                if (transportChoice == "2" || transportChoice == "kafka")
                {
                    selectedMode = "Kafka";
                    Console.WriteLine("Selected: Kafka\n");
                }
                else
                {
                    selectedMode = "Tcp";
                    Console.WriteLine("Selected: TCP");

                    // Ensure TCP connection
                    if (!tcpClient.IsConnected)
                    {
                        Console.WriteLine("Connecting to TCP server...");
                        var connected = await tcpClient.ConnectAsync();
                        if (!connected)
                        {
                            logger.LogWarning(
                                "Could not connect to TCP server. Will attempt to reconnect when sending."
                            );
                            Console.WriteLine(
                                "⚠ Could not connect to TCP server. Will retry on send.\n"
                            );
                        }
                        else
                        {
                            Console.WriteLine("✓ Connected to TCP server\n");
                        }
                    }
                }

                // Send the email
                Console.WriteLine("Sending email...");
                var success = await emailService.SendEmailWithRetryAsync(
                    payload,
                    mode: selectedMode
                );

                if (success)
                {
                    Console.WriteLine("✓ Email sent successfully!\n");
                    logger.LogInformation("Email sent successfully to: {To}", payload.To);
                }
                else
                {
                    Console.WriteLine("✗ Failed to send email. Please check logs for details.\n");
                    logger.LogError("Failed to send email");
                }

                // Always return to menu after sending email
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in interactive mode");
                Console.WriteLine($"Error: {ex.Message}\n");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey(true);
                Console.WriteLine();
            }
        }
    }

    static async Task<(EmailPayload?, bool)> GetWelcomeEmailAsync(string defaultRecipient)
    {
        Console.WriteLine("\n--- Welcome Email ---");
        Console.WriteLine("(Type 'back' or 'cancel' to return to menu)");
        Console.Write($"Recipient email (default: {defaultRecipient}): ");
        var to = Console.ReadLine()?.Trim();
        if (
            to?.Equals("back", StringComparison.OrdinalIgnoreCase) == true
            || to?.Equals("cancel", StringComparison.OrdinalIgnoreCase) == true
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(to))
        {
            to = defaultRecipient;
        }

        return (
            new EmailPayload
            {
                To = to,
                Subject = "Welcome to Our Service!",
                Html =
                    @"
                <html>
                <body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;"">
                    <h1 style=""color: #333;"">Welcome!</h1>
                    <p>Thank you for joining our service. We're excited to have you on board.</p>
                    <p>Here's what you can do next:</p>
                    <ul>
                        <li>Complete your profile</li>
                        <li>Explore our features</li>
                        <li>Get started with your first task</li>
                    </ul>
                    <p>If you have any questions, feel free to reach out to our support team.</p>
                    <p>Best regards,<br>The Team</p>
                </body>
                </html>",
                Text =
                    "Welcome! Thank you for joining our service. We're excited to have you on board. If you have any questions, feel free to reach out to our support team.",
                Priority = "normal",
            },
            true
        );
    }

    static async Task<(EmailPayload?, bool)> GetOrderConfirmationEmailAsync(string defaultRecipient)
    {
        Console.WriteLine("\n--- Order Confirmation Email ---");
        Console.WriteLine("(Type 'back' or 'cancel' to return to menu)");
        Console.Write($"Recipient email (default: {defaultRecipient}): ");
        var to = Console.ReadLine()?.Trim();
        if (
            to?.Equals("back", StringComparison.OrdinalIgnoreCase) == true
            || to?.Equals("cancel", StringComparison.OrdinalIgnoreCase) == true
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(to))
        {
            to = defaultRecipient;
        }

        Console.Write("Order ID (default: ORD-12345, or 'back' to return): ");
        var orderId = Console.ReadLine()?.Trim();
        if (
            orderId.Equals("back", StringComparison.OrdinalIgnoreCase)
            || orderId.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(orderId))
        {
            orderId = "ORD-12345";
        }

        Console.Write("Customer Name (default: John Doe, or 'back' to return): ");
        var customerName = Console.ReadLine()?.Trim();
        if (
            customerName.Equals("back", StringComparison.OrdinalIgnoreCase)
            || customerName.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(customerName))
        {
            customerName = "John Doe";
        }

        return (
            new EmailPayload
            {
                To = to,
                Subject = $"Order Confirmation - {orderId}",
                Html =
                    $@"
                <html>
                <body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;"">
                    <h1 style=""color: #28a745;"">Order Confirmed!</h1>
                    <p>Dear {customerName},</p>
                    <p>Thank you for your order. Your order <strong>{orderId}</strong> has been confirmed and is being processed.</p>
                    <p>We'll send you a tracking number once your order ships.</p>
                    <p>Order Details:</p>
                    <ul>
                        <li>Order ID: {orderId}</li>
                        <li>Status: Confirmed</li>
                        <li>Estimated Delivery: 3-5 business days</li>
                    </ul>
                    <p>If you have any questions about your order, please contact us.</p>
                    <p>Thank you for shopping with us!</p>
                </body>
                </html>",
                Text =
                    $"Order Confirmed! Dear {customerName}, your order {orderId} has been confirmed and is being processed. We'll send you a tracking number once your order ships.",
                Priority = "normal",
            },
            true
        );
    }

    static async Task<(EmailPayload?, bool)> GetPasswordResetEmailAsync(string defaultRecipient)
    {
        Console.WriteLine("\n--- Password Reset Email ---");
        Console.WriteLine("(Type 'back' or 'cancel' to return to menu)");
        Console.Write($"Recipient email (default: {defaultRecipient}): ");
        var to = Console.ReadLine()?.Trim();
        if (
            to?.Equals("back", StringComparison.OrdinalIgnoreCase) == true
            || to?.Equals("cancel", StringComparison.OrdinalIgnoreCase) == true
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(to))
        {
            to = defaultRecipient;
        }

        Console.Write("Reset Token (default: abc123xyz, or 'back' to return): ");
        var token = Console.ReadLine()?.Trim();
        if (
            token.Equals("back", StringComparison.OrdinalIgnoreCase)
            || token.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            token = "abc123xyz";
        }

        return (
            new EmailPayload
            {
                To = to,
                Subject = "Password Reset Request",
                Html =
                    $@"
                <html>
                <body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;"">
                    <h1 style=""color: #dc3545;"">Password Reset</h1>
                    <p>You requested to reset your password.</p>
                    <p>Click the button below to reset your password:</p>
                    <p style=""text-align: center; margin: 30px 0;"">
                        <a href=""https://app.example.com/reset?token={token}"" 
                           style=""background-color: #007bff; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block;"">
                            Reset Password
                        </a>
                    </p>
                    <p>Or copy and paste this link into your browser:</p>
                    <p style=""color: #666; font-size: 12px; word-break: break-all;"">https://app.example.com/reset?token={token}</p>
                    <p><strong>This link will expire in 1 hour.</strong></p>
                    <p>If you didn't request this password reset, please ignore this email.</p>
                </body>
                </html>",
                Text =
                    $"Password Reset: Visit https://app.example.com/reset?token={token} to reset your password. This link will expire in 1 hour. If you didn't request this, please ignore this email.",
                Priority = "high",
            },
            true
        );
    }

    static async Task<(EmailPayload?, bool)> GetNewsletterEmailAsync(string defaultRecipient)
    {
        Console.WriteLine("\n--- Newsletter Email ---");
        Console.WriteLine("(Type 'back' or 'cancel' to return to menu)");
        Console.Write($"Recipient email (default: {defaultRecipient}): ");
        var to = Console.ReadLine()?.Trim();
        if (
            to?.Equals("back", StringComparison.OrdinalIgnoreCase) == true
            || to?.Equals("cancel", StringComparison.OrdinalIgnoreCase) == true
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(to))
        {
            to = defaultRecipient;
        }

        return (
            new EmailPayload
            {
                To = to,
                Subject = "Monthly Newsletter - Latest Updates",
                Html =
                    @"
                <html>
                <body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;"">
                    <h1 style=""color: #333;"">Monthly Newsletter</h1>
                    <h2>What's New This Month</h2>
                    <p>We've been working hard to bring you exciting new features and improvements.</p>
                    <h3>New Features</h3>
                    <ul>
                        <li>Enhanced dashboard with better analytics</li>
                        <li>Improved mobile app experience</li>
                        <li>New collaboration tools</li>
                    </ul>
                    <h3>Tips & Tricks</h3>
                    <p>Did you know you can now export your data directly to Excel? Check out our updated documentation.</p>
                    <h3>Upcoming Events</h3>
                    <p>Join us for our next webinar on productivity tips. Register now!</p>
                    <p>Thank you for being part of our community!</p>
                </body>
                </html>",
                Text =
                    "Monthly Newsletter - What's New: Enhanced dashboard, improved mobile app, new collaboration tools. Join us for our next webinar on productivity tips!",
                Priority = "normal",
            },
            true
        );
    }

    static async Task<(EmailPayload?, bool)> GetBulkAnnouncementEmailAsync(string defaultRecipient)
    {
        Console.WriteLine("\n--- Bulk Announcement Email ---");
        Console.WriteLine("(Type 'back' or 'cancel' to return to menu)");
        Console.Write($"Recipients (comma-separated emails, default: {defaultRecipient}): ");
        var toInput = Console.ReadLine()?.Trim();
        if (
            toInput?.Equals("back", StringComparison.OrdinalIgnoreCase) == true
            || toInput?.Equals("cancel", StringComparison.OrdinalIgnoreCase) == true
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(toInput))
        {
            toInput = defaultRecipient;
        }

        var recipients = toInput.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        object to = recipients.Length == 1 ? recipients[0] : recipients;

        return (
            new EmailPayload
            {
                To = to,
                Subject = "Important System Maintenance Announcement",
                Html =
                    @"
                <html>
                <body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;"">
                    <h1 style=""color: #ffc107;"">System Maintenance Notice</h1>
                    <p>We will be performing scheduled system maintenance on our platform.</p>
                    <p><strong>Maintenance Window:</strong> Saturday, 2:00 AM - 4:00 AM EST</p>
                    <p>During this time, the service may be temporarily unavailable. We apologize for any inconvenience and appreciate your patience.</p>
                    <p>If you have any questions or concerns, please contact our support team.</p>
                    <p>Thank you for your understanding.</p>
                </body>
                </html>",
                Text =
                    "System Maintenance Notice: Scheduled maintenance on Saturday, 2:00 AM - 4:00 AM EST. Service may be temporarily unavailable during this time.",
                Priority = "high",
            },
            true
        );
    }

    static async Task<(EmailPayload?, bool)> GetCustomEmailAsync(string defaultRecipient)
    {
        Console.WriteLine("\n--- Custom Email ---");
        Console.WriteLine("(Type 'back' or 'cancel' to return to menu)");

        Console.Write(
            $"To (email address, or comma-separated for multiple, default: {defaultRecipient}): "
        );
        var toInput = Console.ReadLine()?.Trim();
        if (
            toInput?.Equals("back", StringComparison.OrdinalIgnoreCase) == true
            || toInput?.Equals("cancel", StringComparison.OrdinalIgnoreCase) == true
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(toInput))
        {
            toInput = defaultRecipient;
        }

        var recipients = toInput.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        object to = recipients.Length == 1 ? recipients[0] : recipients;

        Console.Write("Subject (or 'back' to return): ");
        var subject = Console.ReadLine()?.Trim();
        if (
            string.IsNullOrWhiteSpace(subject)
            || subject.Equals("back", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            if (
                subject.Equals("back", StringComparison.OrdinalIgnoreCase)
                || subject.Equals("cancel", StringComparison.OrdinalIgnoreCase)
            )
            {
                return (null, false);
            }
            Console.WriteLine("Subject is required.");
            return (null, true);
        }

        Console.Write("Text content (plain text, or 'back' to return): ");
        var text = Console.ReadLine()?.Trim();
        if (
            text.Equals("back", StringComparison.OrdinalIgnoreCase)
            || text.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (null, false);
        }

        Console.Write("HTML content (optional, press Enter to skip, or 'back' to return): ");
        var html = Console.ReadLine()?.Trim();
        if (
            html.Equals("back", StringComparison.OrdinalIgnoreCase)
            || html.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (null, false);
        }
        if (string.IsNullOrWhiteSpace(html))
        {
            html = null;
        }

        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(html))
        {
            Console.WriteLine("Error: Either text or HTML content is required.");
            return (null, true);
        }

        Console.Write("Priority (normal/high/low, default: normal, or 'back' to return): ");
        var priorityInput = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (
            priorityInput.Equals("back", StringComparison.OrdinalIgnoreCase)
            || priorityInput.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (null, false);
        }
        var priority =
            string.IsNullOrWhiteSpace(priorityInput)
            || (
                !priorityInput.Equals("high", StringComparison.OrdinalIgnoreCase)
                && !priorityInput.Equals("low", StringComparison.OrdinalIgnoreCase)
            )
                ? "normal"
                : priorityInput;

        return (
            new EmailPayload
            {
                To = to,
                Subject = subject,
                Text = text,
                Html = html,
                Priority = priority,
            },
            true
        );
    }
}
