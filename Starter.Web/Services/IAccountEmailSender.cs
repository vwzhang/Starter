namespace Starter.Web.Services;

public interface IAccountEmailSender
{
    Task<EmailSendResult> SendEmailConfirmationAsync(
        string toEmail,
        string confirmationLink,
        CancellationToken cancellationToken = default);

    Task<EmailSendResult> SendPasswordResetAsync(
        string toEmail,
        string resetLink,
        CancellationToken cancellationToken = default);
}
