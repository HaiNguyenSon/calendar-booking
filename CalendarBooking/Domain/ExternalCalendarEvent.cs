namespace CalendarBooking.Domain;

/// <summary>
/// Records that a booking was pushed to a user's external calendar as a specific event, so
/// the event can be deleted later (e.g. when the booking is cancelled). One row per
/// (booking, user, provider) — a booking is pushed to both the owner's and attendee's
/// calendars.
/// </summary>
public class ExternalCalendarEvent
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    /// <summary>Whose calendar the event lives on.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's event id, used to delete it.</summary>
    public string EventId { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
