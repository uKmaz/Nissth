import { parse as parseConnString } from "pg-connection-string";
import { Client, type ClientConfig } from "pg";
import { BridgeError } from "./BridgeError";
import type { BridgeCommand, ParsedConnection } from "./types";

const REDACTED = "***REDACTED***";
const ENV_VAR = "NISSTH_PG_URL";
const DEFAULT_STATEMENT_TIMEOUT_MS = 30_000;

/**
 * Resolves and manages a PostgreSQL connection for a single tool invocation.
 *
 * Resolution order:
 *   1. cmd.scope.extra.connection_string   (per-call, highest precedence)
 *   2. process.env.NISSTH_PG_URL           (session-wide)
 *   3. throw BridgeError(stage=validate)
 *
 * One client per invocation; closed in finally. No pooling.
 * Password redacted from every output channel via redactForLog().
 */
export class ConnectionManager {
  static resolveConnectionString(cmd: BridgeCommand): string {
    const extra = cmd.scope?.extra ?? {};
    const fromExtra = extra["connection_string"];
    if (typeof fromExtra === "string" && fromExtra.length > 0) {
      return fromExtra;
    }
    const fromEnv = process.env[ENV_VAR];
    if (typeof fromEnv === "string" && fromEnv.length > 0) {
      return fromEnv;
    }
    throw new BridgeError({
      stage: "validate",
      tool: cmd.tool,
      message: `No connection string supplied. Set ${ENV_VAR} env var OR pass scope.extra.connection_string (libpq URL form: postgresql://user:pass@host:port/dbname).`,
      errorCode: "no_connection_string",
    });
  }

  /**
   * Parse a libpq URL into structured components. Never logs the raw URL.
   */
  static parse(connStr: string, toolName: string = "unknown"): ParsedConnection {
    let parsed: ReturnType<typeof parseConnString>;
    try {
      parsed = parseConnString(connStr);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      // Never include connStr in the error message — it may contain the password.
      throw new BridgeError({
        stage: "validate",
        tool: toolName,
        message: `Invalid connection string format: ${msg}`,
        errorCode: "invalid_connection_string",
      });
    }
    if (!parsed.host) {
      throw new BridgeError({
        stage: "validate",
        tool: toolName,
        message: "Connection string missing host component.",
        errorCode: "invalid_connection_string",
      });
    }
    return {
      host: parsed.host,
      port: parsed.port ? parseInt(String(parsed.port), 10) : 5432,
      database: parsed.database ?? "",
      user: parsed.user ?? "",
      password: parsed.password ?? "",
      ssl: ConnectionManager.coerceSsl(parsed.ssl),
      application_name: "nissth-bridge-postgres",
    };
  }

  private static coerceSsl(v: unknown): ParsedConnection["ssl"] {
    if (v === true || v === false) return v;
    if (typeof v === "string") {
      const lower = v.toLowerCase();
      if (["require", "prefer", "allow", "disable", "verify-ca", "verify-full"].includes(lower)) {
        return lower as ParsedConnection["ssl"];
      }
    }
    return undefined;
  }

  /**
   * Returns a redacted clone of the parsed connection suitable for logs/reports/errors.
   * The password is replaced with the literal "***REDACTED***".
   */
  static redactForLog(parsed: ParsedConnection): Omit<ParsedConnection, "password"> & { password: string } {
    return {
      host: parsed.host,
      port: parsed.port,
      database: parsed.database,
      user: parsed.user,
      password: REDACTED,
      ssl: parsed.ssl,
      application_name: parsed.application_name,
    };
  }

  /**
   * Build a redacted libpq-style URL string for the freshness.source field.
   * Format: postgresql://<user>@<host>:<port>/<dbname>   (no password component)
   */
  static redactedUrl(parsed: ParsedConnection): string {
    const userPart = parsed.user ? `${parsed.user}@` : "";
    const dbPart = parsed.database ? `/${parsed.database}` : "";
    return `postgresql://${userPart}${parsed.host}:${parsed.port}${dbPart}`;
  }

  /**
   * Strip the password from any string that may contain it.
   * Used as a defense-in-depth scrub before emitting error messages.
   */
  static scrubString(input: string, password: string | undefined): string {
    if (!password || password.length < 4) return input;
    // Split on the literal password to avoid regex-injection issues.
    return input.split(password).join(REDACTED);
  }

  /**
   * Open a fresh client, run the callback, close in finally.
   * statement_timeout is set on the session before the callback runs.
   */
  static async withClient<T>(
    cmd: BridgeCommand,
    fn: (client: Client, parsed: ParsedConnection) => Promise<T>
  ): Promise<T> {
    const connStr = ConnectionManager.resolveConnectionString(cmd);
    const parsed = ConnectionManager.parse(connStr, cmd.tool);
    const timeoutMs = ConnectionManager.resolveTimeout(cmd);

    const config: ClientConfig = {
      host: parsed.host,
      port: parsed.port,
      database: parsed.database,
      user: parsed.user,
      password: parsed.password,
      application_name: parsed.application_name,
      statement_timeout: timeoutMs,
      query_timeout: timeoutMs,
    };
    if (parsed.ssl !== undefined) {
      config.ssl = parsed.ssl === true ? true : parsed.ssl === false ? false : { rejectUnauthorized: parsed.ssl !== "disable" };
    }

    const client = new Client(config);
    try {
      await client.connect();
    } catch (e: unknown) {
      const rawMsg = e instanceof Error ? e.message : String(e);
      const scrubbed = ConnectionManager.scrubString(rawMsg, parsed.password);
      throw new BridgeError({
        stage: "execute",
        tool: cmd.tool,
        message: `Failed to connect to ${ConnectionManager.redactedUrl(parsed)}: ${scrubbed}`,
        errorCode: "connection_failed",
      });
    }
    try {
      return await fn(client, parsed);
    } catch (e: unknown) {
      if (e instanceof BridgeError) throw e;
      const rawMsg = e instanceof Error ? e.message : String(e);
      const scrubbed = ConnectionManager.scrubString(rawMsg, parsed.password);
      throw new BridgeError({
        stage: "execute",
        tool: cmd.tool,
        message: `Query failed: ${scrubbed}`,
        errorCode: "query_failed",
      });
    } finally {
      try {
        await client.end();
      } catch {
        // ignore close errors — connection is being torn down anyway
      }
    }
  }

  static resolveTimeout(cmd: BridgeCommand): number {
    const extra = cmd.scope?.extra ?? {};
    const v = extra["statement_timeout_ms"];
    if (typeof v === "number" && v > 0 && v < 3_600_000) {
      return Math.floor(v);
    }
    if (typeof v === "string") {
      const n = parseInt(v, 10);
      if (Number.isFinite(n) && n > 0 && n < 3_600_000) return n;
    }
    return DEFAULT_STATEMENT_TIMEOUT_MS;
  }

  /**
   * Query the freshness fingerprint at the start of a tool run.
   * Returns the redo_lsn as a string. Falls back to "unknown" on older PG (<13).
   */
  static async fingerprint(client: Client): Promise<string> {
    try {
      const r = await client.query<{ redo_lsn: string }>("SELECT redo_lsn FROM pg_control_checkpoint()");
      return r.rows[0]?.redo_lsn ?? "unknown";
    } catch {
      // Older PG, or pg_control_checkpoint() unavailable to this role.
      try {
        const r2 = await client.query<{ lsn: string }>("SELECT pg_current_wal_lsn() AS lsn");
        return r2.rows[0]?.lsn ?? "unknown";
      } catch {
        return "unknown";
      }
    }
  }
}
