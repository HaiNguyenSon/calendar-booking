using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;

namespace CalendarBooking.Services;

/// <summary>Twilio settings, bound from "Sms:Twilio". When unset, the app logs SMS instead of sending.</summary>
public class SmsOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string FromNumber { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken) && !string.IsNullOrWhiteSpace(FromNumber);
}

/// <summary>
/// Sends an SMS. <see cref="TwilioSmsSender"/> is used when Twilio is configured; otherwise
/// <see cref="LoggingSmsSender"/> logs the message so the verification flow is usable in dev
/// (read the code from the logs) without a real provider.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string toNumber, string message, CancellationToken ct = default);
}

/// <summary>Logs the SMS instead of sending it. Used when Twilio isn't configured.</summary>
public class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string toNumber, string message, CancellationToken ct = default)
    {
        logger.LogInformation("SMS → {To}: {Message}", toNumber, message);
        return Task.CompletedTask;
    }
}

/// <summary>Sends SMS via Twilio's REST API over HTTP (no SDK). Registered only when configured.</summary>
public class TwilioSmsSender(
    IOptions<SmsOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<TwilioSmsSender> logger) : ISmsSender
{
    private readonly SmsOptions options = options.Value;

    public async Task SendAsync(string toNumber, string message, CancellationToken ct = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["From"] = options.FromNumber,
                ["To"] = toNumber,
                ["Body"] = message,
            });

            var url = $"https://api.twilio.com/2010-04-01/Accounts/{options.AccountSid}/Messages.json";
            var response = await client.PostAsync(url, form, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Twilio SMS to {To} failed: {Status}", toNumber, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Best-effort; the code is also derivable by re-sending.
            logger.LogError(ex, "Sending SMS to {To} failed.", toNumber);
        }
    }
}
