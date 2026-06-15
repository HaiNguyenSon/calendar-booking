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
    /// Optional public bio shown on the user's calendar page. Max 150 chars.
    /// Sanitize on display.
    /// </summary>
    [MaxLength(150)]
    public string? Description { get; set; }
}
