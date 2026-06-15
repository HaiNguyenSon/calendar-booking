using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Background worker that expires pending requests once their slot's time has passed —
/// otherwise a request the owner never answered would linger forever and keep counting
/// toward the requester's pending cap. Expired requests are marked Declined and the
/// requester is notified.
/// </summary>
public class StaleRequestService(
    IServiceScopeFactory scopeFactory,
    ILogger<StaleRequestService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

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

                var stale = await (
                    from r in db.BookingRequests
                    join s in db.AvailabilitySlots on r.SlotId equals s.Id
                    where r.Status == RequestStatus.Pending && s.EndUtc <= now
                    select r)
                    .ToListAsync(stoppingToken);

                foreach (var request in stale)
                {
                    request.Status = RequestStatus.Declined;
                    notifications.Queue(request.RequesterId,
                        "A booking request expired because the slot's time passed.", now);
                }

                if (stale.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    notifications.PushQueued();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Stale-request sweep failed.");
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
