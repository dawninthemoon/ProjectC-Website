import 'dotenv/config';
import { PrismaService } from '../dist/prisma/prisma.service.js';

// Uses exactly the application's DATABASE_URL and adapter, not DIRECT_URL.
// Build/generate first. No JWT or Battle key is needed by this local CLI.
let db;
try {
  db = new PrismaService();
  const result = await db.$transaction(async tx => {
    await tx.$executeRaw`SET TRANSACTION READ ONLY`;
    await tx.$executeRaw`SELECT set_config('statement_timeout', '5000', true)`;
    const [version] = await tx.$queryRaw`SELECT current_setting('server_version_num')::integer AS version`;
    const expected = ['public."Match"', 'game."Players"', 'game."RefreshSessions"', 'game."Decks"', 'game."DeckCard"',
      'game."Ranks"', 'game."PromotionGame"', 'game."BattleDeckSnapshots"', 'game."Matches"', 'game."MatchParticipant"', 'game."MatchDeckCard"'];
    const missing = [];
    for (const name of expected) {
      const [row] = await tx.$queryRaw`SELECT to_regclass(${name})::text AS relation`;
      if (!row.relation) missing.push(name);
    }
    return { database: 'ok', postgresqlMajor: Math.floor(version.version / 10000),
      schema: missing.length ? 'incomplete' : 'ok', missingTables: missing };
  }, { maxWait: 3000, timeout: 10000 });
  console.log(JSON.stringify(result));
  if (result.schema !== 'ok' || result.postgresqlMajor !== 17) process.exitCode = 1;
} catch {
  console.error(JSON.stringify({ database: 'unavailable', message: 'Check DATABASE_URL, TLS, network access, and database availability. No changes were made.' }));
  process.exitCode = 1;
} finally {
  if (db) await db.$disconnect();
}
