namespace CalendarBooking.Domain;

/// <summary>
/// How a slot is claimed when someone books it.
/// </summary>
public enum SlotType
{
    /// <summary>Booking the slot confirms it immediately — no owner approval needed.</summary>
    Instant = 0,

    /// <summary>Booking the slot creates a pending request the owner must approve.</summary>
    ApprovalRequired = 1
}
