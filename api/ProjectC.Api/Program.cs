using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProjectC.Api.Auth;
using ProjectC.Api.Data;
using ProjectC.Api.Decks;
using ProjectC.Api.Endpoints;
using ProjectC.Api.Ranking;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new();
jwt.Validate();
var battle = builder.Configuration.GetSection("Battle").Get<BattleSettings>() ?? new();
battle.Validate();
if (battle.ApiKey == jwt.SigningKey) throw new InvalidOperationException("Battle and JWT keys must be different.");
var refresh = builder.Configuration.GetSection("Refresh").Get<RefreshSettings>() ?? new();
if (refresh.LifetimeDays is < 1 or > 365) throw new InvalidOperationException("Refresh:LifetimeDays must be 1..365.");
builder.Services.AddSingleton(battle);
builder.Services.AddSingleton(refresh);
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddSingleton<RankCalculator>();
var connection = builder.Configuration.GetConnectionString("GameDatabase");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("ConnectionStrings:GameDatabase is required.");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddDbContext<GameDbContext>(o => o.UseNpgsql(connection,
    p => p.MigrationsHistoryTable("__EFMigrationsHistory", "game")));
builder.Services.AddSingleton<ICardCatalog>(new CardCatalog(Path.Combine(
    builder.Environment.ContentRootPath, builder.Configuration["Catalog:Path"] ?? "Catalog/cards.json")));
builder.Services.AddScoped<DeckValidator>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateLifetime = true, RequireExpirationTime = true,
        RequireSignedTokens = true, ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(10)
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var sub = context.Principal?.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sub, out var id) ||
                !await context.HttpContext.RequestServices.GetRequiredService<GameDbContext>()
                    .Players.AnyAsync(p => p.Id == id, context.HttpContext.RequestAborted))
                context.Fail("Unknown player.");
        }
    };
}).AddScheme<AuthenticationSchemeOptions, BattleAuthenticationHandler>(BattleAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(o => o.AddPolicy("BattleServer", p =>
    p.AddAuthenticationSchemes(BattleAuthenticationHandler.SchemeName).RequireAuthenticatedUser()));
builder.Services.AddRateLimiter(o =>
{
    var guestLimit = builder.Configuration.GetValue("RateLimits:GuestPermitLimit", 10);
    if (guestLimit < 1) throw new InvalidOperationException("RateLimits:GuestPermitLimit must be positive.");
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("guest", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = guestLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGameEndpoints();
app.MapBattleEndpoints();
app.MapRefreshEndpoints();
// Migrations are an explicit deployment step, never an incidental startup side effect.
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<GameDbContext>().Database.MigrateAsync();
    return;
}
app.Run();

public partial class Program;
