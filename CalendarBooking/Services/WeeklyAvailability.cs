namespace CalendarBooking.Services;

/// <summary>
/// Expands a weekly availability pattern (which weekdays, a daily time window, a slot length)
/// into concrete UTC slot ranges over a date span. Pure and timezone-aware — the window is
/// local time-of-day, converted to UTC per occurrence so it stays correct across DST. Used by
/// both the one-off generator and the standing-rule materializer.
/// </summary>
public static class WeeklyAvailability
{
    /// <summary>Bit per <see cref="DayOfWeek"/> (Sunday = 0 … Saturday = 6).</summary>
    public static int DayBit(DayOfWeek day) => 1 << (int)day;

    public static IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> Expand(
        int daysMask, int startMinutes, int endMinutes, int slotMinutes,
        TimeZoneInfo tz, DateTime fromLocalDate, DateTime toLocalDate, DateTime nowUtc)
    {
        var occurrences = new List<(DateTime, DateTime)>();
        if (daysMask == 0 || endMinutes <= startMinutes || slotMinutes <= 0)
        {
            return occurrences;
        }

        for (var day = fromLocalDate.Date; day <= toLocalDate.Date; day = day.AddDays(1))
        {
            if ((daysMask & DayBit(day.DayOfWeek)) == 0)
            {
                continue;
            }

            for (var m = startMinutes; m + slotMinutes <= endMinutes; m += slotMinutes)
            {
                var localStart = DateTime.SpecifyKind(day.AddMinutes(m), DateTimeKind.Unspecified);
                var localEnd = DateTime.SpecifyKind(day.AddMinutes(m + slotMinutes), DateTimeKind.Unspecified);
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
                var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);
                if (startUtc > nowUtc)
                {
                    occurrences.Add((startUtc, endUtc));
                }
            }
        }
        return occurrences;
    }
}
