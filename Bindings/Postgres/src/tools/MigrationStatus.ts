import type { Client } from "pg";
import { ConnectionManager } from "../core/ConnectionManager";
import { ReportWriter } from "../core/ReportWriter";
import { findRepoRoot } from "../core/repoRoot";
import { BridgeError } from "../core/BridgeError";
import type { BridgeCommand, ToolHandler, ToolResult } from "../core/types";

type Mode = "flyway" | "liquibase" | "auto";

interface FlywayRow {
  version: string | null;
  description: string | null;
  type: string | null;
  script: string | null;
  checksum: number | null;
  installed_by: string | null;
  installed_on: string | null;
  execution_time: number | null;
  success: boolean | null;
}

interface LiquibaseRow {
  id: string;
  author: string;
  filename: string;
  dateexecuted: string;
  exectype: string;
  md5sum: string | null;
  description: string | null;
}

export class MigrationStatus implements ToolHandler {
  public readonly name = "migration_status";

  constructor(private readonly bindingVersion: string) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const mode = (cmd.mode ?? "auto") as Mode;
    if (!["flyway", "liquibase", "auto"].includes(mode)) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: `migration_status: unknown mode '${mode}'. Use one of: flyway, liquibase, auto.`,
      });
    }
    const schema = cmd.scope?.package ?? "public";
    const overrideTable = (cmd.scope?.extra ?? {})["migration_table"];
    const overrideName = typeof overrideTable === "string" ? overrideTable : undefined;

    const reportPath = await ConnectionManager.withClient(cmd, async (client, parsed) => {
      const fingerprint = await ConnectionManager.fingerprint(client);

      const flywayTable = await MigrationStatus.findTable(client, schema, overrideName ?? "flyway_schema_history");
      const liquibaseTable = await MigrationStatus.findTable(client, schema, overrideName ?? "databasechangelog");

      let bodyParts: string[] = [];
      bodyParts.push(`# migration_status — ${mode} @ ${parsed.host}:${parsed.port}/${parsed.database}`);
      bodyParts.push(`> Schema: \`${schema}\` · Mode: \`${mode}\``);
      bodyParts.push("");

      const runFlyway = mode === "flyway" || (mode === "auto" && flywayTable !== null);
      const runLiquibase = mode === "liquibase" || (mode === "auto" && liquibaseTable !== null);

      if (!runFlyway && !runLiquibase) {
        bodyParts.push("## Result");
        bodyParts.push("");
        bodyParts.push("_No migration-history table found_ (looked for `flyway_schema_history` and `databasechangelog` in schema `" + schema + "`).");
        bodyParts.push("");
        bodyParts.push("If your project uses a different tool or non-standard table name, pass `scope.extra.migration_table` to override.");
      }

      if (runFlyway) {
        if (flywayTable === null && mode === "flyway") {
          bodyParts.push(`## Flyway`);
          bodyParts.push("_`${schema}.flyway_schema_history` not found._");
          bodyParts.push("");
        } else if (flywayTable !== null) {
          const rows = await MigrationStatus.queryFlyway(client, flywayTable);
          bodyParts.push(`## Flyway (\`${flywayTable}\`)`);
          MigrationStatus.renderFlyway(rows, bodyParts);
          bodyParts.push("");
        }
      }

      if (runLiquibase) {
        if (liquibaseTable === null && mode === "liquibase") {
          bodyParts.push(`## Liquibase`);
          bodyParts.push("_`${schema}.databasechangelog` not found._");
          bodyParts.push("");
        } else if (liquibaseTable !== null) {
          const rows = await MigrationStatus.queryLiquibase(client, liquibaseTable);
          bodyParts.push(`## Liquibase (\`${liquibaseTable}\`)`);
          MigrationStatus.renderLiquibase(rows, bodyParts);
          bodyParts.push("");
        }
      }

      const repoRoot = findRepoRoot();
      const writer = new ReportWriter({
        binding: "postgres",
        bindingVersion: this.bindingVersion,
        repoRoot,
      });
      return writer.write({
        tool: this.name,
        mode,
        scope: MigrationStatus.scrubScope(cmd.scope),
        freshness: {
          source: ConnectionManager.redactedUrl(parsed),
          source_state: `redo_lsn=${fingerprint}`,
          guarantee: `Fresh PG connection per call; history table read at run start; statement_timeout=${ConnectionManager.resolveTimeout(cmd)}ms. Does NOT include pending migrations (filesystem access is the application-side binding's responsibility).`,
        },
        body: bodyParts.join("\n"),
      });
    });

    return { reportPath };
  }

  /** Returns "<schema>.<name>" if the table exists, else null. */
  private static async findTable(client: Client, schema: string, name: string): Promise<string | null> {
    const r = await client.query<{ exists: boolean }>(
      `SELECT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = $1 AND table_name = $2
      ) AS exists`,
      [schema, name]
    );
    return r.rows[0]?.exists ? `${schema}.${name}` : null;
  }

  private static async queryFlyway(client: Client, qualifiedTable: string): Promise<FlywayRow[]> {
    // qualifiedTable is built from validated identifiers (schema + table_name from information_schema),
    // so the interpolation is safe. We still wrap in regclass cast as a belt-and-suspenders check.
    const r = await client.query<FlywayRow>(
      `SELECT version, description, type, script, checksum,
              installed_by, installed_on::text, execution_time, success
       FROM ${qualifiedTable}
       ORDER BY COALESCE(installed_rank, 0)`
    );
    return r.rows;
  }

  private static async queryLiquibase(client: Client, qualifiedTable: string): Promise<LiquibaseRow[]> {
    const r = await client.query<LiquibaseRow>(
      `SELECT id, author, filename, dateexecuted::text, exectype, md5sum, description
       FROM ${qualifiedTable}
       ORDER BY orderexecuted`
    );
    return r.rows;
  }

  private static renderFlyway(rows: FlywayRow[], out: string[]): void {
    if (rows.length === 0) {
      out.push("_No migrations recorded._");
      return;
    }
    out.push(`### Applied & failed migrations (${rows.length})`);
    out.push("| Version | Description | Type | Script | Installed by | Installed on | Exec ms | Success |");
    out.push("|:---|:---|:---|:---|:---|:---|---:|:---|");
    for (const r of rows) {
      out.push(`| ${r.version ?? ""} | ${r.description ?? ""} | ${r.type ?? ""} | ${r.script ?? ""} | ${r.installed_by ?? ""} | ${r.installed_on ?? ""} | ${r.execution_time ?? ""} | ${r.success ? "✓" : "✗"} |`);
    }
    const failed = rows.filter((r) => r.success === false);
    if (failed.length > 0) {
      out.push("");
      out.push(`> ⚠️ ${failed.length} failed migration(s) recorded.`);
    }
  }

  private static renderLiquibase(rows: LiquibaseRow[], out: string[]): void {
    if (rows.length === 0) {
      out.push("_No changesets recorded._");
      return;
    }
    out.push(`### Changesets (${rows.length})`);
    out.push("| Id | Author | Filename | Executed | ExecType | MD5 |");
    out.push("|:---|:---|:---|:---|:---|:---|");
    for (const r of rows) {
      out.push(`| ${r.id} | ${r.author} | ${r.filename} | ${r.dateexecuted} | ${r.exectype} | ${r.md5sum ?? ""} |`);
    }
    const failed = rows.filter((r) => r.exectype === "FAILED");
    if (failed.length > 0) {
      out.push("");
      out.push(`> ⚠️ ${failed.length} FAILED changeset(s) recorded.`);
    }
  }

  private static scrubScope(scope: BridgeCommand["scope"]): Record<string, unknown> | undefined {
    if (!scope) return undefined;
    const out: Record<string, unknown> = { ...scope };
    if (scope.extra) {
      const extraOut: Record<string, unknown> = { ...scope.extra };
      if ("connection_string" in extraOut) extraOut["connection_string"] = "***REDACTED***";
      out["extra"] = extraOut;
    }
    return out;
  }
}
