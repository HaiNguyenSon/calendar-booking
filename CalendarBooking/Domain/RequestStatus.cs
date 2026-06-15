namespace CalendarBooking.Domain;

/// <summary>
/// Lifecycle of a <see cref="BookingRequest"/> against an approval-required slot.
/// </summary>
public enum RequestStatus
{
    /// <summary>Waiting for the slot owner to approve or decline. Counts toward the pending cap.</summary>
    Pending = 0,

    /// <summary>Owner approved it; a <see cref="Booking"/> was created from it.</summary>
    Approved = 1,

    /// <summary>Owner declined it; the slot was released.</summary>
    Declined = 2,

    /// <summary>Auto-declined because a competing request for the same slot was approved first.</summary>
    Superseded = 3
}
