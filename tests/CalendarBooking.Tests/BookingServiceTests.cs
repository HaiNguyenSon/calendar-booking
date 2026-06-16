using CalendarBooking.Data;
using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CalendarBooking.Tests;

public class BookingServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    private static BookingService NewService(AppDbContext db, int maxPending = 5)
        => new(db, Options.Create(new BookingOptions { MaxPendingRequests = maxPending }), new NotificationService(db, new NotificationBroadcaster()));

    [Fact]
    public async Task BookInstant_creates_a_confirmed_booking_and_marks_the_slot_booked()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.Instant);
        var svc = NewService(db);

        var result = await svc.BookInstantAsync(slot.Id, bob.Id, Now);

        Assert.True(result.Ok);
        Assert.NotNull(result.Booking);
        var booking = await db.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(alice.Id, booking.OwnerId);
        Assert.Equal(bob.Id, booking.AttendeeId);
        Assert.Equal(bob.Id, booking.CreatedById);
        Assert.True((await db.AvailabilitySlots.SingleAsync()).IsBooked);
    }

    [Fact]
    public async Task BookInstant_refuses_an_approval_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        var svc = NewService(db);

        var result = await svc.BookInstantAsync(slot.Id, bob.Id, Now);

        Assert.False(result.Ok);
        Assert.Equal(0, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task BookInstant_refuses_booking_your_own_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.Instant);
        var svc = NewService(db);

        var result = await svc.BookInstantAsync(slot.Id, alice.Id, Now);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task BookInstant_refuses_an_already_booked_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.Instant, booked: true);
        var svc = NewService(db);

        var result = await svc.BookInstantAsync(slot.Id, bob.Id, Now);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task RequestApproval_creates_a_pending_request()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        var svc = NewService(db);

        var result = await svc.RequestApprovalAsync(slot.Id, bob.Id, Now);

        Assert.True(result.Ok);
        var request = await db.BookingRequests.SingleAsync();
        Assert.Equal(RequestStatus.Pending, request.Status);
        Assert.False((await db.AvailabilitySlots.SingleAsync()).IsBooked); // approval slots stay open
    }

    [Fact]
    public async Task RequestApproval_refuses_an_instant_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.Instant);
        var svc = NewService(db);

        var result = await svc.RequestApprovalAsync(slot.Id, bob.Id, Now);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task RequestApproval_refuses_a_duplicate_request_for_the_same_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        var svc = NewService(db);

        Assert.True((await svc.RequestApprovalAsync(slot.Id, bob.Id, Now)).Ok);
        var second = await svc.RequestApprovalAsync(slot.Id, bob.Id, Now);

        Assert.False(second.Ok);
        Assert.Equal(1, await db.BookingRequests.CountAsync());
    }

    [Fact]
    public async Task RequestApproval_enforces_the_pending_request_cap()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot1 = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        var slot2 = TestDb.AddSlot(db, alice.Id, Now.AddHours(3), Now.AddHours(4), SlotType.ApprovalRequired);
        var svc = NewService(db, maxPending: 1);

        Assert.True((await svc.RequestApprovalAsync(slot1.Id, bob.Id, Now)).Ok);
        var overCap = await svc.RequestApprovalAsync(slot2.Id, bob.Id, Now);

        Assert.False(overCap.Ok);
        Assert.Equal(1, await db.BookingRequests.CountAsync());
    }

    [Fact]
    public async Task GetOwnersWithOpenSlots_lists_only_owners_with_open_future_slots_and_filters_by_search()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));            // alice: open
        TestDb.AddSlot(db, bob.Id, Now.AddHours(1), Now.AddHours(2), booked: true); // bob: booked only
        var svc = NewService(db);

        var all = await svc.GetOwnersWithOpenSlotsAsync(Now);
        Assert.Single(all);
        Assert.Equal("alice", all[0].Nickname);
        Assert.Equal(1, all[0].OpenSlots);

        var filtered = await svc.GetOwnersWithOpenSlotsAsync(Now, "ali");
        Assert.Single(filtered);

        var none = await svc.GetOwnersWithOpenSlotsAsync(Now, "zzz");
        Assert.Empty(none);
    }

    [Fact]
    public async Task GetScheduleByNickname_returns_both_open_and_busy_slots_without_details()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));                 // open
        TestDb.AddSlot(db, alice.Id, Now.AddHours(3), Now.AddHours(4), booked: true);   // busy
        var svc = NewService(db);

        var (owner, slots) = await svc.GetScheduleByNicknameAsync("alice", Now);

        Assert.NotNull(owner);
        Assert.Equal(2, slots.Count);                       // both shown
        Assert.Contains(slots, s => s.IsBooked);            // busy is included
        Assert.Contains(slots, s => !s.IsBooked);           // bookable is included
        // CalendarSlot carries no attendee/who fields — privacy by shape.
    }

    [Fact]
    public async Task GetOpenSlotsByNickname_returns_the_owner_and_their_open_slots()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        TestDb.AddSlot(db, alice.Id, Now.AddHours(3), Now.AddHours(4), booked: true);
        var svc = NewService(db);

        var (owner, slots) = await svc.GetOpenSlotsByNicknameAsync("ALICE", Now);

        Assert.NotNull(owner);
        Assert.Single(slots);
    }
}
