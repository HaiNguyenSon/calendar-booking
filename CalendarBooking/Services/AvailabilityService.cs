using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Owner-side availability operations: list, create and delete a user's own slots.
/// Every method takes the acting user's id and enforces that they only touch their own
/// slots. All times are UTC — callers convert to/from the user's local time at the edge.
/// </summary>
public class AvailabilityService(AppDbContext db)
{
    /// <summary>A failure carries a user-facing reason; success may carry the new slot.</summary>
    public readonly record struct Result(bool Ok, string? Error, AvailabilitySlot? Slot = null)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Success(AvailabilitySlot? slot = null) => new(true, null, slot);
    }

    /// <summary>The owner's slots that have not yet ended, soonest first.</summary>
    public async Task<IReadOnlyList<AvailabilitySlot>> GetUpcomingOwnedSlotsAsync(
        string ownerId, DateTime fromUtc, CancellationToken ct = default)
    {
        return await db.AvailabilitySlots
            .Where(s => s.OwnerId == ownerId && s.EndUtc > fromUtc)
            .OrderBy(s => s.StartUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Create a one-off availability slot. <paramref name="nowUtc"/> is passed in (rather
    /// than read from the clock here) so the "not in the past" rule is deterministic and
    /// testable.
    /// </summary>
    public async Task<Result> CreateOneOffAsync(
        string ownerId, DateTime startUtc, DateTime endUtc, SlotType slotType,
        DateTime nowUtc, CancellationToken ct = default)
    {
        if (endUtc <= startUtc)
        {
            return Result.Fail("End time must be after the start time.");
        }

        if (startUtc < nowUtc)
        {
            return Result.Fail("A slot cannot start in the past.");
        }

        // Don't let an owner offer two overlapping slots. Two ranges overlap when each
        // starts before the other ends.
        var overlaps = await db.AvailabilitySlots.AnyAsync(
            s => s.OwnerId == ownerId && s.StartUtc < endUtc && startUtc < s.EndUtc, ct);
        if (overlaps)
        {
            return Result.Fail("This overlaps a slot you already have.");
        }

        var slot = new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            SlotType = slotType,
            IsBooked = false,
            CreatedUtc = nowUtc,
        };

        db.AvailabilitySlots.Add(slot);
        await db.SaveChangesAsync(ct);
        return Result.Success(slot);
    }

    /// <summary>
    /// Delete one of the owner's slots. Refuses if the slot isn't theirs or is already
    /// booked (a booked slot is deleted only by cancelling the booking — Phase 4).
    /// </summary>
    public async Task<Result> DeleteAsync(string ownerId, Guid slotId, CancellationToken ct = default)
    {
        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null || slot.OwnerId != ownerId)
        {
            return Result.Fail("Slot not found.");
        }

        if (slot.IsBooked)
        {
            return Result.Fail("You can't delete a slot that is already booked.");
        }

        db.AvailabilitySlots.Remove(slot);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
