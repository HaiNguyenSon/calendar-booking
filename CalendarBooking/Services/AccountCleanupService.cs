using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Closes a user account. Because every foreign key to a user is DeleteBehavior.Restrict
/// (we never cascade-delete booking history), a user can't simply be removed while any
/// booking references them. So this ANONYMIZES the account — scrubbing personal data and
/// disabling login — while keeping the row so historical bookings stay intact. It also
/// tidies up the user's still-active artifacts first.
/// </summary>
public class AccountCleanupService(AppDbContext db, NotificationService notifications)
{
    public readonly record struct Result(bool Ok, string? Error)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Success() => new(true, null);
    }

    public async Task<Result> CloseAccountAsync(string userId, DateTime nowUtc, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Fail("User not found.");
        }

        // 1. Decline this user's still-pending requests.
        var pending = await db.BookingRequests
            .Where(r => r.RequesterId == userId && r.Status == RequestStatus.Pending)
            .ToListAsync(ct);
        foreach (var request in pending)
        {
            request.Status = RequestStatus.Declined;
        }

        // 2. Cancel their future confirmed bookings (as owner or attendee), free the slots,
        //    and let the other party know.
        var futureBookings = await (
            from b in db.Bookings
            join s in db.AvailabilitySlots on b.SlotId equals s.Id
            where b.Status == BookingStatus.Confirmed
                  && (b.OwnerId == userId || b.AttendeeId == userId)
                  && s.EndUtc > nowUtc
            select new { Booking = b, Slot = s })
            .ToListAsync(ct);
        foreach (var item in futureBookings)
        {
            item.Booking.Status = BookingStatus.Cancelled;
            item.Booking.CancelledById = userId;
            item.Booking.CancelledUtc = nowUtc;
            item.Booking.CancellationReason = "Account closed.";
            item.Slot.IsBooked = false;

            var otherPartyId = item.Booking.OwnerId == userId ? item.Booking.AttendeeId : item.Booking.OwnerId;
            notifications.Queue(otherPartyId,
                "A booking was cancelled because the other person closed their account.", nowUtc);
        }

        // 3. Delete future slots that no booking references (a slot referenced by any booking
        //    — even a cancelled one kept for history — can't be deleted under the Restrict FK).
        var deletableSlots = await db.AvailabilitySlots
            .Where(s => s.OwnerId == userId && s.EndUtc > nowUtc && !db.Bookings.Any(b => b.SlotId == s.Id))
            .ToListAsync(ct);
        db.AvailabilitySlots.RemoveRange(deletableSlots);

        // 4. Delete the user's notifications.
        var notes = await db.Notifications.Where(n => n.UserId == userId).ToListAsync(ct);
        db.Notifications.RemoveRange(notes);

        // 5. Anonymize the account and disable login. The row is kept so historical bookings
        //    (which reference this user) remain valid.
        var tag = Guid.NewGuid().ToString("N")[..8];
        user.Nickname = $"deleted-{tag}";
        user.Description = null;
        user.Email = $"deleted-{tag}@example.invalid";
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.UserName = user.Email;
        user.NormalizedUserName = user.NormalizedEmail;
        user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;

        await db.SaveChangesAsync(ct);
        notifications.PushQueued();
        return Result.Success();
    }
}
