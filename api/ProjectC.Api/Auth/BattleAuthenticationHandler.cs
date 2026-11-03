using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ProjectC.Api.Auth;

public sealed class BattleSettings
{
    public string ApiKey { get; init; } = "";
    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(ApiKey) < 32)
            throw new InvalidOperationException("Battle:ApiKey requires a random secret of at least 32 bytes.");
    }
}

public sealed class BattleAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, BattleSettings settings) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "BattleServer";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var supplied = Request.Headers["X-Battle-Api-Key"];
        if (supplied.Count != 1 || string.IsNullOrEmpty(supplied[0]))
            return Task.FromResult(AuthenticateResult.NoResult());
        var valid = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied[0]!)),
            SHA256.HashData(Encoding.UTF8.GetBytes(settings.ApiKey)));
        return Task.FromResult(valid
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "battle-server")], SchemeName)), SchemeName))
            : AuthenticateResult.Fail("Invalid Battle Server credentials."));
    }
}
