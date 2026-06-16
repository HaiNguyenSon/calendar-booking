using CalendarBooking.Services;

namespace CalendarBooking.Tests;

public class WeeklyAvailabilityTests
{
    // A Monday in UTC so the day math is deterministic.
    private static readonly DateTime Monday = new(2030, 1, 7, 0, 0, 0, DateTimeKind.Utc);

    private static int Bit(DayOfWeek d) => WeeklyAvailability.DayBit(d);

    [Fact]
    public void Expand_splits_the_window_into_back_to_back_slots_on_selected_days()
    {
        var nowUtc = Monday.AddDays(-1); // everything is in the future
        var mask = Bit(DayOfWeek.Monday) | Bit(DayOfWeek.Wednesday);

        // 09:00–11:00, 30-min slots = 4 per day, over Mon..Sun (one Mon + one Wed) = 8.
        var occ = WeeklyAvailability.Expand(mask, 9 * 60, 11 * 60, 30, TimeZoneInfo.Utc,
            Monday.Date, Monday.Date.AddDays(6), nowUtc);

        Assert.Equal(8, occ.Count);
        Assert.All(occ, o => Assert.Equal(30, (o.EndUtc - o.StartUtc).TotalMinutes));
        Assert.All(occ, o => Assert.True(o.StartUtc.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday));
    }

    [Fact]
    public void Expand_drops_a_trailing_partial_slot()
    {
        var nowUtc = Monday.AddDays(-1);
        var mask = Bit(DayOfWeek.Monday);

        // 09:00–10:00 with 45-min slots → only 09:00–09:45 fits (09:45–10:30 exceeds).
        var occ = WeeklyAvailability.Expand(mask, 9 * 60, 10 * 60, 45, TimeZoneInfo.Utc,
            Monday.Date, Monday.Date, nowUtc);

        Assert.Single(occ);
        Assert.Equal(45, (occ[0].EndUtc - occ[0].StartUtc).TotalMinutes);
    }

    [Fact]
    public void Expand_excludes_past_occurrences()
    {
        // "Now" is Monday 09:30, so the 09:00 slot is past and excluded; 10:00 and 11:00 remain.
        var nowUtc = Monday.AddHours(9).AddMinutes(30);
        var mask = Bit(DayOfWeek.Monday);

        var occ = WeeklyAvailability.Expand(mask, 9 * 60, 12 * 60, 60, TimeZoneInfo.Utc,
            Monday.Date, Monday.Date, nowUtc);

        Assert.Equal(2, occ.Count); // 10:00 and 11:00 (09:00 dropped)
        Assert.All(occ, o => Assert.True(o.StartUtc > nowUtc));
    }
}
