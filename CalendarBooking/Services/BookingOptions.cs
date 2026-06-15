namespace CalendarBooking.Services;

/// <summary>
/// Booking-related configuration, bound from the "Booking" section of configuration.
/// </summary>
public class BookingOptions
{
    /// <summary>
    /// The maximum number of pending approval requests a single user may have open at
    /// once. Owner-initiated (on-behalf-of) bookings are confirmed immediately and do not
    /// count toward this cap.
    /// </summary>
    public int MaxPendingRequests { get; set; } = 5;
}
