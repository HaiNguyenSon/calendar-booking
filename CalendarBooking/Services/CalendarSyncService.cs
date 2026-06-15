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

    /// <summary>
    /// Push a confirmed booking to every connected provider (best-effort). Returns the
    /// (provider, eventId) pairs actually created, so the caller can record them for later
    /// deletion.
    /// </summary>
    public async Task<IReadOnlyList<PushedEvent>> PushBookingAsync(string userId, Booking booking, CancellationToken ct = default)
    {
        var created = new List<PushedEvent>();
        foreach (var client in clients)
        {
            try
            {
                var eventId = await client.PushBookingAsync(userId, booking, ct);
                if (!string.IsNullOrEmpty(eventId))
                {
                    created.Add(new PushedEvent(client.Provider, eventId));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pushing booking to {Provider} failed.", client.Provider);
            }
        }
        return created;
    }

    /// <summary>Delete a previously-pushed event from the named provider (best-effort).</summary>
    public async Task DeleteEventAsync(string provider, string userId, string eventId, CancellationToken ct = default)
    {
        var client = clients.FirstOrDefault(c => c.Provider == provider);
        if (client is null)
        {
            return;
        }

        try
        {
            await client.DeleteEventAsync(userId, eventId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Deleting event from {Provider} failed.", provider);
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

/// <summary>An event created on a provider, identified for later deletion.</summary>
public record PushedEvent(string Provider, string EventId);
