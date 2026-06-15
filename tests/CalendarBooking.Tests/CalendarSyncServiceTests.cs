using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarBooking.Tests;

public class CalendarSyncServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    private sealed class FakeClient(string provider, IReadOnlyList<BusyInterval> busy) : IExternalCalendarClient
    {
        public string Provider { get; } = provider;
        public int PushCount { get; private set; }

        public Task PushBookingAsync(string userId, Booking booking, CancellationToken ct = default)
        {
            PushCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BusyInterval>> GetBusyIntervalsAsync(
            string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
            => Task.FromResult(busy);
    }

    [Fact]
    public void HasProviders_is_false_when_none_are_registered()
    {
        var svc = new CalendarSyncService(Array.Empty<IExternalCalendarClient>(), NullLogger<CalendarSyncService>.Instance);
        Assert.False(svc.HasProviders);
    }

    [Fact]
    public async Task PushBooking_pushes_to_every_registered_provider()
    {
        var a = new FakeClient("A", Array.Empty<BusyInterval>());
        var b = new FakeClient("B", Array.Empty<BusyInterval>());
        var svc = new CalendarSyncService(new IExternalCalendarClient[] { a, b }, NullLogger<CalendarSyncService>.Instance);

        await svc.PushBookingAsync("user-1", new Booking());

        Assert.Equal(1, a.PushCount);
        Assert.Equal(1, b.PushCount);
    }

    [Fact]
    public async Task GetBusyIntervals_aggregates_across_providers()
    {
        var a = new FakeClient("A", new[] { new BusyInterval(Now, Now.AddHours(1)) });
        var b = new FakeClient("B", new[] { new BusyInterval(Now.AddHours(2), Now.AddHours(3)) });
        var svc = new CalendarSyncService(new IExternalCalendarClient[] { a, b }, NullLogger<CalendarSyncService>.Instance);

        var busy = await svc.GetBusyIntervalsAsync("user-1", Now, Now.AddDays(1));

        Assert.Equal(2, busy.Count);
    }
}
