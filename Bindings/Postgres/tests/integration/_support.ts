// Shared IT helper. Wraps bootstrapPg() and exposes a skip-aware `describeIfPg`
// so each tool's IT file is identical in shape.
//
// On hosts where bootstrap fails (no Docker + no NISSTH_TEST_PG_URL), every IT
// suite registers as `describe.skip` with a stderr note. This is the documented
// Phase 07 strategy C behavior.

import { bootstrapPg, SkipITError, type BootstrapResult } from "../fixture/pg-bootstrap";

export interface ITContext {
  result: BootstrapResult | null;
  skipReason: string | null;
}

const ctx: ITContext = { result: null, skipReason: null };

export async function getOrInitBootstrap(): Promise<ITContext> {
  if (ctx.result || ctx.skipReason) return ctx;
  try {
    ctx.result = await bootstrapPg();
  } catch (e: unknown) {
    if (e instanceof SkipITError) {
      ctx.skipReason = e.message;
      // eslint-disable-next-line no-console
      console.warn(`[IT] SKIP: ${e.message}`);
    } else {
      throw e;
    }
  }
  return ctx;
}

export async function teardownBootstrap(): Promise<void> {
  if (ctx.result) {
    try {
      await ctx.result.teardown();
    } catch {
      // best-effort
    }
    ctx.result = null;
  }
}

/**
 * Helper to invoke a tool against the bootstrap PG. Throws if no PG available.
 */
export async function runToolAgainstPg<T>(
  fn: (connectionString: string) => Promise<T>
): Promise<T> {
  const c = await getOrInitBootstrap();
  if (!c.result) {
    throw new Error("IT bootstrap unavailable: " + c.skipReason);
  }
  return fn(c.result.connectionString);
}
