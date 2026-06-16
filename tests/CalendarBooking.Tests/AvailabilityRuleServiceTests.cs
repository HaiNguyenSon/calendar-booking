using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Tests;

public class AvailabilityRuleServiceTests
{
    // Fixed "now" = Monday 2030-01-07 08:00 UTC.
    private static readonly DateTime Now = new(2030, 1, 7, 8, 0, 0, DateTimeKind.Utc);

    private static AvailabilityRuleService NewService(CalendarBooking.Data.AppDbContext db) =>
        new(db, new AvailabilityService(db, TestDb.NoSync()));

    [Fact]
    public async Task AddRule_persists_the_rule_and_materializes_slots()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = NewService(db);
        var mask = WeeklyAvailability.DayBit(DayOfWeek.Tuesday); // tomorrow relative to Now

        var result = await svc.AddRuleAsync(alice.Id, mask, 9 * 60, 11 * 60, 30, SlotType.ApprovalRequired, "UTC", Now);

        Assert.True(result.Ok);
        Assert.Equal(1, await db.WeeklyAvailabilityRules.CountAsync());
        // 09:00–11:00, 30-min, over a 28-day horizon: Tuesdays occur 4 times → 16 slots.
        Assert.Equal(16, await db.AvailabilitySlots.CountAsync());
        Assert.All(await db.AvailabilitySlots.ToListAsync(), s => Assert.Equal(SlotType.ApprovalRequired, s.SlotType));
    }

    [Fact]
    public async Task Materialize_is_idempotent_no_duplicate_slots()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = NewService(db);
        var mask = WeeklyAvailability.DayBit(DayOfWeek.Tuesday);
        await svc.AddRuleAsync(alice.Id, mask, 9 * 60, 10 * 60, 30, SlotType.Instant, "UTC", Now);
        var afterFirst = await db.AvailabilitySlots.CountAsync();

        var rule = await db.WeeklyAvailabilityRules.SingleAsync();
        await svc.MaterializeAsync(rule, Now);

        Assert.Equal(afterFirst, await db.AvailabilitySlots.CountAsync()); // no new slots
    }

    [Fact]
    public async Task AddRule_rejects_invalid_input()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = NewService(db);

        Assert.False((await svc.AddRuleAsync(alice.Id, 0, 540, 600, 30, SlotType.Instant, "UTC", Now)).Ok);            // no days
        Assert.False((await svc.AddRuleAsync(alice.Id, 2, 600, 540, 30, SlotType.Instant, "UTC", Now)).Ok);          // end<=start
        Assert.False((await svc.AddRuleAsync(alice.Id, 2, 9 * 60, 11 * 60, 20, SlotType.Instant, "UTC", Now)).Ok);   // slot < 30
        Assert.False((await svc.AddRuleAsync(alice.Id, 2, 547, 600, 30, SlotType.Instant, "UTC", Now)).Ok);          // not quarter-hour
    }

    [Fact]
    public async Task DeleteRule_removes_it_and_its_future_unbooked_slots()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = NewService(db);
        await svc.AddRuleAsync(alice.Id, WeeklyAvailability.DayBit(DayOfWeek.Tuesday), 9 * 60, 10 * 60, 30, SlotType.Instant, "UTC", Now);
        Assert.True(await db.AvailabilitySlots.AnyAsync());
        var rule = await db.WeeklyAvailabilityRules.SingleAsync();

        await svc.DeleteRuleAsync(alice.Id, rule.Id, Now);

        Assert.Equal(0, await db.WeeklyAvailabilityRules.CountAsync());
        Assert.Equal(0, await db.AvailabilitySlots.CountAsync()); // unbooked future slots cleared
    }

    [Fact]
    public async Task DeleteRule_keeps_a_booked_slot_it_generated()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var svc = NewService(db);
        await svc.AddRuleAsync(alice.Id, WeeklyAvailability.DayBit(DayOfWeek.Tuesday), 9 * 60, 10 * 60, 30, SlotType.Instant, "UTC", Now);
        var rule = await db.WeeklyAvailabilityRules.SingleAsync();

        // Mark one generated slot as booked (a confirmed booking references it).
        var booked = await db.AvailabilitySlots.OrderBy(s => s.StartUtc).FirstAsync();
        booked.IsBooked = true;
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = booked.Id,
            OwnerId = alice.Id,
            AttendeeId = bob.Id,
            CreatedById = bob.Id,
            Status = BookingStatus.Confirmed,
            CreatedUtc = Now,
        });
        await db.SaveChangesAsync();

        await svc.DeleteRuleAsync(alice.Id, rule.Id, Now);

        // The booked slot survives; the rest are gone.
        Assert.Equal(1, await db.AvailabilitySlots.CountAsync());
        Assert.NotNull(await db.AvailabilitySlots.FindAsync(booked.Id));
    }
}
