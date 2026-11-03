using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProjectC.Api.Auth;
using ProjectC.Api.Data;
using ProjectC.Api.Decks;
using ProjectC.Api.Domain;
using ProjectC.Api.Ranking;

namespace ProjectC.Api.Endpoints;

public sealed record VerifyPlayerRequest(string? AccessToken);
public sealed record ValidateBattleDeckRequest(string? CatalogVersion);
public sealed record BattleParticipantRequest(Guid PlayerId, Guid DeckId, Guid SnapshotId);
public sealed record SubmitMatchRequest(Guid MatchId, List<BattleParticipantRequest?>? Participants,
    Guid? WinnerPlayerId, string? EndReason, DateTimeOffset EndedAt);

public static class BattleEndpoints
{
    private static IResult Error(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    public static void MapBattleEndpoints(this WebApplication app)
    {
        var battle = app.MapGroup("/api/internal/battle").RequireAuthorization("BattleServer");
        battle.MapPost("/players/verify", async (VerifyPlayerRequest request, IOptionsMonitor<JwtBearerOptions> options,
            GameDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.AccessToken) || request.AccessToken.Length > 16384)
                return Error(401, "invalid_player_token");
            try
            {
                var parameters = options.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters.Clone();
                parameters.ClockSkew = TimeSpan.Zero;
                var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }
                    .ValidateToken(request.AccessToken, parameters, out _);
                if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var playerId))
                    return Error(401, "invalid_player_token");
                var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(p => p.Id == playerId, ct);
                return player is null ? Error(401, "invalid_player_token")
                    : Results.Ok(new UserResponse(player.Id, player.DisplayName, player.CreatedAt));
            }
            catch (Exception e) when (e is SecurityTokenException or ArgumentException)
            { return Error(401, "invalid_player_token"); }
        });
        battle.MapPost("/players/{playerId:guid}/decks/{deckId:guid}/validate", async (
            Guid playerId, Guid deckId, ValidateBattleDeckRequest request, GameDbContext db,
            ICardCatalog catalog, DeckValidator validator, TimeProvider clock, CancellationToken ct) =>
        {
            var deck = await db.Decks.AsNoTracking().Include(d => d.Cards)
                .SingleOrDefaultAsync(d => d.Id == deckId && d.PlayerId == playerId, ct);
            if (deck is null) return Error(404, "deck_not_found");
            if (request.CatalogVersion != catalog.Version || deck.CatalogVersion != catalog.Version)
                return Error(409, "catalog_version_mismatch");
            var cards = deck.Cards.OrderBy(c => c.CardId).Select(c => new CardEntry(c.CardId, c.Quantity)).ToList();
            if (validator.Validate(new(deck.Name, cards.Select(c => (CardEntry?)c).ToList())).Count > 0)
                return Error(422, "invalid_deck");
            var snapshot = new BattleDeckSnapshot { PlayerId = playerId, DeckId = deckId,
                DeckName = deck.Name, CatalogVersion = catalog.Version, CardsJson = JsonSerializer.Serialize(cards),
                CreatedAt = clock.GetUtcNow() };
            db.BattleDeckSnapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { snapshotId = snapshot.Id, playerId, deckId, catalogVersion = catalog.Version,
                cards, cardIds = cards.SelectMany(c => Enumerable.Repeat(c.CardId, c.Quantity)) });
        });
        battle.MapPost("/matches", SubmitMatch);
    }

    private static async Task<IResult> SubmitMatch(SubmitMatchRequest request, GameDbContext db,
        RankCalculator calculator, TimeProvider clock, CancellationToken ct)
    {
        if (request.MatchId == Guid.Empty || request.Participants is not { Count: 2 } ||
            request.Participants.Any(p => p is null || p.PlayerId == Guid.Empty || p.DeckId == Guid.Empty || p.SnapshotId == Guid.Empty) ||
            request.Participants.Select(p => p!.PlayerId).Distinct().Count() != 2 ||
            request.EndedAt == default || request.EndedAt > clock.GetUtcNow().AddMinutes(5) ||
            request.EndReason is not ("Normal" or "Surrender" or "Disconnect" or "Draw" or "Aborted") ||
            (request.EndReason is "Draw" or "Aborted" ? request.WinnerPlayerId is not null
                : !request.Participants.Any(p => p!.PlayerId == request.WinnerPlayerId)))
            return Error(400, "invalid_match");

        var participants = request.Participants.Select(p => p!).OrderBy(p => p.PlayerId).ToArray();
        var canonical = request with { Participants = participants.Select(p => (BattleParticipantRequest?)p).ToList(),
            EndedAt = request.EndedAt.ToUniversalTime() };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Transaction-scoped locks work across API instances. Match first, then players in UUID order.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"match:" + request.MatchId}, 0))", ct);
        var existing = await db.Matches.AsNoTracking().SingleOrDefaultAsync(m => m.MatchId == request.MatchId, ct);
        if (existing is not null)
            return existing.SubmissionHash == hash
                ? Results.Ok(new { matchId = existing.MatchId, duplicate = true,
                    rankChanges = JsonSerializer.Deserialize<RankChange[]>(existing.RankChangesJson!) })
                : Error(409, "match_result_conflict");

        foreach (var p in participants)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"player:" + p.PlayerId}, 0))", ct);
        var ids = participants.Select(p => p.SnapshotId).ToArray();
        var snapshots = await db.BattleDeckSnapshots.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (participants.Any(p => !snapshots.Any(s => s.Id == p.SnapshotId && s.PlayerId == p.PlayerId && s.DeckId == p.DeckId)))
            return Error(422, "invalid_deck_snapshot");
        if (snapshots.Select(s => s.CatalogVersion).Distinct().Count() != 1)
            return Error(409, "catalog_version_mismatch");
        var match = new MatchResult { MatchId = request.MatchId, EndReason = request.EndReason!,
            EndedAt = request.EndedAt.ToUniversalTime(), SubmissionHash = hash,
            Participants = participants.Select(p =>
            {
                var snapshot = snapshots.Single(s => s.Id == p.SnapshotId);
                return new MatchParticipant { PlayerId = p.PlayerId, DeckId = p.DeckId, DeckName = snapshot.DeckName,
                    CatalogVersion = snapshot.CatalogVersion,
                    Cards = JsonSerializer.Deserialize<CardEntry[]>(snapshot.CardsJson)!.Select(c =>
                        new MatchDeckCard { CardId = c.CardId, Quantity = c.Quantity }).ToList() };
            }).ToList() };
        db.Matches.Add(match);
        await db.SaveChangesAsync(ct); // Participants must exist before setting the composite winner FK.
        match.WinnerPlayerId = request.WinnerPlayerId;
        var changes = new List<RankChange>();
        if (request.EndReason != "Aborted")
        {
            var playerIds = participants.Select(p => p.PlayerId).ToArray();
            var ranks = await db.Ranks.Include(r => r.PromotionGames).Where(r => playerIds.Contains(r.PlayerId)).ToListAsync(ct);
            var mmrs = ranks.ToDictionary(r => r.PlayerId, r => r.Mmr);
            foreach (var rank in ranks)
                changes.Add(calculator.Apply(rank, mmrs.Single(m => m.Key != rank.PlayerId).Value,
                    request.WinnerPlayerId is null ? .5 : request.WinnerPlayerId == rank.PlayerId ? 1 : 0));
        }
        match.RankChangesJson = JsonSerializer.Serialize(changes);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { matchId = match.MatchId, duplicate = false, rankChanges = changes });
    }
}
