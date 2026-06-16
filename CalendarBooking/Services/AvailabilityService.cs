using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Owner-side availability operations: list, create and delete a user's own slots.
/// Every method takes the acting user's id and enforces that they only touch their own
/// slots. All times are UTC — callers convert to/from the user's local time at the edge.
///
/// When the owner has a connected external calendar, creating a slot is blocked if it
/// clashes with their external busy time (the "pull" half of sync). With no provider
/// connected, <see cref="CalendarSyncService"/> returns no busy times, so this is inert.
/// </summary>
public class AvailabilityService(AppDbContext db, CalendarSyncService calendarSync)
{
    /// <summary>A failure carries a user-facing reason; success may carry the new slot.</summary>
    public readonly record struct Result(bool Ok, string? Error, AvailabilitySlot? Slot = null)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Success(AvailabilitySlot? slot = null) => new(true, null, slot);
    }

    // Bookable times are on the quarter hour. Real UTC offsets are all multiples of 15
    // minutes, so a quarter-hour local time is still quarter-hour-aligned in UTC.
    private static bool IsAlignedToQuarterHour(DateTime t) =>
        t is { Second: 0, Millisecond: 0 } && t.Minute % 15 == 0;

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

        if (!IsAlignedToQuarterHour(startUtc) || !IsAlignedToQuarterHour(endUtc))
        {
            return Result.Fail("Times must be on the quarter hour (:00, :15, :30, :45).");
        }

        // Don't let an owner offer two overlapping slots. Two ranges overlap when each
        // starts before the other ends.
        var overlaps = await db.AvailabilitySlots.AnyAsync(
            s => s.OwnerId == ownerId && s.StartUtc < endUtc && startUtc < s.EndUtc, ct);
        if (overlaps)
        {
            return Result.Fail("This overlaps a slot you already have.");
        }

        if (calendarSync.HasProviders)
        {
            var busy = await calendarSync.GetBusyIntervalsAsync(ownerId, startUtc, endUtc, ct);
            if (busy.Any(b => b.StartUtc < endUtc && startUtc < b.EndUtc))
            {
                return Result.Fail("Your connected calendar shows you as busy then.");
            }
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
    /// Create a weekly-recurring slot as <paramref name="weeks"/> concrete one-off slots,
    /// one every 7 days from the first occurrence. Occurrences that would overlap an
    /// existing slot (or an earlier occurrence in this batch) are skipped rather than
    /// failing the whole request. Returns failure only if nothing could be created.
    ///
    /// Note: the 7-day step is applied in UTC, so an occurrence's local wall-clock time
    /// can shift by an hour across a daylight-saving boundary. Aligning recurrences to
    /// local time is a Phase 6 refinement.
    /// </summary>
    public async Task<Result> CreateWeeklyAsync(
        string ownerId, DateTime firstStartUtc, DateTime firstEndUtc, int weeks, SlotType slotType,
        DateTime nowUtc, CancellationToken ct = default)
    {
        if (weeks is < 1 or > 52)
        {
            return Result.Fail("Choose between 1 and 52 weeks.");
        }

        // UTC stepping. Callers that care about daylight-saving (the UI) build occurrences
        // by stepping in local time and call CreateManyAsync directly.
        var occurrences = Enumerable.Range(0, weeks)
            .Select(i => (StartUtc: firstStartUtc.AddDays(7 * i), EndUtc: firstEndUtc.AddDays(7 * i)))
            .ToList();

        return await CreateManyAsync(ownerId, occurrences, slotType, nowUtc, ct);
    }

    /// <summary>
    /// Create many slots at once (used for recurrence). Each occurrence is validated; any
    /// that would overlap an existing slot or an earlier occurrence in the batch is skipped.
    /// Fails only if none could be created. Times are UTC, so the caller decides how to step
    /// a recurrence (e.g. in local time, to stay daylight-saving-correct).
    /// </summary>
    public async Task<Result> CreateManyAsync(
        string ownerId, IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> occurrences, SlotType slotType,
        DateTime nowUtc, CancellationToken ct = default, Guid? generatedByRuleId = null)
    {
        if (occurrences.Count == 0)
        {
            return Result.Fail("Nothing to create.");
        }

        foreach (var o in occurrences)
        {
            if (o.EndUtc <= o.StartUtc)
            {
                return Result.Fail("End time must be after the start time.");
            }

            if (o.StartUtc < nowUtc)
            {
                return Result.Fail("A slot cannot start in the past.");
            }

            if (!IsAlignedToQuarterHour(o.StartUtc) || !IsAlignedToQuarterHour(o.EndUtc))
            {
                return Result.Fail("Times must be on the quarter hour (:00, :15, :30, :45).");
            }
        }

        var earliest = occurrences.Min(o => o.StartUtc);
        var latest = occurrences.Max(o => o.EndUtc);
        var existing = await db.AvailabilitySlots
            .Where(s => s.OwnerId == ownerId && s.EndUtc > earliest)
            .Select(s => new { s.StartUtc, s.EndUtc })
            .ToListAsync(ct);

        // Owner's external busy times across the whole series (one call), if connected.
        var busy = calendarSync.HasProviders
            ? await calendarSync.GetBusyIntervalsAsync(ownerId, earliest, latest, ct)
            : Array.Empty<BusyInterval>();

        var toAdd = new List<AvailabilitySlot>();
        foreach (var o in occurrences)
        {
            var overlapsExisting = existing.Any(e => e.StartUtc < o.EndUtc && o.StartUtc < e.EndUtc);
            var overlapsBatch = toAdd.Any(s => s.StartUtc < o.EndUtc && o.StartUtc < s.EndUtc);
            var overlapsBusy = busy.Any(b => b.StartUtc < o.EndUtc && o.StartUtc < b.EndUtc);
            if (overlapsExisting || overlapsBatch || overlapsBusy)
            {
                continue;
            }

            toAdd.Add(new AvailabilitySlot
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                StartUtc = o.StartUtc,
                EndUtc = o.EndUtc,
                SlotType = slotType,
                IsBooked = false,
                CreatedUtc = nowUtc,
                GeneratedByRuleId = generatedByRuleId,
            });
        }

        if (toAdd.Count == 0)
        {
            return Result.Fail("Every occurrence overlaps a slot you already have.");
        }

        db.AvailabilitySlots.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
        return Result.Success();
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
