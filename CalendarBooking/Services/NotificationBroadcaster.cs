namespace CalendarBooking.Services;

/// <summary>
/// In-memory pub/sub for live notification updates. A notification can be created on one
/// user's request/circuit (or a background job) but needs to refresh a DIFFERENT user's
/// open page. This singleton raises an event keyed by recipient id; the recipient's
/// interactive components subscribe and re-render — the update is pushed to their browser
/// over the Blazor Server SignalR circuit they already hold.
///
/// Single-process only; a multi-server deployment would back this with a SignalR backplane.
/// </summary>
public class NotificationBroadcaster
{
    /// <summary>Raised with the recipient's user id when they have a new notification.</summary>
    public event Action<string>? Changed;

    public void NotifyChanged(string userId) => Changed?.Invoke(userId);
}
