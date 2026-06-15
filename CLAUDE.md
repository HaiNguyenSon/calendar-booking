# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A multi-user booking calendar: every user has a calendar, opens availability slots on their own, and books slots on others'. Browsing is public; any action (book, approve, decline, cancel) requires login. Users are symmetric — everyone can both offer and book.

**[`BOOKING-PLAN.md`](BOOKING-PLAN.md) is the source of truth for product design and the phased roadmap.** Read it before adding features — it defines the entity model, the booking/approval/cancellation rules, the pending-request cap, contention handling, and the security-critical concurrency and account-linking guarantees. Phase 0 (data + auth foundation) is complete; Phase 1 (calendar & availability) is next.

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

There is no test project yet. Manual verification is done by running the app and driving HTTP flows.

Google login is optional and off unless credentials are set via user-secrets (`Authentication:Google:ClientId` / `ClientSecret`) — see the README. The app runs fine without them.

## Architecture

.NET 8 **Blazor Web App**, single project, EF Core on PostgreSQL, ASP.NET Core Identity. Three layers inside `CalendarBooking/`: `Domain/` (entities + enums), `Data/` (`AppDbContext` + migrations), `Components/` (Razor UI).

### Interactivity is PER-PAGE, not global (critical)
Despite `BOOKING-PLAN.md` saying "Global", the app uses **per-page interactivity**. `App.razor` does NOT put a render mode on `<Routes>`/`<HeadOutlet>`; pages opt in individually with `@rendermode InteractiveServer` (see `Counter.razor`, `Account/Shared/NicknameField.razor`).

**Why it must stay this way:** signing a user in writes the auth cookie to the HTTP response, which only works during static server-side rendering — not over the interactive SignalR circuit. So all `Account/` pages render statically. When you need interactivity on an otherwise-static page (e.g. live validation), add a small interactive **island** component rather than making the page interactive (`NicknameField.razor` is the pattern).

### Authentication
- `Program.cs` wires Identity cookie schemes + `AddIdentityCore<ApplicationUser>().AddSignInManager()`. `RequireConfirmedAccount` is **false** (no email yet; flip to true when SMTP lands in Phase 5 — a no-op `IEmailSender` is the placeholder).
- Account UI/infra lives in `Components/Account/`. `IdentityRedirectManager` does safe redirects from static pages; `IdentityRevalidatingAuthenticationStateProvider` supplies the cascading auth state. Logout and external-login start are HTTP endpoints mapped by `MapAdditionalIdentityEndpoints` (sign-out must be a real POST to clear the cookie).
- **External login account-linking is security-critical** (`ExternalLogin.razor`): auto-link a Google login to an existing local account ONLY when the provider reports the email as verified. Never link by email alone. New external users are prompted for a nickname before any action.

### Route protection
Public by default — there is no global authorization policy. Protect a page by adding `@attribute [Authorize]`; `AuthorizeRouteView` in `Routes.razor` then redirects anonymous users to login via `RedirectToLogin`. `MyCalendar.razor` (`/my`) is the example.

### Data model invariants (enforced in `AppDbContext.OnModelCreating`)
- **Double-booking guard:** a *partial* unique index on `Booking.SlotId` filtered to confirmed bookings (`"Status" = 0`). This is the database-level guarantee against double-booking — rely on it + a transaction when claiming a slot, never an app-level "is it free?" check. It is filtered so a cancelled booking (kept for history) doesn't block re-booking.
- **UTC everywhere:** store all timestamps in UTC, convert to local only at display.
- **Cancelled bookings are kept, not deleted** (`BookingStatus.Cancelled`) — preserves relationship history (the on-behalf-of feature depends on it) and audit trail.
- **`Booking` references users three+ ways:** `OwnerId` (host), `AttendeeId` (whose calendar it shows on), `CreatedById` (self-booked vs owner-initiated), plus `CancelledById`. All user FKs are `DeleteBehavior.Restrict` — user deletion is handled explicitly in a service (Phase 6), never cascaded.
- **Nickname** is the public identity (email is private), unique via a DB index; uniqueness checks in code are case-insensitive (`LOWER`), with citext/`LOWER()` hardening deferred to Phase 6.

## Conventions
- **Mobile-first UI/UX.** Smartphones and tablets are the primary target — every page must be usable and look good on small touch screens first, then scale up to desktop. Use responsive layouts (Bootstrap's grid/breakpoints are already available), touch-friendly tap targets, and avoid designs that only work at desktop widths. Verify at narrow viewports, not just full-screen.
- Commit per logical unit with a body that explains the *why* in plain terms (a junior dev should understand it); verify the build is green before committing.
- Match the existing comment density in `.razor`/`.cs` files — these are documented for newcomers.
