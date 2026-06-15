using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Creates and reads in-app notifications. <see cref="Queue"/> only adds the row to the
/// current DbContext (it does NOT save), so callers include it in their own atomic
/// SaveChanges — a notification is written if and only if the triggering action committed.
/// Emailing happens out-of-band (see the background email dispatcher), keyed off
/// <see cref="Notification.EmailedUtc"/>.
/// </summary>
public class NotificationService(AppDbContext db, NotificationBroadcaster broadcaster)
{
    private readonly HashSet<string> queuedRecipients = new();

    /// <summary>Add a notification to the context for saving alongside the caller's changes.</summary>
    public void Queue(string userId, string message, DateTime nowUtc)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Message = message,
            CreatedUtc = nowUtc,
        });
        queuedRecipients.Add(userId);
    }

    /// <summary>
    /// Call AFTER a successful SaveChanges: pushes a live refresh to each queued recipient's
    /// open pages (over their Blazor circuit) and resets the queue.
    /// </summary>
    public void PushQueued()
    {
        foreach (var userId in queuedRecipients)
        {
            broadcaster.NotifyChanged(userId);
        }
        queuedRecipients.Clear();
    }

    public async Task<IReadOnlyList<Notification>> GetRecentAsync(string userId, int take = 50, CancellationToken ct = default)
    {
        return await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default)
    {
        return await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadUtc == null, ct);
    }

    /// <summary>Mark all of a user's unread notifications as read.</summary>
    public async Task MarkAllReadAsync(string userId, DateTime nowUtc, CancellationToken ct = default)
    {
        var unread = await db.Notifications
            .Where(n => n.UserId == userId && n.ReadUtc == null)
            .ToListAsync(ct);
        foreach (var n in unread)
        {
            n.ReadUtc = nowUtc;
        }
        await db.SaveChangesAsync(ct);
    }
}
