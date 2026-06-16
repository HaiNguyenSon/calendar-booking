using CalendarBooking.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Data;

/// <summary>
/// The app's database context. Inheriting from IdentityDbContext gives us all the
/// Identity tables (users, roles, logins, tokens) for free, keyed on our
/// <see cref="ApplicationUser"/>. We add our own booking tables on top.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<BookingRequest> BookingRequests => Set<BookingRequest>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ExternalCalendarConnection> ExternalCalendarConnections => Set<ExternalCalendarConnection>();
    public DbSet<ExternalCalendarEvent> ExternalCalendarEvents => Set<ExternalCalendarEvent>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<WeeklyAvailabilityRule> WeeklyAvailabilityRules => Set<WeeklyAvailabilityRule>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Must run first so Identity's own tables are configured.
        base.OnModelCreating(builder);

        // Public nickname must be unique CASE-INSENSITIVELY ("Alice" and "alice" collide).
        // This is enforced by a unique index on LOWER(Nickname), created with raw SQL in the
        // HardenNicknameUniqueness migration (EF can't model a functional index), so there is
        // intentionally no HasIndex(...) here.

        // Opaque public code, unique. It's stored canonically (uppercase Crockford Base32),
        // so a plain unique index is enough.
        builder.Entity<ApplicationUser>(user => user.HasIndex(u => u.PublicId).IsUnique());

        builder.Entity<AvailabilitySlot>(slot =>
        {
            slot.HasOne(s => s.Owner)
                .WithMany()
                .HasForeignKey(s => s.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // We frequently list a user's own slots.
            slot.HasIndex(s => s.OwnerId);
            // Used to clean up a standing rule's slots when it's deleted.
            slot.HasIndex(s => s.GeneratedByRuleId);
        });

        builder.Entity<BookingRequest>(request =>
        {
            request.HasOne(r => r.Slot)
                .WithMany()
                .HasForeignKey(r => r.SlotId)
                .OnDelete(DeleteBehavior.Restrict);

            request.HasOne(r => r.Requester)
                .WithMany()
                .HasForeignKey(r => r.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);

            // "Show me my outstanding requests" and the per-user pending cap both
            // query by requester.
            request.HasIndex(r => r.RequesterId);
            request.HasIndex(r => r.SlotId);
        });

        builder.Entity<Booking>(booking =>
        {
            // THE double-booking guard. A slot may have at most one CONFIRMED booking,
            // enforced by the database, not by an app-level "is it free?" check (which
            // has a race window). It is a PARTIAL unique index: it ignores Cancelled
            // bookings (Status = 1) so a freed slot can legitimately be re-booked while
            // the old cancelled row is kept for history. Status is stored as int;
            // Confirmed = 0.
            booking.HasIndex(b => b.SlotId)
                .IsUnique()
                .HasFilter("\"Status\" = 0");

            booking.HasOne(b => b.Slot)
                .WithMany()
                .HasForeignKey(b => b.SlotId)
                .OnDelete(DeleteBehavior.Restrict);

            // Booking points at users four different ways. All use Restrict: we never
            // want deleting a user to silently cascade-delete booking history, and
            // multiple cascade paths to one table are invalid anyway. User deletion is
            // handled explicitly in a service (Phase 6).
            booking.HasOne(b => b.Owner)
                .WithMany()
                .HasForeignKey(b => b.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            booking.HasOne(b => b.Attendee)
                .WithMany()
                .HasForeignKey(b => b.AttendeeId)
                .OnDelete(DeleteBehavior.Restrict);

            booking.HasOne(b => b.CreatedBy)
                .WithMany()
                .HasForeignKey(b => b.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            booking.HasOne(b => b.CancelledBy)
                .WithMany()
                .HasForeignKey(b => b.CancelledById)
                .OnDelete(DeleteBehavior.Restrict);

            // The calendar view asks "bookings where Attendee = me", so index it.
            booking.HasIndex(b => b.AttendeeId);
            booking.HasIndex(b => b.OwnerId);

            booking.Property(b => b.CancellationReason).HasMaxLength(250);
        });

        builder.Entity<Notification>(notification =>
        {
            notification.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // "My notifications, newest first" and the unread-badge query.
            notification.HasIndex(n => new { n.UserId, n.CreatedUtc });

            notification.Property(n => n.Message).HasMaxLength(500);
        });

        builder.Entity<ExternalCalendarConnection>(connection =>
        {
            connection.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // One connection per (user, provider).
            connection.HasIndex(c => new { c.UserId, c.Provider }).IsUnique();
        });

        builder.Entity<ExternalCalendarEvent>(calendarEvent =>
        {
            // Looked up by booking (to delete on cancellation).
            calendarEvent.HasIndex(e => e.BookingId);
        });

        builder.Entity<Subscription>(subscription =>
        {
            subscription.HasOne(s => s.Subscriber)
                .WithMany()
                .HasForeignKey(s => s.SubscriberId)
                .OnDelete(DeleteBehavior.Restrict);

            subscription.HasOne(s => s.Target)
                .WithMany()
                .HasForeignKey(s => s.TargetId)
                .OnDelete(DeleteBehavior.Restrict);

            // No duplicate subscriptions; the composite also serves "who I follow".
            subscription.HasIndex(s => new { s.SubscriberId, s.TargetId }).IsUnique();
            // "Who follows me" / subscriber counts.
            subscription.HasIndex(s => s.TargetId);
        });

        builder.Entity<WeeklyAvailabilityRule>(rule =>
        {
            rule.HasOne(r => r.Owner)
                .WithMany()
                .HasForeignKey(r => r.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            rule.HasIndex(r => r.OwnerId);
        });
    }
}
