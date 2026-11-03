using ProjectC.Api.Domain;
using ProjectC.Api.Ranking;
using Xunit;

namespace ProjectC.Api.Tests;

public sealed class RankTests
{
    private readonly RankCalculator calculator = new();
    private static RankProfile Rank(string tier = "Gold", int division = 3, int rp = 50) =>
        new() { PlayerId = Guid.NewGuid(), Tier = tier, Division = division, Rp = rp };

    [Fact]
    public void EqualOpponentsHaveSymmetricRewardsAndNoStreakInflation()
    {
        var win = Rank(); var loss = Rank();
        var a = calculator.Apply(win, 1000, 1); var b = calculator.Apply(loss, 1000, 0);
        Assert.Equal(1016, a.MmrAfter); Assert.Equal(984, b.MmrAfter);
        Assert.Equal(20, a.RpDelta); Assert.Equal(-20, b.RpDelta);
        var streak = Rank(); streak.WinStreak = 100;
        Assert.Equal(20, calculator.Apply(streak, 1000, 1).RpDelta);
        // Fixed equally skilled opponents; alternate wins and losses, each at equal pre-game MMR.
        var balanced = Rank(rp: 50);
        for (var i = 0; i < 1000; i++) calculator.Apply(balanced, balanced.Mmr, i % 2 == 0 ? 1 : 0);
        Assert.Equal(50, balanced.Rp); Assert.Equal(1000, balanced.Mmr);
    }

    [Theory]
    [InlineData("Bronze", 5)] [InlineData("Bronze", 1)] [InlineData("Silver", 5)] [InlineData("Silver", 1)]
    public void LowerTiersNeverLoseRp(string tier, int division)
    {
        var rank = Rank(tier, division);
        var result = calculator.Apply(rank, 1000, 0);
        Assert.Equal(50, rank.Rp); Assert.Equal(0, result.RpDelta); Assert.Equal(984, rank.Mmr);
    }

    [Fact]
    public void MmrDifferenceAffectsBothMmrAndRp()
    {
        Assert.True(calculator.Apply(Rank(), 1400, 1).RpDelta > calculator.Apply(Rank(), 600, 1).RpDelta);
        Assert.True(calculator.Apply(Rank(), 600, 0).RpDelta < calculator.Apply(Rank(), 1400, 0).RpDelta);
        Assert.Equal(.5, RankCalculator.Expected(1000, 1000));
        Assert.Equal(1, RankCalculator.Expected(1400, 1000) + RankCalculator.Expected(1000, 1400), 10);
    }

    [Fact]
    public void DivisionPromotionDemotionAndTierDemotionCarryRp()
    {
        var rank = Rank(rp: 95); RankCalculator.ApplyRp(rank, 20);
        Assert.Equal(2, rank.Division); Assert.Equal(15, rank.Rp);
        rank = Rank(rp: 3); RankCalculator.ApplyRp(rank, -5);
        Assert.Equal(4, rank.Division); Assert.Equal(98, rank.Rp);
        rank = Rank(division: 5, rp: 3); RankCalculator.ApplyRp(rank, -5);
        Assert.Equal("Silver", rank.Tier); Assert.Equal(1, rank.Division); Assert.Equal(98, rank.Rp);
    }

    [Theory]
    [InlineData("Bronze", "Silver")] [InlineData("Silver", "Gold")] [InlineData("Gold", "Platinum")]
    public void TierBoundaryStartsBestOfFiveAndThreeWinsPromote(string tier, string target)
    {
        var rank = Rank(tier, 1, 90); calculator.Apply(rank, 1000, 1);
        Assert.Equal("InProgress", rank.PromotionStatus); Assert.Equal(99, rank.Rp);
        var series = rank.ActivePromotionSeriesId;
        foreach (var outcome in new[] { 1d, 0, 1, 0, 1 }) calculator.Apply(rank, rank.Mmr, outcome);
        Assert.Equal(target, rank.Tier); Assert.Equal(5, rank.Division); Assert.Equal(0, rank.Rp);
        Assert.Equal("Succeeded", rank.PromotionStatus); Assert.Null(rank.ActivePromotionSeriesId);
        Assert.Equal(5, rank.PromotionGames.Count(g => g.SeriesId == series));
    }

    [Fact]
    public void FailureReturnsToEightyAndPreservesSeriesHistory()
    {
        var rank = Rank("Gold", 1, 90); calculator.Apply(rank, 1000, 1);
        calculator.Apply(rank, rank.Mmr, .5); Assert.Empty(rank.PromotionGames);
        for (var i = 0; i < 3; i++) calculator.Apply(rank, rank.Mmr, 0);
        Assert.Equal("Failed", rank.PromotionStatus); Assert.Equal("Gold", rank.Tier);
        Assert.Equal(1, rank.Division); Assert.Equal(80, rank.Rp);
        calculator.Apply(rank, rank.Mmr, 1);
        Assert.Equal("InProgress", rank.PromotionStatus); Assert.Equal(3, rank.PromotionGames.Count);
        Assert.DoesNotContain(rank.PromotionGames, g => g.SeriesId == rank.ActivePromotionSeriesId);
    }

    [Fact]
    public void TopAndBottomAreCappedAndLegacyUnrankedInitializes()
    {
        var top = Rank("Platinum", 1, 99); calculator.Apply(top, 1000, 1);
        Assert.Equal(99, top.Rp); Assert.Null(top.ActivePromotionSeriesId);
        var bottom = Rank("Bronze", 5, 0); RankCalculator.ApplyRp(bottom, -20); Assert.Equal(0, bottom.Rp);
        var legacy = new RankProfile { Tier = "Unranked", Division = 0 }; calculator.Apply(legacy, 1000, 1);
        Assert.Equal("Bronze", legacy.Tier); Assert.Equal(5, legacy.Division); Assert.Equal(20, legacy.Rp);
        var draw = Rank(); calculator.Apply(draw, 1000, .5);
        Assert.Equal(50, draw.Rp); Assert.Equal(1000, draw.Mmr);
    }
}
