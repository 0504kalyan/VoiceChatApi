using System.Globalization;

namespace VoiceChat.Api.Services;

public static class GmailAddress
{
    public static bool IsAllowedGmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        email = email.Trim();
        var at = email.LastIndexOf('@');
        if (at < 1 || at == email.Length - 1)
            return false;

        var domain = email[(at + 1)..].ToLowerInvariant();
        return domain is "gmail.com" or "googlemail.com";
    }

    public static string Normalize(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
