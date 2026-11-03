import { randomUUID } from 'node:crypto';
import { RankCalculator, roundAway } from './rank-calculator.js';
import type { RankState } from './rank-calculator.js';

const calculator = new RankCalculator();
const rank = (tier = 'Gold', division = 3, rp = 50): RankState => ({ playerId: randomUUID(), mmr: 1000, tier, division, rp,
  winStreak: 0, promotionStatus: 'None', activePromotionSeriesId: null, promotionGames: [] });

describe('RankCalculator: C# parity', () => {
  it('EqualOpponentsHaveSymmetricRewardsAndNoStreakInflation', () => {
    const win = calculator.apply(rank(), 1000, 1), loss = calculator.apply(rank(), 1000, 0);
    expect([win.mmrAfter, loss.mmrAfter, win.rpDelta, loss.rpDelta]).toEqual([1016, 984, 20, -20]);
    const streak = rank(); streak.winStreak = 100;
    expect(calculator.apply(streak, 1000, 1).rpDelta).toBe(20);
    const balanced = rank();
    for (let i = 0; i < 1000; i++) calculator.apply(balanced, balanced.mmr, i % 2 === 0 ? 1 : 0);
    expect([balanced.rp, balanced.mmr]).toEqual([50, 1000]);
  });
  it.each([['Bronze', 5], ['Bronze', 1], ['Silver', 5], ['Silver', 1]] as const)('LowerTiersNeverLoseRp %s %i', (tier, division) => {
    const r = rank(tier, division); const result = calculator.apply(r, 1000, 0);
    expect([r.rp, result.rpDelta, r.mmr]).toEqual([50, 0, 984]);
  });
  it('MmrDifferenceAffectsBothMmrAndRp', () => {
    expect(calculator.apply(rank(), 1400, 1).rpDelta).toBeGreaterThan(calculator.apply(rank(), 600, 1).rpDelta);
    expect(calculator.apply(rank(), 600, 0).rpDelta).toBeLessThan(calculator.apply(rank(), 1400, 0).rpDelta);
    expect(RankCalculator.expected(1000, 1000)).toBe(.5);
    expect(RankCalculator.expected(1400, 1000) + RankCalculator.expected(1000, 1400)).toBeCloseTo(1, 10);
  });
  it('DivisionPromotionDemotionAndTierDemotionCarryRp', () => {
    let r = rank('Gold', 3, 95); RankCalculator.applyRp(r, 20); expect([r.division, r.rp]).toEqual([2, 15]);
    r = rank('Gold', 3, 3); RankCalculator.applyRp(r, -5); expect([r.division, r.rp]).toEqual([4, 98]);
    r = rank('Gold', 5, 3); RankCalculator.applyRp(r, -5); expect([r.tier, r.division, r.rp]).toEqual(['Silver', 1, 98]);
  });
  it.each([['Bronze', 'Silver'], ['Silver', 'Gold'], ['Gold', 'Platinum']])('TierBoundaryStartsBestOfFiveAndThreeWinsPromote %s', (tier, target) => {
    const r = rank(tier, 1, 90); calculator.apply(r, 1000, 1);
    expect([r.promotionStatus, r.rp]).toEqual(['InProgress', 99]); const series = r.activePromotionSeriesId;
    for (const outcome of [1, 0, 1, 0, 1]) calculator.apply(r, r.mmr, outcome);
    expect([r.tier, r.division, r.rp, r.promotionStatus, r.activePromotionSeriesId]).toEqual([target, 5, 0, 'Succeeded', null]);
    expect(r.promotionGames.filter(g => g.seriesId === series)).toHaveLength(5);
  });
  it('FailureReturnsToEightyAndPreservesSeriesHistory', () => {
    const r = rank('Gold', 1, 90); calculator.apply(r, 1000, 1);
    calculator.apply(r, r.mmr, .5); expect(r.promotionGames).toHaveLength(0);
    for (let i = 0; i < 3; i++) calculator.apply(r, r.mmr, 0);
    expect([r.promotionStatus, r.tier, r.division, r.rp]).toEqual(['Failed', 'Gold', 1, 80]);
    calculator.apply(r, r.mmr, 1);
    expect(r.promotionStatus).toBe('InProgress'); expect(r.promotionGames).toHaveLength(3);
    expect(r.promotionGames.some(g => g.seriesId === r.activePromotionSeriesId)).toBe(false);
  });
  it('TopAndBottomAreCappedAndLegacyUnrankedInitializes', () => {
    const top = rank('Platinum', 1, 99); calculator.apply(top, 1000, 1);
    expect(top.rp).toBe(99); expect(top.activePromotionSeriesId).toBeNull();
    const bottom = rank('Bronze', 5, 0); RankCalculator.applyRp(bottom, -20); expect(bottom.rp).toBe(0);
    const legacy = rank('Unranked', 0, 0); calculator.apply(legacy, 1000, 1);
    expect([legacy.tier, legacy.division, legacy.rp]).toEqual(['Bronze', 5, 20]);
    const draw = rank(); calculator.apply(draw, 1000, .5); expect([draw.rp, draw.mmr]).toEqual([50, 1000]);
  });
  it('rounds negative half values away from zero and rejects invalid input', () => {
    expect([roundAway(-2.5), roundAway(2.5)]).toEqual([-3, 3]);
    expect(() => calculator.apply(rank(), 1000, 2)).toThrow(RangeError);
    expect(() => calculator.apply(rank('Unknown'), 1000, 1)).toThrow('Invalid stored rank');
  });
});
