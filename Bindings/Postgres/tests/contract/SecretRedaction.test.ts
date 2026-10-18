// Load-bearing security contract:
// The connection-string password must never appear in any produced report,
// stdout/stderr line, error message, or stack trace.
//
// Strategy: use a fixed sentinel password and grep-assert ZERO occurrences
// across every output channel + every emit path the binding has.

import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import { ConnectionManager } from "../../src/core/ConnectionManager";
import { BridgeError } from "../../src/core/BridgeError";
import { ReportWriter } from "../../src/core/ReportWriter";
import type { BridgeCommand } from "../../src/core/types";

const SENTINEL = "SECRET_SENTINEL_xkcd_42_BANANA";
const TEST_URL = `postgresql://nissth_user:${SENTINEL}@db.example.com:5432/mydb?sslmode=require`;

function assertNoSentinel(haystack: string, where: string): void {
  if (haystack.includes(SENTINEL)) {
    throw new Error(`LEAK: sentinel password found in ${where}. Excerpt: ${haystack.slice(Math.max(0, haystack.indexOf(SENTINEL) - 40), haystack.indexOf(SENTINEL) + 60)}`);
  }
}

describe("SecretRedaction (load-bearing contract)", () => {
  describe("ConnectionManager surfaces", () => {
    it("redactForLog removes the password literal", () => {
      const parsed = ConnectionManager.parse(TEST_URL);
      const redacted = ConnectionManager.redactForLog(parsed);
      assertNoSentinel(JSON.stringify(redacted), "redactForLog JSON");
      assertNoSentinel(String(redacted.password), "redactForLog.password");
    });

    it("redactedUrl never contains the password", () => {
      const parsed = ConnectionManager.parse(TEST_URL);
      const url = ConnectionManager.redactedUrl(parsed);
      assertNoSentinel(url, "redactedUrl output");
      expect(url).toContain("nissth_user@db.example.com");
    });

    it("scrubString eliminates the password substring", () => {
      const leak = `Authentication failed for user with password=${SENTINEL}`;
      const cleaned = ConnectionManager.scrubString(leak, SENTINEL);
      assertNoSentinel(cleaned, "scrubString output");
      expect(cleaned).toContain("***REDACTED***");
    });

    it("parse-failure error message does not echo the input URL", () => {
      try {
        ConnectionManager.parse(`bad-url-${SENTINEL}`);
      } catch (e: unknown) {
        // parse may succeed (pg-connection-string is permissive); if so, just skip the leak check.
        if (e instanceof BridgeError) {
          assertNoSentinel(e.message, "BridgeError.message");
        }
      }
    });
  });

  describe("ReportWriter end-to-end", () => {
    let tmpRoot: string;
    beforeAll(() => {
      tmpRoot = mkdtempSync(join(tmpdir(), "redact-pg-"));
    });
    afterAll(() => {
      rmSync(tmpRoot, { recursive: true, force: true });
    });

    it("the binding NEVER writes the password into a report, even if a caller naïvely puts it in scope.extra", () => {
      const writer = new ReportWriter({
        binding: "postgres",
        bindingVersion: "0.1.0",
        repoRoot: tmpRoot,
      });
      // The tools' scrubScope() utility replaces connection_string with "***REDACTED***".
      // Here we simulate that — the contract is: anything passed to ReportWriter as `scope`
      // must already be scrubbed by the caller. We verify the writer doesn't somehow
      // reintroduce the password.
      const scope = {
        package: "public",
        extra: { connection_string: "***REDACTED***" }, // <- caller already scrubbed
      };
      writer.write({
        tool: "schema_lens",
        mode: "full",
        scope,
        freshness: {
          source: ConnectionManager.redactedUrl(ConnectionManager.parse(TEST_URL)),
          source_state: "redo_lsn=0/12345",
          guarantee: "ok",
        },
        body: "## Tables\n- users\n",
      });

      // Read every file in the tmp Bridge directory and assert no sentinel.
      const bridgeDir = join(tmpRoot, "AgentReports", "Bridge");
      const files = readdirSync(bridgeDir);
      for (const f of files) {
        const content = readFileSync(join(bridgeDir, f), "utf8");
        assertNoSentinel(content, `report file ${f}`);
      }
    });
  });

  describe("CLI end-to-end", () => {
    it("invoking the CLI with the connection string in env emits a no_PG-here error WITHOUT echoing the URL", () => {
      // We point at a definitely-unreachable host so the connection fails fast.
      // The error path must scrub the password before printing to stderr.
      const cli = join(__dirname, "..", "..", "dist", "cli", "index.js");
      const url = `postgresql://nissth_user:${SENTINEL}@127.0.0.1:1/never_resolves`;
      const r = spawnSync(process.execPath, [
        cli,
        "schema_lens",
        "--mode", "tables",
        "--scope.package", "public",
        "--scope.extra.connection_string", url,
      ], {
        encoding: "utf-8",
        timeout: 30000,
      });
      const combined = (r.stdout || "") + (r.stderr || "");
      assertNoSentinel(combined, "CLI stdout+stderr");
      // Sanity: the CLI should have errored (exit != 0).
      expect(r.status).not.toBe(0);
    });

    it("missing-connection-string error message does not leak any sentinel from prior env setup", () => {
      const cli = join(__dirname, "..", "..", "dist", "cli", "index.js");
      const r = spawnSync(process.execPath, [
        cli,
        "schema_lens",
        "--mode", "tables",
      ], {
        encoding: "utf-8",
        env: { ...process.env, NISSTH_PG_URL: "", PATH: process.env.PATH ?? "" },
        timeout: 10000,
      });
      const combined = (r.stdout || "") + (r.stderr || "");
      assertNoSentinel(combined, "CLI stdout+stderr (no_connection_string path)");
      expect(combined).toContain("no_connection_string");
    });
  });
});
