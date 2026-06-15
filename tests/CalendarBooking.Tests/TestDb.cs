using CalendarBooking.Data;
using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarBooking.Tests;

/// <summary>
/// Helpers for spinning up an isolated in-memory AppDbContext and seeding data. Each
/// context uses a unique database name so tests don't interfere with each other.
///
/// Note: the in-memory provider ignores relational constructs (the partial unique index,
/// transactions), so these tests exercise the services' OWN logic and guard checks. The
/// database-level double-booking guarantee is enforced by Postgres in the real app.
/// </summary>
internal static class TestDb
{
    /// <summary>A fixed "now" so time-based rules are deterministic.</summary>
    public static readonly DateTime Now = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A CalendarSyncService with no external providers (HasProviders == false).</summary>
    public static CalendarSyncService NoSync() =>
        new(Array.Empty<IExternalCalendarClient>(), NullLogger<CalendarSyncService>.Instance);

    /// <summary>A CalendarSyncService whose single provider reports the given busy intervals.</summary>
    public static CalendarSyncService SyncWithBusy(params BusyInterval[] busy) =>
        new(new IExternalCalendarClient[] { new StubBusyClient(busy) }, NullLogger<CalendarSyncService>.Instance);

    private sealed class StubBusyClient(IReadOnlyList<BusyInterval> busy) : IExternalCalendarClient
    {
        public string Provider => "Stub";
        public Task<string?> PushBookingAsync(string userId, Booking booking, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task DeleteEventAsync(string userId, string eventId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<BusyInterval>> GetBusyIntervalsAsync(
            string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) => Task.FromResult(busy);
    }

    public static AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"test-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    public static ApplicationUser AddUser(AppDbContext db, string nickname)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{nickname}@example.com",
            Email = $"{nickname}@example.com",
            Nickname = nickname,
            PublicId = PublicCode.New(),
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static AvailabilitySlot AddSlot(
        AppDbContext db, string ownerId, DateTime startUtc, DateTime endUtc,
        SlotType type = SlotType.Instant, bool booked = false)
    {
        var slot = new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            SlotType = type,
            IsBooked = booked,
            CreatedUtc = startUtc.AddDays(-1),
        };
        db.AvailabilitySlots.Add(slot);
        db.SaveChanges();
        return slot;
    }
}
