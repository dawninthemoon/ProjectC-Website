import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';

const base = process.env.API_BASE_URL || 'http://127.0.0.1:18080';
assert(['localhost', '127.0.0.1'].includes(new URL(base).hostname), 'Smoke writes are restricted to a local test API.');
const key = process.env.BATTLE_API_KEY;
assert(key, 'BATTLE_API_KEY is required.');
async function call(path, { method = 'GET', body, token, server = false, status = 200 } = {}) {
  const response = await fetch(base + path, { method, headers: { 'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(server ? { 'X-Battle-Api-Key': key } : {}) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  assert.equal(response.status, status, `${method} ${path}`);
  return response.status === 204 ? null : response.json();
}
// Allow container startup without fixed sleeps in deployment scripts.
for (let attempt = 0; ; attempt++) {
  try { assert.deepEqual(await call('/health'), { status: 'ok' }); break; }
  catch (error) { if (attempt === 29) throw error; await new Promise(resolve => setTimeout(resolve, 500)); }
}
const catalogVersion = JSON.parse(readFileSync(new URL('../catalog/cards.json', import.meta.url), 'utf8')).version;
assert.deepEqual(await call('/health/db', { server: true }), { status: 'ok', database: 'ok' });
const cards = [1001, 1002, 1003, 1004, 1005, 1006, 1008, 1101, 2001, 2002].map(cardId => ({ cardId, quantity: 3 }));
const players = [];
for (let i = 0; i < 2; i++) {
  const token = await call('/api/auth/guest', { method: 'POST', body: { displayName: 'Docker test' } });
  assert.equal((await call('/api/users/me', { token: token.accessToken })).playerId, token.user.playerId);
  assert.equal((await call('/api/internal/battle/players/verify', { method: 'POST', server: true,
    body: { accessToken: token.accessToken } })).playerId, token.user.playerId);
  const deck = await call('/api/decks', { method: 'POST', token: token.accessToken, body: { name: 'Docker deck', cards }, status: 201 });
  const snapshot = await call(`/api/internal/battle/players/${token.user.playerId}/decks/${deck.id}/validate`, {
    method: 'POST', server: true, body: { catalogVersion } });
  assert.equal(snapshot.cardIds.length, 30);
  players.push({ token, participant: { playerId: token.user.playerId, deckId: deck.id, snapshotId: snapshot.snapshotId } });
}
const body = { matchId: randomUUID(), participants: players.map(p => p.participant), winnerPlayerId: players[0].token.user.playerId,
  endReason: 'Normal', endedAt: new Date().toISOString() };
const first = await call('/api/internal/battle/matches', { method: 'POST', server: true, body });
const retry = await call('/api/internal/battle/matches', { method: 'POST', server: true, body });
assert.equal(first.duplicate, false); assert.equal(retry.duplicate, true); assert.deepEqual(first.rankChanges, retry.rankChanges);
assert.equal((await call('/api/internal/battle/matches', { method: 'POST', server: true, status: 409,
  body: { ...body, winnerPlayerId: players[1].token.user.playerId } })).code, 'match_result_conflict');
const next = await call('/api/auth/refresh', { method: 'POST', body: { refreshToken: players[0].token.refreshToken } });
assert.equal(next.user.playerId, players[0].token.user.playerId);
await call('/api/auth/revoke', { method: 'POST', body: { refreshToken: next.refreshToken }, status: 204 });
console.log('Docker smoke passed: health, JWT, decks, Battle auth/snapshots, match/retry/conflict, refresh/revoke.');
