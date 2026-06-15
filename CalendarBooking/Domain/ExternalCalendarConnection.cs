namespace CalendarBooking.Domain;

/// <summary>
/// A user's connection to an external calendar provider (currently Google). Holds the
/// long-lived refresh token we use to act on their calendar offline. The token is stored
/// ENCRYPTED (ASP.NET Core Data Protection) — never in plaintext.
/// </summary>
public class ExternalCalendarConnection
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>Provider name, e.g. "Google".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Data-Protection-encrypted refresh token. Decrypt only when calling the API.</summary>
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    public DateTime ConnectedUtc { get; set; }
}
