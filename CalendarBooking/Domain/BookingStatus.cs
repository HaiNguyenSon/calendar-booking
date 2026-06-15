namespace CalendarBooking.Domain;

/// <summary>
/// State of a confirmed reservation. Cancelled bookings are kept (not deleted)
/// to preserve relationship history and for audit.
/// </summary>
public enum BookingStatus
{
    Confirmed = 0,
    Cancelled = 1
}
