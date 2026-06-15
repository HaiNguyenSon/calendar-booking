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

authenticationBuilder.AddIdentityCookies();

// ASP.NET Core Identity, storing users/roles in our AppDbContext. AddSignInManager
// is now included so the login page can sign users in.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // Email confirmation is not wired up until Phase 5. Keeping this false lets a
        // newly registered user log in immediately; flip it to true once SMTP exists.
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Identity needs an email sender registered; this one is a no-op until Phase 5.
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Domain services.
builder.Services.Configure<CalendarBooking.Services.BookingOptions>(builder.Configuration.GetSection("Booking"));
builder.Services.AddScoped<CalendarBooking.Services.AvailabilityService>();
builder.Services.AddScoped<CalendarBooking.Services.BookingService>();
builder.Services.AddScoped<CalendarBooking.Services.ApprovalService>();

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

app.Run();
