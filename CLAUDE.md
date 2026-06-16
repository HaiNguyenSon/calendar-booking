# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A multi-user booking calendar: every user has a calendar, opens availability slots on their own, and books slots on others'. Browsing is public; any action (book, approve, decline, cancel) requires login. Users are symmetric — everyone can both offer and book. On top of the core loop the app also has: in-app + email notifications, user-to-user subscriptions, weekly/standing availability rules, optional external calendar sync (Google live, Microsoft scaffolded), and profiles with email + SMS confirmation.

**[`BOOKING-PLAN.md`](BOOKING-PLAN.md) is the source of truth for product design.** It defines the entity model and the security-critical concurrency and account-linking guarantees. All of its phases (0–7) are now implemented; the parts that need real third-party credentials (SMTP, Google/Microsoft calendar sync, Apple login) are config-gated and build green but are **untested against the live services** — see the honesty notes below.

## Commands

```bash
# First-time / after pulling schema changes:
docker compose up -d                                   # local PostgreSQL on :5432
dotnet ef database update --project CalendarBooking    # apply migrations

# Run / build
dotnet run --project CalendarBooking                   # http://localhost:5196, https://localhost:7043
dotnet build

# Migrations (run from repo root)
dotnet ef migrations add <Name> --project CalendarBooking --output-dir Data/Migrations
dotnet ef migrations remove --project CalendarBooking
```

```bash
# Tests (xUnit)
dotnet test
dotnet test --filter "FullyQualifiedName~BookingServiceTests"   # one class
```

Most tests use the EF **in-memory** provider and need no database. Two suites need **Docker**: `DoubleBookingIntegrationTests` (real Postgres via Testcontainers — verifies the partial unique index the in-memory provider can't) and `AntiforgeryTests` (boots the app via `WebApplicationFactory<Program>`; `Program` is `public partial` so the test host can reference it). The interactive Razor UI is verified by running the app.

## Architecture

.NET 8 **Blazor Web App**, single project, EF Core on PostgreSQL, ASP.NET Core Identity. Layers inside `CalendarBooking/`: `Domain/` (entities + enums), `Data/` (`AppDbContext` + migrations), `Services/` (domain logic), `Components/` (Razor UI).

### Domain logic is in `Services/`, not components
Business rules live in plain service classes so they're unit-testable without a DB; the Razor UI is a thin layer over them. Each returns a small `Result` record (Ok + error/payload). Map:
- `AvailabilityService` — define/delete slots (one-off + bulk via `CreateManyAsync`). Enforces **quarter-hour-aligned** start/end (:00/:15/:30/:45, checked in UTC — valid since all real offsets are 15-min multiples) and blocks creation that clashes with the owner's external busy times. `CreateManyAsync` skips occurrences clashing with existing/batch slots (idempotent) and tags generated slots with an optional rule id.
- `AvailabilityRuleService` + `WeeklyAvailability` (pure expander, shared with the one-off generator) — standing weekly rules: persisted `WeeklyAvailabilityRule` materialized into concrete slots, idempotently, by the background worker. Deleting a rule removes its future *unbooked* slots (matched via `AvailabilitySlot.GeneratedByRuleId`), keeping booked ones.
- `BookingService` — public browse, instant book (confirmed), approval request (pending cap from `BookingOptions`). `GetScheduleByNicknameAsync` returns a privacy-safe `CalendarSlot` view (time + booked-or-not, no who/what) — booked = "busy", open = bookable; this powers the followed-person calendar on `/my`.
- `ApprovalService` — approve (auto-declines competing requests as `Superseded`), decline, on-behalf-of (relationship-gated).
- `CancellationService` — cancel by either party (reason required), free slot, keep the row; also the "my schedule" query.
- `SubscriptionService` — follow users by their `PublicId`; new-subscriber login digest (`SubscribersSeenUtc` high-water mark; no push/email by design).
- `NotificationService` + `NotificationBroadcaster` — in-app notifications. `Queue` adds a row to the caller's DbContext (atomic with the action); `PushQueued` (after SaveChanges) fires the broadcaster so a recipient's open page refreshes live over their Blazor circuit.
- Background workers (`AddHostedService`): `EmailDispatcherService` (emails notifications out-of-band via `IAppEmailSender`), `ReminderService` (booking-starts-soon), `StaleRequestService` (expire pending requests whose slot passed), `CalendarSyncDispatcher` (push confirmed bookings to external calendars + delete on cancel), `AvailabilityRuleMaterializer` (rolls standing rules' slots forward ~28 days).
- `AccountCleanupService` — closes an account by anonymizing it (FKs are Restrict; history is kept), tidying future slots/bookings/requests/subscriptions first.
- External sync: `CalendarSyncService` fans out to registered `IExternalCalendarClient`s. `GoogleCalendarClient` (via Google.Apis) and `MicrosoftCalendarClient` (Graph over HTTP) are registered only when configured; refresh tokens are stored encrypted (Data Protection). See `docs/EXTERNAL-SYNC.md`.

### Interactivity is PER-PAGE, not global (critical)
`App.razor` does NOT put a render mode on `<Routes>`/`<HeadOutlet>`; pages opt in with `@rendermode InteractiveServer`. **Why:** signing in writes the auth cookie to the HTTP response, which only works during static SSR — not over the interactive SignalR circuit. So all `Account/` pages render statically. For interactivity on an otherwise-static page, add a small interactive **island** (`NicknameField.razor`, `NotificationBadge.razor`) rather than making the page interactive. Interactive pages load data + do JS interop (browser timezone) in `OnAfterRenderAsync`, not `OnInitializedAsync`, so they don't query the scoped DbContext during prerender.

### Authentication
- `Program.cs` wires Identity cookie schemes + `AddIdentityCore<ApplicationUser>().AddSignInManager()`. `RequireConfirmedAccount` is **config-driven** (`Identity:RequireConfirmedAccount`, default false so dev works without SMTP); when true, registration sends a confirmation link and `Account/ConfirmEmail` validates it.
- Email goes through `IAppEmailSender` — real SMTP (`Email:Smtp:*`, MailKit) when configured, else a logging fallback. The notification email dispatcher and Identity confirmation/reset both use it. SMS (phone confirmation on the Profile page) goes through `ISmsSender` the same way — Twilio (`Sms:Twilio:*`, REST over HTTP) when configured, else logging; the code itself uses Identity's built-in phone token (`PhoneNumberConfirmed`).
- Registration is **enumeration-safe**: an already-registered email shows the same "check your email" page rather than revealing it exists.
- External login: **Google** (live) and **Apple** (scaffold) are config-gated. **Account-linking is security-critical** (`ExternalLogin.razor`): auto-link to an existing local account ONLY when the provider reports the email verified (Apple is always-verified). New external users pick a nickname; first/last name are pulled from the provider profile.
- Logout / external-login start / calendar-connect are HTTP endpoints (sign-out must be a real POST). Antiforgery is enforced on form-POST endpoints (`AntiforgeryTests`).

### Route protection
Public by default — no global authorization policy. Protect a page with `@attribute [Authorize]`; `AuthorizeRouteView` in `Routes.razor` redirects anonymous users to login via `RedirectToLogin`. Protected: `/my`, `/notifications`, `/Account/Profile`, `/Account/Close`.

### Data model invariants (enforced in `AppDbContext.OnModelCreating`)
- **Double-booking guard:** a *partial* unique index on `Booking.SlotId` filtered to confirmed bookings (`"Status" = 0`). This is THE database-level guarantee — a claim is one atomic `SaveChanges`; on `DbUpdateException` report "just taken". Never an app-level "is it free?" check. Filtered so a cancelled (kept) booking doesn't block re-booking.
- **UTC everywhere:** store timestamps in UTC, convert to local only at display (browser timezone via `wwwroot/js/timezone.js` → `TimeZoneInfo`).
- **Cancelled bookings are kept, not deleted** — preserves relationship history (the on-behalf-of relationship gate depends on it) and audit.
- **`Booking` references users 4 ways** (`OwnerId`, `AttendeeId`, `CreatedById`, `CancelledById`), all `DeleteBehavior.Restrict` — user deletion is anonymization in `AccountCleanupService`, never cascaded.
- **Nickname** is the public identity, unique **case-insensitively** via a `LOWER(Nickname)` functional index created with raw SQL in a migration (EF can't model it — so there's intentionally no `HasIndex` for it). **`PublicId`** is a stable opaque 10-char code (`PublicCode`), unique, used for subscribing.
- **Slots created by a standing rule are tagged** with `AvailabilitySlot.GeneratedByRuleId` (a nullable soft tag, not an FK) so a rule's slots can be cleaned up on delete; one-off/manual slots are null.

## Conventions
- **Ask before building when it's ambiguous, and push back on flawed approaches.** If a request is unclear or has materially different reasonable interpretations, ask for clarification before coding rather than guessing. If the requested approach has a logic conflict, correctness/security risk, or a better alternative, say so plainly and propose the alternative — don't just implement it silently. (E.g. surface things like "this hides cross-period requests", "deleting the rule leaves orphaned slots", "this needs a 15-min-aligned UTC check".)
- **Mobile-first UI/UX.** Phones/tablets are the primary target — usable on small touch screens first, then scale up. Use Bootstrap's responsive grid; verify at narrow viewports.
- Commit per logical unit with a body explaining the *why* in plain terms; verify the build is green (and tests, if logic changed) before committing.
- Match the existing comment density in `.razor`/`.cs` files — they're documented for newcomers.
- When a feature needs third-party credentials, keep it **config-gated** (off/inert without config) so the app always builds and runs; flag clearly what hasn't been tested live.
