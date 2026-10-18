#!/usr/bin/env node
// nissth-bridge MCP shim (spring-boot binding).
//
// Registers four MCP tools per CLAUDE.md §11.6 and routes each one to the
// `nissth-bridge` CLI jar via a child process. No in-process JVM; no shared
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
const JAR = join(BINDING_ROOT, "target", "nissth-bridge-0.1.0.jar");
const BRIDGE_DIR = join(REPO_ROOT, "AgentReports", "Bridge");
const VERSION = "0.1.0";

// --- Subprocess --------------------------------------------------------

function runBridge(args, stdinText = null) {
  if (!existsSync(JAR)) {
    return {
      ok: false,
      stdout: "",
      stderr:
        `Jar not found: ${JAR}\n` +
        `Build it first:\n` +
        `  cd "${BINDING_ROOT}" && ./mvnw clean package -DskipTests`,
      exitCode: 3,
    };
  }
  const result = spawnSync(
    "java",
    ["-Dfile.encoding=UTF-8", "-jar", JAR, ...args],
    {
      input: stdinText ?? undefined,
      encoding: "utf-8",
      env: { ...process.env, NISSTH_REPO_ROOT: REPO_ROOT },
      timeout: 10 * 60 * 1000, // 10 min wall-clock cap; matches CLI subprocess defaults
    }
  );
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
      tool: z.string().describe("Tool name, e.g. 'compile_verify'"),
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
  compilation: "compile_verify",
  migrations: "migration_status",
};

const VerifyInput = {
  operation: z
    .enum(Object.keys(VERIFY_OPS))
    .describe("Verification kind. 'compilation' → compile_verify, 'migrations' → migration_status."),
  root_path: z
    .string()
    .optional()
    .describe("Target project directory (defaults to NISSTH_REPO_ROOT)."),
};

async function nissthVerify({ operation, root_path }) {
  const tool = VERIFY_OPS[operation];
  const cmd = { tool };
  if (root_path) cmd.scope = { root_path };
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
      return textResponse("`latest:` requires a tool name (e.g., 'latest:compile_verify').", true);
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
  lines.push(`Binding jar:  ${existsSync(JAR) ? "present" : "MISSING"} (${JAR})`);
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

const server = new McpServer({ name: "nissth-bridge-spring-boot", version: VERSION });

server.tool(
  "Nissth_Gateway",
  "Primary entry point for the Nissth Diagnostic Bridge spring-boot binding. " +
    "Forwards a full JSON command per CLAUDE.md §11.2 to the CLI and returns the report inline. " +
    "Use this for any tool the binding exposes (compile_verify, endpoint_lens, entity_lens, migration_status, entity_field_add).",
  GatewayInput,
  nissthGateway
);

server.tool(
  "Nissth_Verify",
  "Wrapped invocation of verification tools with auto-refresh semantics. " +
    "Maps a verification kind to the right binding tool and returns the fresh report inline.",
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
  "Health probe: lists installed bindings, the N most recent Bridge reports, and whether the jar is built.",
  StatusInput,
  nissthStatus
);

await server.connect(new StdioServerTransport());
