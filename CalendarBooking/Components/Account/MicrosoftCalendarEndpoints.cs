using CalendarBooking.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalendarBooking.Components.Account;

/// <summary>
/// Connect/disconnect endpoints for Microsoft (Graph) calendar, mirroring the Google ones.
/// </summary>
internal static class MicrosoftCalendarEndpoints
{
    private const string StateCookie = "MicrosoftCalendarOAuthState";
    private const string CallbackPath = "/Account/Calendar/MicrosoftCallback";

    public static IEndpointConventionBuilder MapMicrosoftCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/Account/Calendar").RequireAuthorization();

        group.MapGet("/ConnectMicrosoft", (HttpContext http, MicrosoftCalendarConnector connector) =>
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

        group.MapGet("/MicrosoftCallback", async (
            HttpContext http,
            MicrosoftCalendarConnector connector,
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
                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MicrosoftCalendarCallback")
                    .LogError(ex, "Microsoft Calendar token exchange failed.");
                return Results.Redirect("/my?calendar=error");
            }
        });

        group.MapPost("/DisconnectMicrosoft", async (HttpContext http, MicrosoftCalendarConnector connector) =>
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
