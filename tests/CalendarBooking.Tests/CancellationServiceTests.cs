using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Tests;

public class CancellationServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    private static Booking AddConfirmedBooking(
        CalendarBooking.Data.AppDbContext db, AvailabilitySlot slot, string ownerId, string attendeeId)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            OwnerId = ownerId,
            AttendeeId = attendeeId,
            CreatedById = attendeeId,
            Status = BookingStatus.Confirmed,
            CreatedUtc = Now.AddDays(-1),
        };
        slot.IsBooked = true;
        db.Bookings.Add(booking);
        db.SaveChanges();
        return booking;
    }

    [Fact]
    public async Task Cancel_marks_the_booking_cancelled_frees_the_slot_and_records_the_reason()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var booking = AddConfirmedBooking(db, slot, alice.Id, bob.Id);
        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));

        var result = await svc.CancelAsync(booking.Id, bob.Id, "Something came up", Now);

        Assert.True(result.Ok);
        var stored = await db.Bookings.FindAsync(booking.Id);
        Assert.Equal(BookingStatus.Cancelled, stored!.Status); // kept, not deleted
        Assert.Equal(bob.Id, stored.CancelledById);
        Assert.Equal("Something came up", stored.CancellationReason);
        Assert.NotNull(stored.CancelledUtc);
        Assert.False((await db.AvailabilitySlots.SingleAsync()).IsBooked); // slot freed
    }

    [Fact]
    public async Task Cancel_can_be_done_by_the_owner_too()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var booking = AddConfirmedBooking(db, slot, alice.Id, bob.Id);
        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));

        var result = await svc.CancelAsync(booking.Id, alice.Id, "Owner cancelling", Now);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Cancel_requires_a_reason()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var booking = AddConfirmedBooking(db, slot, alice.Id, bob.Id);
        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));

        var result = await svc.CancelAsync(booking.Id, bob.Id, "   ", Now);

        Assert.False(result.Ok);
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.FindAsync(booking.Id))!.Status);
    }

    [Fact]
    public async Task Cancel_rejects_a_reason_that_is_too_long()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var booking = AddConfirmedBooking(db, slot, alice.Id, bob.Id);
        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));

        var result = await svc.CancelAsync(booking.Id, bob.Id, new string('x', CancellationService.MaxReasonLength + 1), Now);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Cancel_refuses_someone_who_is_not_a_party_to_the_booking()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var mallory = TestDb.AddUser(db, "mallory");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var booking = AddConfirmedBooking(db, slot, alice.Id, bob.Id);
        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));

        var result = await svc.CancelAsync(booking.Id, mallory.Id, "not mine", Now);

        Assert.False(result.Ok);
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.FindAsync(booking.Id))!.Status);
    }

    [Fact]
    public async Task Cancel_refuses_an_already_cancelled_booking()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var booking = AddConfirmedBooking(db, slot, alice.Id, bob.Id);
        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));

        Assert.True((await svc.CancelAsync(booking.Id, bob.Id, "first", Now)).Ok);
        var second = await svc.CancelAsync(booking.Id, bob.Id, "again", Now);

        Assert.False(second.Ok);
    }

    [Fact]
    public async Task GetUpcomingBookings_returns_confirmed_future_bookings_for_attendee_and_owner()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");

        var aliceHosts = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        AddConfirmedBooking(db, aliceHosts, alice.Id, bob.Id); // alice owner, bob attendee

        var bobHosts = TestDb.AddSlot(db, bob.Id, Now.AddHours(3), Now.AddHours(4));
        AddConfirmedBooking(db, bobHosts, bob.Id, alice.Id); // bob owner, alice attendee

        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));

        var aliceSchedule = await svc.GetUpcomingBookingsForUserAsync(alice.Id, Now);

        // Alice sees both: one as owner, one as attendee.
        Assert.Equal(2, aliceSchedule.Count);
        Assert.Contains(aliceSchedule, b => b.ActingUserIsOwner);
        Assert.Contains(aliceSchedule, b => !b.ActingUserIsOwner);
    }
}
