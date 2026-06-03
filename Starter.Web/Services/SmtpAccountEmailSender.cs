using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using System.Net;

namespace Starter.Web.Services;

public sealed class SmtpAccountEmailSender(
    SystemConfigurationService systemConfiguration,
    ILogger<SmtpAccountEmailSender> logger) : IAccountEmailSender
{
    public async Task<EmailSendResult> SendEmailConfirmationAsync(
        string toEmail,
        string confirmationLink,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            toEmail,
            settings => CreateEmailConfirmationMessage(settings, toEmail, confirmationLink),
            "email confirmation",
            cancellationToken);
    }

    public async Task<EmailSendResult> SendPasswordResetAsync(
        string toEmail,
        string resetLink,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            toEmail,
            settings => CreatePasswordResetMessage(settings, toEmail, resetLink),
            "password reset",
            cancellationToken);
    }

    private async Task<EmailSendResult> SendAsync(
        string toEmail,
        Func<SmtpEmailSettings, MimeMessage> createMessage,
        string messageKind,
        CancellationToken cancellationToken)
    {
        var settings = await systemConfiguration.GetSmtpEmailSettingsAsync();

        if (!settings.DeliveryEnabled)
        {
            return EmailSendResult.Skipped("Email delivery is disabled.");
        }

        if (!settings.IsConfigured)
        {
            logger.LogWarning("Email delivery is enabled but SMTP settings are incomplete.");
            return EmailSendResult.Skipped("SMTP settings are incomplete.");
        }

        try
        {
            var message = createMessage(settings);

            using var smtpClient = new SmtpClient();
            await smtpClient.ConnectAsync(
                settings.Host,
                settings.Port,
                GetSecureSocketOptions(settings),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                await smtpClient.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
            }

            await smtpClient.SendAsync(message, cancellationToken);
            await smtpClient.DisconnectAsync(quit: true, cancellationToken);

            return EmailSendResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send {MessageKind} email to {Email}.", messageKind, toEmail);
            return EmailSendResult.Failure("SMTP send failed.");
        }
    }

    private static MimeMessage CreateEmailConfirmationMessage(
        SmtpEmailSettings settings,
        string toEmail,
        string confirmationLink)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Confirm your Starter email";

        var encodedLink = WebUtility.HtmlEncode(confirmationLink);
        message.Body = new TextPart(TextFormat.Html)
        {
            Text = $"""
                <p>Welcome to Starter.</p>
                <p><a href="{encodedLink}">Confirm your email address</a></p>
                <p>If you did not create this account, you can ignore this email.</p>
                """,
        };

        return message;
    }

    private static MimeMessage CreatePasswordResetMessage(
        SmtpEmailSettings settings,
        string toEmail,
        string resetLink)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Reset your Starter password";

        var encodedLink = WebUtility.HtmlEncode(resetLink);
        message.Body = new TextPart(TextFormat.Html)
        {
            Text = $"""
                <p>A password reset was requested for your Starter account.</p>
                <p><a href="{encodedLink}">Reset your password</a></p>
                <p>If you did not request this, you can ignore this email.</p>
                """,
        };

        return message;
    }

    private static SecureSocketOptions GetSecureSocketOptions(SmtpEmailSettings settings)
    {
        if (!settings.UseSsl)
        {
            return SecureSocketOptions.None;
        }

        return settings.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;
    }
}
