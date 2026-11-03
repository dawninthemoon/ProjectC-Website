using ProjectC.Api.Domain;

namespace ProjectC.Api.Ranking;

public sealed record RankChange(Guid PlayerId, int MmrBefore, int MmrAfter, string Tier, int Division,
    int Rp, int RpDelta, string PromotionStatus);

public sealed class RankCalculator
{
    private static readonly string[] Tiers = ["Bronze", "Silver", "Gold", "Platinum"];
    public static double Expected(int mmr, int opponentMmr) => 1 / (1 + Math.Pow(10, ((double)opponentMmr - mmr) / 400));

    // outcome: 1 win, 0 loss, .5 draw. Opponent MMR must be captured before either update.
    public RankChange Apply(RankProfile rank, int opponentMmr, double outcome)
    {
        if (outcome != 0 && outcome != .5 && outcome != 1) throw new ArgumentOutOfRangeException(nameof(outcome));
        var before = rank.Mmr;
        var expected = Expected(before, opponentMmr);
        rank.Mmr += (int)Math.Round(32 * (outcome - expected), MidpointRounding.AwayFromZero);
        if (rank.Tier == "Unranked") { rank.Tier = "Bronze"; rank.Division = 5; rank.Rp = 0; }
        var tier = Array.IndexOf(Tiers, rank.Tier);
        if (tier < 0 || rank.Division is < 1 or > 5 || rank.Rp is < 0 or > 99)
            throw new InvalidOperationException("Invalid stored rank.");
        rank.WinStreak = outcome == 1 ? rank.WinStreak + 1 : 0;
        var delta = outcome == .5 ? 0 : (int)Math.Round(40 * (outcome - expected), MidpointRounding.AwayFromZero);
        if (outcome == 0 && tier <= 1) delta = 0;
        if (outcome != .5 && rank.ActivePromotionSeriesId is Guid series)
        {
            var games = rank.PromotionGames.Where(g => g.SeriesId == series).ToList();
            var game = new PromotionGame { PlayerId = rank.PlayerId, SeriesId = series,
                GameNumber = games.Count + 1, Won = outcome == 1 };
            rank.PromotionGames.Add(game);
            games.Add(game);
            if (games.Count(g => g.Won) == 3 || games.Count(g => !g.Won) == 3)
            {
                var passed = games.Count(g => g.Won) == 3;
                rank.ActivePromotionSeriesId = null;
                rank.PromotionStatus = passed ? "Succeeded" : "Failed";
                if (passed) { rank.Tier = Tiers[tier + 1]; rank.Division = 5; rank.Rp = 0; }
                else rank.Rp = 80;
            }
            delta = 0; // Series games change MMR and series score, never RP.
        }
        else if (rank.ActivePromotionSeriesId is null)
            ApplyRp(rank, delta);
        return new(rank.PlayerId, before, rank.Mmr, rank.Tier, rank.Division, rank.Rp, delta, rank.PromotionStatus);
    }

    public static void ApplyRp(RankProfile rank, int delta)
    {
        var tier = Array.IndexOf(Tiers, rank.Tier);
        var rp = rank.Rp + delta;
        while (rp >= 100)
        {
            if (rank.Division > 1) { rank.Division--; rp -= 100; }
            else if (tier < Tiers.Length - 1)
            {
                rank.ActivePromotionSeriesId = Guid.NewGuid(); rank.PromotionStatus = "InProgress";
                rp = 99; break;
            }
            else { rp = 99; break; }
        }
        while (rp < 0)
        {
            if (rank.Division < 5) { rank.Division++; rp += 100; }
            else if (tier > 0) { tier--; rank.Tier = Tiers[tier]; rank.Division = 1; rp += 100; }
            else { rp = 0; break; }
        }
        rank.Rp = rp;
    }
}
