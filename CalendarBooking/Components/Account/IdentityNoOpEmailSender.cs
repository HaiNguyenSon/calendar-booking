using CalendarBooking.Domain;
using CalendarBooking.Services;
using Microsoft.AspNetCore.Identity;

namespace CalendarBooking.Components.Account;

/// <summary>
/// Identity's email sender (confirmation / password reset). Delegates to the app's
/// <see cref="IAppEmailSender"/>, which is either real SMTP or the logging fallback depending
/// on configuration. (Named "NoOp" historically; it now actually sends.)
/// </summary>
internal sealed class IdentityNoOpEmailSender(IAppEmailSender email) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email_, string confirmationLink) =>
        email.SendAsync(email_, "Confirm your CalendarBooking email",
            $"Confirm your account by visiting: {confirmationLink}");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email_, string resetLink) =>
        email.SendAsync(email_, "Reset your CalendarBooking password",
            $"Reset your password by visiting: {resetLink}");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email_, string resetCode) =>
        email.SendAsync(email_, "Your CalendarBooking password reset code",
            $"Your password reset code is: {resetCode}");
}
