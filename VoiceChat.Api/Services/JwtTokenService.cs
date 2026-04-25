using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _opt = options.Value;

    public string CreateAccessToken(Guid userId, string email, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(_opt.SigningKey) || _opt.SigningKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            _opt.Issuer,
            _opt.Audience,
            claims,
            now,
            now.AddMinutes(_opt.ExpiryMinutes),
            creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
