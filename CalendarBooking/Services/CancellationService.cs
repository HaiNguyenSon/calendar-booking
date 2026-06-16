using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// The attendee/owner "my schedule" view and booking cancellation. Either party on a
/// confirmed booking may cancel it, with a required reason; cancelling frees the slot but
/// keeps the booking row (marked Cancelled) so relationship history and audit survive.
/// </summary>
public class CancellationService(AppDbContext db, NotificationService notifications)
{
    public const int MaxReasonLength = 250;

    public readonly record struct Result(bool Ok, string? Error)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Success() => new(true, null);
    }

    /// <summary>
    /// Confirmed, not-yet-ended bookings where the user is the attendee or the owner —
    /// i.e. their upcoming schedule. (Bookings where Attendee = me appear here too, which
    /// is the own-schedule view from Phase 1.)
    /// </summary>
    public async Task<IReadOnlyList<MyBooking>> GetUpcomingBookingsForUserAsync(
        string userId, DateTime nowUtc, CancellationToken ct = default)
    {
        return await (
            from b in db.Bookings
            join s in db.AvailabilitySlots on b.SlotId equals s.Id
            join owner in db.Users on b.OwnerId equals owner.Id
            join attendee in db.Users on b.AttendeeId equals attendee.Id
            where b.Status == BookingStatus.Confirmed
                  && (b.AttendeeId == userId || b.OwnerId == userId)
                  && s.EndUtc > nowUtc
            orderby s.StartUtc
            select new MyBooking(b.Id, s.Id, s.StartUtc, s.EndUtc, owner.Nickname, attendee.Nickname, b.OwnerId == userId)
        ).ToListAsync(ct);
    }

    /// <summary>
    /// Cancel a confirmed booking. Either party may do it; a reason (1–250 chars) is
    /// required and is delivered to the other party (Phase 5). The slot is freed and the
    /// booking is marked Cancelled (kept, not deleted) in one atomic SaveChanges.
    /// </summary>
    public async Task<Result> CancelAsync(
        Guid bookingId, string actingUserId, string reason, DateTime nowUtc, CancellationToken ct = default)
    {
        reason = reason?.Trim() ?? "";
        if (reason.Length == 0)
        {
            return Result.Fail("Please give a reason for cancelling.");
        }

        if (reason.Length > MaxReasonLength)
        {
            return Result.Fail($"Reason must be {MaxReasonLength} characters or fewer.");
        }

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
        {
            return Result.Fail("Booking not found.");
        }

        if (booking.OwnerId != actingUserId && booking.AttendeeId != actingUserId)
        {
            return Result.Fail("You can't cancel this booking.");
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            return Result.Fail("This booking is no longer active.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledById = actingUserId;
        booking.CancelledUtc = nowUtc;
        booking.CancellationReason = reason;

        // Free the slot so it can be offered again. The kept Cancelled row doesn't block
        // re-booking because the unique index only covers confirmed bookings.
        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == booking.SlotId, ct);
        if (slot is not null)
        {
            slot.IsBooked = false;
        }

        // Tell the other party, with the reason.
        var otherPartyId = booking.OwnerId == actingUserId ? booking.AttendeeId : booking.OwnerId;
        notifications.Queue(otherPartyId, $"A booking was cancelled. Reason: {reason}", nowUtc);

        await db.SaveChangesAsync(ct);
        notifications.PushQueued();
        return Result.Success();
    }
}

/// <summary>A confirmed booking as shown on the user's own schedule.</summary>
public record MyBooking(
    Guid BookingId, Guid SlotId, DateTime StartUtc, DateTime EndUtc,
    string OwnerNickname, string AttendeeNickname, bool ActingUserIsOwner);
