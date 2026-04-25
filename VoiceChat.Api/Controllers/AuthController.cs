using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Data;
using VoiceChat.Api.Infrastructure;
using VoiceChat.Api.Models.Dtos;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    OtpAuthService otp,
    PasswordResetService passwordReset,
    IPasswordHasher<User> passwordHasher,
    IOptions<WebClientOptions> webOptions,
    IOptions<GoogleAuthOptions> googleOptions,
    ILogger<AuthController> log) : ControllerBase
{
    private readonly WebClientOptions _web = webOptions.Value;
    private readonly GoogleAuthOptions _google = googleOptions.Value;

    private string ResolvePublicOrigin() => WebOriginResolver.ResolvePublicOrigin(Request, _web.PublicOrigin);

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, message = "Auth API is running." });

    /// <summary>Creates or reuses a pending user, then sends a registration OTP (always linked to <c>UserId</c>).</summary>
    [HttpPost("register/send-otp")]
    public async Task<IActionResult> RegisterSendOtp([FromBody] SendOtpRequest body,
        CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Use a Gmail address (@gmail.com or @googlemail.com)." });

        var norm = GmailAddress.Normalize(body.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == norm && u.EmailConfirmed, cancellationToken))
            return Conflict(new { message = "This email is already registered. Sign in instead." });

        var pending = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == norm, cancellationToken);
        if (pending is { EmailConfirmed: true })
            return Conflict(new { message = "This email is already registered." });

        if (pending is null)
        {
            pending = new User
            {
                Id = Guid.NewGuid(),
                ExternalId = $"pending:{Guid.NewGuid():N}",
                Email = norm,
                NormalizedEmail = norm,
                EmailConfirmed = false,
                DisplayName = norm.Split('@')[0],
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };
            db.Users.Add(pending);
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await otp.SendRegisterOtpAsync(pending.Id, norm, cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to send registration OTP email to {Email}.", norm);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message =
                    "Could not send the verification email. Gmail no longer accepts your normal password for SMTP: create a 16-character App Password (Google Account → Security → 2-Step Verification → App passwords) and set Email:SmtpPassword on the API. See https://support.google.com/mail/?p=BadCredentials"
            });
        }

        return Ok(new { message = "If the address is valid, a verification code was sent." });
    }

    [HttpPost("register/complete")]
    public async Task<IActionResult> RegisterComplete([FromBody] RegisterCompleteRequest body,
        CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Invalid email." });

        if (!PasswordPolicy.IsValid(body.Password, out var pwdErr))
            return BadRequest(new { message = pwdErr });

        var norm = GmailAddress.Normalize(body.Email);
        var (ok, err) = await otp.VerifyRegisterOtpAsync(norm, body.Code, cancellationToken);
        if (!ok)
            return BadRequest(new { message = err });

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == norm && !u.EmailConfirmed,
            cancellationToken);
        if (user is null)
            return BadRequest(new { message = "No pending registration for this email. Request a new code." });

        user.PasswordHash = passwordHasher.HashPassword(user, body.Password);
        user.EmailConfirmed = true;
        user.ExternalId = $"email:{norm}";
        await db.SaveChangesAsync(cancellationToken);

        var token = otp.IssueJwtForUser(user.Id, user.Email!);
        return Ok(new AuthResponseDto { AccessToken = token, Email = user.Email!, UserId = user.Id });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Use a Gmail address (@gmail.com or @googlemail.com)." });

        var norm = GmailAddress.Normalize(body.Email);
        try
        {
            await passwordReset.RequestResetAsync(norm, ResolvePublicOrigin(), cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to send password reset email.");
            return StatusCode(500, new { message = "Could not send email. Verify Email SMTP settings on the API." });
        }

        return Ok(new { message = "If an account exists with that email, a reset link was sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordWithToken([FromBody] ResetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Invalid email." });

        var norm = GmailAddress.Normalize(body.Email);
        var (ok, err) = await passwordReset.TryResetPasswordAsync(
            norm,
            body.Token,
            body.NewPassword,
            passwordHasher,
            cancellationToken);

        if (!ok)
            return BadRequest(new { message = err });

        return Ok(new { message = "Password updated. You can sign in." });
    }

    /// <summary>
    /// Google OAuth status. <paramref name="authorizedRedirectUri"/> is the exact value you must add under
    /// Google Cloud Console → Credentials → your OAuth client → Authorized redirect URIs (must match how the browser calls this API: same scheme, host, port).
    /// </summary>
    [HttpGet("google/status")]
    public IActionResult GoogleOAuthStatus()
    {
        var authorizedRedirectUri = $"{Request.Scheme}://{Request.Host.Value}/signin-google";
        return Ok(new
        {
            configured = _google.IsConfigured,
            authorizedRedirectUri,
            hint =
                "If Google shows redirect_uri_mismatch, add authorizedRedirectUri exactly (and both http/https + ports if you use more than one)."
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Use a Gmail address (@gmail.com or @googlemail.com)." });

        var norm = GmailAddress.Normalize(body.Email);
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == norm && u.EmailConfirmed,
            cancellationToken);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, body.Password);
        if (verify is PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid email or password." });

        if (verify is PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, body.Password);
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = otp.IssueJwtForUser(user.Id, user.Email!);
        return Ok(new AuthResponseDto { AccessToken = token, Email = user.Email!, UserId = user.Id });
    }

    [HttpGet("google")]
    public IActionResult GoogleStart()
    {
        var publicOrigin = ResolvePublicOrigin();
        if (!_google.IsConfigured)
        {
            var login = $"{publicOrigin}/login?error=google_not_configured";
            return Redirect(login);
        }

        var redirect = Url.Action(nameof(GoogleCallback), "Auth", new { clientOrigin = publicOrigin }, Request.Scheme)
                       ?? throw new InvalidOperationException("Could not build callback URL.");
        return Challenge(new AuthenticationProperties { RedirectUri = redirect }, "Google");
    }

    [HttpGet("google-callback")]
    [Authorize(AuthenticationSchemes = "External")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string? clientOrigin, CancellationToken cancellationToken)
    {
        var publicOrigin = WebOriginResolver.ResolvePublicOrigin(Request, clientOrigin ?? _web.PublicOrigin);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(sub))
        {
            log.LogWarning("Google callback missing email or sub.");
            return Redirect($"{publicOrigin}/login?error=google_claims");
        }

        if (!GmailAddress.IsAllowedGmail(email))
        {
            await HttpContext.SignOutAsync("External");
            return Redirect($"{publicOrigin}/login?error=gmail_only");
        }

        var norm = GmailAddress.Normalize(email);

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.GoogleSub == sub || u.NormalizedEmail == norm,
            cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                ExternalId = $"google:{sub}",
                GoogleSub = sub,
                Email = norm,
                NormalizedEmail = norm,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(name) ? norm.Split('@')[0] : name,
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };
            db.Users.Add(user);
        }
        else
        {
            user.GoogleSub ??= sub;
            user.Email = norm;
            user.NormalizedEmail = norm;
            user.EmailConfirmed = true;
            if (user.ExternalId.StartsWith("pending:", StringComparison.OrdinalIgnoreCase))
                user.ExternalId = $"google:{sub}";
        }

        await db.SaveChangesAsync(cancellationToken);

        var token = otp.IssueJwtForUser(user.Id, user.Email!);
        await HttpContext.SignOutAsync("External");

        var next =
            $"{publicOrigin}/auth/google-callback?token={Uri.EscapeDataString(token)}";
        return Redirect(next);
    }
}
