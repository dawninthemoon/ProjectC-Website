import { createHash } from 'node:crypto';
import { object, problem, uuid } from '../common/http.js';

export interface Participant { playerId: string; deckId: string; snapshotId: string }
export interface MatchRequest {
  matchId: string; participants: Participant[]; winnerPlayerId: string | null; endReason: string;
  endedAt: Date; canonicalTime: string;
}
export function matchRequest(body: unknown): MatchRequest {
  const r = object(body, ['matchId', 'participants', 'winnerPlayerId', 'endReason', 'endedAt']);
  if (!uuid(r.matchId) || !Array.isArray(r.participants) || r.participants.length !== 2) problem(400, 'invalid_match');
  const participants = r.participants.map(value => {
    if (!value) problem(400, 'invalid_match');
    const p = object(value, ['playerId', 'deckId', 'snapshotId']);
    if (!uuid(p.playerId) || !uuid(p.deckId) || !uuid(p.snapshotId)) problem(400, 'invalid_match');
    return { playerId: p.playerId.toLowerCase(), deckId: p.deckId.toLowerCase(), snapshotId: p.snapshotId.toLowerCase() };
  }).sort((a, b) => a.playerId.localeCompare(b.playerId));
  if (participants[0].playerId === participants[1].playerId) problem(400, 'invalid_match');
  if (!['Normal', 'Surrender', 'Disconnect', 'Draw', 'Aborted'].includes(r.endReason as string)) problem(400, 'invalid_match');
  const winner = r.winnerPlayerId == null ? null : uuid(r.winnerPlayerId) ? r.winnerPlayerId.toLowerCase() : '';
  if (r.endReason === 'Draw' || r.endReason === 'Aborted') {
    if (winner !== null) problem(400, 'invalid_match');
  } else if (!participants.some(p => p.playerId === winner)) problem(400, 'invalid_match');
  // Preserve .NET's 100ns precision for hashes even though JS Date stores milliseconds.
  const iso = typeof r.endedAt === 'string' && /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(r.endedAt);
  const endedAt = new Date(iso ? r.endedAt as string : NaN);
  if (!iso || !Number.isFinite(endedAt.getTime()) || endedAt.getUTCFullYear() < 2 || endedAt.getTime() > Date.now() + 300000)
    problem(400, 'invalid_match');
  // Reject JS's rollover of impossible calendar dates (e.g. February 30).
  const day = Number(iso[1].slice(8, 10)), month = Number(iso[1].slice(5, 7)), year = Number(iso[1].slice(0, 4));
  if (month < 1 || month > 12 || day < 1 || day > new Date(Date.UTC(year, month, 0)).getUTCDate()) problem(400, 'invalid_match');
  const fraction = (iso[2] ?? '').replace(/0+$/, '');
  const canonicalTime = endedAt.toISOString().slice(0, 19) + (fraction ? `.${fraction}` : '') + '+00:00';
  return { matchId: r.matchId.toLowerCase(), participants, winnerPlayerId: winner, endReason: r.endReason as string, endedAt, canonicalTime };
}
export function submissionHash(r: MatchRequest) {
  // Property order/casing matches the preserved C# SubmitMatchRequest serializer.
  const canonical = { MatchId: r.matchId, Participants: r.participants.map(p => ({ PlayerId: p.playerId, DeckId: p.deckId, SnapshotId: p.snapshotId })),
    WinnerPlayerId: r.winnerPlayerId, EndReason: r.endReason, EndedAt: r.canonicalTime };
  return createHash('sha256').update(JSON.stringify(canonical)).digest('hex').toUpperCase();
}
