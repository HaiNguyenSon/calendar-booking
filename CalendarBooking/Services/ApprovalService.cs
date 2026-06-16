using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Owner-side handling of approval-required slots: reviewing incoming requests,
/// approving (which confirms a booking and auto-declines the competition), declining,
/// and booking a slot on behalf of an existing client.
/// </summary>
public class ApprovalService(AppDbContext db, NotificationService notifications)
{
    public readonly record struct Result(bool Ok, string? Error, Booking? Booking = null)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Success(Booking? booking = null) => new(true, null, booking);
    }

    /// <summary>Pending requests against the owner's still-open slots, soonest first.</summary>
    public async Task<IReadOnlyList<IncomingRequest>> GetIncomingRequestsAsync(
        string ownerId, DateTime nowUtc, CancellationToken ct = default)
    {
        return await (
            from r in db.BookingRequests
            join s in db.AvailabilitySlots on r.SlotId equals s.Id
            join u in db.Users on r.RequesterId equals u.Id
            where r.Status == RequestStatus.Pending && s.OwnerId == ownerId && !s.IsBooked && s.EndUtc > nowUtc
            orderby s.StartUtc
            select new IncomingRequest(r.Id, s.Id, u.Nickname, s.StartUtc, s.EndUtc)
        ).ToListAsync(ct);
    }

    /// <summary>
    /// Approve a request: confirm a booking for the requester, mark the slot booked, and
    /// auto-decline every other pending request for that slot as Superseded. All in one
    /// atomic SaveChanges; a unique-index violation means the slot was already taken.
    /// </summary>
    public async Task<Result> ApproveAsync(Guid requestId, string ownerId, DateTime nowUtc, CancellationToken ct = default)
    {
        var request = await db.BookingRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || request.Status != RequestStatus.Pending)
        {
            return Result.Fail("That request no longer needs an answer.");
        }

        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == request.SlotId, ct);
        if (slot is null || slot.OwnerId != ownerId)
        {
            return Result.Fail("Request not found.");
        }

        if (slot.IsBooked || slot.EndUtc <= nowUtc)
        {
            return Result.Fail("That slot is no longer available.");
        }

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            OwnerId = ownerId,
            AttendeeId = request.RequesterId,
            CreatedById = request.RequesterId, // the requester initiated it; the owner just approved
            Status = BookingStatus.Confirmed,
            CreatedUtc = nowUtc,
        };
        slot.IsBooked = true;
        request.Status = RequestStatus.Approved;
        db.Bookings.Add(booking);

        // Contention: the losers for this slot are auto-declined and (Phase 5) notified.
        var others = await db.BookingRequests
            .Where(r => r.SlotId == slot.Id && r.Id != request.Id && r.Status == RequestStatus.Pending)
            .ToListAsync(ct);
        foreach (var other in others)
        {
            other.Status = RequestStatus.Superseded;
            notifications.Queue(other.RequesterId,
                "A slot you requested was taken by someone else — feel free to book another.", nowUtc);
        }

        notifications.Queue(request.RequesterId, "Your booking request was approved.", nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result.Fail("That slot was just taken.");
        }

        notifications.PushQueued();
        return Result.Success(booking);
    }

    /// <summary>Decline a request. The slot stays open for others.</summary>
    public async Task<Result> DeclineAsync(Guid requestId, string ownerId, CancellationToken ct = default)
    {
        var request = await db.BookingRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || request.Status != RequestStatus.Pending)
        {
            return Result.Fail("That request no longer needs an answer.");
        }

        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == request.SlotId, ct);
        if (slot is null || slot.OwnerId != ownerId)
        {
            return Result.Fail("Request not found.");
        }

        request.Status = RequestStatus.Declined;
        notifications.Queue(request.RequesterId, "Your booking request was declined.", DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        notifications.PushQueued();
        return Result.Success();
    }

    /// <summary>
    /// Owner books one of their slots on behalf of an existing client — confirmed
    /// immediately, no client approval, bypassing the pending cap. Gated on a prior
    /// booking relationship (in either direction, including cancelled history). The
    /// booking's attendee is the client, so it appears on the client's calendar.
    /// </summary>
    public async Task<Result> BookOnBehalfAsync(
        string ownerId, Guid slotId, string clientNickname, DateTime nowUtc, CancellationToken ct = default)
    {
        var term = clientNickname.Trim().ToLower();
        var client = await db.Users.FirstOrDefaultAsync(u => u.Nickname.ToLower() == term, ct);
        if (client is null)
        {
            return Result.Fail("No user with that nickname.");
        }

        if (client.Id == ownerId)
        {
            return Result.Fail("Choose someone other than yourself.");
        }

        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null || slot.OwnerId != ownerId)
        {
            return Result.Fail("Slot not found.");
        }

        if (slot.IsBooked || slot.EndUtc <= nowUtc)
        {
            return Result.Fail("That slot is no longer available.");
        }

        // Relationship gate: they must share booking history, either direction.
        var hasHistory = await db.Bookings.AnyAsync(
            b => (b.OwnerId == ownerId && b.AttendeeId == client.Id)
              || (b.OwnerId == client.Id && b.AttendeeId == ownerId), ct);
        if (!hasHistory)
        {
            return Result.Fail("You can only book on behalf of someone you've booked with before.");
        }

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            OwnerId = ownerId,
            AttendeeId = client.Id,
            CreatedById = ownerId, // owner-initiated
            Status = BookingStatus.Confirmed,
            CreatedUtc = nowUtc,
        };
        slot.IsBooked = true;
        db.Bookings.Add(booking);

        var ownerNickname = await db.Users.Where(u => u.Id == ownerId).Select(u => u.Nickname).FirstOrDefaultAsync(ct) ?? "Someone";
        notifications.Queue(client.Id, $"{ownerNickname} booked you into one of their slots.", nowUtc);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result.Fail("That slot was just taken.");
        }

        notifications.PushQueued();
        return Result.Success(booking);
    }
}

/// <summary>A pending request as shown to the slot owner.</summary>
public record IncomingRequest(Guid RequestId, Guid SlotId, string RequesterNickname, DateTime StartUtc, DateTime EndUtc);
