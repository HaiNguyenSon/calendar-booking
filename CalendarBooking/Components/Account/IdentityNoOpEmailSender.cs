using CalendarBooking.Domain;
using Microsoft.AspNetCore.Identity;

namespace CalendarBooking.Components.Account;

/// <summary>
/// A placeholder email sender that does nothing. Identity expects an
/// <see cref="IEmailSender{TUser}"/> to be registered for confirmation/reset emails.
/// Real SMTP is wired up in Phase 5; until then these calls are no-ops. This is safe
/// only because account confirmation is currently disabled
/// (RequireConfirmedAccount = false), so registration and login do not depend on a
/// confirmation email actually arriving.
/// </summary>
internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        Task.CompletedTask;

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        Task.CompletedTask;

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        Task.CompletedTask;
}
