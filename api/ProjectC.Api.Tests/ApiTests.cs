using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ProjectC.Api.Auth;
using ProjectC.Api.Data;
using ProjectC.Api.Decks;
using ProjectC.Api.Domain;
using ProjectC.Api.Endpoints;
using Xunit;

namespace ProjectC.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Test-only key, deliberately separate from runtime configuration.
    public const string TestKey = "projectc-integration-tests-only-signing-key-123456789";
    private readonly string connection = Environment.GetEnvironmentVariable("PROJECTC_TEST_DATABASE")
        ?? throw new InvalidOperationException("Set PROJECTC_TEST_DATABASE to a dedicated PostgreSQL test database. Tests reset its game schema.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:GameDatabase", connection);
        builder.UseSetting("Jwt:SigningKey", TestKey);
        builder.UseSetting("Battle:ApiKey", "battle-integration-tests-only-key-1234567890");
        builder.UseSetting("RateLimits:GuestPermitLimit", "1000");
    }
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS game CASCADE");
        await db.Database.MigrateAsync();
    }
    public new Task DisposeAsync() => base.DisposeAsync().AsTask();
}

public sealed partial class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, TokenResponse Token)> Guest()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/guest", new { displayName = "테스터" });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return (client, token);
    }
    private SaveDeckRequest ValidDeck() => new("백귀야행", new[] { 1001, 1002, 1003, 1004, 1005, 1006, 1008, 1101, 2001, 2002 }
        .Select(id => (CardEntry?)new CardEntry(id, 3)).ToList());

    [Fact]
    public async Task GuestIdentityAndRankArePersistedAndProtected()
    {
        var (client, token) = await Guest();
        Assert.NotEqual(Guid.Empty, token.User.PlayerId);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.NotNull(token.RefreshToken);
        Assert.True(token.RefreshExpiresAt > token.ExpiresAt);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/users/me");
        Assert.Equal(token.User.PlayerId, me!.PlayerId);
        Assert.Equal(token.User.DisplayName, me.DisplayName);
        Assert.True((token.User.CreatedAt - me.CreatedAt).Duration() < TimeSpan.FromMilliseconds(1));
        var rank = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/rank/me");
        Assert.Equal(1000, rank.GetProperty("mmr").GetInt32());
        Assert.Equal("None", rank.GetProperty("promotionStatus").GetString());
        using var anonymous = factory.CreateClient();
        foreach (var url in new[] { "/api/users/me", "/api/decks", "/api/rank/me" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.PostAsJsonAsync("/api/auth/guest", new { displayName = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.PostAsJsonAsync("/api/auth/guest", new { playerId = token.User.PlayerId })).StatusCode);
    }

    [Fact]
    public async Task DeckCrudIsScopedToTokenOwner()
    {
        var (owner, _) = await Guest();
        var (other, _) = await Guest();
        var request = ValidDeck();
        var create = await owner.PostAsJsonAsync("/api/decks", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var deck = (await create.Content.ReadFromJsonAsync<DeckResponse>())!;
        Assert.Contains((await owner.GetFromJsonAsync<List<DeckResponse>>("/api/decks"))!, d => d.Id == deck.Id);
        Assert.DoesNotContain((await other.GetFromJsonAsync<List<DeckResponse>>("/api/decks"))!, d => d.Id == deck.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/decks/{deck.Id}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/decks/{deck.Id}")).StatusCode);
        // Exercise removing/adding composite keys as well as changing existing quantities.
        request.Cards![0] = new CardEntry(2003, 3);
        var update = await owner.PutAsJsonAsync($"/api/decks/{deck.Id}", request with { Name = "수정 덱" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<DeckResponse>())!;
        Assert.Equal("수정 덱", updated.Name);
        Assert.Contains(updated.Cards, c => c.CardId == 2003);
        Assert.DoesNotContain(updated.Cards, c => c.CardId == 1001);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/decks/{deck.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/decks/{deck.Id}")).StatusCode);
    }

    [Fact]
    public async Task InvalidDecksAndPlayerIdInjectionAreRejectedWithoutMutation()
    {
        var (client, _) = await Guest();
        var invalid = new List<SaveDeckRequest> {
            new("", ValidDeck().Cards), new("덱", null), new("덱", []), new("덱", [null]),
            new("덱", [new(1007, 3)]), new("덱", [new(999999, 30)]), new("덱", [new(1001, int.MaxValue), new(1002, int.MaxValue)]),
            new("덱", [new(1001, -1)]), new("덱", [new(1001, 3), new(1001, 3)]) };
        using var scope = factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICardCatalog>();
        Assert.False(catalog.IsMainDeckCard(999999));
        foreach (var request in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/decks", request)).StatusCode);
        var valid = ValidDeck();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/decks",
            new { valid.Name, valid.Cards, playerId = Guid.NewGuid() })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<DeckResponse>>("/api/decks"))!);
    }

    [Fact]
    public async Task InvalidTokensAreRejected()
    {
        var (client, guest) = await Guest();
        foreach (var reason in new[] { "expired", "signature", "issuer", "audience", "unknown-player" })
        {
        var jwt = new JwtSecurityToken(
            reason == "issuer" ? "wrong" : "ProjectC.Website",
            reason == "audience" ? "wrong" : "ProjectC.Client",
            [new Claim("sub", (reason == "unknown-player" ? Guid.NewGuid() : guest.User.PlayerId).ToString())],
            DateTime.UtcNow.AddHours(-2), reason == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                reason == "signature" ? new string('x', 64) : ApiFactory.TestKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        using var battle = BattleClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await battle.PostAsJsonAsync("/api/internal/battle/players/verify",
            new { accessToken = new JwtSecurityTokenHandler().WriteToken(jwt) })).StatusCode);
        }
    }

    [Fact]
    public async Task MatchIdIsUniqueAndDeckSnapshotSurvivesDeckDeletion()
    {
        var (client, token) = await Guest();
        var (_, opponent) = await Guest();
        var create = await client.PostAsJsonAsync("/api/decks", ValidDeck());
        var deck = (await create.Content.ReadFromJsonAsync<DeckResponse>())!;
        var matchId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var match = new MatchResult { MatchId = matchId, EndedAt = DateTimeOffset.UtcNow, EndReason = "Surrender",
                Participants = new[] { token.User.PlayerId, opponent.User.PlayerId }.Select(id => new MatchParticipant
                {
                    PlayerId = id, DeckId = deck.Id, DeckName = deck.Name, CatalogVersion = deck.CatalogVersion,
                    Cards = deck.Cards.Select(c => new MatchDeckCard { CardId = c.CardId, Quantity = c.Quantity }).ToList()
                }).ToList() };
            db.Matches.Add(match);
            await db.SaveChangesAsync();
            // Winner FK refers to a participant: insert both in the same transaction in a future writer.
            match.WinnerPlayerId = token.User.PlayerId;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/decks/{deck.Id}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            var match = await db.Matches.AsNoTracking().Include(m => m.Participants).ThenInclude(p => p.Cards)
                .SingleAsync(m => m.MatchId == matchId);
            Assert.Equal(2, match.Participants.Count);
            Assert.All(match.Participants, p => Assert.Equal(30, p.Cards.Sum(c => c.Quantity)));
            db.Matches.Add(new MatchResult { MatchId = matchId, EndedAt = DateTimeOffset.UtcNow, EndReason = "Retry" });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
        }
    }

    [Fact]
    public async Task MigrationModelHasNoPendingChanges()
    {
        using var scope = factory.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<GameDbContext>().Database.HasPendingModelChanges());
    }
}
