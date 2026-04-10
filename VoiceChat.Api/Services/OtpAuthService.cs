using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Data;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public class OtpAuthService(
    AppDbContext db,
    IMailSender mail,
    IOptions<OtpOptions> otpOptions,
    IOptions<JwtOptions> jwtOptions,
    JwtTokenService jwt,
    ILogger<OtpAuthService> log)
{
    private readonly OtpOptions _otp = otpOptions.Value;
    private readonly JwtOptions _jwtOpt = jwtOptions.Value;

    private string Pepper => string.IsNullOrWhiteSpace(_otp.Pepper) ? _jwtOpt.SigningKey : _otp.Pepper;

    private static string Hash(string code, string normalizedEmail, string pepper)
    {
        var input = $"{pepper}|{normalizedEmail}|{code}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    /// <summary>Sends registration OTP; <paramref name="userId"/> is the pending user created before the code is issued.</summary>
    public async Task SendRegisterOtpAsync(
        Guid userId,
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString("D6", null);
        var hash = Hash(code, normalizedEmail, Pepper);

        await InvalidatePendingRegisterAsync(normalizedEmail, cancellationToken);

        var row = new OtpVerification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NormalizedEmail = normalizedEmail,
            CodeHash = hash,
            Purpose = OtpPurpose.Register,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_otp.ExpiryMinutes),
            CreatedAt = DateTimeOffset.UtcNow,
            FailedAttemptCount = 0,
            IsActive = true
        };
        db.OtpVerifications.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        const string subject = "Your VoiceChat registration code";
        var body = $"""
                    Your verification code is: {code}

                    It expires in {_otp.ExpiryMinutes} minutes.
                    If you did not request this, you can ignore this message.
                    """;

        await mail.SendAsync(normalizedEmail, subject, body, cancellationToken);
        log.LogInformation("Registration OTP issued for user {UserId} email {Email}", userId, normalizedEmail);
    }

    private async Task InvalidatePendingRegisterAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var pending = await db.OtpVerifications
            .Where(x => x.NormalizedEmail == normalizedEmail && x.Purpose == OtpPurpose.Register && x.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var p in pending)
        {
            p.ConsumedAt = DateTimeOffset.UtcNow;
            p.IsActive = false;
        }
        if (pending.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(bool ok, string? error)> VerifyRegisterOtpAsync(
        string normalizedEmail,
        string code,
        CancellationToken cancellationToken = default)
    {
        var row = await db.OtpVerifications
            .Where(x => x.NormalizedEmail == normalizedEmail && x.Purpose == OtpPurpose.Register && x.ConsumedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return (false, "No active verification code. Request a new one.");

        if (row.ExpiresAt < DateTimeOffset.UtcNow)
            return (false, "This code has expired. Request a new one.");

        if (row.FailedAttemptCount >= 5)
            return (false, "Too many attempts. Request a new code.");

        var expected = Hash(code.Trim(), normalizedEmail, Pepper);
        if (!ConstantTimeHexEquals(expected, row.CodeHash))
        {
            row.FailedAttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            return (false, "Invalid code.");
        }

        row.ConsumedAt = DateTimeOffset.UtcNow;
        row.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public string IssueJwtForUser(Guid userId, string email) => jwt.CreateAccessToken(userId, email);

    private static bool ConstantTimeHexEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a), Convert.FromHexString(b));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
