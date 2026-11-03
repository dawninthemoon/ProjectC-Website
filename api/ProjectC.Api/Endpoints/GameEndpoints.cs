using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjectC.Api.Auth;
using ProjectC.Api.Data;
using ProjectC.Api.Decks;
using ProjectC.Api.Domain;

namespace ProjectC.Api.Endpoints;

public sealed record GuestRequest(string? DisplayName);
public sealed record DeckResponse(Guid Id, string Name, string CatalogVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<CardEntry> Cards);

public static class GameEndpoints
{
    private static Guid PlayerId(ClaimsPrincipal user) => Guid.Parse(user.FindFirst("sub")!.Value);
    private static DeckResponse Response(Deck d) => new(d.Id, d.Name, d.CatalogVersion,
        d.CreatedAt, d.UpdatedAt, d.Cards.OrderBy(c => c.CardId).Select(c => new CardEntry(c.CardId, c.Quantity)).ToArray());

    public static void MapGameEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/api/auth/guest", async (GuestRequest request, GameDbContext db, TokenService tokens, CancellationToken ct) =>
        {
            var name = request.DisplayName?.Trim() ?? "Guest";
            if (name.Length is < 1 or > 32)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["displayName"] = ["표시 이름은 1~32자여야 합니다."] });
            var player = new Player { DisplayName = name };
            db.Players.Add(player);
            await db.SaveChangesAsync(ct);
            return Results.Ok(tokens.Issue(player));
        }).RequireRateLimiting("guest");

        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/users/me", async (ClaimsPrincipal user, GameDbContext db, CancellationToken ct) =>
        {
            var id = PlayerId(user);
            return await db.Players.AsNoTracking().Where(p => p.Id == id)
                .Select(p => new UserResponse(p.Id, p.DisplayName, p.CreatedAt)).SingleAsync(ct);
        });
        api.MapGet("/rank/me", async (ClaimsPrincipal user, GameDbContext db, CancellationToken ct) =>
        {
            var rank = await db.Ranks.AsNoTracking().Include(r => r.PromotionGames)
                .SingleAsync(r => r.PlayerId == PlayerId(user), ct);
            return Results.Ok(new
            {
                rank.Mmr, rank.Tier, rank.Division, rank.Rp, rank.WinStreak, rank.PromotionStatus,
                promotionGames = rank.PromotionGames.OrderBy(g => g.SeriesId).ThenBy(g => g.GameNumber)
                    .Select(g => new { g.SeriesId, g.GameNumber, g.Won })
            });
        });
        api.MapGet("/decks", async (ClaimsPrincipal user, GameDbContext db, CancellationToken ct) =>
        {
            var decks = await db.Decks.AsNoTracking().Include(d => d.Cards)
                .Where(d => d.PlayerId == PlayerId(user)).OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).ToListAsync(ct);
            return Results.Ok(decks.Select(Response));
        });
        api.MapPost("/decks", async (SaveDeckRequest request, ClaimsPrincipal user, GameDbContext db,
            DeckValidator validator, ICardCatalog catalog, CancellationToken ct) =>
        {
            var errors = validator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var deck = new Deck { PlayerId = PlayerId(user), Name = request.Name!.Trim(), CatalogVersion = catalog.Version,
                Cards = request.Cards!.Select(c => new DeckCard { CardId = c!.CardId, Quantity = c.Quantity }).ToList() };
            db.Decks.Add(deck);
            await db.SaveChangesAsync(ct);
            return Results.Created("/api/decks", Response(deck));
        });
        api.MapPut("/decks/{id:guid}", async (Guid id, SaveDeckRequest request, ClaimsPrincipal user,
            GameDbContext db, DeckValidator validator, ICardCatalog catalog, CancellationToken ct) =>
        {
            var deck = await db.Decks.Include(d => d.Cards)
                .SingleOrDefaultAsync(d => d.Id == id && d.PlayerId == PlayerId(user), ct);
            if (deck is null) return Results.NotFound();
            var errors = validator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            deck.Name = request.Name!.Trim();
            deck.CatalogVersion = catalog.Version;
            deck.UpdatedAt = DateTimeOffset.UtcNow;
            // Update existing composite keys instead of tracking two instances of the same key.
            var entries = request.Cards!.Select(c => c!).ToDictionary(c => c.CardId);
            foreach (var old in deck.Cards.ToArray())
            {
                if (entries.Remove(old.CardId, out var entry)) old.Quantity = entry.Quantity;
                else { deck.Cards.Remove(old); db.Remove(old); }
            }
            foreach (var entry in entries.Values)
                deck.Cards.Add(new DeckCard { CardId = entry.CardId, Quantity = entry.Quantity });
            await db.SaveChangesAsync(ct);
            return Results.Ok(Response(deck));
        });
        api.MapDelete("/decks/{id:guid}", async (Guid id, ClaimsPrincipal user, GameDbContext db, CancellationToken ct) =>
        {
            var deck = await db.Decks.SingleOrDefaultAsync(d => d.Id == id && d.PlayerId == PlayerId(user), ct);
            if (deck is null) return Results.NotFound();
            db.Decks.Remove(deck);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
