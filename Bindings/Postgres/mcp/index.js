#!/usr/bin/env node
// nissth-bridge MCP shim (postgres binding).
//
// Registers four MCP tools per CLAUDE.md §11.6 and routes each one to the
// `nissth-bridge` CLI (a Node script) via a child process. No in-process
// state; the shim is pure plumbing over the same JSON command grammar that
// the CLI consumes.

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, isAbsolute, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

// --- Paths --------------------------------------------------------------

const __dirname = dirname(fileURLToPath(import.meta.url));
const BINDING_ROOT = resolve(__dirname, "..");
const REPO_ROOT = resolve(BINDING_ROOT, "..", "..");
const CLI_ENTRY = join(BINDING_ROOT, "dist", "cli", "index.js");
const BRIDGE_DIR = join(REPO_ROOT, "AgentReports", "Bridge");
const VERSION = "0.1.0";

// --- Subprocess --------------------------------------------------------

function runBridge(args, stdinText = null) {
  if (!existsSync(CLI_ENTRY)) {
    return {
      ok: false,
      stdout: "",
      stderr:
        `CLI not built at: ${CLI_ENTRY}\n` +
        `Build it first:\n` +
        `  cd "${BINDING_ROOT}" && npm run build`,
      exitCode: 3,
    };
  }
  const result = spawnSync(process.execPath, [CLI_ENTRY, ...args], {
    input: stdinText ?? undefined,
    encoding: "utf-8",
    env: { ...process.env, NISSTH_REPO_ROOT: REPO_ROOT },
    timeout: 10 * 60 * 1000, // 10 min wall-clock cap
  });
  return {
    ok: result.status === 0,
    stdout: result.stdout || "",
    stderr: result.stderr || "",
    exitCode: result.status ?? -1,
  };
}

function textResponse(text, isError = false) {
  return { content: [{ type: "text", text }], isError };
}

function readReportSafely(path, maxChars = 50000) {
  try {
    const body = readFileSync(path, "utf-8");
    if (body.length <= maxChars) return body;
    return (
      body.slice(0, maxChars) +
      `\n\n… (truncated at ${maxChars} chars; full report at ${path})\n`
    );
  } catch (e) {
    return `<could not read report: ${e.message}>`;
  }
}

function listBridgeReports({ tool = null, limit = Infinity } = {}) {
  if (!existsSync(BRIDGE_DIR)) return [];
  const prefix = tool ? `${tool}_` : "";
  return readdirSync(BRIDGE_DIR)
    .filter((f) => f.endsWith(".md") && f.startsWith(prefix))
    .map((f) => {
      const p = join(BRIDGE_DIR, f);
      return { name: f, path: p, mtime: statSync(p).mtimeMs };
    })
    .sort((a, b) => b.mtime - a.mtime)
    .slice(0, limit);
}

// --- Tool: Nissth_Gateway ---------------------------------------------

const GatewayInput = {
  command: z
    .object({
      tool: z.string().describe("Tool name, e.g. 'schema_lens'"),
      mode: z.string().optional(),
      context_id: z.string().optional(),
      scope: z.record(z.any()).optional(),
      output: z.record(z.any()).optional(),
    })
    .describe("Full Bridge JSON command per CLAUDE.md §11.2"),
};

async function nissthGateway({ command }) {
  const result = runBridge(["--json-stdin"], JSON.stringify(command));
  if (!result.ok) {
    return textResponse(
      `Bridge CLI exited ${result.exitCode}\n\n${result.stderr || result.stdout}`,
      true
    );
  }
  const reportPath = result.stdout.trim();
  const body = readReportSafely(reportPath);
  return textResponse(`Report: ${reportPath}\n\n${body}`);
}

// --- Tool: Nissth_Verify ----------------------------------------------

const VERIFY_OPS = {
  // Postgres-binding mappings — operation → diagnostic tool name.
  schema: "schema_lens",
  locks: "lock_audit",
  migrations: "migration_status",
};

const VerifyInput = {
  operation: z
    .enum(Object.keys(VERIFY_OPS))
    .describe(
      "Verification kind. 'schema' → schema_lens (mode=full); 'locks' → lock_audit (mode=waiting); 'migrations' → migration_status (mode=auto). The Postgres binding has no compile_verify analog — Postgres is a service, not a compilable artifact."
    ),
  connection_string: z
    .string()
    .optional()
    .describe("Per-call connection string override; if absent, the binding reads NISSTH_PG_URL from env."),
};

async function nissthVerify({ operation, connection_string }) {
  const tool = VERIFY_OPS[operation];
  const cmd = { tool };
  // Pick the most useful default mode per operation.
  if (operation === "schema") cmd.mode = "full";
  if (operation === "locks") cmd.mode = "waiting";
  if (operation === "migrations") cmd.mode = "auto";
  if (connection_string) {
    cmd.scope = { extra: { connection_string } };
  }
  const result = runBridge(["--json-stdin"], JSON.stringify(cmd));
  if (!result.ok) {
    return textResponse(
      `Verify '${operation}' exited ${result.exitCode}\n\n${result.stderr || result.stdout}`,
      true
    );
  }
  const reportPath = result.stdout.trim();
  return textResponse(
    `Verified ${operation} via ${tool}.\nReport: ${reportPath}\n\n${readReportSafely(reportPath)}`
  );
}

// --- Tool: Nissth_ReadReport ------------------------------------------

const ReadReportInput = {
  relativePath: z
    .string()
    .describe(
      "Report filename under AgentReports/Bridge/, an absolute path, or 'latest:<tool>' to fetch the newest report for that tool."
    ),
  maxChars: z
    .number()
    .int()
    .positive()
    .optional()
    .describe("Truncate body to this many chars (default 50000)."),
};

async function nissthReadReport({ relativePath, maxChars = 50000 }) {
  let resolvedPath;
  if (relativePath.startsWith("latest:")) {
    const tool = relativePath.slice("latest:".length).trim();
    if (!tool) {
      return textResponse("`latest:` requires a tool name (e.g., 'latest:schema_lens').", true);
    }
    const found = listBridgeReports({ tool, limit: 1 })[0];
    if (!found) {
      return textResponse(
        `No reports found for tool '${tool}' in ${BRIDGE_DIR}`,
        true
      );
    }
    resolvedPath = found.path;
  } else if (isAbsolute(relativePath) || /^[A-Za-z]:[\\/]/.test(relativePath)) {
    resolvedPath = relativePath;
  } else {
    resolvedPath = join(BRIDGE_DIR, relativePath);
  }
  if (!existsSync(resolvedPath)) {
    return textResponse(`Report not found: ${resolvedPath}`, true);
  }
  return textResponse(`${resolvedPath}\n\n${readReportSafely(resolvedPath, maxChars)}`);
}

// --- Tool: Nissth_Status ----------------------------------------------

const StatusInput = {
  recent: z
    .number()
    .int()
    .nonnegative()
    .optional()
    .describe("Include the N most recent Bridge reports (default 5)."),
};

async function nissthStatus({ recent = 5 }) {
  const bindings = runBridge(["--list-bindings"]);
  const recentReports = listBridgeReports({ limit: recent });
  const lines = [];
  lines.push(`Shim version: ${VERSION}`);
  lines.push(`Repo root:    ${REPO_ROOT}`);
  lines.push(`Binding CLI:  ${existsSync(CLI_ENTRY) ? "present" : "MISSING"} (${CLI_ENTRY})`);
  lines.push(`PG URL env:   ${process.env.NISSTH_PG_URL ? "set" : "unset"}`);
  lines.push("");
  if (bindings.ok) {
    lines.push("Bindings:");
    lines.push(bindings.stdout.trim());
  } else {
    lines.push(`Bindings: <unavailable — bridge exited ${bindings.exitCode}>`);
    if (bindings.stderr) lines.push(bindings.stderr.trim());
  }
  lines.push("");
  lines.push(`Recent reports (${recent}):`);
  if (recentReports.length === 0) {
    lines.push("  (none)");
  } else {
    for (const r of recentReports) {
      lines.push(`  ${new Date(r.mtime).toISOString()}  ${r.name}`);
    }
  }
  return textResponse(lines.join("\n"));
}

// --- Server wiring ----------------------------------------------------

const server = new McpServer({ name: "nissth-bridge-postgres", version: VERSION });

server.tool(
  "Nissth_Gateway",
  "Primary entry point for the Nissth Diagnostic Bridge postgres binding. " +
    "Forwards a full JSON command per CLAUDE.md §11.2 to the CLI and returns the report inline. " +
    "Use this for any tool the binding exposes (schema_lens, query_plan, index_audit, lock_audit, migration_status). " +
    "Connection string is read from NISSTH_PG_URL env or scope.extra.connection_string.",
  GatewayInput,
  nissthGateway
);

server.tool(
  "Nissth_Verify",
  "Wrapped invocation of postgres diagnostic tools. " +
    "Maps a verification kind to the right binding tool and returns the fresh report inline. " +
    "'schema' → schema_lens, 'locks' → lock_audit, 'migrations' → migration_status.",
  VerifyInput,
  nissthVerify
);

server.tool(
  "Nissth_ReadReport",
  "Read an existing Bridge report by filename, absolute path, or 'latest:<tool>' shortcut. " +
    "Reports live at <repo-root>/AgentReports/Bridge/ and are auto-generated; do not hand-edit them.",
  ReadReportInput,
  nissthReadReport
);

server.tool(
  "Nissth_Status",
  "Health probe: lists installed bindings, the N most recent Bridge reports, whether the binding CLI is built, and whether NISSTH_PG_URL is set.",
  StatusInput,
  nissthStatus
);

await server.connect(new StdioServerTransport());
