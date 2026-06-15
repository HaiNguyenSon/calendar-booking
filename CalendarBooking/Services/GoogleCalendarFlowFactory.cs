using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Calendar.v3;

namespace CalendarBooking.Services;

/// <summary>
/// Builds the Google OAuth authorization-code flow from the same credentials used for
/// Google login (Authentication:Google), but with calendar scope. Registered as a
/// singleton; <see cref="IsConfigured"/> is false when no credentials are set.
/// </summary>
public class GoogleCalendarFlowFactory(IConfiguration config)
{
    public const string ApplicationName = "CalendarBooking";
    public const string Provider = "Google";

    private string? ClientId => config["Authentication:Google:ClientId"];
    private string? ClientSecret => config["Authentication:Google:ClientSecret"];

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);

    public GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
        // Calendar scope covers both pushing events and reading free/busy.
        Scopes = new[] { CalendarService.Scope.Calendar },
    });
}
