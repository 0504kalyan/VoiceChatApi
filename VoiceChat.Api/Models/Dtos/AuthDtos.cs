namespace VoiceChat.Api.Models.Dtos;

public sealed class SendOtpRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class RegisterCompleteRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}
