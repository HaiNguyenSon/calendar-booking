using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CalendarBooking.Services;

/// <summary>
/// Microsoft Graph calendar provider over plain HTTP (no SDK). Pushes/deletes events and
/// reads free/busy on the user's primary calendar, using an access token minted by
/// <see cref="MicrosoftCalendarConnector"/>. Registered as an <see cref="IExternalCalendarClient"/>
/// only when Microsoft is configured.
///
/// Scaffold: builds, but has not been exercised against the live Graph API.
/// </summary>
public class MicrosoftCalendarClient(
    AppDbContext db,
    MicrosoftCalendarConnector connector,
    IHttpClientFactory httpClientFactory,
    ILogger<MicrosoftCalendarClient> logger) : IExternalCalendarClient
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    public string Provider => MicrosoftCalendarConnector.Provider;

    private async Task<HttpClient?> CreateClientAsync(string userId, CancellationToken ct)
    {
        var token = await connector.GetAccessTokenAsync(userId, ct);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string?> PushBookingAsync(string userId, Booking booking, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(userId, ct);
        if (client is null)
        {
            return null;
        }

        var slot = await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == booking.SlotId, ct);
        if (slot is null)
        {
            return null;
        }

        var body = new
        {
            subject = "CalendarBooking reservation",
            start = new { dateTime = slot.StartUtc.ToString("o"), timeZone = "UTC" },
            end = new { dateTime = slot.EndUtc.ToString("o"), timeZone = "UTC" },
        };

        var response = await client.PostAsJsonAsync($"{GraphBase}/me/events", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Graph create event failed: {Status}", response.StatusCode);
            return null;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    public async Task DeleteEventAsync(string userId, string eventId, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(userId, ct);
        if (client is null)
        {
            return;
        }
        await client.DeleteAsync($"{GraphBase}/me/events/{eventId}", ct);
    }

    public async Task<IReadOnlyList<BusyInterval>> GetBusyIntervalsAsync(
        string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(userId, ct);
        if (client is null)
        {
            return Array.Empty<BusyInterval>();
        }

        // We need the user's own address for getSchedule; ask Graph for it.
        var meResponse = await client.GetAsync($"{GraphBase}/me", ct);
        if (!meResponse.IsSuccessStatusCode)
        {
            return Array.Empty<BusyInterval>();
        }
        using var me = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync(ct));
        var address = me.RootElement.TryGetProperty("mail", out var mail) && mail.GetString() is { Length: > 0 } m
            ? m
            : me.RootElement.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null;
        if (address is null)
        {
            return Array.Empty<BusyInterval>();
        }

        var body = new
        {
            schedules = new[] { address },
            startTime = new { dateTime = fromUtc.ToString("o"), timeZone = "UTC" },
            endTime = new { dateTime = toUtc.ToString("o"), timeZone = "UTC" },
            availabilityViewInterval = 30,
        };

        var response = await client.PostAsJsonAsync($"{GraphBase}/me/calendar/getSchedule", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<BusyInterval>();
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = new List<BusyInterval>();
        if (json.RootElement.TryGetProperty("value", out var schedules))
        {
            foreach (var schedule in schedules.EnumerateArray())
            {
                if (!schedule.TryGetProperty("scheduleItems", out var items))
                {
                    continue;
                }
                foreach (var item in items.EnumerateArray())
                {
                    var start = item.GetProperty("start").GetProperty("dateTime").GetString();
                    var end = item.GetProperty("end").GetProperty("dateTime").GetString();
                    if (DateTime.TryParse(start, out var s) && DateTime.TryParse(end, out var e))
                    {
                        result.Add(new BusyInterval(
                            DateTime.SpecifyKind(s, DateTimeKind.Utc),
                            DateTime.SpecifyKind(e, DateTimeKind.Utc)));
                    }
                }
            }
        }
        return result;
    }
}
