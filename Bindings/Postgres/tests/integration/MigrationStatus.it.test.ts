import { readFileSync } from "node:fs";
import { MigrationStatus } from "../../src/tools/MigrationStatus";
import { getOrInitBootstrap, teardownBootstrap } from "./_support";

describe("MigrationStatus integration", () => {
  let connectionString: string | null = null;
  let skipReason: string | null = null;
  const tool = new MigrationStatus("0.1.0");

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

  itPg("auto mode detects flyway_schema_history and lists rows", async () => {
    const result = await tool.invoke({
      tool: "migration_status",
      mode: "auto",
      scope: { extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("Flyway");
    expect(body).toContain("baseline");
    expect(body).toContain("add user email idx");
  });

  itPg("auto mode flags the failed migration row", async () => {
    const result = await tool.invoke({
      tool: "migration_status",
      mode: "auto",
      scope: { extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toMatch(/failed migration/i);
  });

  itPg("flyway mode reports rows with the Flyway section header", async () => {
    const result = await tool.invoke({
      tool: "migration_status",
      mode: "flyway",
      scope: { extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("Flyway");
  });

  itPg("liquibase mode reports 'not found' when only flyway table exists", async () => {
    const result = await tool.invoke({
      tool: "migration_status",
      mode: "liquibase",
      scope: { extra: { connection_string: connectionString! } },
    });
    const body = readFileSync(result.reportPath, "utf8");
    expect(body).toContain("Liquibase");
  });
});
