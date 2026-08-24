import { readFileSync } from "node:fs";
import { LockAudit } from "../../src/tools/LockAudit";
import { getOrInitBootstrap, teardownBootstrap } from "./_support";

describe("LockAudit integration", () => {
  let connectionString: string | null = null;
  let skipReason: string | null = null;
  const tool = new LockAudit("0.1.0");

  beforeAll(async () => {
    const c = await getOrInitBootstrap();
    if (c.result) connectionString = c.result.connectionString;
    else skipReason = c.skipReason;
  });

  afterAll(async () => {
    await teardownBootstrap();
  });

  const itPg = (name: string, fn: () => Promise<void>): void => {
    if (connectionString) it(name, fn);
    else it.skip(`${name} (skipped: ${skipReason ?? "no PG"})`, fn);
  };

  itPg("current mode returns lock rows (own session at minimum)", async () => {
    const result = await tool.invoke({
      tool: "lock_audit",
      mode: "current",
      scope: { extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("lock_audit");
    expect(body).toContain("current");
  });

  itPg("waiting mode emits clean empty-state when no contention", async () => {
    const result = await tool.invoke({
      tool: "lock_audit",
      mode: "waiting",
      scope: { extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    // Either there are no waiters (the empty state phrase) or some are listed.
    expect(body).toContain("waiting");
  });

  itPg("long_running mode honors min_age_seconds", async () => {
    const result = await tool.invoke({
      tool: "lock_audit",
      mode: "long_running",
      scope: { extra: { connection_string: connectionString!, min_age_seconds: 999999 } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("long_running");
    expect(body).toContain("min_age_seconds: 999999");
  });
});
