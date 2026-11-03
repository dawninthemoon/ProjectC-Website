import { matchRequest, submissionHash } from './match-contract.js';

describe('C# SubmitMatchRequest hash compatibility', () => {
  const body = {
    matchId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    participants: [
      { playerId: '11111111-1111-4111-8111-111111111111', deckId: '22222222-2222-4222-8222-222222222222', snapshotId: '33333333-3333-4333-8333-333333333333' },
      { playerId: '44444444-4444-4444-8444-444444444444', deckId: '55555555-5555-4555-8555-555555555555', snapshotId: '66666666-6666-4666-8666-666666666666' }],
    winnerPlayerId: '11111111-1111-4111-8111-111111111111', endReason: 'Normal', endedAt: '2026-11-03T10:00:00.1234567+09:00',
  };
  // Generated with .NET 10 System.Text.Json and SHA256 from the original record definitions.
  const expected = '011D59422F39B46A2A562FE6EEBF16A691683E28FB24FC4CBBBD20FD34515636';
  it('matches the .NET hash including all seven fractional timestamp digits', () => {
    expect(submissionHash(matchRequest(body))).toBe(expected);
    expect(submissionHash(matchRequest({ ...body, endedAt: '2026-11-03T01:00:00.1234567Z', participants: [...body.participants].reverse() }))).toBe(expected);
  });
  it('distinguishes timestamps below JavaScript Date precision', () => {
    expect(submissionHash(matchRequest({ ...body, endedAt: '2026-11-03T01:00:00.1234566Z' }))).not.toBe(expected);
  });
});
