namespace CalendarBooking.Domain;

/// <summary>
/// A standing weekly availability pattern: on the chosen weekdays, within a daily window, the
/// owner is bookable in slots of a fixed length. A background job keeps materializing it into
/// concrete <see cref="AvailabilitySlot"/>s for a rolling horizon. The window is local
/// time-of-day, so the owner's timezone is stored to convert correctly (incl. DST).
/// </summary>
public class WeeklyAvailabilityRule
{
    public Guid Id { get; set; }

    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    /// <summary>Bit per DayOfWeek (Sunday = 0 … Saturday = 6).</summary>
    public int DaysMask { get; set; }

    /// <summary>Daily window, minutes from local midnight.</summary>
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }

    /// <summary>Length of each generated slot, minutes.</summary>
    public int SlotMinutes { get; set; }

    public SlotType SlotType { get; set; }

    /// <summary>IANA timezone the window is expressed in.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public DateTime CreatedUtc { get; set; }
}
