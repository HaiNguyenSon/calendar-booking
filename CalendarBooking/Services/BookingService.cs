using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CalendarBooking.Services;

/// <summary>
/// The heart of the app: browsing open slots and claiming them. An instant slot becomes
/// a confirmed <see cref="Booking"/> right away; an approval slot creates a pending
/// <see cref="BookingRequest"/> the owner must approve (handled in
/// <see cref="ApprovalService"/>).
///
/// Double-booking is prevented at the database level by the partial unique index on
/// <c>Booking.SlotId</c> (confirmed bookings only): even if two people claim the same
/// instant slot at the same moment, the second <c>SaveChanges</c> fails and we report the
/// slot as taken. The in-memory <c>IsBooked</c> check just handles the common case nicely.
/// </summary>
public class BookingService(AppDbContext db, IOptions<BookingOptions> options, NotificationService notifications)
{
    private readonly int maxPendingRequests = options.Value.MaxPendingRequests;

    public readonly record struct Result(bool Ok, string? Error, Booking? Booking = null, BookingRequest? Request = null)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Booked(Booking booking) => new(true, null, Booking: booking);
        public static Result Requested(BookingRequest request) => new(true, null, Request: request);
    }

    /// <summary>Public: nicknames with at least one open (future, unbooked) slot, optionally filtered.</summary>
    public async Task<IReadOnlyList<OwnerSummary>> GetOwnersWithOpenSlotsAsync(
        DateTime nowUtc, string? search = null, CancellationToken ct = default)
    {
        var grouped = await db.AvailabilitySlots
            .Where(s => !s.IsBooked && s.EndUtc > nowUtc)
            .GroupBy(s => s.OwnerId)
            .Select(g => new { OwnerId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var ids = grouped.Select(g => g.OwnerId).ToList();
        var usersQuery = db.Users.Where(u => ids.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            usersQuery = usersQuery.Where(u => u.Nickname.ToLower().Contains(term));
        }

        var users = await usersQuery.ToListAsync(ct);
        return users
            .Select(u => new OwnerSummary(u.Nickname, grouped.First(g => g.OwnerId == u.Id).Count))
            .OrderBy(o => o.Nickname)
            .ToList();
    }

    /// <summary>Public: an owner (by nickname) and their open slots.</summary>
    public async Task<(ApplicationUser? Owner, IReadOnlyList<AvailabilitySlot> Slots)> GetOpenSlotsByNicknameAsync(
        string nickname, DateTime nowUtc, CancellationToken ct = default)
    {
        var term = nickname.Trim().ToLower();
        var owner = await db.Users.FirstOrDefaultAsync(u => u.Nickname.ToLower() == term, ct);
        if (owner is null)
        {
            return (null, Array.Empty<AvailabilitySlot>());
        }

        var slots = await db.AvailabilitySlots
            .Where(s => s.OwnerId == owner.Id && !s.IsBooked && s.EndUtc > nowUtc)
            .OrderBy(s => s.StartUtc)
            .ToListAsync(ct);

        return (owner, slots);
    }

    /// <summary>
    /// A user's calendar as others may see it: every future slot with time + booked-or-not,
    /// but NO who/what details. Booked slots show only that the person is busy then; open
    /// slots are bookable. Treats the calendar as private.
    /// </summary>
    public async Task<(ApplicationUser? Owner, IReadOnlyList<CalendarSlot> Slots)> GetScheduleByNicknameAsync(
        string nickname, DateTime nowUtc, CancellationToken ct = default)
    {
        var term = nickname.Trim().ToLower();
        var owner = await db.Users.FirstOrDefaultAsync(u => u.Nickname.ToLower() == term, ct);
        if (owner is null)
        {
            return (null, Array.Empty<CalendarSlot>());
        }

        var slots = await db.AvailabilitySlots
            .Where(s => s.OwnerId == owner.Id && s.EndUtc > nowUtc)
            .OrderBy(s => s.StartUtc)
            .Select(s => new CalendarSlot(s.Id, s.StartUtc, s.EndUtc, s.SlotType, s.IsBooked))
            .ToListAsync(ct);

        return (owner, slots);
    }

    /// <summary>
    /// Claim an instant slot — creates a confirmed booking and marks the slot booked in a
    /// single atomic SaveChanges. A unique-index violation means someone else won the race.
    /// </summary>
    public async Task<Result> BookInstantAsync(Guid slotId, string attendeeId, DateTime nowUtc, CancellationToken ct = default)
    {
        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null || slot.EndUtc <= nowUtc)
        {
            return Result.Fail("That slot is no longer available.");
        }

        if (slot.SlotType != SlotType.Instant)
        {
            return Result.Fail("This slot needs the owner's approval — request it instead.");
        }

        if (slot.OwnerId == attendeeId)
        {
            return Result.Fail("You can't book your own slot.");
        }

        if (slot.IsBooked)
        {
            return Result.Fail("That slot has just been taken.");
        }

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            OwnerId = slot.OwnerId,
            AttendeeId = attendeeId,
            CreatedById = attendeeId,
            Status = BookingStatus.Confirmed,
            CreatedUtc = nowUtc,
        };
        slot.IsBooked = true;
        db.Bookings.Add(booking);

        var attendeeNickname = await GetNicknameAsync(attendeeId, ct);
        notifications.Queue(slot.OwnerId, $"{attendeeNickname} booked one of your slots.", nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The partial unique index on confirmed Booking.SlotId rejected a second
            // claim — another attendee got there first.
            return Result.Fail("That slot has just been taken.");
        }

        notifications.PushQueued();
        return Result.Booked(booking);
    }

    /// <summary>
    /// Ask to book an approval-required slot. Subject to the per-user pending-request cap;
    /// owner-initiated bookings bypass the cap because they are created confirmed elsewhere.
    /// </summary>
    public async Task<Result> RequestApprovalAsync(Guid slotId, string requesterId, DateTime nowUtc, CancellationToken ct = default)
    {
        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null || slot.IsBooked || slot.EndUtc <= nowUtc)
        {
            return Result.Fail("That slot is no longer available.");
        }

        if (slot.SlotType != SlotType.ApprovalRequired)
        {
            return Result.Fail("This slot books instantly — no request needed.");
        }

        if (slot.OwnerId == requesterId)
        {
            return Result.Fail("You can't request your own slot.");
        }

        var alreadyRequested = await db.BookingRequests.AnyAsync(
            r => r.SlotId == slotId && r.RequesterId == requesterId && r.Status == RequestStatus.Pending, ct);
        if (alreadyRequested)
        {
            return Result.Fail("You've already requested this slot.");
        }

        var pendingCount = await db.BookingRequests.CountAsync(
            r => r.RequesterId == requesterId && r.Status == RequestStatus.Pending, ct);
        if (pendingCount >= maxPendingRequests)
        {
            return Result.Fail($"You already have {maxPendingRequests} pending requests. Wait for some to be answered first.");
        }

        var request = new BookingRequest
        {
            Id = Guid.NewGuid(),
            SlotId = slotId,
            RequesterId = requesterId,
            Status = RequestStatus.Pending,
            CreatedUtc = nowUtc,
        };
        db.BookingRequests.Add(request);

        var requesterNickname = await GetNicknameAsync(requesterId, ct);
        notifications.Queue(slot.OwnerId, $"{requesterNickname} requested one of your slots.", nowUtc);

        await db.SaveChangesAsync(ct);
        notifications.PushQueued();
        return Result.Requested(request);
    }

    private async Task<string> GetNicknameAsync(string userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Nickname).FirstOrDefaultAsync(ct) ?? "Someone";
}

/// <summary>An owner shown in browse results, with how many open slots they have.</summary>
public record OwnerSummary(string Nickname, int OpenSlots);

/// <summary>
/// A slot on someone's calendar as others see it — time + type + booked-or-not, never who or
/// what. A booked slot just means "busy then".
/// </summary>
public record CalendarSlot(Guid SlotId, DateTime StartUtc, DateTime EndUtc, SlotType SlotType, bool IsBooked);
