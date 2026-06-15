using CalendarBooking.Domain;

namespace CalendarBooking.Services;

/// <summary>
/// Fans booking sync out across whatever external calendar providers are connected. With
/// no <see cref="IExternalCalendarClient"/> registered (the current state) every method is a
/// safe no-op, so the rest of the app can call it unconditionally. One misbehaving provider
/// never breaks the others or the caller.
/// </summary>
public class CalendarSyncService(
    IEnumerable<IExternalCalendarClient> clients,
    ILogger<CalendarSyncService> logger)
{
    private readonly IReadOnlyList<IExternalCalendarClient> clients = clients.ToList();

    /// <summary>True when at least one external provider is connected.</summary>
    public bool HasProviders => clients.Count > 0;

    /// <summary>Push a confirmed booking to every connected provider (best-effort).</summary>
    public async Task PushBookingAsync(string userId, Booking booking, CancellationToken ct = default)
    {
        foreach (var client in clients)
        {
            try
            {
                await client.PushBookingAsync(userId, booking, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pushing booking to {Provider} failed.", client.Provider);
            }
        }
    }

    /// <summary>Aggregate busy intervals across all connected providers for the window.</summary>
    public async Task<IReadOnlyList<BusyInterval>> GetBusyIntervalsAsync(
        string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var all = new List<BusyInterval>();
        foreach (var client in clients)
        {
            try
            {
                all.AddRange(await client.GetBusyIntervalsAsync(userId, fromUtc, toUtc, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reading busy times from {Provider} failed.", client.Provider);
            }
        }
        return all;
    }
}
