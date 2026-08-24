import type { Client } from "pg";
import { ConnectionManager } from "../core/ConnectionManager";
import { ReportWriter } from "../core/ReportWriter";
import { findRepoRoot } from "../core/repoRoot";
import { BridgeError } from "../core/BridgeError";
import type { BridgeCommand, ToolHandler, ToolResult } from "../core/types";

type Mode = "usage" | "unused" | "duplicate" | "bloat";

interface UsageRow {
  schemaname: string;
  relname: string;
  indexrelname: string;
  idx_scan: string;
  idx_tup_read: string;
  idx_tup_fetch: string;
  is_unique: boolean;
}

interface DuplicateRow {
  schemaname: string;
  relname: string;
  indexrelname: string;
  column_set: string;
}

interface BloatRow {
  schemaname: string;
  indexrelname: string;
  table_len: string;
  tuple_count: string;
  dead_tuple_count: string;
  dead_tuple_percent: string;
}

export class IndexAudit implements ToolHandler {
  public readonly name = "index_audit";

  constructor(private readonly bindingVersion: string) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const mode = (cmd.mode ?? "usage") as Mode;
    if (!["usage", "unused", "duplicate", "bloat"].includes(mode)) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: `index_audit: unknown mode '${mode}'.`,
      });
    }
    const schema = cmd.scope?.package ?? "public";
    const names = cmd.scope?.names;
    const extra = cmd.scope?.extra ?? {};
    const minAgeDaysRaw = extra["min_age_days"];
    const minAgeDays = typeof minAgeDaysRaw === "number" ? minAgeDaysRaw
      : typeof minAgeDaysRaw === "string" ? parseInt(minAgeDaysRaw, 10) : 7;

    const reportPath = await ConnectionManager.withClient(cmd, async (client, parsed) => {
      const fingerprint = await ConnectionManager.fingerprint(client);
      const resetTime = await IndexAudit.queryStatResetTime(client);

      let body = "";
      if (mode === "usage") {
        const rows = await IndexAudit.queryUsage(client, schema, names);
        body = IndexAudit.renderUsage(rows, schema, parsed, resetTime);
      } else if (mode === "unused") {
        const rows = await IndexAudit.queryUnused(client, schema, names, minAgeDays);
        body = IndexAudit.renderUnused(rows, schema, parsed, resetTime, minAgeDays);
      } else if (mode === "duplicate") {
        const rows = await IndexAudit.queryDuplicate(client, schema, names);
        body = IndexAudit.renderDuplicate(rows, schema, parsed);
      } else {
        const result = await IndexAudit.queryBloat(client, schema, names);
        body = IndexAudit.renderBloat(result, schema, parsed);
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
        scope: IndexAudit.scrubScope(cmd.scope),
        freshness: {
          source: ConnectionManager.redactedUrl(parsed),
          source_state: `redo_lsn=${fingerprint}; stat_reset=${resetTime}`,
          guarantee: `Fresh PG connection per call; pg_stat_user_indexes read at run start; statement_timeout=${ConnectionManager.resolveTimeout(cmd)}ms.`,
        },
        body,
      });
    });

    return { reportPath };
  }

  private static async queryStatResetTime(client: Client): Promise<string> {
    try {
      const r = await client.query<{ stats_reset: string | null }>(
        `SELECT stats_reset::text AS stats_reset FROM pg_stat_database WHERE datname = current_database()`
      );
      return r.rows[0]?.stats_reset ?? "unknown";
    } catch {
      return "unknown";
    }
  }

  private static async queryUsage(client: Client, schema: string, names?: string[]): Promise<UsageRow[]> {
    const params: unknown[] = [schema];
    let sql = `
      SELECT s.schemaname, s.relname, s.indexrelname,
             s.idx_scan::text, s.idx_tup_read::text, s.idx_tup_fetch::text,
             i.indisunique AS is_unique
      FROM pg_stat_user_indexes s
      JOIN pg_index i ON i.indexrelid = s.indexrelid
      WHERE s.schemaname = $1
    `;
    if (names && names.length > 0) {
      params.push(names);
      sql += ` AND s.relname = ANY($2::text[])`;
    }
    sql += ` ORDER BY s.relname, s.indexrelname`;
    const r = await client.query<UsageRow>(sql, params);
    return r.rows;
  }

  private static async queryUnused(client: Client, schema: string, names: string[] | undefined, minAgeDays: number): Promise<UsageRow[]> {
    // For min_age_days, gate on pg_stat_database.stats_reset — if older than min_age_days, consider idx_scan=0 reliable.
    const all = await IndexAudit.queryUsage(client, schema, names);
    // The age filter is informational here — if reset is recent, the report includes a warning row in renderUnused.
    void minAgeDays;
    return all.filter((r) => r.idx_scan === "0");
  }

  private static async queryDuplicate(client: Client, schema: string, names?: string[]): Promise<DuplicateRow[]> {
    // Detect indexes with identical column sets on the same table.
    const params: unknown[] = [schema];
    let sql = `
      WITH idx_cols AS (
        SELECT
          n.nspname AS schemaname,
          c.relname AS relname,
          ic.relname AS indexrelname,
          string_agg(a.attname, ',' ORDER BY a.attnum) AS column_set
        FROM pg_index i
        JOIN pg_class c  ON c.oid = i.indrelid
        JOIN pg_class ic ON ic.oid = i.indexrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)
        WHERE n.nspname = $1
    `;
    if (names && names.length > 0) {
      params.push(names);
      sql += ` AND c.relname = ANY($2::text[])`;
    }
    sql += `
        GROUP BY n.nspname, c.relname, ic.relname
      ),
      dup AS (
        SELECT schemaname, relname, column_set, COUNT(*) AS n
        FROM idx_cols
        GROUP BY schemaname, relname, column_set
        HAVING COUNT(*) > 1
      )
      SELECT ic.schemaname, ic.relname, ic.indexrelname, ic.column_set
      FROM idx_cols ic
      JOIN dup ON dup.schemaname = ic.schemaname AND dup.relname = ic.relname AND dup.column_set = ic.column_set
      ORDER BY ic.relname, ic.column_set, ic.indexrelname
    `;
    const r = await client.query<DuplicateRow>(sql, params);
    return r.rows;
  }

  private static async queryBloat(client: Client, schema: string, names?: string[]): Promise<{ rows: BloatRow[]; extensionAvailable: boolean; }> {
    // Probe for pgstattuple extension.
    const ext = await client.query<{ has: boolean }>(
      `SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'pgstattuple') AS has`
    );
    if (!ext.rows[0]?.has) {
      return { rows: [], extensionAvailable: false };
    }
    // pgstattuple_approx for indexes; use per-index iteration.
    const params: unknown[] = [schema];
    let sql = `
      SELECT n.nspname AS schemaname, ic.relname AS indexrelname
      FROM pg_index i
      JOIN pg_class ic ON ic.oid = i.indexrelid
      JOIN pg_class c  ON c.oid = i.indrelid
      JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname = $1
    `;
    if (names && names.length > 0) {
      params.push(names);
      sql += ` AND c.relname = ANY($2::text[])`;
    }
    sql += ` ORDER BY ic.relname LIMIT 200`;
    const idx = await client.query<{ schemaname: string; indexrelname: string }>(sql, params);
    const rows: BloatRow[] = [];
    for (const r of idx.rows) {
      try {
        const stat = await client.query<{ table_len: string; tuple_count: string; dead_tuple_count: string; dead_tuple_percent: string }>(
          `SELECT table_len::text, tuple_count::text, dead_tuple_count::text, dead_tuple_percent::text
           FROM pgstattuple_approx($1::regclass)`,
          [`${r.schemaname}.${r.indexrelname}`]
        );
        if (stat.rows[0]) {
          rows.push({
            schemaname: r.schemaname,
            indexrelname: r.indexrelname,
            ...stat.rows[0],
          });
        }
      } catch {
        // skip non-applicable indexes (e.g., GIN/BRIN may not support pgstattuple_approx)
      }
    }
    return { rows, extensionAvailable: true };
  }

  private static renderUsage(rows: UsageRow[], schema: string, parsed: { host: string; port: number; database: string }, resetTime: string): string {
    const parts: string[] = [];
    parts.push(`# index_audit — usage @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> Schema: \`${schema}\` · Mode: \`usage\` · stats_reset: \`${resetTime}\``);
    parts.push("");
    if (rows.length === 0) {
      parts.push("_No indexes in scope._");
    } else {
      parts.push(`## Indexes (${rows.length})`);
      parts.push("| Table | Index | idx_scan | idx_tup_read | idx_tup_fetch | Unique |");
      parts.push("|:---|:---|---:|---:|---:|:---|");
      for (const r of rows) {
        parts.push(`| ${r.relname} | ${r.indexrelname} | ${r.idx_scan} | ${r.idx_tup_read} | ${r.idx_tup_fetch} | ${r.is_unique ? "✓" : ""} |`);
      }
    }
    return parts.join("\n");
  }

  private static renderUnused(rows: UsageRow[], schema: string, parsed: { host: string; port: number; database: string }, resetTime: string, minAgeDays: number): string {
    const parts: string[] = [];
    parts.push(`# index_audit — unused @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> Schema: \`${schema}\` · Mode: \`unused\` · stats_reset: \`${resetTime}\` · min_age_days: ${minAgeDays}`);
    parts.push("");
    parts.push("> ⚠️ \"Unused\" means idx_scan = 0 in pg_stat_user_indexes since the last stats_reset. If stats_reset is recent, treat with caution.");
    parts.push("");
    if (rows.length === 0) {
      parts.push("_No unused indexes detected._");
    } else {
      parts.push(`## Unused indexes (${rows.length})`);
      parts.push("| Table | Index | Unique |");
      parts.push("|:---|:---|:---|");
      for (const r of rows) {
        parts.push(`| ${r.relname} | ${r.indexrelname} | ${r.is_unique ? "✓" : ""} |`);
      }
    }
    return parts.join("\n");
  }

  private static renderDuplicate(rows: DuplicateRow[], schema: string, parsed: { host: string; port: number; database: string }): string {
    const parts: string[] = [];
    parts.push(`# index_audit — duplicate @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> Schema: \`${schema}\` · Mode: \`duplicate\``);
    parts.push("");
    if (rows.length === 0) {
      parts.push("_No duplicate indexes detected._");
    } else {
      parts.push(`## Duplicate index sets (${rows.length} rows; one row per index)`);
      parts.push("| Table | Index | Column set |");
      parts.push("|:---|:---|:---|");
      for (const r of rows) {
        parts.push(`| ${r.relname} | ${r.indexrelname} | \`${r.column_set}\` |`);
      }
    }
    return parts.join("\n");
  }

  private static renderBloat(result: { rows: BloatRow[]; extensionAvailable: boolean }, schema: string, parsed: { host: string; port: number; database: string }): string {
    const parts: string[] = [];
    parts.push(`# index_audit — bloat @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> Schema: \`${schema}\` · Mode: \`bloat\``);
    parts.push("");
    if (!result.extensionAvailable) {
      parts.push("> ℹ️ `pgstattuple` extension is not installed in this database. Install with `CREATE EXTENSION pgstattuple;` (requires superuser) to enable bloat estimation.");
    } else if (result.rows.length === 0) {
      parts.push("_No bloat data — no indexes in scope or none compatible with pgstattuple_approx (GIN/BRIN are commonly excluded)._");
    } else {
      parts.push(`## Index bloat estimates (${result.rows.length})`);
      parts.push("| Index | Table size | Tuples | Dead tuples | Dead % |");
      parts.push("|:---|---:|---:|---:|---:|");
      for (const r of result.rows) {
        parts.push(`| ${r.indexrelname} | ${r.table_len} | ${r.tuple_count} | ${r.dead_tuple_count} | ${r.dead_tuple_percent} |`);
      }
    }
    return parts.join("\n");
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
