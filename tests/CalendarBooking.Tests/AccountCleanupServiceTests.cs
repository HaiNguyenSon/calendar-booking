using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Tests;

public class AccountCleanupServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    private static AccountCleanupService NewService(CalendarBooking.Data.AppDbContext db)
        => new(db, new NotificationService(db, new NotificationBroadcaster()));

    [Fact]
    public async Task CloseAccount_anonymizes_the_user_and_disables_login()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        alice.Description = "hi there";
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var result = await svc.CloseAccountAsync(alice.Id, Now);

        Assert.True(result.Ok);
        var stored = await db.Users.FindAsync(alice.Id);
        Assert.NotNull(stored); // row kept for history
        Assert.StartsWith("deleted-", stored!.Nickname);
        Assert.Null(stored.Description);
        Assert.Null(stored.PasswordHash);
        Assert.Equal(DateTimeOffset.MaxValue, stored.LockoutEnd);
    }

    [Fact]
    public async Task CloseAccount_deletes_future_unbooked_slots_but_keeps_slots_with_history()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");

        var freeSlot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));      // deletable
        var bookedSlot = TestDb.AddSlot(db, alice.Id, Now.AddHours(3), Now.AddHours(4), booked: true);
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = bookedSlot.Id,
            OwnerId = alice.Id,
            AttendeeId = bob.Id,
            CreatedById = bob.Id,
            Status = BookingStatus.Confirmed,
            CreatedUtc = Now.AddDays(-1),
        });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        await svc.CloseAccountAsync(alice.Id, Now);

        Assert.Null(await db.AvailabilitySlots.FindAsync(freeSlot.Id));       // deleted
        Assert.NotNull(await db.AvailabilitySlots.FindAsync(bookedSlot.Id));  // kept (referenced by a booking)
    }

    [Fact]
    public async Task CloseAccount_cancels_future_bookings_frees_slots_and_notifies_the_other_party()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), booked: true);
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            OwnerId = alice.Id,
            AttendeeId = bob.Id,
            CreatedById = bob.Id,
            Status = BookingStatus.Confirmed,
            CreatedUtc = Now.AddDays(-1),
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        await svc.CloseAccountAsync(alice.Id, Now);

        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.FindAsync(booking.Id))!.Status);
        Assert.False((await db.AvailabilitySlots.FindAsync(slot.Id))!.IsBooked);
        Assert.Contains(await db.Notifications.ToListAsync(), n => n.UserId == bob.Id);
    }

    [Fact]
    public async Task CloseAccount_declines_the_users_pending_requests()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, bob.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        db.BookingRequests.Add(new BookingRequest
        {
            Id = Guid.NewGuid(),
            SlotId = slot.Id,
            RequesterId = alice.Id,
            Status = RequestStatus.Pending,
            CreatedUtc = Now,
        });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        await svc.CloseAccountAsync(alice.Id, Now);

        Assert.Equal(RequestStatus.Declined, (await db.BookingRequests.SingleAsync()).Status);
    }
}
