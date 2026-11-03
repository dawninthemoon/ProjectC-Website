using Microsoft.EntityFrameworkCore;
using ProjectC.Api.Domain;

namespace ProjectC.Api.Data;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<RankProfile> Ranks => Set<RankProfile>();
    public DbSet<MatchResult> Matches => Set<MatchResult>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<BattleDeckSnapshot> BattleDeckSnapshots => Set<BattleDeckSnapshot>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Own schema avoids collisions with the existing Prisma analytics tables.
        model.HasDefaultSchema("game");
        model.Entity<RefreshSession>(e =>
        {
            e.HasKey(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BattleDeckSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DeckName).HasMaxLength(64);
            e.Property(x => x.CatalogVersion).HasMaxLength(128);
            e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Player>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(32);
            e.HasOne(x => x.Rank).WithOne().HasForeignKey<RankProfile>(x => x.PlayerId);
        });
        model.Entity<Deck>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.CatalogVersion).HasMaxLength(128);
            e.HasIndex(x => x.PlayerId);
            e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Cards).WithOne().HasForeignKey(x => x.DeckId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<DeckCard>(e =>
        {
            e.HasKey(x => new { x.DeckId, x.CardId });
            e.ToTable(t => t.HasCheckConstraint("CK_DeckCard_Quantity", "\"Quantity\" > 0"));
        });
        model.Entity<RankProfile>(e =>
        {
            e.HasKey(x => x.PlayerId);
            e.Property(x => x.Tier).HasMaxLength(32);
            e.Property(x => x.PromotionStatus).HasMaxLength(32);
            e.HasMany(x => x.PromotionGames).WithOne().HasForeignKey(x => x.PlayerId);
            e.ToTable(t => t.HasCheckConstraint("CK_Rank_Nonnegative",
                "\"Division\" >= 0 AND \"Rp\" >= 0 AND \"WinStreak\" >= 0"));
        });
        model.Entity<PromotionGame>(e =>
        {
            e.HasKey(x => new { x.PlayerId, x.SeriesId, x.GameNumber });
            e.ToTable(t => t.HasCheckConstraint("CK_PromotionGame_Number", "\"GameNumber\" > 0"));
        });
        model.Entity<MatchResult>(e =>
        {
            e.HasKey(x => x.MatchId);
            e.Property(x => x.MatchId).ValueGeneratedNever();
            e.Property(x => x.EndReason).HasMaxLength(64);
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.MatchId);
            // A non-null winner must be one of this match's participants.
            e.HasOne<MatchParticipant>().WithMany()
                .HasForeignKey(x => new { x.MatchId, x.WinnerPlayerId })
                .HasPrincipalKey(x => new { x.MatchId, x.PlayerId })
                .OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<MatchParticipant>(e =>
        {
            e.HasKey(x => new { x.MatchId, x.PlayerId });
            e.Property(x => x.DeckName).HasMaxLength(64);
            e.Property(x => x.CatalogVersion).HasMaxLength(128);
            e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Cards).WithOne().HasForeignKey(x => new { x.MatchId, x.PlayerId });
        });
        model.Entity<MatchDeckCard>(e =>
        {
            e.HasKey(x => new { x.MatchId, x.PlayerId, x.CardId });
            e.ToTable(t => t.HasCheckConstraint("CK_MatchDeckCard_Quantity", "\"Quantity\" > 0"));
        });
    }
}
