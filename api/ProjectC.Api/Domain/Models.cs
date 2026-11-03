namespace ProjectC.Api.Domain;

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "Guest";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public RankProfile Rank { get; set; } = new();
}

public sealed class Deck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<DeckCard> Cards { get; set; } = [];
}

public sealed class DeckCard
{
    public Guid DeckId { get; set; }
    public int CardId { get; set; }
    public int Quantity { get; set; }
}

public sealed class RankProfile
{
    public Guid? ActivePromotionSeriesId { get; set; }
    public Guid PlayerId { get; set; }
    public int Mmr { get; set; } = 1000;
    public string Tier { get; set; } = "Bronze";
    public int Division { get; set; } = 5;
    public int Rp { get; set; }
    public int WinStreak { get; set; }
    public string PromotionStatus { get; set; } = "None";
    public List<PromotionGame> PromotionGames { get; set; } = [];
}

// SeriesId retains previous promotion series instead of overwriting their records.
public sealed class PromotionGame
{
    public Guid PlayerId { get; set; }
    public Guid SeriesId { get; set; }
    public int GameNumber { get; set; }
    public bool Won { get; set; }
}

public sealed class MatchResult
{
    public string? SubmissionHash { get; set; }
    public string? RankChangesJson { get; set; }
    // Battle Server supplies this stable ID. The primary key makes retries idempotent.
    public Guid MatchId { get; set; }
    public Guid? WinnerPlayerId { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public string EndReason { get; set; } = "";
    public List<MatchParticipant> Participants { get; set; } = [];
}

public sealed class RefreshSession
{
    public string TokenHash { get; set; } = "";
    public Guid PlayerId { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

// Immutable server-issued deck selection. No FK to a mutable deck.
public sealed class BattleDeckSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }
    public Guid DeckId { get; set; }
    public string DeckName { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public string CardsJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MatchParticipant
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    // Historical identifier, deliberately not a foreign key to the mutable/deletable Deck.
    public Guid DeckId { get; set; }
    public string DeckName { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public List<MatchDeckCard> Cards { get; set; } = [];
}

public sealed class MatchDeckCard
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public int CardId { get; set; }
    public int Quantity { get; set; }
}
