import { readFileSync } from "node:fs";
import { QueryPlan } from "../../src/tools/QueryPlan";
import { BridgeError } from "../../src/core/BridgeError";
import { getOrInitBootstrap, teardownBootstrap } from "./_support";

describe("QueryPlan integration", () => {
  let connectionString: string | null = null;
  let skipReason: string | null = null;
  const tool = new QueryPlan("0.1.0");

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

  itPg("explains a parameterized SELECT", async () => {
    const result = await tool.invoke({
      tool: "query_plan",
      mode: "explain",
      scope: {
        extra: {
          connection_string: connectionString!,
          sql: "SELECT * FROM orders WHERE user_id = $1",
          params: [1],
        },
      },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("query_plan");
    expect(body).toContain("Plan summary");
    expect(body).toContain("Plan");
  });

  itPg("ANALYZE returns actual rows", async () => {
    const result = await tool.invoke({
      tool: "query_plan",
      mode: "analyze",
      scope: {
        extra: {
          connection_string: connectionString!,
          sql: "SELECT COUNT(*) FROM orders",
        },
      },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("Actual");
  });

  itPg("refuses ANALYZE on a mutating statement", async () => {
    await expect(
      tool.invoke({
        tool: "query_plan",
        mode: "analyze",
        scope: {
          extra: {
            connection_string: connectionString!,
            sql: "UPDATE users SET name = 'X' WHERE id = 1",
          },
        },
      })
    ).rejects.toThrow(BridgeError);
  });

  itPg("allows EXPLAIN (no execution) on a mutating statement", async () => {
    const result = await tool.invoke({
      tool: "query_plan",
      mode: "explain",
      scope: {
        extra: {
          connection_string: connectionString!,
          sql: "UPDATE users SET name = 'X' WHERE id = 1",
        },
      },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("query_plan");
  });
});
