using CalendarBooking.Data;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Background worker that emails notifications out-of-band. It picks up notifications whose
/// <c>EmailedUtc</c> is null, sends each via <see cref="IAppEmailSender"/>, and stamps them
/// so they're emailed exactly once. Decoupling email from the booking transaction means a
/// slow/failed mail server never blocks or rolls back a booking.
/// </summary>
public class EmailDispatcherService(
    IServiceScopeFactory scopeFactory,
    IAppEmailSender email,
    ILogger<EmailDispatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var pending = await (
                    from n in db.Notifications
                    where n.EmailedUtc == null
                    join u in db.Users on n.UserId equals u.Id
                    select new { Notification = n, u.Email })
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var item in pending)
                {
                    if (!string.IsNullOrEmpty(item.Email))
                    {
                        await email.SendAsync(item.Email, "CalendarBooking", item.Notification.Message, stoppingToken);
                    }
                    item.Notification.EmailedUtc = DateTime.UtcNow;
                }

                if (pending.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Email dispatch run failed.");
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
