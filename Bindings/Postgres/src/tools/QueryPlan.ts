import type { Client } from "pg";
import { ConnectionManager } from "../core/ConnectionManager";
import { ReportWriter } from "../core/ReportWriter";
import { findRepoRoot } from "../core/repoRoot";
import { BridgeError } from "../core/BridgeError";
import type { BridgeCommand, ToolHandler, ToolResult } from "../core/types";

type Mode = "explain" | "analyze" | "buffers";

const MUTATING_REGEX = /^\s*(INSERT|UPDATE|DELETE|TRUNCATE|CREATE|DROP|ALTER|GRANT|REVOKE|COPY|VACUUM|CLUSTER|REINDEX)\b/i;

export class QueryPlan implements ToolHandler {
  public readonly name = "query_plan";

  constructor(private readonly bindingVersion: string) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const mode = (cmd.mode ?? "explain") as Mode;
    if (!["explain", "analyze", "buffers"].includes(mode)) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: `query_plan: unknown mode '${mode}'. Use one of: explain, analyze, buffers.`,
      });
    }
    const extra = cmd.scope?.extra ?? {};
    const sql = typeof extra["sql"] === "string" ? (extra["sql"] as string) : "";
    if (!sql) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: "query_plan requires scope.extra.sql (the SQL statement to plan).",
        errorCode: "missing_sql",
      });
    }
    const isMutating = MUTATING_REGEX.test(sql);
    if (isMutating && (mode === "analyze" || mode === "buffers")) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: `query_plan: refusing to ANALYZE a mutating statement (matched: ${sql.trim().split(/\s+/)[0]}). Use mode='explain' for read-only plan inspection, or rewrite the SQL.`,
        errorCode: "mutating_sql_refused_for_analyze",
      });
    }
    const paramsRaw = extra["params"];
    const params: unknown[] = Array.isArray(paramsRaw) ? (paramsRaw as unknown[]) : [];

    const explainPrefix = QueryPlan.explainPrefix(mode);
    const wrappedSql = `${explainPrefix} ${sql}`;

    const reportPath = await ConnectionManager.withClient(cmd, async (client, parsed) => {
      const fingerprint = await ConnectionManager.fingerprint(client);
      const r = await client.query(wrappedSql, params);

      // EXPLAIN (FORMAT JSON) returns a single row with a JSON array column "QUERY PLAN"
      const planJson = QueryPlan.extractPlan(r.rows);

      const body = QueryPlan.renderBody(mode, sql, params, planJson, parsed);

      const repoRoot = findRepoRoot();
      const writer = new ReportWriter({
        binding: "postgres",
        bindingVersion: this.bindingVersion,
        repoRoot,
      });
      return writer.write({
        tool: this.name,
        mode,
        scope: QueryPlan.scrubScope(cmd.scope, sql, params),
        freshness: {
          source: ConnectionManager.redactedUrl(parsed),
          source_state: `redo_lsn=${fingerprint}; plan_generated_at_connection_start`,
          guarantee: `Fresh PG connection per call; mode=${mode}${mode !== "explain" ? " (query actually executed)" : " (no execution)"}; FORMAT JSON; statement_timeout=${ConnectionManager.resolveTimeout(cmd)}ms.`,
        },
        body,
      });
    });

    return { reportPath };
  }

  static explainPrefix(mode: Mode): string {
    if (mode === "analyze") return "EXPLAIN (ANALYZE, FORMAT JSON)";
    if (mode === "buffers") return "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)";
    return "EXPLAIN (FORMAT JSON)";
  }

  static isMutating(sql: string): boolean {
    return MUTATING_REGEX.test(sql);
  }

  private static extractPlan(rows: Array<Record<string, unknown>>): unknown {
    if (rows.length === 0) return null;
    const row = rows[0];
    const key = Object.keys(row)[0]; // "QUERY PLAN"
    return row[key];
  }

  private static renderBody(
    mode: Mode,
    sql: string,
    params: unknown[],
    plan: unknown,
    parsed: { host: string; port: number; database: string; user: string }
  ): string {
    const parts: string[] = [];
    parts.push(`# query_plan — ${mode} @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> User: \`${parsed.user}\` · Mode: \`${mode}\``);
    parts.push("");
    parts.push(`## SQL`);
    parts.push("```sql");
    parts.push(sql.trim());
    parts.push("```");
    if (params.length > 0) {
      parts.push("");
      parts.push(`## Bind params`);
      parts.push("```json");
      parts.push(JSON.stringify(params, null, 2));
      parts.push("```");
    }
    parts.push("");
    const summary = QueryPlan.summarizePlan(plan);
    if (summary) {
      parts.push(`## Plan summary`);
      parts.push(summary);
      parts.push("");
    }
    parts.push(`## Full plan (JSON)`);
    parts.push("```json");
    parts.push(JSON.stringify(plan, null, 2));
    parts.push("```");
    return parts.join("\n");
  }

  private static summarizePlan(plan: unknown): string {
    if (!Array.isArray(plan) || plan.length === 0) return "";
    const first = plan[0] as Record<string, unknown> | undefined;
    if (!first) return "";
    const root = first["Plan"] as Record<string, unknown> | undefined;
    if (!root) return "";
    const parts: string[] = [];
    parts.push(`- Top-level operator: \`${root["Node Type"] ?? "?"}\``);
    if ("Total Cost" in root) parts.push(`- Total cost: ${root["Total Cost"]}`);
    if ("Plan Rows" in root) parts.push(`- Estimated rows: ${root["Plan Rows"]}`);
    if ("Actual Total Time" in root) parts.push(`- Actual total time: ${root["Actual Total Time"]} ms`);
    if ("Actual Rows" in root) parts.push(`- Actual rows: ${root["Actual Rows"]}`);
    return parts.join("\n");
  }

  private static scrubScope(scope: BridgeCommand["scope"], sql: string, params: unknown[]): Record<string, unknown> | undefined {
    if (!scope) return undefined;
    const out: Record<string, unknown> = { ...scope };
    if (scope.extra) {
      const extraOut: Record<string, unknown> = { ...scope.extra };
      if ("connection_string" in extraOut) {
        extraOut["connection_string"] = "***REDACTED***";
      }
      // SQL and params are user-supplied — echo them as-is for traceability;
      // it's the agent's responsibility to avoid passing secrets in literal SQL.
      extraOut["sql"] = sql;
      extraOut["params"] = params;
      out["extra"] = extraOut;
    }
    return out;
  }
}
