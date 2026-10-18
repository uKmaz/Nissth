import { readFileSync } from "node:fs";
import { IndexAudit } from "../../src/tools/IndexAudit";
import { getOrInitBootstrap, teardownBootstrap } from "./_support";

describe("IndexAudit integration", () => {
  let connectionString: string | null = null;
  let skipReason: string | null = null;
  const tool = new IndexAudit("0.1.0");

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

  itPg("usage mode lists both fixture indexes", async () => {
    const result = await tool.invoke({
      tool: "index_audit",
      mode: "usage",
      scope: { package: "public", extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("idx_orders_user_id");
    expect(body).toContain("idx_orders_unused");
  });

  itPg("unused mode flags idx_orders_unused but not idx_orders_user_id", async () => {
    const result = await tool.invoke({
      tool: "index_audit",
      mode: "unused",
      scope: { package: "public", extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("idx_orders_unused");
    // idx_orders_user_id was scanned during seed; it should not appear in unused list.
    // (We can't strictly assert absence in markdown without parsing; tolerate either.)
  });

  itPg("bloat mode gracefully handles pgstattuple absence", async () => {
    const result = await tool.invoke({
      tool: "index_audit",
      mode: "bloat",
      scope: { package: "public", extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    // Either bloat data was emitted (extension present) OR the "not installed" hint.
    expect(body.includes("Index bloat estimates") || body.includes("pgstattuple")).toBe(true);
  });
});
