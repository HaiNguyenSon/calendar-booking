using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Tests;

public class AvailabilityServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    [Fact]
    public async Task CreateOneOff_succeeds_for_a_valid_future_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new AvailabilityService(db);

        var result = await svc.CreateOneOffAsync(alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.Instant, Now);

        Assert.True(result.Ok);
        Assert.Equal(1, await db.AvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task CreateOneOff_rejects_end_before_start()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new AvailabilityService(db);

        var result = await svc.CreateOneOffAsync(alice.Id, Now.AddHours(2), Now.AddHours(1), SlotType.Instant, Now);

        Assert.False(result.Ok);
        Assert.Equal(0, await db.AvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task CreateOneOff_rejects_start_in_the_past()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new AvailabilityService(db);

        var result = await svc.CreateOneOffAsync(alice.Id, Now.AddHours(-2), Now.AddHours(-1), SlotType.Instant, Now);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task CreateOneOff_rejects_overlap_with_existing_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(3));
        var svc = new AvailabilityService(db);

        var result = await svc.CreateOneOffAsync(alice.Id, Now.AddHours(2), Now.AddHours(4), SlotType.Instant, Now);

        Assert.False(result.Ok);
        Assert.Equal(1, await db.AvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task CreateWeekly_creates_one_slot_per_week()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new AvailabilityService(db);

        var result = await svc.CreateWeeklyAsync(alice.Id, Now.AddHours(1), Now.AddHours(2), 4, SlotType.Instant, Now);

        Assert.True(result.Ok);
        Assert.Equal(4, await db.AvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task CreateWeekly_skips_occurrences_that_overlap_existing_slots()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        // An existing slot exactly where the 2nd weekly occurrence would land.
        TestDb.AddSlot(db, alice.Id, Now.AddHours(1).AddDays(7), Now.AddHours(2).AddDays(7));
        var svc = new AvailabilityService(db);

        var result = await svc.CreateWeeklyAsync(alice.Id, Now.AddHours(1), Now.AddHours(2), 3, SlotType.Instant, Now);

        Assert.True(result.Ok);
        // 1 pre-existing + 2 created (week 0 and week 2; week 1 skipped) = 3 total.
        Assert.Equal(3, await db.AvailabilitySlots.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public async Task CreateWeekly_rejects_out_of_range_week_counts(int weeks)
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new AvailabilityService(db);

        var result = await svc.CreateWeeklyAsync(alice.Id, Now.AddHours(1), Now.AddHours(2), weeks, SlotType.Instant, Now);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Delete_removes_an_owned_unbooked_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var svc = new AvailabilityService(db);

        var result = await svc.DeleteAsync(alice.Id, slot.Id);

        Assert.True(result.Ok);
        Assert.Equal(0, await db.AvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task Delete_refuses_a_booked_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), booked: true);
        var svc = new AvailabilityService(db);

        var result = await svc.DeleteAsync(alice.Id, slot.Id);

        Assert.False(result.Ok);
        Assert.Equal(1, await db.AvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task Delete_refuses_a_slot_owned_by_someone_else()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var svc = new AvailabilityService(db);

        var result = await svc.DeleteAsync(bob.Id, slot.Id);

        Assert.False(result.Ok);
        Assert.Equal(1, await db.AvailabilitySlots.CountAsync());
    }

    [Fact]
    public async Task GetUpcomingOwnedSlots_returns_only_the_owners_future_slots_in_order()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        TestDb.AddSlot(db, alice.Id, Now.AddHours(3), Now.AddHours(4));   // future, later
        TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));   // future, earlier
        TestDb.AddSlot(db, alice.Id, Now.AddHours(-2), Now.AddHours(-1)); // past
        TestDb.AddSlot(db, bob.Id, Now.AddHours(1), Now.AddHours(2));     // other owner
        var svc = new AvailabilityService(db);

        var slots = await svc.GetUpcomingOwnedSlotsAsync(alice.Id, Now);

        Assert.Equal(2, slots.Count);
        Assert.True(slots[0].StartUtc < slots[1].StartUtc);
    }
}
