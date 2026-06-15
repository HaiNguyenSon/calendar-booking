using CalendarBooking.Domain;

namespace CalendarBooking.Services;

/// <summary>A busy time pulled from an external calendar, used to block availability.</summary>
public record BusyInterval(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// A connection to one external calendar provider (Google Calendar, Microsoft Graph).
/// Implementations are registered in DI; <see cref="CalendarSyncService"/> fans out to all
/// of them. None are registered yet — implementing one (reusing the provider's existing
/// OAuth login, with calendar scopes and an offline refresh token) is the Phase 7 work.
/// See docs/EXTERNAL-SYNC.md.
/// </summary>
public interface IExternalCalendarClient
{
    /// <summary>Provider name, e.g. "Google".</summary>
    string Provider { get; }

    /// <summary>
    /// Push a confirmed booking out to the user's external calendar. Returns the created
    /// event's id (so it can be deleted later), or null if nothing was created (e.g. the
    /// user hasn't connected this provider).
    /// </summary>
    Task<string?> PushBookingAsync(string userId, Booking booking, CancellationToken ct = default);

    /// <summary>Delete a previously-pushed event from the user's external calendar.</summary>
    Task DeleteEventAsync(string userId, string eventId, CancellationToken ct = default);

    /// <summary>Read the user's busy intervals in a window, to block their availability.</summary>
    Task<IReadOnlyList<BusyInterval>> GetBusyIntervalsAsync(
        string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
