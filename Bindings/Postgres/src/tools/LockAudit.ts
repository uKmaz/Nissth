import type { Client } from "pg";
import { ConnectionManager } from "../core/ConnectionManager";
import { ReportWriter } from "../core/ReportWriter";
import { findRepoRoot } from "../core/repoRoot";
import { BridgeError } from "../core/BridgeError";
import type { BridgeCommand, ToolHandler, ToolResult } from "../core/types";

type Mode = "current" | "waiting" | "long_running";

interface LockRow {
  pid: number | null;
  locktype: string | null;
  mode: string | null;
  granted: boolean | null;
  relation_name: string | null;
  usename: string | null;
  application_name: string | null;
  state: string | null;
  query: string | null;
  query_age_seconds: string | null;
  wait_event_type: string | null;
}

interface WaitingRow {
  waiter_pid: number;
  waiter_query: string | null;
  waiter_usename: string | null;
  blocker_pids: number[];
  lock_type: string | null;
  mode: string | null;
}

interface LongRunningRow {
  pid: number;
  usename: string | null;
  application_name: string | null;
  state: string | null;
  query_age_seconds: string;
  query: string | null;
}

export class LockAudit implements ToolHandler {
  public readonly name = "lock_audit";

  constructor(private readonly bindingVersion: string) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const mode = (cmd.mode ?? "current") as Mode;
    if (!["current", "waiting", "long_running"].includes(mode)) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: `lock_audit: unknown mode '${mode}'. Use one of: current, waiting, long_running.`,
      });
    }
    const extra = cmd.scope?.extra ?? {};
    const minAgeRaw = extra["min_age_seconds"];
    const minAgeSeconds = typeof minAgeRaw === "number" ? minAgeRaw
      : typeof minAgeRaw === "string" ? parseInt(minAgeRaw, 10) : 30;

    const reportPath = await ConnectionManager.withClient(cmd, async (client, parsed) => {
      const fingerprint = await ConnectionManager.fingerprint(client);
      const hasMonitorRole = await LockAudit.hasMonitorRole(client);

      let body = "";
      if (mode === "current") {
        const rows = await LockAudit.queryCurrent(client);
        body = LockAudit.renderCurrent(rows, parsed, hasMonitorRole);
      } else if (mode === "waiting") {
        const rows = await LockAudit.queryWaiting(client);
        body = LockAudit.renderWaiting(rows, parsed, hasMonitorRole);
      } else {
        const rows = await LockAudit.queryLongRunning(client, minAgeSeconds);
        body = LockAudit.renderLongRunning(rows, parsed, hasMonitorRole, minAgeSeconds);
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
        scope: LockAudit.scrubScope(cmd.scope),
        freshness: {
          source: ConnectionManager.redactedUrl(parsed),
          source_state: `redo_lsn=${fingerprint}; pg_locks_snapshot_at_query_time`,
          guarantee: `Fresh PG connection per call; pg_locks + pg_stat_activity read at run start; statement_timeout=${ConnectionManager.resolveTimeout(cmd)}ms; visibility=${hasMonitorRole ? "cross-session" : "own-session-only"}.`,
        },
        body,
      });
    });

    return { reportPath };
  }

  private static async hasMonitorRole(client: Client): Promise<boolean> {
    try {
      const r = await client.query<{ has: boolean }>(
        `SELECT (current_setting('is_superuser') = 'on' OR pg_has_role(current_user, 'pg_read_all_stats', 'MEMBER')) AS has`
      );
      return r.rows[0]?.has === true;
    } catch {
      return false;
    }
  }

  private static async queryCurrent(client: Client): Promise<LockRow[]> {
    const r = await client.query<LockRow>(
      `
        SELECT
          l.pid,
          l.locktype,
          l.mode,
          l.granted,
          CASE WHEN l.relation IS NOT NULL THEN l.relation::regclass::text ELSE NULL END AS relation_name,
          a.usename,
          a.application_name,
          a.state,
          a.query,
          EXTRACT(EPOCH FROM (now() - a.query_start))::text AS query_age_seconds,
          a.wait_event_type
        FROM pg_locks l
        LEFT JOIN pg_stat_activity a ON a.pid = l.pid
        ORDER BY l.pid, l.granted DESC
        LIMIT 500
      `
    );
    return r.rows;
  }

  private static async queryWaiting(client: Client): Promise<WaitingRow[]> {
    const r = await client.query<WaitingRow>(
      `
        SELECT
          a.pid AS waiter_pid,
          a.query AS waiter_query,
          a.usename AS waiter_usename,
          pg_blocking_pids(a.pid) AS blocker_pids,
          l.locktype AS lock_type,
          l.mode
        FROM pg_stat_activity a
        LEFT JOIN pg_locks l ON l.pid = a.pid AND NOT l.granted
        WHERE pg_blocking_pids(a.pid) <> '{}'
        ORDER BY a.pid
      `
    );
    return r.rows;
  }

  private static async queryLongRunning(client: Client, minAgeSeconds: number): Promise<LongRunningRow[]> {
    const r = await client.query<LongRunningRow>(
      `
        SELECT pid, usename, application_name, state,
               EXTRACT(EPOCH FROM (now() - query_start))::text AS query_age_seconds,
               query
        FROM pg_stat_activity
        WHERE state = 'active'
          AND query_start IS NOT NULL
          AND now() - query_start > make_interval(secs => $1)
        ORDER BY query_start ASC
      `,
      [minAgeSeconds]
    );
    return r.rows;
  }

  private static renderCurrent(rows: LockRow[], parsed: { host: string; port: number; database: string }, hasMonitor: boolean): string {
    const parts: string[] = [];
    parts.push(`# lock_audit — current @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> Mode: \`current\` · Visibility: ${hasMonitor ? "cross-session (pg_read_all_stats/superuser)" : "own-session-only — request pg_read_all_stats for full view"}`);
    if (!hasMonitor) {
      parts.push("");
      parts.push("> ⚠️ Connecting role lacks `pg_read_all_stats`. Only this session's own locks are visible.");
    }
    parts.push("");
    if (rows.length === 0) {
      parts.push("_No locks visible to this role._");
    } else {
      parts.push(`## Locks (${rows.length})`);
      parts.push("| PID | Lock type | Mode | Granted | Relation | User | App | State | Age (s) |");
      parts.push("|---:|:---|:---|:---|:---|:---|:---|:---|---:|");
      for (const r of rows) {
        parts.push(`| ${r.pid ?? ""} | ${r.locktype ?? ""} | ${r.mode ?? ""} | ${r.granted ? "✓" : ""} | ${r.relation_name ?? ""} | ${r.usename ?? ""} | ${r.application_name ?? ""} | ${r.state ?? ""} | ${r.query_age_seconds ?? ""} |`);
      }
    }
    return parts.join("\n");
  }

  private static renderWaiting(rows: WaitingRow[], parsed: { host: string; port: number; database: string }, hasMonitor: boolean): string {
    const parts: string[] = [];
    parts.push(`# lock_audit — waiting @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> Mode: \`waiting\` · Visibility: ${hasMonitor ? "cross-session" : "own-session-only"}`);
    parts.push("");
    if (rows.length === 0) {
      parts.push("_No sessions are currently waiting on locks._");
    } else {
      parts.push(`## Blocked sessions (${rows.length})`);
      parts.push("| Waiter PID | Blocker PID(s) | Lock | Mode | User | Query |");
      parts.push("|---:|:---|:---|:---|:---|:---|");
      for (const r of rows) {
        const blockers = Array.isArray(r.blocker_pids) ? r.blocker_pids.join(", ") : "";
        const q = (r.waiter_query ?? "").replace(/\|/g, "\\|").slice(0, 80);
        parts.push(`| ${r.waiter_pid} | ${blockers} | ${r.lock_type ?? ""} | ${r.mode ?? ""} | ${r.waiter_usename ?? ""} | \`${q}\` |`);
      }
    }
    return parts.join("\n");
  }

  private static renderLongRunning(rows: LongRunningRow[], parsed: { host: string; port: number; database: string }, hasMonitor: boolean, minAge: number): string {
    const parts: string[] = [];
    parts.push(`# lock_audit — long_running @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> Mode: \`long_running\` · min_age_seconds: ${minAge} · Visibility: ${hasMonitor ? "cross-session" : "own-session-only"}`);
    parts.push("");
    if (rows.length === 0) {
      parts.push(`_No sessions active for > ${minAge}s._`);
    } else {
      parts.push(`## Long-running active sessions (${rows.length})`);
      parts.push("| PID | User | App | State | Age (s) | Query |");
      parts.push("|---:|:---|:---|:---|---:|:---|");
      for (const r of rows) {
        const q = (r.query ?? "").replace(/\|/g, "\\|").slice(0, 80);
        parts.push(`| ${r.pid} | ${r.usename ?? ""} | ${r.application_name ?? ""} | ${r.state ?? ""} | ${r.query_age_seconds} | \`${q}\` |`);
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
