using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoiceChat.Api.Data;
using VoiceChat.Api.Models.Dtos;
using VoiceChat.Api.Models.Entities;

namespace VoiceChat.Api.Services;

public sealed class AuthAccountService(
    AppDbContext db,
    OtpAuthService otp,
    JwtTokenService jwt,
    IPasswordHasher<User> passwordHasher,
    ILogger<AuthAccountService> log)
{
    public async Task<(bool ok, string? error)> CreatePendingUserAndSendOtpAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var ok = false;
        string? error = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.EmailConfirmed, cancellationToken))
            {
                error = "This email is already registered. Sign in instead.";
                return;
            }

            var pending = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
            if (pending is { EmailConfirmed: true })
            {
                error = "This email is already registered.";
                return;
            }

            if (pending is null)
            {
                pending = new User
                {
                    Id = Guid.NewGuid(),
                    ExternalId = $"pending:{Guid.NewGuid():N}",
                    Email = normalizedEmail,
                    NormalizedEmail = normalizedEmail,
                    EmailConfirmed = false,
                    DisplayName = normalizedEmail.Split('@')[0],
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                };
                db.Users.Add(pending);
                await db.SaveChangesAsync(cancellationToken);
            }

            await otp.SendRegisterOtpAsync(pending.Id, normalizedEmail, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            ok = true;
        });

        return (ok, error);
    }

    public async Task<(AuthResponseDto? response, string? error)> CompleteRegistrationAsync(
        string normalizedEmail,
        string code,
        string password,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        AuthResponseDto? response = null;
        string? error = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var (ok, err) = await otp.VerifyRegisterOtpAsync(normalizedEmail, code, cancellationToken);
            if (!ok)
            {
                error = err;
                return;
            }

            var user = await db.Users.FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalizedEmail && !u.EmailConfirmed,
                cancellationToken);
            if (user is null)
            {
                error = "No pending registration for this email. Request a new code.";
                return;
            }

            user.PasswordHash = passwordHasher.HashPassword(user, password);
            user.EmailConfirmed = true;
            user.ExternalId = $"email:{normalizedEmail}";
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            response = IssueAuthResponse(user);
        });

        return (response, error);
    }

    public async Task<(AuthResponseDto? response, string? error)> LoginWithPasswordAsync(
        string normalizedEmail,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == normalizedEmail && u.EmailConfirmed,
            cancellationToken);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
            return (null, "Invalid email or password.");

        PasswordVerificationResult verify;
        try
        {
            verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Password hash verification failed for user {UserId}.", user.Id);
            return (null, "Invalid email or password.");
        }

        if (verify is PasswordVerificationResult.Failed)
            return (null, "Invalid email or password.");

        if (verify is PasswordVerificationResult.SuccessRehashNeeded)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
                user.PasswordHash = passwordHasher.HashPassword(user, password);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            });
        }

        return (IssueAuthResponse(user), null);
    }

    public async Task<AuthResponseDto> UpsertGoogleUserAsync(
        string normalizedEmail,
        string googleSub,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        AuthResponseDto? response = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var user = await db.Users.FirstOrDefaultAsync(
                u => u.GoogleSub == googleSub || u.NormalizedEmail == normalizedEmail,
                cancellationToken);

            if (user is null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    ExternalId = $"google:{googleSub}",
                    GoogleSub = googleSub,
                    Email = normalizedEmail,
                    NormalizedEmail = normalizedEmail,
                    EmailConfirmed = true,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedEmail.Split('@')[0] : displayName,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                };
                db.Users.Add(user);
            }
            else
            {
                user.GoogleSub ??= googleSub;
                user.Email = normalizedEmail;
                user.NormalizedEmail = normalizedEmail;
                user.EmailConfirmed = true;
                if (user.ExternalId.StartsWith("pending:", StringComparison.OrdinalIgnoreCase))
                    user.ExternalId = $"google:{googleSub}";
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            response = IssueAuthResponse(user);
        });

        return response ?? throw new InvalidOperationException("Google user upsert did not produce an auth response.");
    }

    private AuthResponseDto IssueAuthResponse(User user) =>
        new()
        {
            AccessToken = jwt.CreateAccessToken(user.Id, user.Email!),
            Email = user.Email!,
            UserId = user.Id
        };
}
