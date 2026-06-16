using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Manages standing weekly availability rules and materializes them into concrete slots.
/// Materialization reuses <see cref="AvailabilityService.CreateManyAsync"/>, which skips any
/// occurrence that clashes with an existing slot — so re-running is idempotent (it only fills
/// gaps, never duplicates).
/// </summary>
public class AvailabilityRuleService(AppDbContext db, AvailabilityService availability)
{
    /// <summary>How far ahead the rule keeps slots materialized.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(28);

    public readonly record struct Result(bool Ok, string? Error)
    {
        public static Result Fail(string error) => new(false, error);
        public static Result Success() => new(true, null);
    }

    public async Task<Result> AddRuleAsync(
        string ownerId, int daysMask, int startMinutes, int endMinutes, int slotMinutes,
        SlotType slotType, string timeZoneId, DateTime nowUtc, CancellationToken ct = default)
    {
        if (daysMask == 0)
        {
            return Result.Fail("Pick at least one day.");
        }
        if (endMinutes <= startMinutes)
        {
            return Result.Fail("End must be after start.");
        }
        if (startMinutes % 15 != 0 || endMinutes % 15 != 0)
        {
            return Result.Fail("Times must be on the quarter hour.");
        }
        if (slotMinutes < 30 || slotMinutes % 15 != 0)
        {
            return Result.Fail("Slot length must be at least 30 minutes, in 15-minute steps.");
        }

        var rule = new WeeklyAvailabilityRule
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            DaysMask = daysMask,
            StartMinutes = startMinutes,
            EndMinutes = endMinutes,
            SlotMinutes = slotMinutes,
            SlotType = slotType,
            TimeZoneId = timeZoneId,
            CreatedUtc = nowUtc,
        };
        db.WeeklyAvailabilityRules.Add(rule);
        await db.SaveChangesAsync(ct);

        // Create the upcoming slots now so they appear immediately.
        await MaterializeAsync(rule, nowUtc, ct);
        return Result.Success();
    }

    public Task<List<WeeklyAvailabilityRule>> GetRulesAsync(string ownerId, CancellationToken ct = default) =>
        db.WeeklyAvailabilityRules.Where(r => r.OwnerId == ownerId).OrderBy(r => r.CreatedUtc).ToListAsync(ct);

    /// <summary>
    /// Delete a rule and remove the FUTURE, UNBOOKED slots it generated. Booked slots (and any
    /// slot a booking still references) are kept so existing bookings/history are never yanked.
    /// </summary>
    public async Task DeleteRuleAsync(string ownerId, Guid ruleId, DateTime nowUtc, CancellationToken ct = default)
    {
        var rule = await db.WeeklyAvailabilityRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.OwnerId == ownerId, ct);
        if (rule is null)
        {
            return;
        }

        var deletableSlots = await db.AvailabilitySlots
            .Where(s => s.GeneratedByRuleId == ruleId
                        && !s.IsBooked
                        && s.EndUtc > nowUtc
                        && !db.Bookings.Any(b => b.SlotId == s.Id))
            .ToListAsync(ct);
        db.AvailabilitySlots.RemoveRange(deletableSlots);

        db.WeeklyAvailabilityRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Materialize a single rule's slots for the rolling horizon (idempotent).</summary>
    public async Task MaterializeAsync(WeeklyAvailabilityRule rule, DateTime nowUtc, CancellationToken ct = default)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        var nowLocalDate = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Date;
        var toLocalDate = nowLocalDate.Add(Horizon);
        var occurrences = WeeklyAvailability.Expand(
            rule.DaysMask, rule.StartMinutes, rule.EndMinutes, rule.SlotMinutes, tz, nowLocalDate, toLocalDate, nowUtc);

        if (occurrences.Count > 0)
        {
            await availability.CreateManyAsync(rule.OwnerId, occurrences, rule.SlotType, nowUtc, ct, rule.Id);
        }
    }

    /// <summary>Materialize every rule (used by the background worker).</summary>
    public async Task MaterializeAllAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var rules = await db.WeeklyAvailabilityRules.ToListAsync(ct);
        foreach (var rule in rules)
        {
            await MaterializeAsync(rule, nowUtc, ct);
        }
    }
}
