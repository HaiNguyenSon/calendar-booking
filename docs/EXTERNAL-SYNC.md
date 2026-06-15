# External calendar sync (Phase 7)

**Status: scaffolding only.** The extension seam is in place and unit-tested, but no real
provider is implemented yet. Connecting Google Calendar or Microsoft Graph needs live OAuth
credentials, the provider's API client package, and an offline refresh-token flow — none of
which are wired up. This document is the plan for finishing it.

## What exists today

- `IExternalCalendarClient` — one connected provider. Two operations:
  - `PushBookingAsync` — push a confirmed booking out to the user's external calendar.
  - `GetBusyIntervalsAsync` — read the user's busy times, to block their availability.
- `CalendarSyncService` — fans out to every registered `IExternalCalendarClient`,
  best-effort (one failing provider never breaks the others). With no client registered it
  is a safe no-op, so callers can use it unconditionally. `HasProviders` reports whether any
  are connected.

## What's left to build

1. **OAuth with calendar scopes.** Reuse the existing Google external login (see the README)
   but request calendar scopes (e.g. `https://www.googleapis.com/auth/calendar`) and offline
   access so we receive a refresh token. Microsoft Graph is analogous.
2. **Token storage.** Add an `ExternalCalendarConnection` entity (UserId, Provider, encrypted
   refresh token, connected-at) and a migration. Encrypt tokens at rest.
3. **Provider client.** Implement `IExternalCalendarClient` for Google using
   `Google.Apis.Calendar.v3`: map a `Booking` to a calendar event for `PushBookingAsync`, and
   query free/busy for `GetBusyIntervalsAsync`. Register it in `Program.cs`.
4. **Wire the hooks.**
   - After a booking is confirmed (instant book, approval, on-behalf-of), call
     `CalendarSyncService.PushBookingAsync`. Do it out-of-band (like the email dispatcher) so
     a slow API never blocks the booking transaction.
   - When listing or claiming availability, subtract `GetBusyIntervalsAsync` so externally
     busy times don't show as bookable.
5. **Two-way reconciliation.** Decide how external changes (an event deleted in Google) flow
   back, and how to avoid loops.

## Why it's structured this way

Keeping the provider behind `IExternalCalendarClient` means the booking code depends only on
`CalendarSyncService`, never on Google/Microsoft specifics, and the feature can ship one
provider at a time. The dispatcher is already covered by unit tests using a fake client.
