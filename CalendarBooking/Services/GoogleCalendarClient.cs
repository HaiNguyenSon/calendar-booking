using CalendarBooking.Data;
using CalendarBooking.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Talks to Google Calendar on a user's behalf using their stored refresh token. Registered
/// as an <see cref="IExternalCalendarClient"/> only when Google is configured, so
/// <see cref="CalendarSyncService"/> picks it up automatically. If a user hasn't connected
/// their calendar, every method is a no-op for them.
/// </summary>
public class GoogleCalendarClient(
    AppDbContext db,
    GoogleCalendarFlowFactory flowFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<GoogleCalendarClient> logger) : IExternalCalendarClient
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("GoogleCalendarRefreshToken");

    public string Provider => GoogleCalendarFlowFactory.Provider;

    private async Task<CalendarService?> CreateServiceAsync(string userId, CancellationToken ct)
    {
        var connection = await db.ExternalCalendarConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == Provider, ct);
        if (connection is null)
        {
            return null;
        }

        string refreshToken;
        try
        {
            refreshToken = protector.Unprotect(connection.EncryptedRefreshToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not decrypt the Google refresh token for {UserId}.", userId);
            return null;
        }

        // UserCredential refreshes the access token from the refresh token as needed.
        var credential = new UserCredential(flowFactory.CreateFlow(), userId,
            new TokenResponse { RefreshToken = refreshToken });

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = GoogleCalendarFlowFactory.ApplicationName,
        });
    }

    public async Task PushBookingAsync(string userId, Booking booking, CancellationToken ct = default)
    {
        var service = await CreateServiceAsync(userId, ct);
        if (service is null)
        {
            return;
        }

        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == booking.SlotId, ct);
        if (slot is null)
        {
            return;
        }

        var calendarEvent = new Event
        {
            Summary = "CalendarBooking reservation",
            Start = new EventDateTime { DateTimeDateTimeOffset = ToUtcOffset(slot.StartUtc) },
            End = new EventDateTime { DateTimeDateTimeOffset = ToUtcOffset(slot.EndUtc) },
        };

        await service.Events.Insert(calendarEvent, "primary").ExecuteAsync(ct);
    }

    public async Task<IReadOnlyList<BusyInterval>> GetBusyIntervalsAsync(
        string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var service = await CreateServiceAsync(userId, ct);
        if (service is null)
        {
            return Array.Empty<BusyInterval>();
        }

        var request = service.Freebusy.Query(new FreeBusyRequest
        {
            TimeMinDateTimeOffset = ToUtcOffset(fromUtc),
            TimeMaxDateTimeOffset = ToUtcOffset(toUtc),
            Items = new List<FreeBusyRequestItem> { new() { Id = "primary" } },
        });

        var response = await request.ExecuteAsync(ct);
        if (response.Calendars is null || !response.Calendars.TryGetValue("primary", out var calendar) || calendar.Busy is null)
        {
            return Array.Empty<BusyInterval>();
        }

        var result = new List<BusyInterval>();
        foreach (var period in calendar.Busy)
        {
            if (period.StartDateTimeOffset is { } start && period.EndDateTimeOffset is { } end)
            {
                result.Add(new BusyInterval(start.UtcDateTime, end.UtcDateTime));
            }
        }
        return result;
    }

    private static DateTimeOffset ToUtcOffset(DateTime utc) =>
        new(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), TimeSpan.Zero);
}
