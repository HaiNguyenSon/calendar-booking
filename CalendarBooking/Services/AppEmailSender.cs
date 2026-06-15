namespace CalendarBooking.Services;

/// <summary>
/// Sends an application email. The real SMTP implementation (Mailtrap in dev, a provider
/// like Brevo/SendGrid in prod) is wired up later; until then <see cref="LoggingAppEmailSender"/>
/// just logs, which is enough to develop and test the rest of the pipeline.
/// </summary>
public interface IAppEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

/// <summary>Logs the email instead of sending it. Swap for an SMTP sender in production.</summary>
public class LoggingAppEmailSender(ILogger<LoggingAppEmailSender> logger) : IAppEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL → {To} | {Subject} | {Body}", toEmail, subject, body);
        return Task.CompletedTask;
    }
}
