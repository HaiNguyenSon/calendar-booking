# External calendar sync (Phase 7)

**Status: Google Calendar is implemented (push + free/busy); Microsoft Graph is not.** The
Google integration builds and is wired end to end, but the OAuth round-trip and live API
calls can only be verified with real Google credentials and a real account — see
"Verifying" below.

## The seam

- `IExternalCalendarClient` — one connected provider. Two operations:
  - `PushBookingAsync` — push a confirmed booking out to the user's external calendar.
  - `GetBusyIntervalsAsync` — read the user's busy times, to block their availability.
- `CalendarSyncService` — fans out to every registered `IExternalCalendarClient`,
  best-effort (one failing provider never breaks the others). With no client registered it
  is a safe no-op. `HasProviders` reports whether any are connected.

## Google Calendar

- **Credentials:** reuses the Google login credentials (`Authentication:Google:ClientId` /
  `ClientSecret`). The `GoogleCalendarClient` is registered as an `IExternalCalendarClient`
  only when those are set.
- **Connect flow** (`GoogleCalendarConnector` + `GoogleCalendarEndpoints`): the user clicks
  *Connect Google Calendar* on `/my` → `GET /Account/Calendar/Connect` redirects to Google's
  consent screen (offline access, so we get a refresh token) → Google redirects to
  `GET /Account/Calendar/Callback`, which exchanges the code and stores the refresh token
  **encrypted** (ASP.NET Data Protection) in `ExternalCalendarConnection`. Disconnect via
  `POST /Account/Calendar/Disconnect`.
- **Push:** `CalendarSyncDispatcher` (background) pushes confirmed bookings to both parties'
  calendars out-of-band, once each (`Booking.CalendarSyncedUtc`), so a slow API never blocks
  a booking.
- **Free/busy:** `GoogleCalendarClient.GetBusyIntervalsAsync` is implemented via the
  Freebusy API. **Not yet wired into availability** — subtracting busy times when listing or
  claiming slots is the remaining "pull" step.

### Google Cloud setup

1. Reuse the OAuth client from Google login. Add the calendar scope is automatic (requested
   at connect time); just add the redirect URI:
   `https://localhost:7043/Account/Calendar/Callback` (and your prod equivalent).
2. Enable the **Google Calendar API** for the project.
3. Set `Authentication:Google:ClientId` / `ClientSecret` (user-secrets in dev — see README).

### Verifying (needs a real account)

Sign in, open `/my`, click *Connect Google Calendar*, grant access. Then make a confirmed
booking; within ~30s the `CalendarSyncDispatcher` should create an event on the primary
Google calendar. None of this can be exercised without live credentials, so it is not
covered by automated tests (the dispatcher/fan-out logic is, via a fake client).

## What's left

- Wire `GetBusyIntervalsAsync` into availability so externally-busy times aren't bookable.
- Two-way reconciliation (handle events changed/deleted in Google; avoid loops).
- A Microsoft Graph `IExternalCalendarClient` (same shape, analogous OAuth).
