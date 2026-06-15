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

                    var due = await (
                        from b in db.Bookings
                        join s in db.AvailabilitySlots on b.SlotId equals s.Id
                        where b.Status == BookingStatus.Confirmed && b.CalendarSyncedUtc == null && s.EndUtc > now
                        select b)
                        .Take(50)
                        .ToListAsync(stoppingToken);

                    foreach (var booking in due)
                    {
                        await sync.PushBookingAsync(booking.OwnerId, booking, stoppingToken);
                        await sync.PushBookingAsync(booking.AttendeeId, booking, stoppingToken);
                        booking.CalendarSyncedUtc = now;
                    }

                    if (due.Count > 0)
                    {
                        await db.SaveChangesAsync(stoppingToken);
                    }
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
}
