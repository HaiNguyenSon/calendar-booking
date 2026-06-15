# Booking Calendar — Project Plan

A web app where every user has their own calendar. Users open availability slots on
their own calendar and book slots on others'. Some slots are instant-book; others need
the owner's approval. **Browsing calendars is public; any action (book, approve, decline,
cancel) requires login.** Every user is symmetric — anyone can both offer slots and book
others as a client.

---

## Solution type (Rider)
- **ASP.NET Core → Blazor Web App** template
- Solution name: `BookingApp`
- Framework: **.NET 9.0** (or 8.0 LTS)
- Authentication: **None** (add Identity manually — cleaner for custom nickname/bio + Google login)
- **Interactive render mode: Server**
- **Interactivity location: Global**
- Docker support: unchecked (add a Compose file for Postgres separately)

**Why Blazor Server:** real-time in-app notifications via SignalR come essentially free
(Server already runs over SignalR); one app to run/deploy; booking logic, OAuth secrets,
and DB access run server-side by nature — exactly what a multi-user app with double-booking
guards needs.

---

## Locked Decisions

| Area | Choice |
|------|--------|
| **Solution** | Blazor Web App, Server render mode (single project) |
| **Backend** | ASP.NET Core (in the Blazor Server app) |
| **Frontend** | Blazor (all C#) |
| **Database** | PostgreSQL (multi-user, concurrent writes) — Docker for local dev |
| **ORM** | EF Core |
| **Auth** | ASP.NET Core Identity + external login: **Google first, Apple later** |
| **Background jobs** | Hangfire or Quartz.NET (reminders, expiry) |
| **Email** | SMTP provider (Mailtrap dev → Brevo/SendGrid free tier) |
| **In-app notifications** | SignalR |
| **External sync** | Google Calendar API + Microsoft Graph — designed for, built later |
| **Timezones** | Store UTC, convert at display — from day one |

---

## User & access model
- **Everyone is both roles:** can offer slots AND book others' slots as a client.
- **Unique nickname:** public identity in browse/search (email never shown publicly).
  Required, max 50 chars, **case-insensitive uniqueness** enforced by DB index
  (harden to Postgres `citext` or a `LOWER(nickname)` unique index).
- **Profile description:** optional public bio, **max 150 chars**, shown on the calendar
  page. Enforced at DB column length + app validation; sanitize on display.
- **Public (no login):** browse and search calendars, view open slots.
- **Requires login:** booking, approving, declining, cancelling, managing availability.
- Auth wall on action endpoints only; browse/search is read-only/anonymous.
- **Login options:** register with email/password, OR external provider (Google first;
  Apple later). External users get a local account auto-created and linked.

### Account linking rule (security-critical)
- Auto-link an external login to an existing account **only when the provider reports the
  email as verified** (Google `email_verified`; Apple always verified).
- One account per person across email/Google/Apple.
- **Never link by email address alone** — an unverified external email could hijack an
  existing account.

---

## Booking model

Four entities:
- **ApplicationUser** — extends Identity user; adds `Nickname` + `Description` (bio).
- **AvailabilitySlot** — a time block a user opens; `SlotType` = `Instant` or
  `ApprovalRequired`; `IsBooked` flag; times in UTC.
- **BookingRequest** — a pending ask against an approval slot; `RequestStatus` =
  `Pending` / `Approved` / `Declined` / `Superseded`. Counts toward the pending cap.
- **Booking** — a confirmed reservation. Key fields:
  - `OwnerId` — who's being booked / hosting.
  - `AttendeeId` — who's "coming to the event"; whose calendar it appears on.
  - `CreatedById` — who made it (distinguishes self-booked vs owner-initiated).
  - `Status` = `Confirmed` / `Cancelled`, plus `CancelledById`, `CancelledUtc`,
    `CancellationReason`.

### Slot flow
- Instant slot → becomes a confirmed Booking immediately (skips request).
- Approval slot → creates a pending BookingRequest.

### Pending-request cap
- A user may have at most **N pending requests** at once (N configurable).
- Enforced when creating a request (count Pending requests for that user, reject if at cap).
- **Owner-initiated bookings bypass the cap** — they're created confirmed, never pending.

### On-behalf-of bookings (owner books a client)
- The slot owner can create a **confirmed** booking for a client (no client approval).
- **Relationship gate:** allowed only if the client has a prior booking with the owner
  (derived from booking history, bidirectional — either direction counts). A dedicated
  relationship table can come later if needed.
- The booking's `AttendeeId` = client, so it appears on the **client's** calendar as them
  attending — no special calendar code needed (calendar queries "bookings where Attendee = me").
- Client is **notified** of the owner-created booking.

### Contention rule (multiple requests, one slot)
- Owner approves one request → it becomes a Booking.
- All other pending requests for that slot are **auto-declined** (`Superseded`).
- Losers are **notified the slot was taken** and **prompted to rebook**.

### Cancellation
- **Either party** (owner or attendee) can cancel a confirmed booking.
- **Reason is required** (max 250 chars); delivered to the *other* party in the notification.
- Cancelling frees the slot (sets `IsBooked = false`) **within a transaction**.
- Booking row is **kept and marked `Cancelled`** (not hard-deleted) to preserve the
  relationship history the on-behalf-of feature relies on, and for audit.

---

## Why PostgreSQL (not SQLite)
Multiple users write at once; the core risk is **double-booking** — two people grabbing the
same slot simultaneously. That needs real transactions, row locking, and unique constraints.
SQLite's single-writer model fights this. Postgres in Docker is the clean standard.

## Critical engineering principles
1. **UTC everywhere.** Store all timestamps in UTC; convert to local only at display.
2. **Database-level concurrency guarantees.** Prevent double-booking with a **unique index
   on `Booking.SlotId`** + transactions when claiming a slot — NOT app-level "is it free?"
   checks, which have race windows.

---

## Phase 0 — Setup
- [ ] Create the Blazor Web App solution (Server render mode) per "Solution type" above.
- [ ] Add NuGet: `Npgsql.EntityFrameworkCore.PostgreSQL`,
      `Microsoft.EntityFrameworkCore.Design`,
      `Microsoft.AspNetCore.Identity.EntityFrameworkCore`,
      `Microsoft.AspNetCore.Authentication.Google`.
- [ ] PostgreSQL via Docker Compose; wire up EF Core + connection string.
- [ ] `ApplicationUser : IdentityUser` with `Nickname` + `Description`.
- [ ] `AppDbContext : IdentityDbContext<ApplicationUser>` with DbSets, unique nickname
      index, unique `Booking.SlotId` index, attendee/requester indexes,
      `DeleteBehavior.Restrict` on user FKs.
- [ ] Register DbContext + Identity in `Program.cs`; create initial migration + update DB.
- [ ] Add ASP.NET Core Identity register / login / logout (email + password).
- [ ] Nickname on registration: required, unique (case-insensitive), live availability check.
- [ ] External-login users: prompt for a nickname on first sign-in before any action.
- [ ] Add Google external login (Google Cloud OAuth client — free; reused for Calendar sync).
- [ ] Verified-email account linking (link only when `email_verified` is true).
- [ ] Public-vs-authenticated route split.
- [ ] *(Later)* Apple login via `AspNet.Security.OAuth.Apple` — Apple Developer account
      ($99/yr) + signed-JWT client secret.

## Phase 1 — Calendar & availability
- [ ] Entities: ApplicationUser (nickname + 150-char bio), AvailabilitySlot, Booking,
      BookingRequest (all built in Phase 0 data layer).
- [ ] Define availability — one-off and recurring weekly slots.
- [ ] Mark each slot instant-book or approval-required.
- [ ] Own-schedule calendar view (query bookings where Attendee = me).
- [ ] Store all times UTC.

## Phase 2 — Booking flow
- [ ] Public browse/search of users and their open slots (no login).
- [ ] Booking action requires login.
- [ ] Instant slot → confirmed Booking immediately.
- [ ] Approval slot → pending BookingRequest, subject to the **pending-request cap**.
- [ ] **Double-booking prevention:** unique `SlotId` index + transaction on claim.

## Phase 3 — Approval & on-behalf-of
- [ ] "Incoming requests" view for slot owners (login required).
- [ ] Approve → converts request to Booking.
- [ ] Decline → releases the slot.
- [ ] **Contention handling:** approving one auto-declines (`Superseded`) the rest;
      notify losers "slot taken" + prompt to rebook.
- [ ] **On-behalf-of booking:** owner books a confirmed event for an existing client;
      relationship gate; attendee = client; bypasses pending cap; notify client.

## Phase 4 — Cancellation
- [ ] Cancel action available to **either party** on a confirmed booking.
- [ ] **Required reason** (max 250 chars).
- [ ] Mark booking `Cancelled` (keep record), free the slot, all in one transaction.
- [ ] Notify the **other** party with the reason.

## Phase 5 — Notifications
- [ ] In-app (SignalR): new request, approved, declined, slot-taken, on-behalf-of booking,
      cancellation.
- [ ] Email (SMTP): same events.
- [ ] Reminders: background job before event start.

## Phase 6 — Polish & hardening
- [ ] Timezone display verified end to end (store UTC, render local).
- [ ] Harden nickname uniqueness to `citext` / `LOWER()` index.
- [ ] Prevent editing/deleting slots that already have bookings.
- [ ] Expire stale pending requests via background job.
- [ ] Explicit user-deletion cleanup (FKs are `Restrict`, so handle in a service).

## Phase 7 — External sync (later)
- [ ] Google Calendar + Microsoft Graph OAuth.
- [ ] Two-way sync: push bookings out, pull busy times in to block availability.
- [ ] Reuse Google/Microsoft external-login credentials from Identity.

---

## Suggested build order
Phase 0 → 1 → 2 → 3 → 4 gets the core loop correct: open slots, book them (instant or
approval), approve/decline with contention handling, owner-on-behalf-of bookings, and
cancellation with reasons. Then 5 (notifications), 6 (hardening), 7 (sync). Get booking,
approval, and cancellation correct first — that's the heart of the app — before layering
notifications and sync on top.
