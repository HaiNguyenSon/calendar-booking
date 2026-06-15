using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Tests;

public class ApprovalServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    private static BookingRequest AddPendingRequest(CalendarBooking.Data.AppDbContext db, Guid slotId, string requesterId)
    {
        var request = new BookingRequest
        {
            Id = Guid.NewGuid(),
            SlotId = slotId,
            RequesterId = requesterId,
            Status = RequestStatus.Pending,
            CreatedUtc = Now,
        };
        db.BookingRequests.Add(request);
        db.SaveChanges();
        return request;
    }

    [Fact]
    public async Task Approve_confirms_a_booking_books_the_slot_and_supersedes_other_requests()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var carol = TestDb.AddUser(db, "carol");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        var bobReq = AddPendingRequest(db, slot.Id, bob.Id);
        var carolReq = AddPendingRequest(db, slot.Id, carol.Id);
        var svc = new ApprovalService(db, new NotificationService(db));

        var result = await svc.ApproveAsync(bobReq.Id, alice.Id, Now);

        Assert.True(result.Ok);
        var booking = await db.Bookings.SingleAsync();
        Assert.Equal(bob.Id, booking.AttendeeId);
        Assert.Equal(bob.Id, booking.CreatedById);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.True((await db.AvailabilitySlots.SingleAsync()).IsBooked);
        Assert.Equal(RequestStatus.Approved, (await db.BookingRequests.FindAsync(bobReq.Id))!.Status);
        Assert.Equal(RequestStatus.Superseded, (await db.BookingRequests.FindAsync(carolReq.Id))!.Status);
    }

    [Fact]
    public async Task Approve_refuses_a_request_for_someone_elses_slot()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var mallory = TestDb.AddUser(db, "mallory");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        var req = AddPendingRequest(db, slot.Id, bob.Id);
        var svc = new ApprovalService(db, new NotificationService(db));

        var result = await svc.ApproveAsync(req.Id, mallory.Id, Now);

        Assert.False(result.Ok);
        Assert.Equal(0, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task Decline_marks_the_request_declined_and_leaves_the_slot_open()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        var req = AddPendingRequest(db, slot.Id, bob.Id);
        var svc = new ApprovalService(db, new NotificationService(db));

        var result = await svc.DeclineAsync(req.Id, alice.Id);

        Assert.True(result.Ok);
        Assert.Equal(RequestStatus.Declined, (await db.BookingRequests.FindAsync(req.Id))!.Status);
        Assert.False((await db.AvailabilitySlots.SingleAsync()).IsBooked);
    }

    [Fact]
    public async Task GetIncomingRequests_returns_pending_requests_for_the_owners_open_slots()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2), SlotType.ApprovalRequired);
        AddPendingRequest(db, slot.Id, bob.Id);
        var svc = new ApprovalService(db, new NotificationService(db));

        var incoming = await svc.GetIncomingRequestsAsync(alice.Id, Now);

        Assert.Single(incoming);
        Assert.Equal("bob", incoming[0].RequesterNickname);
    }

    [Fact]
    public async Task BookOnBehalf_requires_a_prior_booking_relationship()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var stranger = TestDb.AddUser(db, "stranger");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var svc = new ApprovalService(db, new NotificationService(db));

        var result = await svc.BookOnBehalfAsync(alice.Id, slot.Id, "stranger", Now);

        Assert.False(result.Ok);
        Assert.Equal(0, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task BookOnBehalf_succeeds_with_history_and_books_the_slot_for_the_client()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var client = TestDb.AddUser(db, "client");
        // Prior relationship (either direction counts): a past booking, even cancelled.
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            SlotId = Guid.NewGuid(),
            OwnerId = client.Id,
            AttendeeId = alice.Id,
            CreatedById = client.Id,
            Status = BookingStatus.Cancelled,
            CreatedUtc = Now.AddDays(-30),
        });
        db.SaveChanges();
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var svc = new ApprovalService(db, new NotificationService(db));

        var result = await svc.BookOnBehalfAsync(alice.Id, slot.Id, "client", Now);

        Assert.True(result.Ok);
        var created = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Confirmed);
        Assert.Equal(client.Id, created.AttendeeId); // attendee is the client
        Assert.Equal(alice.Id, created.CreatedById); // owner-initiated
        Assert.True((await db.AvailabilitySlots.SingleAsync()).IsBooked);
    }

    [Fact]
    public async Task BookOnBehalf_refuses_booking_for_yourself()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var slot = TestDb.AddSlot(db, alice.Id, Now.AddHours(1), Now.AddHours(2));
        var svc = new ApprovalService(db, new NotificationService(db));

        var result = await svc.BookOnBehalfAsync(alice.Id, slot.Id, "alice", Now);

        Assert.False(result.Ok);
    }
}
