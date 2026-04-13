namespace VoiceChat.Api.Services;

/// <summary>
/// Matches client-side rules: min 8 chars, uppercase, lowercase, digit, and one non-alphanumeric character.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static bool IsValid(string? password, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            error = $"Password must be at least {MinLength} characters.";
            return false;
        }

        if (!password.Any(char.IsUpper))
        {
            error = "Password must contain at least one uppercase letter.";
            return false;
        }

        if (!password.Any(char.IsLower))
        {
            error = "Password must contain at least one lowercase letter.";
            return false;
        }

        if (!password.Any(char.IsDigit))
        {
            error = "Password must contain at least one number.";
            return false;
        }

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
        {
            error = "Password must contain at least one special character.";
            return false;
        }

        return true;
    }
}
