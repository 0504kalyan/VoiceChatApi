using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public class SmtpMailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpMailSender> log) : IMailSender
{
    private readonly EmailOptions _opt = options.Value;

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "(empty)";

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 1)
            return "***";

        return $"{trimmed[0]}***{trimmed[(at - 1)..]}";
    }

    public static string NormalizePasswordForHost(string smtpHost, string password) =>
        NormalizeSmtpPassword(smtpHost, password);

    /// <summary>Gmail App Passwords are often copied with spaces; SMTP expects 16 characters without spaces.</summary>
    private static string NormalizeSmtpPassword(string smtpHost, string password)
    {
        if (string.IsNullOrEmpty(password))
            return password;
        var trimmed = password.Trim();
        if (!smtpHost.Contains("gmail", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        // App passwords are sometimes copied with spaces/newlines/non-breaking spaces.
        var compact = new string(trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return compact;
    }

    public async Task SendAsync(string toAddress, string subject, string plainTextBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.SmtpUser) || string.IsNullOrWhiteSpace(_opt.SmtpPassword))
        {
            log.LogWarning(
                "Email not configured (SmtpUser/SmtpPassword empty). Cannot deliver email to {To}.",
                MaskEmail(toAddress));
            throw new InvalidOperationException("Email SMTP is not configured. Set Email__SmtpUser and Email__SmtpPassword.");
        }

        var smtpUser = _opt.SmtpUser.Trim();
        var from = string.IsNullOrWhiteSpace(_opt.FromAddress) ? smtpUser : _opt.FromAddress.Trim();

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_opt.FromName, from));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = plainTextBody };

        var smtpPassword = NormalizeSmtpPassword(_opt.SmtpHost, _opt.SmtpPassword);

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_opt.SmtpHost, _opt.SmtpPort,
                _opt.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.StartTlsWhenAvailable,
                cancellationToken);
            await client.AuthenticateAsync(smtpUser, smtpPassword, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (AuthenticationException ex)
        {
            log.LogWarning(ex,
                "SMTP authentication failed. Host={Host}; Port={Port}; User={User}; NormalizedPasswordLength={PasswordLength}.",
                _opt.SmtpHost,
                _opt.SmtpPort,
                MaskEmail(smtpUser),
                smtpPassword.Length);
            throw new InvalidOperationException(
                "Gmail rejected the SMTP login. Create an App Password: Google Account → Security → 2-Step Verification → App passwords. Set Email:SmtpUser to your Gmail and Email:SmtpPassword to the 16-character app password (spaces are ignored). Do not use your normal Gmail password.",
                ex);
        }
        catch (SmtpCommandException ex) when (ex.Message.Contains("5.7.8", StringComparison.Ordinal) ||
                                               ex.Message.Contains("BadCredentials", StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning(ex,
                "SMTP command failed during auth. Host={Host}; Port={Port}; User={User}; NormalizedPasswordLength={PasswordLength}.",
                _opt.SmtpHost,
                _opt.SmtpPort,
                MaskEmail(smtpUser),
                smtpPassword.Length);
            throw new InvalidOperationException(
                "Gmail rejected the SMTP login. Use an App Password for Email:SmtpPassword, not your account password.",
                ex);
        }
    }
}
