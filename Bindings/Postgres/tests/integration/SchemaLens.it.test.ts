import { readFileSync } from "node:fs";
import { SchemaLens } from "../../src/tools/SchemaLens";
import { getOrInitBootstrap, teardownBootstrap } from "./_support";

describe("SchemaLens integration", () => {
  let connectionString: string | null = null;
  let skipReason: string | null = null;
  const tool = new SchemaLens("0.1.0");

  beforeAll(async () => {
    const c = await getOrInitBootstrap();
    if (c.result) connectionString = c.result.connectionString;
    else skipReason = c.skipReason;
  });

  afterAll(async () => {
    await teardownBootstrap();
  });

  const itPg = (name: string, fn: () => Promise<void>): void => {
    if (connectionString) {
      it(name, fn);
    } else {
      it.skip(`${name} (skipped: ${skipReason ?? "no PG"})`, fn);
    }
  };

  itPg("lists tables in public schema", async () => {
    const result = await tool.invoke({
      tool: "schema_lens",
      mode: "tables",
      scope: { package: "public", extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("users");
    expect(body).toContain("orders");
  });

  itPg("lists columns for orders", async () => {
    const result = await tool.invoke({
      tool: "schema_lens",
      mode: "columns",
      scope: { package: "public", names: ["orders"], extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("user_id");
    expect(body).toContain("amount");
  });

  itPg("reports relationships (orders.user_id -> users.id)", async () => {
    const result = await tool.invoke({
      tool: "schema_lens",
      mode: "relationships",
      scope: { package: "public", extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("orders");
    expect(body).toContain("users");
  });

  itPg("full mode emits all three sections", async () => {
    const result = await tool.invoke({
      tool: "schema_lens",
      mode: "full",
      scope: { package: "public", extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("## Tables");
    expect(body).toContain("## Columns");
    expect(body).toContain("## Foreign-key relationships");
  });
});
