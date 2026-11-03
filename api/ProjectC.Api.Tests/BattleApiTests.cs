using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectC.Api.Auth;
using ProjectC.Api.Data;
using ProjectC.Api.Decks;
using ProjectC.Api.Domain;
using ProjectC.Api.Endpoints;
using Xunit;

namespace ProjectC.Api.Tests;

public sealed partial class ApiTests
{
    private HttpClient BattleClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Battle-Api-Key", "battle-integration-tests-only-key-1234567890");
        return client;
    }

    private async Task<(TokenResponse Token, Deck Deck)> Seed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var player = new Player { Rank = new RankProfile { Tier = "Gold", Division = 3, Rp = 50 } };
        db.Players.Add(player);
        var deck = new Deck { PlayerId = player.Id, Name = "Battle deck",
            CatalogVersion = scope.ServiceProvider.GetRequiredService<ICardCatalog>().Version,
            Cards = ValidDeck().Cards!.Select(c => new DeckCard { CardId = c!.CardId, Quantity = c.Quantity }).ToList() };
        db.Decks.Add(deck);
        var token = scope.ServiceProvider.GetRequiredService<RefreshTokenService>().Issue(player);
        await db.SaveChangesAsync();
        return (token, deck);
    }

    private async Task<Guid> Snapshot(HttpClient battle, Guid playerId, Deck deck)
    {
        var response = await battle.PostAsJsonAsync($"/api/internal/battle/players/{playerId}/decks/{deck.Id}/validate",
            new { deck.CatalogVersion });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30, body.GetProperty("cardIds").GetArrayLength());
        return body.GetProperty("snapshotId").GetGuid();
    }

    private async Task<SubmitMatchRequest> Match(HttpClient battle)
    {
        var (a, da) = await Seed(); var (b, db) = await Seed();
        return new(Guid.NewGuid(), [new(a.User.PlayerId, da.Id, await Snapshot(battle, a.User.PlayerId, da)),
            new(b.User.PlayerId, db.Id, await Snapshot(battle, b.User.PlayerId, db))], a.User.PlayerId,
            "Normal", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task BattleCredentialsAreRequiredAndCannotActAsPlayer()
    {
        var (token, deck) = await Seed();
        using var client = factory.CreateClient();
        foreach (var key in new string?[] { null, "incorrect" })
        {
            client.DefaultRequestHeaders.Remove("X-Battle-Api-Key");
            if (key is not null) client.DefaultRequestHeaders.Add("X-Battle-Api-Key", key);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/internal/battle/matches", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/internal/battle/players/verify",
                new { token.AccessToken })).StatusCode);
        }
        using var battle = BattleClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await battle.GetAsync("/api/users/me")).StatusCode);
        var verified = await battle.PostAsJsonAsync("/api/internal/battle/players/verify", new { token.AccessToken });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal(token.User.PlayerId, (await verified.Content.ReadFromJsonAsync<UserResponse>())!.PlayerId);
        foreach (var invalid in new[] { "bad", token.AccessToken + "tampered", "" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await battle.PostAsJsonAsync("/api/internal/battle/players/verify",
                new { accessToken = invalid })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await battle.PostAsJsonAsync(
            $"/api/internal/battle/players/{Guid.NewGuid()}/decks/{deck.Id}/validate", new { deck.CatalogVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await battle.PostAsJsonAsync(
            $"/api/internal/battle/players/{token.User.PlayerId}/decks/{deck.Id}/validate", new { catalogVersion = "old" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        await db.Set<DeckCard>().Where(c => c.DeckId == deck.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Quantity, 99));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await battle.PostAsJsonAsync(
            $"/api/internal/battle/players/{token.User.PlayerId}/decks/{deck.Id}/validate", new { deck.CatalogVersion })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRetriesAreIdempotentAndConflictDoesNotChangeRanks()
    {
        using var battle = BattleClient(); var match = await Match(battle);
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => battle.PostAsJsonAsync("/api/internal/battle/matches", match)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Single(bodies, b => !b.GetProperty("duplicate").GetBoolean());
        Assert.All(bodies, b => Assert.Equal(2, b.GetProperty("rankChanges").GetArrayLength()));
        var reversed = match with { Participants = match.Participants!.AsEnumerable().Reverse().ToList() };
        Assert.Equal(HttpStatusCode.OK, (await battle.PostAsJsonAsync("/api/internal/battle/matches", reversed)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await battle.PostAsJsonAsync("/api/internal/battle/matches",
            match with { WinnerPlayerId = match.Participants![1]!.PlayerId })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        Assert.Equal(1, await db.Matches.CountAsync(m => m.MatchId == match.MatchId));
        var winner = await db.Ranks.SingleAsync(r => r.PlayerId == match.WinnerPlayerId);
        Assert.Equal(1016, winner.Mmr); Assert.Equal(70, winner.Rp);
    }

    [Fact]
    public async Task SnapshotSurvivesDeckDeletionAndBadRequestsAreRejected()
    {
        using var battle = BattleClient(); var match = await Match(battle);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var deckIds = match.Participants!.Select(p => p!.DeckId).ToArray();
            await db.Decks.Where(d => deckIds.Contains(d.Id)).ExecuteDeleteAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await battle.PostAsJsonAsync("/api/internal/battle/matches", match)).StatusCode);
        foreach (var bad in new[] { match with { MatchId = Guid.Empty }, match with { EndReason = "bad" },
            match with { WinnerPlayerId = Guid.NewGuid() }, match with { Participants = [null, null] },
            match with { EndReason = "Draw" }, match with { EndedAt = default } })
            Assert.Equal(HttpStatusCode.BadRequest, (await battle.PostAsJsonAsync("/api/internal/battle/matches", bad)).StatusCode);
        var invalidSnapshot = match with { MatchId = Guid.NewGuid(), Participants = [
            match.Participants![0]! with { SnapshotId = match.Participants[1]!.SnapshotId }, match.Participants[1]] };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await battle.PostAsJsonAsync("/api/internal/battle/matches", invalidSnapshot)).StatusCode);
    }

    [Fact]
    public async Task FailureAfterMatchInsertRollsBackMatchAndBothRanks()
    {
        using var battle = BattleClient(); var match = await Match(battle);
        // A real database constraint fails on the second SaveChanges, after match/participant INSERTs.
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE game.\"Ranks\" ADD CONSTRAINT \"Test_Rank_Rollback\" CHECK (\"Mmr\" = 1000) NOT VALID");
        try
        {
            try
            {
                var response = await battle.PostAsJsonAsync("/api/internal/battle/matches", match);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            }
            catch (HttpRequestException) { /* TestServer may propagate an unhandled server error. */ }
            Assert.False(await db.Matches.AnyAsync(m => m.MatchId == match.MatchId));
            var ids = match.Participants!.Select(p => p!.PlayerId).ToArray();
            Assert.All(await db.Ranks.AsNoTracking().Where(r => ids.Contains(r.PlayerId)).ToListAsync(), r =>
            { Assert.Equal(1000, r.Mmr); Assert.Equal(50, r.Rp); });
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE game.\"Ranks\" DROP CONSTRAINT \"Test_Rank_Rollback\""); }
        Assert.Equal(HttpStatusCode.OK, (await battle.PostAsJsonAsync("/api/internal/battle/matches", match)).StatusCode);
    }

    [Fact]
    public async Task RefreshRotatesRetainsIdentityAndReplayRevokesFamily()
    {
        var (token, deck) = await Seed(); using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { token.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var next = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(token.User.PlayerId, next.User.PlayerId); Assert.NotEqual(token.RefreshToken, next.RefreshToken);
        client.DefaultRequestHeaders.Authorization = new("Bearer", next.AccessToken);
        Assert.Contains((await client.GetFromJsonAsync<List<DeckResponse>>("/api/decks"))!, d => d.Id == deck.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { token.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { next.RefreshToken })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        Assert.False(await db.RefreshSessions.AnyAsync(s => s.TokenHash == token.RefreshToken));
        Assert.All(await db.RefreshSessions.Where(s => s.PlayerId == token.User.PlayerId).ToListAsync(), s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task ExpiredRefreshIsRejectedAndLegacyJwtCanAcquireSession()
    {
        var (token, _) = await Seed(); using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            await db.RefreshSessions.Where(s => s.TokenHash == RefreshTokenService.Hash(token.RefreshToken!))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { token.RefreshToken })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        var response = await client.PostAsync("/api/auth/session", null); response.EnsureSuccessStatusCode();
        var persistent = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(token.User.PlayerId, persistent.User.PlayerId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/revoke", new { persistent.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { persistent.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task ExpiredAccessTokenCanRefreshAndConcurrentRotationRevokesFamily()
    {
        var (token, _) = await Seed(); using var client = factory.CreateClient();
        var expired = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("ProjectC.Website", "ProjectC.Client",
            [new System.Security.Claims.Claim("sub", token.User.PlayerId.ToString())],
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1),
            new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(ApiFactory.TestKey)), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(expired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        var rotated = await client.PostAsJsonAsync("/api/auth/refresh", new { token.RefreshToken });
        rotated.EnsureSuccessStatusCode();
        var next = (await rotated.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(token.User.PlayerId, next.User.PlayerId);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            client.PostAsJsonAsync("/api/auth/refresh", new { next.RefreshToken })));
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.Unauthorized);
        var successful = (await concurrent.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { successful.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task PromotionGamesPersistAcrossRequestsAndAbortedMatchLeavesRanksAlone()
    {
        using var battle = BattleClient(); var match = await Match(battle);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            await db.Ranks.Where(r => r.PlayerId == match.WinnerPlayerId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Division, 1).SetProperty(r => r.Rp, 99));
        }
        for (var i = 0; i < 4; i++)
        {
            var response = await battle.PostAsJsonAsync("/api/internal/battle/matches", match with { MatchId = Guid.NewGuid() });
            response.EnsureSuccessStatusCode();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var rank = await db.Ranks.Include(r => r.PromotionGames).SingleAsync(r => r.PlayerId == match.WinnerPlayerId);
            Assert.Equal("Platinum", rank.Tier); Assert.Equal(5, rank.Division); Assert.Equal(0, rank.Rp);
            Assert.Equal(3, rank.PromotionGames.Count); Assert.Null(rank.ActivePromotionSeriesId);
            var aborted = await battle.PostAsJsonAsync("/api/internal/battle/matches", match with
                { EndReason = "Aborted", WinnerPlayerId = null });
            aborted.EnsureSuccessStatusCode();
            Assert.Empty((await aborted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rankChanges").EnumerateArray());
            var after = await db.Ranks.AsNoTracking().SingleAsync(r => r.PlayerId == match.WinnerPlayerId);
            Assert.Equal(rank.Mmr, after.Mmr); Assert.Equal(rank.Rp, after.Rp);
        }
    }

    [Fact]
    public async Task DifferentMatchesWithSamePlayersSerializeRankUpdates()
    {
        using var battle = BattleClient(); var match = await Match(battle);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            battle.PostAsJsonAsync("/api/internal/battle/matches", match with { MatchId = Guid.NewGuid() })));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var winner = await db.Ranks.SingleAsync(r => r.PlayerId == match.WinnerPlayerId);
        Assert.Equal(1031, winner.Mmr); Assert.Equal(88, winner.Rp); Assert.Equal(2, winner.WinStreak);
    }
}
