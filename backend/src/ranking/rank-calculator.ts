import { randomUUID } from 'node:crypto';
import type { PromotionGame, Rank } from '../generated/prisma/client.js';

export type RankState = Rank & { promotionGames: PromotionGame[] };
export interface RankChange {
  playerId: string; mmrBefore: number; mmrAfter: number; tier: string; division: number;
  rp: number; rpDelta: number; promotionStatus: string;
}
const tiers = ['Bronze', 'Silver', 'Gold', 'Platinum'];
// JS Math.round(-x.5) rounds toward +infinity; C# uses midpoint away from zero.
export const roundAway = (value: number) => value < 0 ? -Math.round(-value) : Math.round(value);
export class RankCalculator {
  static expected(mmr: number, opponentMmr: number) { return 1 / (1 + 10 ** ((opponentMmr - mmr) / 400)); }
  apply(rank: RankState, opponentMmr: number, outcome: number): RankChange {
    if (![0, .5, 1].includes(outcome)) throw new RangeError('Invalid outcome.');
    const before = rank.mmr;
    const expected = RankCalculator.expected(before, opponentMmr);
    rank.mmr += roundAway(32 * (outcome - expected));
    if (rank.tier === 'Unranked') { rank.tier = 'Bronze'; rank.division = 5; rank.rp = 0; }
    const tier = tiers.indexOf(rank.tier);
    if (tier < 0 || rank.division < 1 || rank.division > 5 || rank.rp < 0 || rank.rp > 99)
      throw new Error('Invalid stored rank.');
    rank.winStreak = outcome === 1 ? rank.winStreak + 1 : 0;
    let delta = outcome === .5 ? 0 : roundAway(40 * (outcome - expected));
    if (outcome === 0 && tier <= 1) delta = 0;
    if (outcome !== .5 && rank.activePromotionSeriesId) {
      const games = rank.promotionGames.filter(g => g.seriesId === rank.activePromotionSeriesId);
      const game = { playerId: rank.playerId, seriesId: rank.activePromotionSeriesId, gameNumber: games.length + 1, won: outcome === 1 };
      rank.promotionGames.push(game); games.push(game);
      if (games.filter(g => g.won).length === 3 || games.filter(g => !g.won).length === 3) {
        const passed = games.filter(g => g.won).length === 3;
        rank.activePromotionSeriesId = null;
        rank.promotionStatus = passed ? 'Succeeded' : 'Failed';
        if (passed) { rank.tier = tiers[tier + 1]; rank.division = 5; rank.rp = 0; }
        else rank.rp = 80;
      }
      delta = 0;
    } else if (!rank.activePromotionSeriesId) RankCalculator.applyRp(rank, delta);
    return { playerId: rank.playerId, mmrBefore: before, mmrAfter: rank.mmr, tier: rank.tier, division: rank.division,
      rp: rank.rp, rpDelta: delta, promotionStatus: rank.promotionStatus };
  }
  static applyRp(rank: RankState, delta: number) {
    let tier = tiers.indexOf(rank.tier);
    let rp = rank.rp + delta;
    while (rp >= 100) {
      if (rank.division > 1) { rank.division--; rp -= 100; }
      else if (tier < tiers.length - 1) {
        rank.activePromotionSeriesId = randomUUID(); rank.promotionStatus = 'InProgress'; rp = 99; break;
      } else { rp = 99; break; }
    }
    while (rp < 0) {
      if (rank.division < 5) { rank.division++; rp += 100; }
      else if (tier > 0) { tier--; rank.tier = tiers[tier]; rank.division = 1; rp += 100; }
      else { rp = 0; break; }
    }
    rank.rp = rp;
  }
}
