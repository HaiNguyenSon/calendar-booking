namespace CalendarBooking.Domain;

/// <summary>
/// An in-app message for a single user (new request, approved, declined, slot taken,
/// booked on your behalf, cancelled, reminder). Written in the same transaction as the
/// event that caused it, so a notification never goes missing if the action succeeded.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    /// <summary>The recipient.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    /// <summary>Set when the user has seen it (drives the unread badge).</summary>
    public DateTime? ReadUtc { get; set; }

    /// <summary>
    /// Set once the email channel has delivered this notification. Null means a background
    /// dispatcher still needs to email it. Decouples emailing from the booking transaction.
    /// </summary>
    public DateTime? EmailedUtc { get; set; }
}
