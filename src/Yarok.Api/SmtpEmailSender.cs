using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Yarok.Api;

public interface IEmailSender
{
    Task Send(MimeMessage message, CancellationToken ct);
}

public sealed class SmtpEmailSender(IConfiguration config) : IEmailSender
{
    public async Task Send(MimeMessage message, CancellationToken ct)
    {
        using var smtp = new SmtpClient();
        // Refuse to send credentials over an unencrypted connection.
        var security = string.Equals(config["SMTP_SECURE"], "true", StringComparison.OrdinalIgnoreCase)
            ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await smtp.ConnectAsync(Settings.Required(config, "SMTP_HOST"), int.Parse(config["SMTP_PORT"] ?? "587"), security, ct);
        await smtp.AuthenticateAsync(Settings.Required(config, "SMTP_USER"), Settings.Required(config, "SMTP_PASSWORD"), ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}

