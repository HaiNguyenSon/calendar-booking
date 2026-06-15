namespace CalendarBooking.Domain;

/// <summary>
/// A pending ask to book an approval-required slot. The owner approves one (which
/// becomes a <see cref="Booking"/>) and any competing requests for the same slot are
/// auto-declined as <see cref="RequestStatus.Superseded"/>.
/// </summary>
public class BookingRequest
{
    public Guid Id { get; set; }

    /// <summary>The slot being requested.</summary>
    public Guid SlotId { get; set; }
    public AvailabilitySlot? Slot { get; set; }

    /// <summary>The user asking to book the slot.</summary>
    public string RequesterId { get; set; } = string.Empty;
    public ApplicationUser? Requester { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public DateTime CreatedUtc { get; set; }
}
