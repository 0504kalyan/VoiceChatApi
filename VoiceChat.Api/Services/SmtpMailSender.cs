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
    ILogger<SmtpMailSender> log,
    IHostEnvironment env) : IMailSender
{
    private readonly EmailOptions _opt = options.Value;

    /// <summary>Gmail App Passwords are often copied with spaces; SMTP expects 16 characters without spaces.</summary>
    private static string NormalizeSmtpPassword(string smtpHost, string password)
    {
        if (string.IsNullOrEmpty(password))
            return password;
        if (smtpHost.Contains("gmail", StringComparison.OrdinalIgnoreCase))
            return password.Replace(" ", "", StringComparison.Ordinal);
        return password;
    }

    public async Task SendAsync(string toAddress, string subject, string plainTextBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.SmtpUser) || string.IsNullOrWhiteSpace(_opt.SmtpPassword))
        {
            log.LogWarning(
                "Email not configured (SmtpUser/SmtpPassword empty). OTP delivery — To: {To}, Subject: {Subject}\n{Body}",
                toAddress, subject, plainTextBody);
            if (!env.IsDevelopment())
                throw new InvalidOperationException("Email is not configured.");
            return;
        }

        var from = string.IsNullOrWhiteSpace(_opt.FromAddress) ? _opt.SmtpUser : _opt.FromAddress;

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
            await client.AuthenticateAsync(_opt.SmtpUser, smtpPassword, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (AuthenticationException ex)
        {
            log.LogWarning(ex, "SMTP authentication failed for SmtpUser {SmtpUser} on {Host}.", _opt.SmtpUser, _opt.SmtpHost);
            throw new InvalidOperationException(
                "Gmail rejected the SMTP login. Create an App Password: Google Account → Security → 2-Step Verification → App passwords. Set Email:SmtpUser to your Gmail and Email:SmtpPassword to the 16-character app password (spaces are ignored). Do not use your normal Gmail password.",
                ex);
        }
        catch (SmtpCommandException ex) when (ex.Message.Contains("5.7.8", StringComparison.Ordinal) ||
                                               ex.Message.Contains("BadCredentials", StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning(ex, "SMTP command failed during auth for SmtpUser {SmtpUser}.", _opt.SmtpUser);
            throw new InvalidOperationException(
                "Gmail rejected the SMTP login. Use an App Password for Email:SmtpPassword, not your account password.",
                ex);
        }
    }
}
