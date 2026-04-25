using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Data;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public class PasswordResetService(
    AppDbContext db,
    IMailSender mail,
    IOptions<JwtOptions> jwtOptions,
    IOptions<PasswordResetOptions> resetOptions,
    ILogger<PasswordResetService> log)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly PasswordResetOptions _reset = resetOptions.Value;

    private string Pepper => _jwt.SigningKey;

    private static string HashRawToken(string rawToken, string pepper)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pepper + "|" + rawToken.Trim()));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Sends a reset email only for confirmed users who have a password (not Google-only).
    /// Always succeeds from a caller perspective (no user enumeration).
    /// </summary>
    public async Task RequestResetAsync(
        string normalizedEmail,
        string publicOrigin,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == normalizedEmail && u.EmailConfirmed,
            cancellationToken);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            log.LogInformation("Password reset requested for {Email} — no eligible account (or Google-only).", normalizedEmail);
            return;
        }

        var stale = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var t in stale)
        {
            t.UsedAt = DateTimeOffset.UtcNow;
            t.IsActive = false;
        }
        if (stale.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToHexString(rawBytes);
        var tokenHash = HashRawToken(rawToken, Pepper);

        var row = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(15, _reset.ExpiryMinutes)),
            IsActive = true
        };
        db.PasswordResetTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var baseUrl = publicOrigin.TrimEnd('/');
        var link =
            $"{baseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}&email={Uri.EscapeDataString(normalizedEmail)}";

        var body = $"""
                    You requested a password reset for VoiceChat.

                    Open this link to choose a new password (valid for {_reset.ExpiryMinutes} minutes):
                    {link}

                    If you did not request this, you can ignore this email.
                    """;

        await mail.SendAsync(user.Email!, "Reset your VoiceChat password", body, cancellationToken);
        log.LogInformation("Password reset email sent for user {UserId}.", user.Id);
    }

    public async Task<(bool ok, string? error)> TryResetPasswordAsync(
        string normalizedEmail,
        string rawToken,
        string newPassword,
        IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken = default)
    {
        if (!PasswordPolicy.IsValid(newPassword, out var pwdErr))
            return (false, pwdErr);

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == normalizedEmail && u.EmailConfirmed,
            cancellationToken);
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
            return (false, "Invalid or expired reset link.");

        var hashIn = HashRawToken(rawToken, Pepper);
        var candidates = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        var row = candidates.FirstOrDefault(t =>
            string.Equals(t.TokenHash, hashIn, StringComparison.OrdinalIgnoreCase));

        if (row is null)
            return (false, "Invalid or expired reset link.");

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        row.UsedAt = DateTimeOffset.UtcNow;
        row.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
