import { ConnectionManager } from "../../src/core/ConnectionManager";
import { BridgeError } from "../../src/core/BridgeError";
import type { BridgeCommand } from "../../src/core/types";

const ENV_VAR = "NISSTH_PG_URL";
const SENTINEL = "SUPERSECRET_SENTINEL_xyz_42";
const URL_WITH_PW = `postgresql://nissth_ro:${SENTINEL}@db.example.com:5432/mydb?sslmode=require`;

describe("ConnectionManager.resolveConnectionString", () => {
  const original = process.env[ENV_VAR];
  afterEach(() => {
    if (original === undefined) delete process.env[ENV_VAR];
    else process.env[ENV_VAR] = original;
  });

  it("prefers scope.extra.connection_string over env var", () => {
    process.env[ENV_VAR] = "postgresql://env_user@env_host/env_db";
    const cmd: BridgeCommand = {
      tool: "schema_lens",
      scope: { extra: { connection_string: URL_WITH_PW } },
    };
    expect(ConnectionManager.resolveConnectionString(cmd)).toBe(URL_WITH_PW);
  });

  it("falls back to NISSTH_PG_URL when scope.extra absent", () => {
    process.env[ENV_VAR] = URL_WITH_PW;
    const cmd: BridgeCommand = { tool: "schema_lens" };
    expect(ConnectionManager.resolveConnectionString(cmd)).toBe(URL_WITH_PW);
  });

  it("throws BridgeError(stage=validate, code=no_connection_string) when neither set", () => {
    delete process.env[ENV_VAR];
    const cmd: BridgeCommand = { tool: "schema_lens" };
    try {
      ConnectionManager.resolveConnectionString(cmd);
      fail("expected BridgeError");
    } catch (e: unknown) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("validate");
      expect(be.errorCode).toBe("no_connection_string");
      expect(be.exitCode()).toBe(2);
    }
  });

  it("treats empty env var as absent", () => {
    process.env[ENV_VAR] = "";
    const cmd: BridgeCommand = { tool: "schema_lens" };
    expect(() => ConnectionManager.resolveConnectionString(cmd)).toThrow(BridgeError);
  });
});

describe("ConnectionManager.parse", () => {
  it("extracts host/port/db/user/password from libpq URL", () => {
    const p = ConnectionManager.parse(URL_WITH_PW);
    expect(p.host).toBe("db.example.com");
    expect(p.port).toBe(5432);
    expect(p.database).toBe("mydb");
    expect(p.user).toBe("nissth_ro");
    expect(p.password).toBe(SENTINEL);
    expect(p.application_name).toBe("nissth-bridge-postgres");
  });

  it("defaults port to 5432 when omitted", () => {
    const p = ConnectionManager.parse("postgresql://u:p@host/db");
    expect(p.port).toBe(5432);
  });

  it("rejects URL without host", () => {
    expect(() => ConnectionManager.parse("postgresql:///nohost")).toThrow(BridgeError);
  });

  it("never includes raw URL in error message (defense vs leak)", () => {
    try {
      ConnectionManager.parse("postgresql:///nohost");
      fail("expected throw");
    } catch (e: unknown) {
      const msg = (e as Error).message;
      expect(msg).not.toContain("nohost");
    }
  });
});

describe("ConnectionManager.redactForLog", () => {
  it("replaces password with sentinel constant; preserves other fields", () => {
    const parsed = ConnectionManager.parse(URL_WITH_PW);
    const redacted = ConnectionManager.redactForLog(parsed);
    expect(redacted.password).toBe("***REDACTED***");
    expect(redacted.password).not.toBe(SENTINEL);
    expect(redacted.host).toBe(parsed.host);
    expect(redacted.port).toBe(parsed.port);
    expect(redacted.user).toBe(parsed.user);
    expect(redacted.database).toBe(parsed.database);
  });

  it("redacted object stringifies without leaking password", () => {
    const parsed = ConnectionManager.parse(URL_WITH_PW);
    const redacted = ConnectionManager.redactForLog(parsed);
    const json = JSON.stringify(redacted);
    expect(json).not.toContain(SENTINEL);
    expect(json).toContain("***REDACTED***");
  });
});

describe("ConnectionManager.redactedUrl", () => {
  it("produces URL without password component", () => {
    const parsed = ConnectionManager.parse(URL_WITH_PW);
    const url = ConnectionManager.redactedUrl(parsed);
    expect(url).toBe("postgresql://nissth_ro@db.example.com:5432/mydb");
    expect(url).not.toContain(SENTINEL);
    expect(url).not.toContain("REDACTED"); // sentinel placeholder not exposed either
  });
});

describe("ConnectionManager.scrubString", () => {
  it("replaces literal password with sentinel", () => {
    const input = `Failed to connect: password=${SENTINEL} bad auth`;
    expect(ConnectionManager.scrubString(input, SENTINEL)).not.toContain(SENTINEL);
    expect(ConnectionManager.scrubString(input, SENTINEL)).toContain("***REDACTED***");
  });

  it("is a no-op when password is undefined", () => {
    const input = "Some error";
    expect(ConnectionManager.scrubString(input, undefined)).toBe(input);
  });

  it("is a no-op for very short passwords (less than 4 chars) — avoids over-redaction", () => {
    const input = "the cat sat on the mat";
    expect(ConnectionManager.scrubString(input, "the")).toBe(input);
  });
});

describe("ConnectionManager.resolveTimeout", () => {
  it("defaults to 30000ms", () => {
    expect(ConnectionManager.resolveTimeout({ tool: "x" })).toBe(30_000);
  });

  it("accepts number override via scope.extra.statement_timeout_ms", () => {
    const cmd: BridgeCommand = { tool: "x", scope: { extra: { statement_timeout_ms: 5000 } } };
    expect(ConnectionManager.resolveTimeout(cmd)).toBe(5000);
  });

  it("accepts numeric-string override", () => {
    const cmd: BridgeCommand = { tool: "x", scope: { extra: { statement_timeout_ms: "10000" } } };
    expect(ConnectionManager.resolveTimeout(cmd)).toBe(10_000);
  });

  it("ignores out-of-range values (>=1 hour or <=0)", () => {
    expect(ConnectionManager.resolveTimeout({ tool: "x", scope: { extra: { statement_timeout_ms: 0 } } })).toBe(30_000);
    expect(ConnectionManager.resolveTimeout({ tool: "x", scope: { extra: { statement_timeout_ms: -1 } } })).toBe(30_000);
    expect(ConnectionManager.resolveTimeout({ tool: "x", scope: { extra: { statement_timeout_ms: 3_600_000 } } })).toBe(30_000);
  });
});
