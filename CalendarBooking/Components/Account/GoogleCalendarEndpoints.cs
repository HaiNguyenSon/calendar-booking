using CalendarBooking.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalendarBooking.Components.Account;

/// <summary>
/// HTTP endpoints for connecting/disconnecting Google Calendar. These run as plain requests
/// (not over the Blazor circuit) because they redirect out to Google's consent screen and
/// handle its redirect back.
/// </summary>
internal static class GoogleCalendarEndpoints
{
    private const string StateCookie = "GoogleCalendarOAuthState";
    private const string CallbackPath = "/Account/Calendar/Callback";

    public static IEndpointConventionBuilder MapGoogleCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/Account/Calendar").RequireAuthorization();

        // Start: redirect the signed-in user to Google's consent screen.
        group.MapGet("/Connect", (HttpContext http, GoogleCalendarConnector connector) =>
        {
            if (!connector.IsConfigured)
            {
                return Results.Redirect("/my?calendar=unconfigured");
            }

            var state = Guid.NewGuid().ToString("N");
            http.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10),
            });

            var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}{CallbackPath}";
            return Results.Redirect(connector.BuildConsentUrl(redirectUri, state));
        });

        // Google redirects back here with ?code=...&state=...
        group.MapGet("/Callback", async (
            HttpContext http,
            GoogleCalendarConnector connector,
            [FromQuery] string? code,
            [FromQuery] string? state) =>
        {
            var expectedState = http.Request.Cookies[StateCookie];
            http.Response.Cookies.Delete(StateCookie);

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || state != expectedState)
            {
                return Results.Redirect("/my?calendar=error");
            }

            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Redirect("/my?calendar=error");
            }

            var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}{CallbackPath}";
            try
            {
                var ok = await connector.CompleteAsync(userId, code, redirectUri);
                return Results.Redirect(ok ? "/my?calendar=connected" : "/my?calendar=error");
            }
            catch (Exception ex)
            {
                // A bad/expired/denied code makes the token exchange throw — don't 500.
                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleCalendarCallback")
                    .LogError(ex, "Google Calendar token exchange failed.");
                return Results.Redirect("/my?calendar=error");
            }
        });

        // Disconnect (POST so it carries the antiforgery token from the form).
        group.MapPost("/Disconnect", async (HttpContext http, GoogleCalendarConnector connector) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                await connector.DisconnectAsync(userId);
            }
            return Results.Redirect("/my?calendar=disconnected");
        });

        return group;
    }
}
