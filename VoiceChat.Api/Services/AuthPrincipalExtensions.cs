using System.Security.Claims;

namespace VoiceChat.Api.Services;

public static class AuthPrincipalExtensions
{
    public static Guid? TryUserId(this ClaimsPrincipal? principal)
    {
        var v = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(v, out var id) ? id : null;
    }

    public static Guid RequireUserId(this ClaimsPrincipal principal)
    {
        return principal.TryUserId()
               ?? throw new InvalidOperationException("Authenticated user id claim is missing.");
    }
}
