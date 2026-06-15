using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Background worker that pushes confirmed bookings to connected external calendars
/// out-of-band, so a slow calendar API never blocks or rolls back a booking. Each booking is
/// pushed at most once (tracked by Booking.CalendarSyncedUtc). It pushes for BOTH parties;
/// the provider client no-ops for whoever hasn't connected their calendar. Idle when no
/// external providers are configured.
/// </summary>
public class CalendarSyncDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarSyncDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<CalendarSyncService>();

                if (sync.HasProviders)
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;

                    await PushNewBookingsAsync(db, sync, now, stoppingToken);
                    await RemoveCancelledBookingEventsAsync(db, sync, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Calendar sync dispatch failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Push confirmed bookings not yet synced, recording each created event so it can be
    // removed later. Pushes to both parties; the client no-ops for whoever isn't connected.
    private static async Task PushNewBookingsAsync(AppDbContext db, CalendarSyncService sync, DateTime now, CancellationToken ct)
    {
        var due = await (
            from b in db.Bookings
            join s in db.AvailabilitySlots on b.SlotId equals s.Id
            where b.Status == BookingStatus.Confirmed && b.CalendarSyncedUtc == null && s.EndUtc > now
            select b)
            .Take(50)
            .ToListAsync(ct);

        foreach (var booking in due)
        {
            foreach (var userId in new[] { booking.OwnerId, booking.AttendeeId })
            {
                var pushed = await sync.PushBookingAsync(userId, booking, ct);
                foreach (var ev in pushed)
                {
                    db.ExternalCalendarEvents.Add(new ExternalCalendarEvent
                    {
                        Id = Guid.NewGuid(),
                        BookingId = booking.Id,
                        UserId = userId,
                        Provider = ev.Provider,
                        EventId = ev.EventId,
                        CreatedUtc = now,
                    });
                }
            }
            booking.CalendarSyncedUtc = now;
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    // Delete external events for bookings that have since been cancelled.
    private static async Task RemoveCancelledBookingEventsAsync(AppDbContext db, CalendarSyncService sync, CancellationToken ct)
    {
        var orphaned = await (
            from e in db.ExternalCalendarEvents
            join b in db.Bookings on e.BookingId equals b.Id
            where b.Status == BookingStatus.Cancelled
            select e)
            .Take(50)
            .ToListAsync(ct);

        foreach (var ev in orphaned)
        {
            await sync.DeleteEventAsync(ev.Provider, ev.UserId, ev.EventId, ct);
            db.ExternalCalendarEvents.Remove(ev);
        }

        if (orphaned.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
