namespace CalendarBooking.Services;

/// <summary>
/// SMTP settings, bound from the "Email:Smtp" configuration section. When <see cref="Host"/>
/// is empty the app falls back to the logging email sender, so it runs without SMTP.
/// </summary>
public class EmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>The From address; falls back to <see cref="User"/> when empty.</summary>
    public string From { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
