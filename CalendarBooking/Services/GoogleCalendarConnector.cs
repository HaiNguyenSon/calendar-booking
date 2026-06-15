using CalendarBooking.Data;
using CalendarBooking.Domain;
using Google.Apis.Auth.OAuth2.Requests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Services;

/// <summary>
/// Connects/disconnects a user's Google Calendar: builds the consent URL, exchanges the
/// returned code for a refresh token, and stores it encrypted. The refresh token is the
/// long-lived secret that lets <see cref="GoogleCalendarClient"/> act offline, so it is
/// protected with Data Protection and never stored in plaintext.
/// </summary>
public class GoogleCalendarConnector(
    AppDbContext db,
    GoogleCalendarFlowFactory flowFactory,
    IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("GoogleCalendarRefreshToken");

    public bool IsConfigured => flowFactory.IsConfigured;

    /// <summary>The Google consent screen URL to redirect the user to.</summary>
    public string BuildConsentUrl(string redirectUri, string state)
    {
        var request = (GoogleAuthorizationCodeRequestUrl)flowFactory.CreateFlow().CreateAuthorizationCodeRequest(redirectUri);
        request.AccessType = "offline"; // ask for a refresh token
        request.Prompt = "consent";     // force consent so we always receive one
        request.State = state;
        return request.Build().ToString();
    }

    /// <summary>Exchange the OAuth code for a refresh token and store it (encrypted).</summary>
    public async Task<bool> CompleteAsync(string userId, string code, string redirectUri, CancellationToken ct = default)
    {
        var token = await flowFactory.CreateFlow().ExchangeCodeForTokenAsync(userId, code, redirectUri, ct);
        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            return false;
        }

        var encrypted = protector.Protect(token.RefreshToken);
        var existing = await db.ExternalCalendarConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == GoogleCalendarFlowFactory.Provider, ct);

        if (existing is null)
        {
            db.ExternalCalendarConnections.Add(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = GoogleCalendarFlowFactory.Provider,
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
        return true;
    }

    public Task<bool> IsConnectedAsync(string userId, CancellationToken ct = default) =>
        db.ExternalCalendarConnections.AnyAsync(c => c.UserId == userId && c.Provider == GoogleCalendarFlowFactory.Provider, ct);

    public async Task DisconnectAsync(string userId, CancellationToken ct = default)
    {
        var connections = await db.ExternalCalendarConnections
            .Where(c => c.UserId == userId && c.Provider == GoogleCalendarFlowFactory.Provider)
            .ToListAsync(ct);
        db.ExternalCalendarConnections.RemoveRange(connections);
        await db.SaveChangesAsync(ct);
    }
}
