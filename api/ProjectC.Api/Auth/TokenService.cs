using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProjectC.Api.Domain;

namespace ProjectC.Api.Auth;

public sealed class JwtSettings
{
    public string Issuer { get; init; } = "ProjectC.Website";
    public string Audience { get; init; } = "ProjectC.Client";
    public string SigningKey { get; init; } = "";
    public int LifetimeMinutes { get; init; } = 1440;

    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32 ||
            string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) ||
            LifetimeMinutes is < 1 or > 10080)
            throw new InvalidOperationException("Configure Jwt issuer, audience, a random signing key of at least 32 bytes, and lifetime (1..10080 minutes).");
    }
}

public sealed class TokenService(JwtSettings settings, TimeProvider clock)
{
    public TokenResponse Issue(Player player)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, player.Id.ToString()),
             new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires,
            new(player.Id, player.DisplayName, player.CreatedAt));
    }
}

public sealed record UserResponse(Guid PlayerId, string DisplayName, DateTimeOffset CreatedAt);
public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserResponse User);
