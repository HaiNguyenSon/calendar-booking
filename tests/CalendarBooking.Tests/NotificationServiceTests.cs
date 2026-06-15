using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CalendarBooking.Tests;

public class NotificationServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    [Fact]
    public async Task Queue_then_save_creates_an_unread_notification()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new NotificationService(db, new NotificationBroadcaster());

        svc.Queue(alice.Id, "Hello", Now);
        await db.SaveChangesAsync();

        Assert.Equal(1, await svc.GetUnreadCountAsync(alice.Id));
    }

    [Fact]
    public async Task MarkAllRead_clears_the_unread_count()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new NotificationService(db, new NotificationBroadcaster());
        svc.Queue(alice.Id, "One", Now);
        svc.Queue(alice.Id, "Two", Now);
        await db.SaveChangesAsync();

        await svc.MarkAllReadAsync(alice.Id, Now);

        Assert.Equal(0, await svc.GetUnreadCountAsync(alice.Id));
        Assert.Equal(2, (await svc.GetRecentAsync(alice.Id)).Count);
    }

    [Fact]
    public async Task Booking_an_instant_slot_notifies_the_owner()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.Instant);
        var notifications = new NotificationService(db, new NotificationBroadcaster());
        var booking = new BookingService(db, Options.Create(new BookingOptions()), notifications);

        await booking.BookInstantAsync(slot.Id, bob.Id, Now);

        var ownerNotes = await db.Notifications.Where(n => n.UserId == alice.Id).ToListAsync();
        Assert.Single(ownerNotes);
        Assert.Contains("bob", ownerNotes[0].Message);
    }

    [Fact]
    public async Task Cancelling_notifies_the_other_party_with_the_reason()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
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
        slot.IsBooked = true;
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var svc = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));
        await svc.CancelAsync(booking.Id, bob.Id, "Conflict", Now);

        // The attendee cancelled, so the owner is notified, with the reason.
        var ownerNotes = await db.Notifications.Where(n => n.UserId == alice.Id).ToListAsync();
        Assert.Single(ownerNotes);
        Assert.Contains("Conflict", ownerNotes[0].Message);
    }
}
