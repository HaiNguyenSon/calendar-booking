using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CalendarBooking.Services;

/// <summary>
/// Microsoft (Azure AD / Graph) OAuth, done over plain HTTP to keep it self-contained.
/// Mirrors the Google connector: build a consent URL, exchange the code for a refresh token,
/// store it encrypted, and mint access tokens from it on demand. Microsoft rotates refresh
/// tokens, so a fresh one returned at access-token time is persisted.
///
/// Scaffold: needs an Azure app registration (Authentication:Microsoft) and has not been run
/// against live Microsoft endpoints.
/// </summary>
public class MicrosoftCalendarConnector(
    AppDbContext db,
    IConfiguration config,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory)
{
    public const string Provider = "Microsoft";
    public const string Scopes = "offline_access Calendars.ReadWrite";

    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("MicrosoftCalendarRefreshToken");

    private string? ClientId => config["Authentication:Microsoft:ClientId"];
    private string? ClientSecret => config["Authentication:Microsoft:ClientSecret"];
    private string Tenant => config["Authentication:Microsoft:Tenant"] ?? "common";
    private string TokenEndpoint => $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token";
    private string AuthorizeEndpoint => $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/authorize";

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);

    public string BuildConsentUrl(string redirectUri, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scopes,
            ["state"] = state,
        };
        var qs = string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
        return $"{AuthorizeEndpoint}?{qs}";
    }

    public async Task<bool> CompleteAsync(string userId, string code, string redirectUri, CancellationToken ct = default)
    {
        var refreshToken = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        }, ct);
        if (refreshToken is null)
        {
            return false;
        }

        await StoreRefreshTokenAsync(userId, refreshToken, ct);
        return true;
    }

    /// <summary>Mint a fresh access token from the stored refresh token (persisting a rotated one).</summary>
    public async Task<string?> GetAccessTokenAsync(string userId, CancellationToken ct = default)
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
        catch
        {
            return null;
        }

        using var client = httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId!,
            ["client_secret"] = ClientSecret!,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scopes,
        };
        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        // Microsoft rotates refresh tokens — keep the newest.
        if (root.TryGetProperty("refresh_token", out var rotated) && rotated.GetString() is { Length: > 0 } newToken)
        {
            connection.EncryptedRefreshToken = protector.Protect(newToken);
            await db.SaveChangesAsync(ct);
        }

        return root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
    }

    public Task<bool> IsConnectedAsync(string userId, CancellationToken ct = default) =>
        db.ExternalCalendarConnections.AnyAsync(c => c.UserId == userId && c.Provider == Provider, ct);

    public async Task DisconnectAsync(string userId, CancellationToken ct = default)
    {
        var connections = await db.ExternalCalendarConnections
            .Where(c => c.UserId == userId && c.Provider == Provider)
            .ToListAsync(ct);
        db.ExternalCalendarConnections.RemoveRange(connections);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> ExchangeAsync(Dictionary<string, string> grant, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        grant["client_id"] = ClientId!;
        grant["client_secret"] = ClientSecret!;
        grant["scope"] = Scopes;

        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(grant), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
    }

    private async Task StoreRefreshTokenAsync(string userId, string refreshToken, CancellationToken ct)
    {
        var encrypted = protector.Protect(refreshToken);
        var existing = await db.ExternalCalendarConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == Provider, ct);
        if (existing is null)
        {
            db.ExternalCalendarConnections.Add(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = Provider,
                EncryptedRefreshToken = encrypted,
                ConnectedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.EncryptedRefreshToken = encrypted;
            existing.ConnectedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
