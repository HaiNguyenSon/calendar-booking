namespace CalendarBooking.Domain;

/// <summary>
/// A confirmed reservation against a slot. The three user references look similar but
/// answer different questions, which matters for the "owner books a client" feature:
/// <list type="bullet">
///   <item><c>OwnerId</c> — who is being booked / hosting (the slot owner).</item>
///   <item><c>AttendeeId</c> — who is attending; whose calendar this shows up on.</item>
///   <item><c>CreatedById</c> — who created it (distinguishes self-booked from owner-initiated).</item>
/// </list>
/// </summary>
public class Booking
{
    public Guid Id { get; set; }

    /// <summary>
    /// The claimed slot. A unique index on this column is the database-level guarantee
    /// that one slot maps to at most one (active) booking — our double-booking guard.
    /// </summary>
    public Guid SlotId { get; set; }
    public AvailabilitySlot? Slot { get; set; }

    /// <summary>The slot owner / host.</summary>
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    /// <summary>The person attending; the booking appears on their calendar.</summary>
    public string AttendeeId { get; set; } = string.Empty;
    public ApplicationUser? Attendee { get; set; }

    /// <summary>Who created the booking (the attendee for self-booking, the owner for on-behalf-of).</summary>
    public string CreatedById { get; set; } = string.Empty;
    public ApplicationUser? CreatedBy { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

    public DateTime CreatedUtc { get; set; }

    // --- Cancellation (either party may cancel; reason is required and shown to the other party) ---

    public string? CancelledById { get; set; }
    public ApplicationUser? CancelledBy { get; set; }

    public DateTime? CancelledUtc { get; set; }

    /// <summary>Required when cancelling; max 250 chars. Delivered to the other party.</summary>
    public string? CancellationReason { get; set; }
}
