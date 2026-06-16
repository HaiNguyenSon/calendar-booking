namespace CalendarBooking.Domain;

/// <summary>
/// A block of time a user opens on their own calendar for others to book.
/// All times are stored in UTC and converted to local only at display.
/// </summary>
public class AvailabilitySlot
{
    public Guid Id { get; set; }

    /// <summary>The user who owns this slot (whose calendar it lives on).</summary>
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    /// <summary>Start of the slot, in UTC.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>End of the slot, in UTC.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Whether booking this slot confirms instantly or needs owner approval.</summary>
    public SlotType SlotType { get; set; }

    /// <summary>
    /// True once the slot is taken. Setting this is done inside a transaction together
    /// with creating the Booking, so two people cannot claim the same slot. A unique
    /// index on Booking.SlotId is the ultimate guard against double-booking.
    /// </summary>
    public bool IsBooked { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// If this slot was materialized from a standing <see cref="WeeklyAvailabilityRule"/>, its id
    /// (a soft tag, not an FK). Lets deleting a rule clean up the future unbooked slots it made.
    /// Null for one-off / manually created slots.
    /// </summary>
    public Guid? GeneratedByRuleId { get; set; }
}
