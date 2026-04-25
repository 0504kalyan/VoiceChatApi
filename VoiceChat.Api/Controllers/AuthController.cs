using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
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
    AuthAccountService accounts,
    OtpAuthService otp,
    PasswordResetService passwordReset,
    IPasswordHasher<User> passwordHasher,
    IOptions<WebClientOptions> webOptions,
    IOptions<GoogleAuthOptions> googleOptions,
    ILogger<AuthController> log) : ControllerBase
{
    private readonly WebClientOptions _web = webOptions.Value;
    private readonly GoogleAuthOptions _google = NormalizeGoogleOptions(googleOptions.Value);

    private string ResolvePublicOrigin() => WebOriginResolver.ResolvePublicOrigin(Request, _web.PublicOrigin);

    private static GoogleAuthOptions NormalizeGoogleOptions(GoogleAuthOptions options) =>
        new()
        {
            ClientId = CleanCredential(options.ClientId),
            ClientSecret = CleanCredential(options.ClientSecret)
        };

    private static string CleanCredential(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, message = "Auth API is running." });

    [HttpGet("email/status")]
    public IActionResult EmailStatus([FromServices] IOptions<EmailOptions> emailOptions)
    {
        var email = emailOptions.Value;
        var password = string.IsNullOrWhiteSpace(email.SmtpPassword)
            ? string.Empty
            : SmtpMailSender.NormalizePasswordForHost(email.SmtpHost, email.SmtpPassword);

        return Ok(new
        {
            configured = !string.IsNullOrWhiteSpace(email.SmtpUser) && !string.IsNullOrWhiteSpace(email.SmtpPassword),
            host = email.SmtpHost,
            port = email.SmtpPort,
            useSsl = email.UseSsl,
            smtpUser = SmtpMailSender.MaskEmail(email.SmtpUser),
            fromAddress = SmtpMailSender.MaskEmail(string.IsNullOrWhiteSpace(email.FromAddress) ? email.SmtpUser : email.FromAddress),
            passwordConfigured = !string.IsNullOrWhiteSpace(email.SmtpPassword),
            normalizedPasswordLength = password.Length,
            gmailAppPasswordLengthOk = email.SmtpHost.Contains("gmail", StringComparison.OrdinalIgnoreCase) && password.Length == 16,
            expectedForGmail = new
            {
                host = "smtp.gmail.com",
                port = 587,
                useSsl = true,
                appPasswordLength = 16
            }
        });
    }

    /// <summary>Creates or reuses a pending user, then sends a registration OTP (always linked to <c>UserId</c>).</summary>
    [HttpPost("register/send-otp")]
    public async Task<IActionResult> RegisterSendOtp([FromBody] SendOtpRequest body,
        CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Use a Gmail address (@gmail.com or @googlemail.com)." });

        var norm = GmailAddress.Normalize(body.Email);
        try
        {
            var (ok, err) = await accounts.CreatePendingUserAndSendOtpAsync(norm, cancellationToken);
            if (!ok)
                return Conflict(new { message = err });
        }
        catch (Exception ex) when (ex is NpgsqlException || ex is TimeoutException || ex is DbUpdateException)
        {
            log.LogError(ex, "Database unavailable during register/send-otp for {Email}.", norm);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message =
                    "Database is temporarily unavailable. Please try again in a few moments."
            });
        }

        catch (Exception ex)
        {
            log.LogError(ex, "Failed to send registration OTP email to {Email}.", norm);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message =
                    "Could not send the verification email. Gmail rejected the SMTP login. Verify /api/auth/email/status, then ensure Email__SmtpUser is the same Gmail account that generated Email__SmtpPassword (16-character App Password)."
            });
        }

        return Ok(new { message = "If the address is valid, a verification code was sent." });
    }

    [HttpPost("register/validate-otp")]
    public async Task<IActionResult> RegisterValidateOtp([FromBody] ValidateOtpRequest body,
        CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Invalid email." });

        var norm = GmailAddress.Normalize(body.Email);
        var (ok, err) = await otp.ValidateRegisterOtpAsync(norm, body.Code, cancellationToken);
        if (!ok)
            return BadRequest(new { message = err });

        return Ok(new { message = "OTP validated. You can now set your password." });
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
        var (response, err) = await accounts.CompleteRegistrationAsync(
            norm,
            body.Code,
            body.Password,
            cancellationToken);
        if (response is null)
            return BadRequest(new { message = err });

        return Ok(response);
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
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message =
                    "Could not send reset email. Configure Email__SmtpUser and Email__SmtpPassword (Gmail App Password) in environment variables."
            });
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
            clientIdLooksValid = _google.HasValidClientIdShape,
            authorizedRedirectUri,
            hint =
                "Google client id must end with .apps.googleusercontent.com. If Google shows redirect_uri_mismatch, add authorizedRedirectUri exactly."
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken cancellationToken)
    {
        if (!GmailAddress.IsAllowedGmail(body.Email))
            return BadRequest(new { message = "Use a Gmail address (@gmail.com or @googlemail.com)." });

        var norm = GmailAddress.Normalize(body.Email);
        var (response, _) = await accounts.LoginWithPasswordAsync(norm, body.Password, cancellationToken);
        if (response is null)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(response);
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

        var auth = await accounts.UpsertGoogleUserAsync(norm, sub, name, cancellationToken);
        await HttpContext.SignOutAsync("External");

        var next =
            $"{publicOrigin}/auth/google-callback?token={Uri.EscapeDataString(auth.AccessToken)}";
        return Redirect(next);
    }
}
