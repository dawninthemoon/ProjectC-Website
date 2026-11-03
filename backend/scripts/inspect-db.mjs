import 'dotenv/config';
import pg from 'pg';
const url = process.env.DIRECT_URL || process.env.DATABASE_URL;
if (!url) throw new Error('DATABASE_URL or DIRECT_URL is required for read-only inspection.');
const pool = new pg.Pool({ connectionString: url, connectionTimeoutMillis: 10000 });
try {
  const client = await pool.connect();
  try {
    await client.query('BEGIN READ ONLY');
    for (const sql of [
      "SELECT table_schema,table_name FROM information_schema.tables WHERE table_schema IN ('public','game') ORDER BY 1,2",
      "SELECT table_schema,table_name,column_name,data_type,is_nullable FROM information_schema.columns WHERE table_schema IN ('public','game') ORDER BY 1,2,ordinal_position",
      "SELECT n.nspname AS schema, c.relname AS table, con.conname, pg_get_constraintdef(con.oid) AS definition FROM pg_constraint con JOIN pg_class c ON c.oid=con.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname IN ('public','game') ORDER BY 1,2,3",
    ]) console.log(JSON.stringify((await client.query(sql)).rows, null, 2));
    await client.query('ROLLBACK');
  } finally { client.release(); }
} finally { await pool.end(); }
