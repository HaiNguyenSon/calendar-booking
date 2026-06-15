using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CalendarBooking.Services;

/// <summary>
/// Sends an application email. <see cref="SmtpAppEmailSender"/> is used when SMTP is
/// configured (Email:Smtp:Host); otherwise <see cref="LoggingAppEmailSender"/> just logs,
/// which keeps the app runnable in development without a mail server.
/// </summary>
public interface IAppEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

/// <summary>Logs the email instead of sending it. Used when no SMTP host is configured.</summary>
public class LoggingAppEmailSender(ILogger<LoggingAppEmailSender> logger) : IAppEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL → {To} | {Subject} | {Body}", toEmail, subject, body);
        return Task.CompletedTask;
    }
}

/// <summary>Sends real email over SMTP via MailKit. Registered only when SMTP is configured.</summary>
public class SmtpAppEmailSender(IOptions<EmailOptions> options, ILogger<SmtpAppEmailSender> logger) : IAppEmailSender
{
    private readonly EmailOptions options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        var from = string.IsNullOrWhiteSpace(options.From) ? options.User : options.From;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(options.Host, options.Port,
                options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
            if (!string.IsNullOrEmpty(options.User))
            {
                await client.AuthenticateAsync(options.User, options.Password, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            // Swallow so one bad recipient doesn't poison the dispatcher batch. Email is
            // best-effort here; the in-app notification is the durable channel.
            logger.LogError(ex, "Sending email to {To} failed.", toEmail);
        }
    }
}
