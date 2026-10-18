// Strategy A (Testcontainers), B (NISSTH_TEST_PG_URL), C (skip) selection.
// Returns a connection URL the IT tests can use; throws SkipITError under C.

import { readFileSync } from "node:fs";
import { join } from "node:path";

export class SkipITError extends Error {
  constructor(reason: string) {
    super(reason);
    this.name = "SkipITError";
  }
}

export interface BootstrapResult {
  connectionString: string;
  /** Optional teardown — closes containers, drops temp schemas, etc. Idempotent. */
  teardown: () => Promise<void>;
  /** Which strategy actually ran. */
  strategy: "A_testcontainers" | "B_env_var" | "C_skip";
}

const FIXTURE_DIR = __dirname;
const SEED_SQL_PATH = join(FIXTURE_DIR, "seed.sql");
const FLYWAY_HIST_PATH = join(FIXTURE_DIR, "flyway_history.sql");

/**
 * Boot a fresh Postgres for integration tests, seeded with the fixture data.
 * Strategy precedence: B (env var) — fastest, no Docker — then A (testcontainers),
 * then C (skip with a documented reason).
 */
export async function bootstrapPg(): Promise<BootstrapResult> {
  if (process.env.NISSTH_TEST_PG_URL) {
    return bootstrapStrategyB();
  }
  try {
    return await bootstrapStrategyA();
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e);
    throw new SkipITError(
      `No Postgres available: NISSTH_TEST_PG_URL unset AND Testcontainers failed (${msg}). ` +
        `On a Docker-capable host: ensure the daemon is reachable. ` +
        `Otherwise: set NISSTH_TEST_PG_URL='postgresql://user:pass@host/db' to use an existing database. ` +
        `On this offline host, ITs skip (acceptable per Phase 07 §1.3 strategy C).`
    );
  }
}

async function bootstrapStrategyB(): Promise<BootstrapResult> {
  const url = process.env.NISSTH_TEST_PG_URL!;
  // Create a per-test schema to isolate from other test runs. The fixture seed
  // recreates tables, so we cannot share the public schema across parallel runs.
  const { Client } = await import("pg");
  const tenantSchema = `nissth_test_${Date.now()}_${Math.floor(Math.random() * 10000)}`;
  const client = new Client({ connectionString: url, statement_timeout: 30000 });
  await client.connect();
  try {
    await client.query(`CREATE SCHEMA "${tenantSchema}"`);
    await client.query(`SET search_path TO "${tenantSchema}"`);
    await client.query(readFileSync(SEED_SQL_PATH, "utf8"));
    await client.query(readFileSync(FLYWAY_HIST_PATH, "utf8"));
  } finally {
    await client.end();
  }
  // Build a URL with the new search_path baked in via options=-csearch_path%3D<schema>.
  const sep = url.includes("?") ? "&" : "?";
  const tenantUrl = `${url}${sep}options=-csearch_path%3D${tenantSchema}`;
  return {
    connectionString: tenantUrl,
    strategy: "B_env_var",
    teardown: async () => {
      const c2 = new Client({ connectionString: url, statement_timeout: 30000 });
      await c2.connect();
      try {
        await c2.query(`DROP SCHEMA IF EXISTS "${tenantSchema}" CASCADE`);
      } finally {
        await c2.end();
      }
    },
  };
}

async function bootstrapStrategyA(): Promise<BootstrapResult> {
  // PostgreSqlContainer moved to @testcontainers/postgresql in testcontainers v10+.
  const { PostgreSqlContainer } = await import("@testcontainers/postgresql");
  const container = await new PostgreSqlContainer("postgres:15-alpine")
    .withDatabase("nissth_test")
    .withUsername("nissth")
    .withPassword("nissth_pass")
    .start();
  const url = container.getConnectionUri();
  const { Client } = await import("pg");
  const client = new Client({ connectionString: url, statement_timeout: 30000 });
  await client.connect();
  try {
    await client.query(readFileSync(SEED_SQL_PATH, "utf8"));
    await client.query(readFileSync(FLYWAY_HIST_PATH, "utf8"));
  } finally {
    await client.end();
  }
  return {
    connectionString: url,
    strategy: "A_testcontainers",
    teardown: async () => {
      try {
        await container.stop();
      } catch {
        // ignore — best-effort cleanup
      }
    },
  };
}
