import { Injectable } from '@nestjs/common';
import { PrismaService } from '../prisma/prisma.service.js';
import { CatalogService } from '../decks/catalog.service.js';
import type { CardEntry } from '../decks/catalog.service.js';
import { problem } from '../common/http.js';
import { RankCalculator } from '../ranking/rank-calculator.js';
import type { RankChange } from '../ranking/rank-calculator.js';
import { matchRequest, submissionHash } from './match-contract.js';

function storedCards(json: string): CardEntry[] {
  // Existing EF snapshots used PascalCase internal JSON; new snapshots use camelCase.
  return (JSON.parse(json) as Record<string, number>[]).map(c => ({ cardId: c.cardId ?? c.CardId, quantity: c.quantity ?? c.Quantity }));
}
function storedChanges(json: string): RankChange[] {
  return (JSON.parse(json) as Record<string, unknown>[]).map(c => Object.fromEntries(
    Object.entries(c).map(([key, value]) => [key[0].toLowerCase() + key.slice(1), value])) as unknown as RankChange);
}

@Injectable()
export class BattleService {
  constructor(private readonly db: PrismaService, private readonly catalog: CatalogService, private readonly calculator: RankCalculator) {}
  async validateDeck(playerId: string, deckId: string, catalogVersion: unknown) {
    const deck = await this.db.deck.findFirst({ where: { id: deckId, playerId }, include: { cards: true } });
    if (!deck) problem(404, 'deck_not_found');
    if (catalogVersion !== this.catalog.version || deck.catalogVersion !== this.catalog.version) problem(409, 'catalog_version_mismatch');
    const cards = deck.cards.sort((a, b) => a.cardId - b.cardId).map(c => ({ cardId: c.cardId, quantity: c.quantity }));
    try { this.catalog.validate({ name: deck.name, cards }); } catch { problem(422, 'invalid_deck'); }
    const snapshot = await this.db.battleDeckSnapshot.create({ data: { playerId, deckId, deckName: deck.name,
      catalogVersion: this.catalog.version, cardsJson: JSON.stringify(cards), createdAt: new Date() } });
    return { snapshotId: snapshot.id, playerId, deckId, catalogVersion: this.catalog.version, cards,
      cardIds: cards.flatMap(c => Array<number>(c.quantity).fill(c.cardId)) };
  }

  submit(body: unknown) {
    const r = matchRequest(body); const hash = submissionHash(r);
    return this.db.$transaction(async tx => {
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtextextended(${'match:' + r.matchId}, 0))`;
      const existing = await tx.match.findUnique({ where: { matchId: r.matchId } });
      if (existing) {
        if (existing.submissionHash !== hash) problem(409, 'match_result_conflict');
        return { matchId: r.matchId, duplicate: true, rankChanges: storedChanges(existing.rankChangesJson!) };
      }
      for (const p of r.participants)
        await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtextextended(${'player:' + p.playerId}, 0))`;
      const snapshots = await tx.battleDeckSnapshot.findMany({ where: { id: { in: r.participants.map(p => p.snapshotId) } } });
      if (r.participants.some(p => !snapshots.some(s => s.id === p.snapshotId && s.playerId === p.playerId && s.deckId === p.deckId)))
        problem(422, 'invalid_deck_snapshot');
      if (new Set(snapshots.map(s => s.catalogVersion)).size !== 1) problem(409, 'catalog_version_mismatch');
      await tx.match.create({ data: { matchId: r.matchId, endedAt: r.endedAt, endReason: r.endReason,
        submissionHash: hash, participants: { create: r.participants.map(p => {
          const s = snapshots.find(s => s.id === p.snapshotId)!;
          return { playerId: p.playerId, deckId: p.deckId, deckName: s.deckName, catalogVersion: s.catalogVersion,
            cards: { create: storedCards(s.cardsJson) } };
        }) } } });
      const changes: RankChange[] = [];
      if (r.endReason !== 'Aborted') {
        const ranks = await tx.rank.findMany({ where: { playerId: { in: r.participants.map(p => p.playerId) } }, include: { promotionGames: true } });
        if (ranks.length !== 2) throw new Error('Missing rank profile.');
        const mmrs = new Map(ranks.map(rank => [rank.playerId, rank.mmr]));
        for (const rank of ranks) {
          const oldGames = rank.promotionGames.length;
          changes.push(this.calculator.apply(rank, [...mmrs].find(([id]) => id !== rank.playerId)![1],
            r.winnerPlayerId === null ? .5 : r.winnerPlayerId === rank.playerId ? 1 : 0));
          const { playerId, promotionGames, ...data } = rank;
          await tx.rank.update({ where: { playerId }, data });
          for (const game of promotionGames.slice(oldGames)) await tx.promotionGame.create({ data: game });
        }
      }
      // Winner references an already-inserted participant; all writes commit atomically.
      await tx.match.update({ where: { matchId: r.matchId }, data: {
        winnerPlayerId: r.winnerPlayerId, rankChangesJson: JSON.stringify(changes),
      } });
      return { matchId: r.matchId, duplicate: false, rankChanges: changes };
    }, { isolationLevel: 'ReadCommitted', maxWait: 10000, timeout: 20000 });
  }
}
