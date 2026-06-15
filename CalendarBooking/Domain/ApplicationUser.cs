using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace CalendarBooking.Domain;

/// <summary>
/// Our app's user. Extends ASP.NET Core Identity's built-in user (which already
/// holds email, password hash, external logins, etc.) with the two public-profile
/// fields the booking app needs.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Public display name shown in browse/search. The email is never shown publicly.
    /// Required, max 50 chars. Uniqueness is enforced case-insensitively by a database
    /// index configured in AppDbContext.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// Stable, opaque 10-character public code (Crockford Base32). Used to subscribe to this
    /// user via a link/code that doesn't depend on the (changeable) nickname or leak the
    /// email. Generated at account creation; unique (index in AppDbContext).
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string PublicId { get; set; } = string.Empty;

    /// <summary>
    /// Optional public bio shown on the user's calendar page. Max 150 chars.
    /// Sanitize on display.
    /// </summary>
    [MaxLength(150)]
    public string? Description { get; set; }

    /// <summary>
    /// High-water mark for the "new subscribers" digest: subscribers whose subscription is
    /// newer than this are shown once when the user next visits their calendar, then this is
    /// advanced. Null = never checked (all current subscribers are "new"). Avoids per-subscribe
    /// notification spam.
    /// </summary>
    public DateTime? SubscribersSeenUtc { get; set; }
}
