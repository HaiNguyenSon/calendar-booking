using CalendarBooking.Data;
using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace CalendarBooking.Tests;

/// <summary>
/// Integration tests against a REAL PostgreSQL (via Testcontainers) — needs Docker. These
/// exercise the database-level guarantees the in-memory provider can't: the partial unique
/// index on confirmed Booking.SlotId. The schema is created with the actual migrations.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync(); // applies real migrations, incl. the partial unique index
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
}

public class DoubleBookingIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private BookingService NewBooking(AppDbContext db) =>
        new(db, Options.Create(new BookingOptions()), new NotificationService(db, new NotificationBroadcaster()));

    private static ApplicationUser MakeUser()
    {
        var tag = Guid.NewGuid().ToString("N");
        return new ApplicationUser { Id = tag, UserName = $"{tag}@x.test", Email = $"{tag}@x.test", Nickname = tag };
    }

    private async Task<(string ownerId, string a1, string a2, Guid slotId)> SeedInstantSlotAsync()
    {
        await using var db = fixture.CreateContext();
        var owner = MakeUser();
        var attendee1 = MakeUser();
        var attendee2 = MakeUser();
        db.Users.AddRange(owner, attendee1, attendee2);

        var slot = new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            StartUtc = DateTime.UtcNow.AddHours(1),
            EndUtc = DateTime.UtcNow.AddHours(2),
            SlotType = SlotType.Instant,
            CreatedUtc = DateTime.UtcNow,
        };
        db.AvailabilitySlots.Add(slot);
        await db.SaveChangesAsync();
        return (owner.Id, attendee1.Id, attendee2.Id, slot.Id);
    }

    [Fact]
    public async Task Two_simultaneous_bookings_of_the_same_instant_slot_yield_exactly_one_winner()
    {
        var (_, attendee1, attendee2, slotId) = await SeedInstantSlotAsync();
        var now = DateTime.UtcNow;

        // Two independent contexts/connections booking the SAME slot at the same time.
        await using var dbA = fixture.CreateContext();
        await using var dbB = fixture.CreateContext();
        var taskA = NewBooking(dbA).BookInstantAsync(slotId, attendee1, now);
        var taskB = NewBooking(dbB).BookInstantAsync(slotId, attendee2, now);
        var results = await Task.WhenAll(taskA, taskB);

        // The partial unique index lets exactly one confirmed booking exist for the slot.
        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, results.Count(r => !r.Ok));

        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Bookings.CountAsync(b => b.SlotId == slotId && b.Status == BookingStatus.Confirmed));
    }

    [Fact]
    public async Task A_slot_can_be_rebooked_after_its_booking_is_cancelled()
    {
        var (ownerId, attendee1, attendee2, slotId) = await SeedInstantSlotAsync();
        var now = DateTime.UtcNow;

        await using (var db = fixture.CreateContext())
        {
            var first = await NewBooking(db).BookInstantAsync(slotId, attendee1, now);
            Assert.True(first.Ok);
        }

        // Cancel it: frees the slot, keeps the cancelled row.
        await using (var db = fixture.CreateContext())
        {
            var cancel = new CancellationService(db, new NotificationService(db, new NotificationBroadcaster()));
            var booking = await db.Bookings.FirstAsync(b => b.SlotId == slotId);
            var result = await cancel.CancelAsync(booking.Id, attendee1, "changed plans", now);
            Assert.True(result.Ok);
        }

        // Re-booking the freed slot succeeds — the unique index is filtered to confirmed rows,
        // so the kept cancelled row doesn't block it.
        await using (var db = fixture.CreateContext())
        {
            var second = await NewBooking(db).BookInstantAsync(slotId, attendee2, now);
            Assert.True(second.Ok);
        }

        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Bookings.CountAsync(b => b.SlotId == slotId && b.Status == BookingStatus.Confirmed));
        Assert.Equal(1, await verify.Bookings.CountAsync(b => b.SlotId == slotId && b.Status == BookingStatus.Cancelled));
    }
}
