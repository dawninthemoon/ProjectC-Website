using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProjectC.Api.Data;
using ProjectC.Api.Domain;

namespace ProjectC.Api.Auth;

public sealed class RefreshSettings
{
    public int LifetimeDays { get; init; } = 30;
}

public sealed record RefreshRequest(string? RefreshToken);

public sealed class RefreshTokenService(GameDbContext db, TokenService tokens, RefreshSettings settings, TimeProvider clock)
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public TokenResponse Issue(Player player, Guid? familyId = null)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = clock.GetUtcNow().AddDays(settings.LifetimeDays);
        db.RefreshSessions.Add(new RefreshSession { TokenHash = Hash(secret), PlayerId = player.Id,
            FamilyId = familyId ?? Guid.NewGuid(), ExpiresAt = expires });
        return tokens.Issue(player) with { RefreshToken = secret, RefreshExpiresAt = expires };
    }

    public async Task<TokenResponse?> Rotate(string token, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = Hash(token);
        var session = await db.RefreshSessions.AsNoTracking().SingleOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null) return null;
        // All operations in a token family serialize, including replay detection and logout.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({session.FamilyId.ToString()}, 0))", ct);
        session = await db.RefreshSessions.SingleAsync(s => s.TokenHash == hash, ct);
        var now = clock.GetUtcNow();
        if (session.RevokedAt is not null)
        {
            await RevokeFamily(session.FamilyId, ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        if (session.ExpiresAt <= now) return null;
        session.RevokedAt = now;
        var player = await db.Players.SingleAsync(p => p.Id == session.PlayerId, ct);
        var response = Issue(player, session.FamilyId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return response;
    }

    public async Task Revoke(string token, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = Hash(token);
        var session = await db.RefreshSessions.AsNoTracking().SingleOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({session.FamilyId.ToString()}, 0))", ct);
            await RevokeFamily(session.FamilyId, ct);
        }
        await transaction.CommitAsync(ct);
    }

    private Task<int> RevokeFamily(Guid familyId, CancellationToken ct) => db.RefreshSessions
        .Where(s => s.FamilyId == familyId && s.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);
}
