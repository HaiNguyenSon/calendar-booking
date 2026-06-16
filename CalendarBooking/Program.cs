using CalendarBooking.Components;
using CalendarBooking.Components.Account;
using CalendarBooking.Data;
using CalendarBooking.Domain;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Make the signed-in user available to components as a cascading AuthenticationState,
// and supply/revalidate that state for interactive Server circuits.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Database: EF Core on PostgreSQL, using the "DefaultConnection" string from
// appsettings.json (points at the local Docker Postgres in development).
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Authentication uses Identity's cookie schemes. The application cookie carries the
// signed-in user; the external cookie is a temporary holder during external (Google)
// logins.
var authenticationBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    });

// Google login is enabled only when credentials are configured (e.g. via user-secrets
// in development, or environment variables in production), so the app still runs
// without them. To enable locally:
//   dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>"
//   dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>"
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // Surface Google's "email_verified" flag as a claim so the account-linking rule
        // can require it (see ExternalLogin.razor). We read it straight from the raw
        // userinfo JSON Google returns.
        options.Events.OnCreatingTicket = context =>
        {
            if (context.User.TryGetProperty("email_verified", out var verified)
                && verified.ValueKind == JsonValueKind.True)
            {
                context.Identity?.AddClaim(new Claim("email_verified", "true"));
            }
            return Task.CompletedTask;
        };
    });
}

// Apple login, enabled only when configured. Needs an Apple Developer account: a Services ID
// (ClientId), a Sign in with Apple key (KeyId + the AuthKey_<KeyId>.p8 file at the content
// root), and the TeamId. Apple always reports the email as verified.
var appleClientId = builder.Configuration["Authentication:Apple:ClientId"];
var appleKeyId = builder.Configuration["Authentication:Apple:KeyId"];
var appleTeamId = builder.Configuration["Authentication:Apple:TeamId"];
if (!string.IsNullOrEmpty(appleClientId) && !string.IsNullOrEmpty(appleKeyId) && !string.IsNullOrEmpty(appleTeamId))
{
    authenticationBuilder.AddApple(options =>
    {
        options.ClientId = appleClientId;
        options.KeyId = appleKeyId;
        options.TeamId = appleTeamId;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.GenerateClientSecret = true;
        options.UsePrivateKey(keyId =>
            builder.Environment.ContentRootFileProvider.GetFileInfo($"AuthKey_{keyId}.p8"));
    });
}

authenticationBuilder.AddIdentityCookies();

// ASP.NET Core Identity, storing users/roles in our AppDbContext. AddSignInManager
// is now included so the login page can sign users in.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // Config-driven: keep false (immediate sign-in) when there's no mail server; set
        // Identity:RequireConfirmedAccount=true in production (with SMTP) to require a
        // confirmed email before login.
        options.SignIn.RequireConfirmedAccount =
            builder.Configuration.GetValue<bool>("Identity:RequireConfirmedAccount");
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Email: real SMTP when Email:Smtp:Host is configured, otherwise log-only. Identity's email
// sender (confirmation/reset) and the notification dispatcher both go through IAppEmailSender.
builder.Services.Configure<CalendarBooking.Services.EmailOptions>(builder.Configuration.GetSection("Email:Smtp"));
var smtpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Email:Smtp:Host"]);
if (smtpConfigured)
{
    builder.Services.AddSingleton<CalendarBooking.Services.IAppEmailSender, CalendarBooking.Services.SmtpAppEmailSender>();
}
else
{
    builder.Services.AddSingleton<CalendarBooking.Services.IAppEmailSender, CalendarBooking.Services.LoggingAppEmailSender>();
}
builder.Services.AddScoped<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// SMS: real Twilio when Sms:Twilio is configured, otherwise log-only. Used for phone
// confirmation codes.
builder.Services.Configure<CalendarBooking.Services.SmsOptions>(builder.Configuration.GetSection("Sms:Twilio"));
if (!string.IsNullOrWhiteSpace(builder.Configuration["Sms:Twilio:AccountSid"]))
{
    builder.Services.AddSingleton<CalendarBooking.Services.ISmsSender, CalendarBooking.Services.TwilioSmsSender>();
}
else
{
    builder.Services.AddSingleton<CalendarBooking.Services.ISmsSender, CalendarBooking.Services.LoggingSmsSender>();
}

// Domain services.
builder.Services.Configure<CalendarBooking.Services.BookingOptions>(builder.Configuration.GetSection("Booking"));
builder.Services.AddSingleton<CalendarBooking.Services.NotificationBroadcaster>();
builder.Services.AddScoped<CalendarBooking.Services.NotificationService>();
builder.Services.AddHostedService<CalendarBooking.Services.EmailDispatcherService>();
builder.Services.AddHostedService<CalendarBooking.Services.ReminderService>();
builder.Services.AddScoped<CalendarBooking.Services.AvailabilityService>();
builder.Services.AddScoped<CalendarBooking.Services.BookingService>();
builder.Services.AddScoped<CalendarBooking.Services.ApprovalService>();
builder.Services.AddScoped<CalendarBooking.Services.CancellationService>();
builder.Services.AddScoped<CalendarBooking.Services.AccountCleanupService>();
builder.Services.AddScoped<CalendarBooking.Services.SubscriptionService>();
builder.Services.AddHostedService<CalendarBooking.Services.StaleRequestService>();

// External calendar sync (Phase 7). The dispatcher fans out to any registered
// IExternalCalendarClient. See docs/EXTERNAL-SYNC.md.
builder.Services.AddScoped<CalendarBooking.Services.CalendarSyncService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CalendarBooking.Services.GoogleCalendarFlowFactory>();
builder.Services.AddScoped<CalendarBooking.Services.GoogleCalendarConnector>();
builder.Services.AddScoped<CalendarBooking.Services.MicrosoftCalendarConnector>();
// Register each external client only when its credentials are configured.
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddScoped<CalendarBooking.Services.IExternalCalendarClient, CalendarBooking.Services.GoogleCalendarClient>();
}
if (!string.IsNullOrEmpty(builder.Configuration["Authentication:Microsoft:ClientId"])
    && !string.IsNullOrEmpty(builder.Configuration["Authentication:Microsoft:ClientSecret"]))
{
    builder.Services.AddScoped<CalendarBooking.Services.IExternalCalendarClient, CalendarBooking.Services.MicrosoftCalendarClient>();
}
// The push dispatcher idles when no providers are configured, so it's always registered.
builder.Services.AddHostedService<CalendarBooking.Services.CalendarSyncDispatcher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Maps the account HTTP endpoints (e.g. POST /Account/Logout).
app.MapAdditionalIdentityEndpoints();
app.MapGoogleCalendarEndpoints();
app.MapMicrosoftCalendarEndpoints();

app.Run();

// Exposed so the integration test host (WebApplicationFactory<Program>) can boot the app.
public partial class Program { }
