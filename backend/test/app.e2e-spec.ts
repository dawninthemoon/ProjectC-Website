import { Test } from '@nestjs/testing';
import type { INestApplication } from '@nestjs/common';
import request from 'supertest';
import jwt from 'jsonwebtoken';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { AppModule } from '../src/app.module.js';
import { ApiExceptionFilter } from '../src/common/http.js';
import { PrismaService } from '../src/prisma/prisma.service.js';
import { AuthService, tokenHash } from '../src/auth/auth.service.js';
import { CatalogService } from '../src/decks/catalog.service.js';

const key = 'nest-tests-only-jwt-signing-key-1234567890';
const battleKey = 'nest-tests-only-battle-api-key-1234567890';
const cards = () => [1001, 1002, 1003, 1004, 1005, 1006, 1008, 1101, 2001, 2002].map(cardId => ({ cardId, quantity: 3 }));
const validDeck = () => ({ name: '백귀야행', cards: cards() });
interface Participant { playerId: string; deckId: string; snapshotId: string }
interface MatchBody { matchId: string; participants: Participant[]; winnerPlayerId: string | null; endReason: string; endedAt: string }

describe('PostgreSQL 17 API integration: C# parity', () => {
  let app: INestApplication, db: PrismaService, auth: AuthService, catalog: CatalogService;
  const api = () => request(app.getHttpServer());
  const battle = () => api().post('/api/internal/battle/matches').set('X-Battle-Api-Key', battleKey);
  const seed = () => auth.guest('Test guest');
  const deck = async (token: string) => (await api().post('/api/decks').auth(token, { type: 'bearer' }).send(validDeck()).expect(201)).body;
  const snapshot = async (playerId: string, deckId: string) => {
    const response = await api().post(`/api/internal/battle/players/${playerId}/decks/${deckId}/validate`)
      .set('X-Battle-Api-Key', battleKey).send({ catalogVersion: catalog.version }).expect(200);
    expect(response.body.cardIds).toHaveLength(30); return response.body.snapshotId as string;
  };
  const match = async (): Promise<MatchBody> => {
    const a = await seed(), b = await seed();
    const da = await deck(a.accessToken), dbb = await deck(b.accessToken);
    for (const token of [a, b]) await db.rank.update({ where: { playerId: token.user.playerId }, data: { tier: 'Gold', division: 3, rp: 50 } });
    return { matchId: randomUUID(), participants: [
      { playerId: a.user.playerId, deckId: da.id, snapshotId: await snapshot(a.user.playerId, da.id) },
      { playerId: b.user.playerId, deckId: dbb.id, snapshotId: await snapshot(b.user.playerId, dbb.id) }],
      winnerPlayerId: a.user.playerId, endReason: 'Normal', endedAt: new Date().toISOString() };
  };
  beforeAll(async () => {
    const connection = process.env.PROJECTC_TEST_DATABASE;
    if (!connection) throw new Error('PROJECTC_TEST_DATABASE must be a dedicated local PostgreSQL URL. No schemas are reset.');
    const target = new URL(connection);
    if (!['localhost', '127.0.0.1', 'postgres'].includes(target.hostname) || !target.pathname.includes('test'))
      throw new Error('Integration tests require a local database named with test; production is refused.');
    process.env.DATABASE_URL = connection; process.env.DIRECT_URL = connection;
    process.env.JWT_SIGNING_KEY = key; process.env.BATTLE_API_KEY = battleKey; process.env.GUEST_PERMIT_LIMIT = '1000';
    execFileSync(process.execPath, ['node_modules/prisma/build/index.js', 'migrate', 'deploy', '--config', 'prisma.config.ts'], { stdio: 'pipe', env: { ...process.env } });
    const module = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = module.createNestApplication(); app.useGlobalFilters(new ApiExceptionFilter()); await app.init();
    db = app.get(PrismaService); auth = app.get(AuthService); catalog = app.get(CatalogService);
  });
  afterAll(async () => { if (app) await app.close(); });

  it('GuestIdentityAndRankArePersistedAndProtected', async () => {
    const { body: token } = await api().post('/api/auth/guest').send({ displayName: ' 테스터 ' }).expect(200);
    expect(token.user.displayName).toBe('테스터'); expect(token.refreshToken).toHaveLength(64);
    expect(Date.parse(token.refreshExpiresAt)).toBeGreaterThan(Date.parse(token.expiresAt));
    expect((await api().get('/api/users/me').auth(token.accessToken, { type: 'bearer' }).expect(200)).body).toEqual(token.user);
    const rank = (await api().get('/api/rank/me').auth(token.accessToken, { type: 'bearer' }).expect(200)).body;
    expect([rank.mmr, rank.tier, rank.division, rank.rp, rank.promotionStatus]).toEqual([1000, 'Bronze', 5, 0, 'None']);
    for (const path of ['/api/users/me', '/api/rank/me', '/api/decks']) await api().get(path).expect(401);
    await api().post('/api/auth/guest').send({ displayName: ' ' }).expect(400);
    await api().post('/api/auth/guest').send({ playerId: randomUUID() }).expect(400);
    expect((await api().post('/api/auth/guest').send({}).expect(200)).body.user.displayName).toBe('Guest');
    await api().get('/health').expect(200, { status: 'ok' });
  });
  it('DeckCrudIsScopedToTokenOwner', async () => {
    const a = await seed(), b = await seed(), d = await deck(a.accessToken);
    expect((await api().get('/api/decks').auth(a.accessToken, { type: 'bearer' })).body.map((v: { id: string }) => v.id)).toContain(d.id);
    expect((await api().get('/api/decks').auth(b.accessToken, { type: 'bearer' })).body).toEqual([]);
    await api().put(`/api/decks/${d.id}`).auth(b.accessToken, { type: 'bearer' }).send(validDeck()).expect(404);
    await api().delete(`/api/decks/${d.id}`).auth(b.accessToken, { type: 'bearer' }).expect(404);
    const changed = validDeck(); changed.cards[0] = { cardId: 2003, quantity: 3 }; changed.name = '수정 덱';
    const updated = (await api().put(`/api/decks/${d.id}`).auth(a.accessToken, { type: 'bearer' }).send(changed).expect(200)).body;
    expect(updated.name).toBe(changed.name); expect(updated.cards).toEqual(expect.arrayContaining([{ cardId: 2003, quantity: 3 }]));
    expect(updated.cards.some((c: { cardId: number }) => c.cardId === 1001)).toBe(false);
    await api().delete(`/api/decks/${d.id}`).auth(a.accessToken, { type: 'bearer' }).expect(204);
    await api().delete(`/api/decks/${d.id}`).auth(a.accessToken, { type: 'bearer' }).expect(404);
  });
  it('InvalidDecksAndPlayerIdInjectionAreRejectedWithoutMutation', async () => {
    const a = await seed();
    for (const body of [{ name: '', cards: cards() }, { name: 'D', cards: null }, { name: 'D', cards: [] }, { name: 'D', cards: [null] },
      ...[[{ cardId: 1007, quantity: 3 }], [{ cardId: 999999, quantity: 30 }], [{ cardId: 1001, quantity: 2147483647 }, { cardId: 1002, quantity: 2147483647 }],
        [{ cardId: 1001, quantity: -1 }], [{ cardId: 1001, quantity: 3 }, { cardId: 1001, quantity: 3 }]].map(cards => ({ name: 'D', cards })),
      { ...validDeck(), playerId: randomUUID() }, { ...validDeck(), cards: [{ cardId: 1001, quantity: '3' }] }])
      await api().post('/api/decks').auth(a.accessToken, { type: 'bearer' }).send(body).expect(400);
    expect(catalog.isMainDeckCard(999999)).toBe(false);
    expect((await api().get('/api/decks').auth(a.accessToken, { type: 'bearer' })).body).toEqual([]);
  });
  it('InvalidTokensAreRejected by player and battle endpoints', async () => {
    const a = await seed();
    for (const reason of ['expired', 'signature', 'issuer', 'audience', 'unknown-player', 'algorithm', 'missing-exp', 'reversed-lifetime']) {
      const token = jwt.sign({ sub: reason === 'unknown-player' ? randomUUID() : a.user.playerId,
        ...(reason === 'reversed-lifetime' ? { nbf: Math.floor(Date.now() / 1000) + 2 } : {}),
        ...(reason === 'missing-exp' ? {} : { exp: Math.floor(Date.now() / 1000) + (reason === 'expired' ? -3600 : reason === 'reversed-lifetime' ? -2 : 3600) }) },
        reason === 'signature' ? 'different-test-only-key-12345678901234567890' : key,
        { algorithm: reason === 'algorithm' ? 'HS384' : 'HS256', issuer: reason === 'issuer' ? 'wrong' : 'ProjectC.Website',
          audience: reason === 'audience' ? 'wrong' : 'ProjectC.Client' });
      await api().get('/api/users/me').auth(token, { type: 'bearer' }).expect(401);
      const response = await api().post('/api/internal/battle/players/verify').set('X-Battle-Api-Key', battleKey).send({ accessToken: token }).expect(401);
      expect(response.body.code).toBe('invalid_player_token');
    }
  });
  it('MatchIdIsUniqueAndDeckSnapshotSurvivesDeckDeletion', async () => {
    const m = await match(); await battle().send(m).expect(200);
    await expect(db.match.create({ data: { matchId: m.matchId, endedAt: new Date(), endReason: 'Retry' } })).rejects.toMatchObject({ code: 'P2002' });
    await db.deck.deleteMany({ where: { id: { in: m.participants.map(p => p.deckId) } } });
    const saved = await db.match.findUniqueOrThrow({ where: { matchId: m.matchId }, include: { participants: { include: { cards: true } } } });
    expect(saved.participants).toHaveLength(2); expect(saved.winnerPlayerId).toBe(m.winnerPlayerId);
    for (const p of saved.participants) expect(p.cards.reduce((sum, c) => sum + c.quantity, 0)).toBe(30);
  });
  it('MigrationModelHasNoPendingChanges and retains database checks/FKs', async () => {
    const diff = execFileSync(process.execPath, ['node_modules/prisma/build/index.js', 'migrate', 'diff', '--from-config-datasource', '--to-schema',
      'prisma/schema.prisma', '--script', '--config', 'prisma.config.ts'], { encoding: 'utf8', stdio: 'pipe', env: { ...process.env } });
    // EF's history table is deliberately outside Prisma ownership; never execute this diff.
    expect(diff.replace('DROP TABLE "game"."__EFMigrationsHistory";', '').replace(/^--.*$/gm, '').trim()).toBe('');
    const checks = await db.$queryRaw<{ conname: string }[]>`SELECT conname FROM pg_constraint WHERE connamespace = 'game'::regnamespace AND contype = 'c'`;
    expect(checks.map(c => c.conname)).toEqual(expect.arrayContaining(['CK_DeckCard_Quantity', 'CK_MatchDeckCard_Quantity', 'CK_Rank_Nonnegative', 'CK_PromotionGame_Number']));
  });
  it('BattleCredentialsAreRequiredAndCannotActAsPlayer', async () => {
    const a = await seed(), d = await deck(a.accessToken);
    for (const secret of ['', 'wrong']) {
      await api().post('/api/internal/battle/matches').auth(a.accessToken, { type: 'bearer' }).set('X-Battle-Api-Key', secret).send({}).expect(401);
      await api().post('/api/internal/battle/players/verify').set('X-Battle-Api-Key', secret).send({ accessToken: a.accessToken }).expect(401);
    }
    await api().get('/api/users/me').set('X-Battle-Api-Key', battleKey).expect(401);
    expect((await api().post('/api/internal/battle/players/verify').set('X-Battle-Api-Key', battleKey).send({ accessToken: a.accessToken }).expect(200)).body).toEqual(JSON.parse(JSON.stringify(a.user)));
    for (const value of ['bad', '', a.accessToken + 'tampered']) await api().post('/api/internal/battle/players/verify').set('X-Battle-Api-Key', battleKey).send({ accessToken: value }).expect(401);
    await api().post(`/api/internal/battle/players/${randomUUID()}/decks/${d.id}/validate`).set('X-Battle-Api-Key', battleKey).send({ catalogVersion: catalog.version }).expect(404);
    await api().post(`/api/internal/battle/players/${a.user.playerId}/decks/${d.id}/validate`).set('X-Battle-Api-Key', battleKey).send({ catalogVersion: 'old' }).expect(409);
    await db.deckCard.updateMany({ where: { deckId: d.id }, data: { quantity: 99 } });
    await api().post(`/api/internal/battle/players/${a.user.playerId}/decks/${d.id}/validate`).set('X-Battle-Api-Key', battleKey).send({ catalogVersion: catalog.version }).expect(422);
  });
  it('ConcurrentRetriesAreIdempotentAndConflictDoesNotChangeRanks', async () => {
    const m = await match(); const responses = await Promise.all(Array.from({ length: 5 }, () => battle().send(m).expect(200)));
    expect(responses.filter(r => !r.body.duplicate)).toHaveLength(1);
    const firstChanges = responses[0].body.rankChanges;
    for (const response of responses) expect(response.body.rankChanges).toEqual(firstChanges);
    const retry = await battle().send({ ...m, participants: [...m.participants].reverse() }).expect(200);
    expect(retry.body.duplicate).toBe(true);
    const conflict = await battle().send({ ...m, winnerPlayerId: m.participants[1].playerId }).expect(409);
    expect(conflict.body.code).toBe('match_result_conflict');
    expect(await db.match.count({ where: { matchId: m.matchId } })).toBe(1);
    expect(await db.rank.findUnique({ where: { playerId: m.winnerPlayerId! } })).toMatchObject({ mmr: 1016, rp: 70 });
  });
  it('SnapshotSurvivesDeckDeletionAndBadRequestsAreRejected', async () => {
    const m = await match(); await db.deck.deleteMany({ where: { id: { in: m.participants.map(p => p.deckId) } } });
    await battle().send(m).expect(200);
    for (const invalid of [{ ...m, matchId: '00000000-0000-0000-0000-000000000000' }, { ...m, endReason: 'bad' },
      { ...m, winnerPlayerId: randomUUID() }, { ...m, participants: [null, null] }, { ...m, endReason: 'Draw' },
      { ...m, endedAt: '0001-01-01T00:00:00Z' }, { ...m, endedAt: new Date(Date.now() + 600000).toISOString() }])
      await battle().send(invalid).expect(400);
    const invalidSnapshot = { ...m, matchId: randomUUID(), participants: [
      { ...m.participants[0], snapshotId: m.participants[1].snapshotId }, m.participants[1]] };
    expect((await battle().send(invalidSnapshot).expect(422)).body.code).toBe('invalid_deck_snapshot');
  });
  it('FailureAfterMatchInsertRollsBackMatchAndBothRanks', async () => {
    const m = await match();
    await db.$executeRawUnsafe('ALTER TABLE game."Ranks" ADD CONSTRAINT "Test_Rank_Rollback" CHECK ("Mmr" = 1000) NOT VALID');
    try {
      await battle().send(m).expect(500);
      expect(await db.match.count({ where: { matchId: m.matchId } })).toBe(0);
      expect(await db.matchParticipant.count({ where: { matchId: m.matchId } })).toBe(0);
      for (const p of m.participants) expect(await db.rank.findUnique({ where: { playerId: p.playerId } })).toMatchObject({ mmr: 1000, rp: 50 });
    } finally { await db.$executeRawUnsafe('ALTER TABLE game."Ranks" DROP CONSTRAINT "Test_Rank_Rollback"'); }
    await battle().send(m).expect(200);
  });
  it('RefreshRotatesRetainsIdentityAndReplayRevokesFamily', async () => {
    const a = await seed(), d = await deck(a.accessToken);
    const next = (await api().post('/api/auth/refresh').send({ refreshToken: a.refreshToken }).expect(200)).body;
    expect(next.user.playerId).toBe(a.user.playerId); expect(next.refreshToken).not.toBe(a.refreshToken);
    expect((await api().get('/api/decks').auth(next.accessToken, { type: 'bearer' })).body.map((v: { id: string }) => v.id)).toContain(d.id);
    await api().post('/api/auth/refresh').send({ refreshToken: a.refreshToken }).expect(401);
    await api().post('/api/auth/refresh').send({ refreshToken: next.refreshToken }).expect(401);
    const stored = await db.refreshToken.findMany({ where: { playerId: a.user.playerId } });
    for (const s of stored) { expect(s.revokedAt).not.toBeNull(); expect(s.tokenHash).not.toBe(a.refreshToken); }
  });
  it('ExpiredRefreshIsRejectedAndLegacyJwtCanAcquireSession', async () => {
    const a = await seed(); await db.refreshToken.update({ where: { tokenHash: tokenHash(a.refreshToken) }, data: { expiresAt: new Date(Date.now() - 86400000) } });
    await api().post('/api/auth/refresh').send({ refreshToken: a.refreshToken }).expect(401);
    const next = (await api().post('/api/auth/session').auth(a.accessToken, { type: 'bearer' }).expect(200)).body;
    expect(next.user.playerId).toBe(a.user.playerId);
    await api().post('/api/auth/revoke').send({ refreshToken: next.refreshToken }).expect(204);
    await api().post('/api/auth/refresh').send({ refreshToken: next.refreshToken }).expect(401);
  });
  it('ExpiredAccessTokenCanRefreshAndConcurrentRotationRevokesFamily', async () => {
    const a = await seed(); const expired = jwt.sign({ sub: a.user.playerId, exp: Math.floor(Date.now() / 1000) - 3600 }, key,
      { issuer: 'ProjectC.Website', audience: 'ProjectC.Client', algorithm: 'HS256' });
    await api().get('/api/users/me').auth(expired, { type: 'bearer' }).expect(401);
    const next = (await api().post('/api/auth/refresh').auth(expired, { type: 'bearer' }).send({ refreshToken: a.refreshToken }).expect(200)).body;
    const responses = await Promise.all([0, 1].map(() => api().post('/api/auth/refresh').send({ refreshToken: next.refreshToken })));
    expect(responses.map(r => r.status).sort((a, b) => a - b)).toEqual([200, 401]);
    const success = responses.find(r => r.status === 200)!.body;
    await api().post('/api/auth/refresh').send({ refreshToken: success.refreshToken }).expect(401);
  });
  it('PromotionGamesPersistAcrossRequestsAndAbortedMatchLeavesRanksAlone', async () => {
    const m = await match(); await db.rank.update({ where: { playerId: m.winnerPlayerId! }, data: { division: 1, rp: 99 } });
    for (let i = 0; i < 4; i++) await battle().send({ ...m, matchId: randomUUID() }).expect(200);
    const before = await db.rank.findUniqueOrThrow({ where: { playerId: m.winnerPlayerId! }, include: { promotionGames: true } });
    expect(before).toMatchObject({ tier: 'Platinum', division: 5, rp: 0, activePromotionSeriesId: null }); expect(before.promotionGames).toHaveLength(3);
    expect((await battle().send({ ...m, endReason: 'Aborted', winnerPlayerId: null }).expect(200)).body.rankChanges).toEqual([]);
    expect(await db.rank.findUnique({ where: { playerId: m.winnerPlayerId! }, include: { promotionGames: true } })).toEqual(before);
  });
  it('DifferentMatchesWithSamePlayersSerializeRankUpdates', async () => {
    const m = await match(); await Promise.all([0, 1].map(() => battle().send({ ...m, matchId: randomUUID() }).expect(200)));
    expect(await db.rank.findUnique({ where: { playerId: m.winnerPlayerId! } })).toMatchObject({ mmr: 1031, rp: 88, winStreak: 2 });
  });
  it('reads EF PascalCase snapshots and rank responses after cutover', async () => {
    const m = await match();
    for (const p of m.participants) {
      const old = JSON.stringify(cards().map(c => ({ CardId: c.cardId, Quantity: c.quantity })));
      await db.battleDeckSnapshot.update({ where: { id: p.snapshotId }, data: { cardsJson: old } });
    }
    const first = (await battle().send(m).expect(200)).body;
    const pascalChanges = first.rankChanges.map((c: Record<string, unknown>) => Object.fromEntries(
      Object.entries(c).map(([key, value]) => [key[0].toUpperCase() + key.slice(1), value])));
    await db.match.update({ where: { matchId: m.matchId }, data: { rankChangesJson: JSON.stringify(pascalChanges) } });
    expect((await battle().send(m).expect(200)).body.rankChanges).toEqual(first.rankChanges);
  });
  it('persists failed promotion series and excludes draws', async () => {
    const m = await match(), winner = m.winnerPlayerId!;
    await db.rank.update({ where: { playerId: winner }, data: { division: 1, rp: 99 } });
    await battle().send({ ...m, matchId: randomUUID() }).expect(200);
    await battle().send({ ...m, matchId: randomUUID(), endReason: 'Draw', winnerPlayerId: null }).expect(200);
    expect(await db.promotionGame.count({ where: { playerId: winner } })).toBe(0);
    for (let i = 0; i < 3; i++) await battle().send({ ...m, matchId: randomUUID(), winnerPlayerId: m.participants[1].playerId }).expect(200);
    expect(await db.rank.findUnique({ where: { playerId: winner } })).toMatchObject({ tier: 'Gold', division: 1, rp: 80, promotionStatus: 'Failed', activePromotionSeriesId: null });
    expect(await db.promotionGame.count({ where: { playerId: winner } })).toBe(3);
  });
  it('keeps the legacy public.Match endpoint and constraints on winners', async () => {
    const legacy = await db.legacyMatch.create({ data: { winnerId: 'legacy-winner', loserId: 'legacy-loser' } });
    expect((await api().get('/api/matches').expect(200)).body).toEqual(expect.arrayContaining([JSON.parse(JSON.stringify(legacy))]));
    await expect(db.match.create({ data: { matchId: randomUUID(), winnerPlayerId: randomUUID(), endedAt: new Date(), endReason: 'Normal' } })).rejects.toMatchObject({ code: 'P2003' });
  });
});
