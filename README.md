# CalendarBooking

A multi-user booking calendar web app. Every user has their own calendar: they open
availability slots on it and book slots on others'. Some slots are instant-book; others
require the owner's approval. **Browsing calendars is public; any action (book, approve,
decline, cancel) requires login.** Every user is symmetric — anyone can both offer slots
and book others as a client.

> **Status:** Early scaffold. The solution is the default Blazor Web App template
> (Home / Counter / Weather pages). The product described below is the plan being built
> toward — see [`BOOKING-PLAN.md`](BOOKING-PLAN.md) for the full design and phased roadmap.

## Tech stack

| Area | Choice |
|------|--------|
| Framework | .NET 8.0 |
| App | ASP.NET Core **Blazor Web App**, Server interactive render mode (global) |
| Database | PostgreSQL (planned) via Docker for local dev |
| ORM | EF Core (planned) |
| Auth | ASP.NET Core Identity + Google external login (planned) |
| Real-time | SignalR (built into Blazor Server) |

**Why Blazor Server:** real-time in-app notifications come essentially free (Server runs
over SignalR), it's a single app to run and deploy, and booking logic, OAuth secrets, and
DB access all run server-side — exactly what a multi-user app with double-booking guards
needs.

## Getting started

### Prerequisites
- [.NET SDK 8.0](https://dotnet.microsoft.com/download) (see [`global.json`](global.json))
- [Docker](https://www.docker.com/) — runs the local PostgreSQL database
- EF Core CLI tools (for migrations): `dotnet tool install --global dotnet-ef`

### Run

```bash
# 1. Start the database
docker compose up -d

# 2. Apply migrations (creates the schema on first run)
dotnet ef database update --project CalendarBooking

# 3. Run the app
dotnet run --project CalendarBooking
```

Then open the URL printed in the console:
- HTTPS: <https://localhost:7043>
- HTTP: <http://localhost:5196>

Register a local account (email + password + a public nickname), or use Google once
it's configured (see below).

### Build

```bash
dotnet build
```

### Enabling Google login (optional)

Google login is off until you supply OAuth credentials, so the app runs fine without
them. To enable it locally:

1. In the [Google Cloud Console](https://console.cloud.google.com/), create an OAuth
   2.0 Client ID (type *Web application*) and add this authorized redirect URI:
   `https://localhost:7043/signin-google`.
2. Store the credentials with [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
   (kept out of source control):

   ```bash
   cd CalendarBooking
   dotnet user-secrets init
   dotnet user-secrets set "Authentication:Google:ClientId" "<your-client-id>"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "<your-client-secret>"
   ```

3. Restart the app — a "Google" button now appears on the login/register pages.

Security note: an external login is auto-linked to an existing local account only when
the provider reports the email as verified. We never link by email address alone.

## Project structure

```
CalendarBooking.sln
CalendarBooking/
  Program.cs                 # App startup & DI
  Components/
    App.razor, Routes.razor  # Root + routing
    Layout/                  # MainLayout, NavMenu
    Pages/                   # Home, Counter, Weather, Error
  wwwroot/                   # Static assets (CSS, Bootstrap, favicon)
BOOKING-PLAN.md              # Full product design & roadmap
global.json                  # Pins the .NET SDK
```

## Roadmap

Build order targets the core loop first, then layers on notifications and sync:

- **Phase 0** — Solution setup, PostgreSQL + EF Core, Identity + Google login
- **Phase 1** — Calendar & availability slots (one-off and recurring)
- **Phase 2** — Booking flow (instant vs. approval), double-booking prevention
- **Phase 3** — Approval, contention handling, owner on-behalf-of bookings
- **Phase 4** — Cancellation (either party, required reason)
- **Phase 5** — Notifications (in-app via SignalR, email)
- **Phase 6** — Polish & hardening
- **Phase 7** — External calendar sync (Google Calendar, Microsoft Graph)

See [`BOOKING-PLAN.md`](BOOKING-PLAN.md) for the detailed design, including the booking
entity model, the pending-request cap, contention rules, and the security-critical
account-linking and concurrency guarantees.
