using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Background worker that reminds both parties shortly before a confirmed booking starts.
/// Each booking is reminded at most once (tracked by <c>Booking.ReminderSentUtc</c>).
/// </summary>
public class ReminderService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

                var now = DateTime.UtcNow;
                var soon = now + LeadTime;

                var due = await (
                    from b in db.Bookings
                    join s in db.AvailabilitySlots on b.SlotId equals s.Id
                    where b.Status == BookingStatus.Confirmed
                          && b.ReminderSentUtc == null
                          && s.StartUtc > now && s.StartUtc <= soon
                    select b)
                    .ToListAsync(stoppingToken);

                foreach (var booking in due)
                {
                    notifications.Queue(booking.OwnerId, "Reminder: you have a booking starting within the hour.", now);
                    notifications.Queue(booking.AttendeeId, "Reminder: you have a booking starting within the hour.", now);
                    booking.ReminderSentUtc = now;
                }

                if (due.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    notifications.PushQueued();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminder run failed.");
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
