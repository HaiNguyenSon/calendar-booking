using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Subscriptions between users: subscribe to someone by their public code, unsubscribe, and
/// list who you follow / count who follows you. A user can subscribe to many others; the
/// relationship is a row in the Subscriptions join table.
/// </summary>
public class SubscriptionService(AppDbContext db)
{
    public readonly record struct Result(bool Ok, string? Error)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Success() => new(true, null);
    }

    /// <summary>Subscribe to the user identified by <paramref name="targetPublicId"/>. Idempotent.</summary>
    public async Task<Result> SubscribeAsync(string subscriberId, string targetPublicId, DateTime nowUtc, CancellationToken ct = default)
    {
        var code = targetPublicId.Trim().ToUpperInvariant();
        var target = await db.Users.FirstOrDefaultAsync(u => u.PublicId == code, ct);
        if (target is null)
        {
            return Result.Fail("No user with that code.");
        }

        if (target.Id == subscriberId)
        {
            return Result.Fail("You can't subscribe to yourself.");
        }

        var already = await db.Subscriptions.AnyAsync(
            s => s.SubscriberId == subscriberId && s.TargetId == target.Id, ct);
        if (already)
        {
            return Result.Success(); // idempotent
        }

        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriberId = subscriberId,
            TargetId = target.Id,
            CreatedUtc = nowUtc,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index lost a race — already subscribed, which is fine.
            return Result.Success();
        }

        return Result.Success();
    }

    public async Task<Result> UnsubscribeAsync(string subscriberId, string targetId, CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.SubscriberId == subscriberId && s.TargetId == targetId, ct);
        if (subscription is null)
        {
            return Result.Success(); // already not subscribed
        }

        db.Subscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<bool> IsSubscribedAsync(string subscriberId, string targetId, CancellationToken ct = default) =>
        db.Subscriptions.AnyAsync(s => s.SubscriberId == subscriberId && s.TargetId == targetId, ct);

    /// <summary>The users this subscriber follows, by nickname/code, newest first.</summary>
    public async Task<IReadOnlyList<SubscriptionView>> GetSubscriptionsAsync(string subscriberId, CancellationToken ct = default)
    {
        return await (
            from s in db.Subscriptions
            join u in db.Users on s.TargetId equals u.Id
            where s.SubscriberId == subscriberId
            orderby s.CreatedUtc descending
            select new SubscriptionView(u.Id, u.Nickname, u.PublicId))
            .ToListAsync(ct);
    }

    /// <summary>How many users subscribe to this target.</summary>
    public Task<int> GetSubscriberCountAsync(string targetId, CancellationToken ct = default) =>
        db.Subscriptions.CountAsync(s => s.TargetId == targetId, ct);

    /// <summary>
    /// Subscribers gained since the target last checked (SubscribersSeenUtc), in subscribe
    /// order. Shown as a login digest, then cleared via <see cref="MarkSubscribersSeenAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<NewSubscriber>> GetNewSubscribersAsync(string targetId, CancellationToken ct = default)
    {
        var seenUtc = await db.Users
            .Where(u => u.Id == targetId)
            .Select(u => u.SubscribersSeenUtc)
            .FirstOrDefaultAsync(ct);

        return await (
            from s in db.Subscriptions
            join u in db.Users on s.SubscriberId equals u.Id
            where s.TargetId == targetId && (seenUtc == null || s.CreatedUtc > seenUtc)
            orderby s.CreatedUtc
            select new NewSubscriber(u.Nickname, s.CreatedUtc))
            .ToListAsync(ct);
    }

    /// <summary>Advance the digest high-water mark so already-shown subscribers aren't shown again.</summary>
    public async Task MarkSubscribersSeenAsync(string targetId, DateTime seenUtc, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == targetId, ct);
        if (user is null)
        {
            return;
        }

        // Only move forward.
        if (user.SubscribersSeenUtc is null || seenUtc > user.SubscribersSeenUtc)
        {
            user.SubscribersSeenUtc = seenUtc;
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>A subscriber shown in the "new subscribers" digest.</summary>
public record NewSubscriber(string Nickname, DateTime SubscribedUtc);

/// <summary>A user this subscriber follows.</summary>
public record SubscriptionView(string UserId, string Nickname, string PublicId);
