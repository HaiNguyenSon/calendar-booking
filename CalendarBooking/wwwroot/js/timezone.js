// The browser is the only place that knows the user's timezone. We read the IANA zone
// id (e.g. "Europe/Oslo") once and hand it to the server, which does all UTC<->local
// conversion in C# with TimeZoneInfo. This is the one piece that genuinely can't be done
// server-side.
window.calendarBooking = {
    getTimeZone: function () {
        try {
            return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
        } catch {
            return 'UTC';
        }
    }
};
