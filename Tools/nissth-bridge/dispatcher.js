#!/usr/bin/env node
// Cross-binding nissth-bridge dispatcher.
//
// Discovers Bindings/*/*.bridge.json (skipping _schemas/), builds a tool→binding
// map, and routes a <tool> invocation to that binding's CLI entrypoint.
//
// Per CLAUDE.md §11.5 (existing) + §11.15 (added by Phase 08):
//   - tool name is unique within the framework; if two bindings register the
//     same name we error out (--binding <stack> disambiguates).
//   - per-binding launchers under Bindings/<stack>/scripts/nissth-bridge are
//     kept as escape hatches; the dispatcher targets the binding's CLI
//     entrypoint directly (cli_entry field in the binding's .bridge.json).
//
// Exit codes (matching CLAUDE.md §11.5):
//   0  success
//   1  --health found problems (Phase 26)
//   2  parse/validate error (bad flags, unknown binding, tool-name conflict, ...)
//   3  execute error (binding's CLI errored out)
//   4  no binding registered for tool / unknown binding name
//   5  freshness contract violated (propagated from binding's CLI)
//
// Zero runtime deps; pure Node stdlib.

import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { spawnSync } from "node:child_process";

const ROOT_MARKER = "CLAUDE.md";
const BINDINGS_DIR_NAME = "Bindings";
const SCHEMAS_SUBDIR = "_schemas";
const FRAMEWORK_ENV_VAR = "NISSTH_FRAMEWORK_ROOT";
const SUBMODULE_CONVENTION = join("Tools", "Nissth");

// --- Repo root resolution --------------------------------------------------

export function findRepoRoot(startDir) {
  let dir = resolve(startDir);
  for (let i = 0; i < 32; i++) {
    try {
      if (statSync(join(dir, ROOT_MARKER)).isFile()) return dir;
    } catch {
      // not here
    }
    const parent = dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error(
    `Could not locate ${ROOT_MARKER} in any ancestor of ${startDir}. nissth-bridge must be run from inside a Nissth-bound repository.`
  );
}

// --- Framework root resolution (Phase 09) ----------------------------------
// Where the Bindings/ tree actually lives. Three-tier resolution:
//   1. NISSTH_FRAMEWORK_ROOT env var (explicit; highest precedence). Path must
//      contain a Bindings/ subdir or we throw a validate-stage DispatchError.
//   2. <repoRoot>/Tools/Nissth/   (submodule convention for consumer projects
//      that have installed Nissth as a git submodule at the canonical path).
//   3. <repoRoot>                  (fallback — Nissth's own dogfooding).
//
// The dispatcher's reports still go to <repoRoot>/AgentReports/Bridge/ — only
// the manifest-discovery path is rerouted. This means a consumer project's
// Bridge reports land in the consumer project, not in the framework checkout.

export function findFrameworkRoot(repoRoot) {
  const fromEnv = process.env[FRAMEWORK_ENV_VAR];
  if (typeof fromEnv === "string" && fromEnv.length > 0) {
    const abs = resolve(fromEnv);
    const bindingsDir = join(abs, BINDINGS_DIR_NAME);
    try {
      if (statSync(bindingsDir).isDirectory()) return abs;
    } catch {
      // fall through to throw
    }
    throw new DispatchError(
      2,
      `${FRAMEWORK_ENV_VAR}='${fromEnv}' does not contain a ${BINDINGS_DIR_NAME}/ subdirectory. Set it to the absolute path of a Nissth checkout (the directory that holds CLAUDE.md + ${BINDINGS_DIR_NAME}/).`,
      "invalid_framework_root"
    );
  }
  const submoduleCandidate = join(repoRoot, SUBMODULE_CONVENTION);
  try {
    if (statSync(join(submoduleCandidate, BINDINGS_DIR_NAME)).isDirectory()) {
      return submoduleCandidate;
    }
  } catch {
    // submodule not present; fall through
  }
  return repoRoot;
}

// --- Manifest discovery ----------------------------------------------------

export function discoverManifests(repoRoot) {
  const bindingsDir = join(repoRoot, BINDINGS_DIR_NAME);
  let entries;
  try {
    entries = readdirSync(bindingsDir);
  } catch {
    return [];
  }
  const out = [];
  for (const entry of entries) {
    if (entry.startsWith("_") || entry === SCHEMAS_SUBDIR) continue;
    const bindingDir = join(bindingsDir, entry);
    try {
      if (!statSync(bindingDir).isDirectory()) continue;
    } catch {
      continue;
    }
    // Look for any *.bridge.json file inside the binding directory (one expected).
    let files;
    try {
      files = readdirSync(bindingDir);
    } catch {
      continue;
    }
    for (const f of files) {
      if (!f.endsWith(".bridge.json")) continue;
      const manifestPath = join(bindingDir, f);
      try {
        const data = JSON.parse(readFileSync(manifestPath, "utf8"));
        out.push({
          dir: entry,
          path: manifestPath,
          bindingDir,
          data,
        });
        break;
      } catch (e) {
        process.stderr.write(`warning: could not parse ${manifestPath}: ${e.message}\n`);
      }
    }
  }
  return out;
}

export function buildToolMap(manifests) {
  // Map<toolName, Array<{bindingId, manifest}>>
  const m = new Map();
  for (const man of manifests) {
    const bindingId = man.data.binding;
    if (!bindingId) continue;
    const tools = Array.isArray(man.data.tools) ? man.data.tools : [];
    for (const t of tools) {
      if (!t || typeof t.name !== "string") continue;
      const arr = m.get(t.name) ?? [];
      arr.push({ bindingId, manifest: man });
      m.set(t.name, arr);
    }
  }
  return m;
}

// --- Argv parsing ----------------------------------------------------------

export function parseArgv(argv) {
  // Recognized flags:
  //   --list-bindings, --list-tools, --describe <tool>, --health [--json], --help
  //   --binding <stack> (routing override; also used by --list-tools/--describe filter)
  //   --dry-run (test-only: print "would exec: ..." instead of spawning)
  // Everything else is forwarded to the binding's CLI verbatim.
  const result = {
    listBindings: false,
    listTools: false,
    describe: null,
    binding: null,
    health: false,
    json: false,
    help: false,
    dryRun: false,
    tool: null,
    passthrough: [],
  };
  const args = [...argv];
  let i = 0;
  while (i < args.length) {
    const a = args[i];
    if (a === "--list-bindings") {
      result.listBindings = true;
      i++;
    } else if (a === "--list-tools") {
      result.listTools = true;
      i++;
    } else if (a === "--describe") {
      const v = args[i + 1];
      if (v === undefined || v.startsWith("--")) {
        throw new DispatchError(2, "--describe requires a tool name");
      }
      result.describe = v;
      i += 2;
    } else if (a === "--binding") {
      const v = args[i + 1];
      if (v === undefined || v.startsWith("--")) {
        throw new DispatchError(2, "--binding requires a binding id (e.g., 'postgres', 'expo', 'spring-boot')");
      }
      result.binding = v;
      i += 2;
    } else if (a === "--health") {
      result.health = true;
      i++;
    } else if (a === "--json" && args.includes("--health")) {
      result.json = true;
      i++;
    } else if (a === "--help" || a === "-h") {
      result.help = true;
      i++;
    } else if (a === "--dry-run") {
      result.dryRun = true;
      i++;
    } else if (a.startsWith("--")) {
      // Unknown flag — forward verbatim along with any value-looking arg.
      result.passthrough.push(a);
      const next = args[i + 1];
      if (next !== undefined && !next.startsWith("--")) {
        result.passthrough.push(next);
        i += 2;
      } else {
        i++;
      }
    } else {
      // Positional: first one is the tool name; rest are passthrough positional args.
      if (result.tool === null) {
        result.tool = a;
      } else {
        result.passthrough.push(a);
      }
      i++;
    }
  }
  return result;
}

// --- Tool resolution -------------------------------------------------------

export function resolveBindingForTool(toolMap, manifests, toolName, explicitBinding) {
  if (explicitBinding) {
    const man = manifests.find((m) => m.data.binding === explicitBinding);
    if (!man) {
      throw new DispatchError(4, `Unknown binding: '${explicitBinding}'. Run --list-bindings to see installed bindings.`);
    }
    const tool = (man.data.tools ?? []).find((t) => t.name === toolName);
    if (!tool) {
      throw new DispatchError(4, `Binding '${explicitBinding}' does not register tool '${toolName}'. Run '--list-tools --binding ${explicitBinding}' to see its catalog.`);
    }
    return { bindingId: explicitBinding, manifest: man };
  }
  const owners = toolMap.get(toolName) ?? [];
  if (owners.length === 0) {
    throw new DispatchError(4, `Unknown tool: '${toolName}'. Run --list-tools for the catalog.`);
  }
  if (owners.length > 1) {
    const names = owners.map((o) => o.bindingId).sort().join(", ");
    throw new DispatchError(2, `Tool '${toolName}' is registered by multiple bindings: ${names}. Use --binding <stack> to disambiguate.`);
  }
  return owners[0];
}

// --- CLI-entry resolution + exec ------------------------------------------

export function resolveCliEntry(manifest) {
  const e = manifest.data.cli_entry;
  if (!e || typeof e !== "object") {
    throw new DispatchError(
      2,
      `Binding '${manifest.data.binding}' manifest is missing a 'cli_entry' field. Expected {runtime: 'node' | 'java-jar', path: '<rel-path>'}. Add this field to ${manifest.path}.`
    );
  }
  const runtime = e.runtime;
  const path = e.path;
  if (typeof runtime !== "string" || typeof path !== "string") {
    throw new DispatchError(2, `Binding '${manifest.data.binding}' has invalid cli_entry: runtime + path strings required.`);
  }
  if (runtime !== "node" && runtime !== "java-jar") {
    throw new DispatchError(2, `Binding '${manifest.data.binding}' has unsupported cli_entry.runtime: '${runtime}'. Supported: 'node', 'java-jar'.`);
  }
  return { runtime, absPath: resolve(manifest.bindingDir, path), bindingDir: manifest.bindingDir };
}

export function buildSpawnSpec(cliEntry, passthrough) {
  if (cliEntry.runtime === "node") {
    return { command: process.execPath, args: [cliEntry.absPath, ...passthrough] };
  }
  if (cliEntry.runtime === "java-jar") {
    return { command: "java", args: ["-jar", cliEntry.absPath, ...passthrough] };
  }
  throw new DispatchError(2, `Unsupported runtime: ${cliEntry.runtime}`);
}

// --- Health (Phase 26) ----------------------------------------------------
// `--list-bindings` reads manifests and nothing else, so it exits 0 on a fresh
// clone where no binding has been built and every tool call would fail. Two
// consumers ran five weeks that way with a launcher pointing at a previous
// machine, writing "Bridge reports: none" in every status entry. `--health` is
// the check that can fail: it asks whether each tool could actually run, and —
// from a consumer — whether the CLAUDE.md body still matches the framework.

const BUILD_HINT = {
  node: (dir) => `cd "${dir}" && npm ci && npm run build`,
  "java-jar": (dir) => `cd "${dir}" && ./mvnw -q -B package -DskipTests`,
};

function newestMtime(dir, depth = 0) {
  let newest = 0;
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return 0;
  }
  for (const e of entries) {
    if (e.name === "node_modules" || e.name.startsWith(".")) continue;
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      if (depth < 12) newest = Math.max(newest, newestMtime(p, depth + 1));
    } else {
      try {
        newest = Math.max(newest, statSync(p).mtimeMs);
      } catch {
        // vanished mid-walk
      }
    }
  }
  return newest;
}

function frameworkRootWithTier(repoRoot) {
  const root = findFrameworkRoot(repoRoot);
  if (process.env[FRAMEWORK_ENV_VAR]) return { root, via: `${FRAMEWORK_ENV_VAR} env var` };
  if (root !== repoRoot) return { root, via: `${SUBMODULE_CONVENTION} submodule` };
  return { root, via: "repo root (framework checkout)" };
}

/**
 * Everything `--health` reports, as data. `problems` is the list a session must
 * treat as Blockers (CLAUDE.md §1); `notes` never change the exit code.
 * opts.javaAvailable lets tests avoid spawning java.
 */
export async function healthReport(repoRoot, opts = {}) {
  const report = { repoRoot, frameworkRoot: null, via: null, bindings: [], body: null, problems: [], notes: [] };
  try {
    const { root, via } = frameworkRootWithTier(repoRoot);
    report.frameworkRoot = root;
    report.via = via;
  } catch (e) {
    report.problems.push(`framework root unresolved — ${e.message}`);
    return report;
  }
  const manifests = discoverManifests(report.frameworkRoot);
  if (manifests.length === 0) {
    report.problems.push(`no bindings under ${join(report.frameworkRoot, BINDINGS_DIR_NAME)}`);
  }
  let javaAvailable = opts.javaAvailable;
  for (const m of manifests) {
    const id = m.data.binding ?? m.dir;
    const row = { binding: id, status: "ok", detail: "" };
    let entry;
    try {
      entry = resolveCliEntry(m);
    } catch (e) {
      row.status = "bad-manifest";
      row.detail = e.message;
      report.bindings.push(row);
      report.problems.push(`${id}: ${e.message}`);
      continue;
    }
    const rel = m.data.cli_entry.path;
    const built = (() => {
      try {
        return statSync(entry.absPath);
      } catch {
        return null;
      }
    })();
    if (!built) {
      row.status = "not-built";
      row.detail = `${rel} missing`;
      row.fix = `build: ${BUILD_HINT[entry.runtime](m.bindingDir)}`;
    } else if (newestMtime(join(m.bindingDir, "src")) > built.mtimeMs) {
      row.status = "build-older-than-source";
      row.detail = `${rel} predates a file under src/`;
      row.fix = `rebuild: ${BUILD_HINT[entry.runtime](m.bindingDir)}`;
    } else if (entry.runtime === "java-jar") {
      if (javaAvailable === undefined) {
        const r = spawnSync("java", ["-version"], { stdio: "ignore" });
        javaAvailable = !r.error && r.status === 0;
      }
      if (!javaAvailable) {
        row.status = "runtime-missing";
        row.detail = "java not on PATH";
        row.fix = "install a JDK 17+ or put it on PATH";
      }
    }
    if (row.status === "ok") row.detail = rel;
    else report.problems.push(`${id}: ${row.status} — ${row.fix}`);
    report.bindings.push(row);
  }
  if (manifests.some((m) => m.data.binding === "postgres") && !process.env.NISSTH_PG_URL) {
    report.notes.push("NISSTH_PG_URL unset — postgres tools need it or a per-call scope.extra.connection_string");
  }

  if (resolve(report.frameworkRoot) === resolve(repoRoot)) {
    report.body = { status: "n/a", detail: "this is the framework checkout" };
  } else {
    try {
      const initUrl = pathToFileURL(join(report.frameworkRoot, "Tools", "nissth-init", "init.mjs")).href;
      const { checkConsumer } = await import(initUrl);
      const r = checkConsumer(repoRoot, report.frameworkRoot);
      if (r.inSync) {
        report.body = { status: "in-sync", detail: "" };
      } else {
        const parts = [];
        if (r.bodyShape === "unreadable") parts.push("CLAUDE.md has no banner rule");
        else if (r.driftLines) parts.push(`framework body differs on ${r.driftLines} line(s)`);
        if (r.missing?.length) parts.push(`missing ${r.missing.join(", ")}`);
        for (const l of r.launchers ?? []) parts.push(`${l.file}: ${l.problem}`);
        report.body = { status: "drift", detail: parts.join("; ") };
        report.problems.push(
          `consumer drift — ${parts.join("; ")}. Details: node ${join(report.frameworkRoot, "Tools", "nissth-init", "init.mjs")} --check ${repoRoot}`
        );
      }
    } catch (e) {
      report.body = { status: "unchecked", detail: e.message };
      report.problems.push(`consumer drift could not be checked — ${e.message}`);
    }
  }
  return report;
}

function printHealth(r) {
  const out = [`nissth-bridge --health`, `  repo root:      ${r.repoRoot}`];
  out.push(`  framework root: ${r.frameworkRoot ?? "(unresolved)"}${r.via ? `  (via ${r.via})` : ""}`);
  if (r.bindings.length) {
    out.push("  bindings:");
    for (const b of r.bindings) out.push(`    ${b.status.padEnd(24)} ${b.binding.padEnd(12)} ${b.detail}`);
  }
  if (r.body) out.push(`  framework body: ${r.body.status}${r.body.detail ? ` — ${r.body.detail}` : ""}`);
  for (const n of r.notes) out.push(`  note: ${n}`);
  out.push(
    r.problems.length === 0
      ? "  HEALTHY"
      : `  ${r.problems.length} problem(s) — record under Blockers and settle before other work (CLAUDE.md §1):\n` +
          r.problems.map((p) => `    - ${p}`).join("\n")
  );
  process.stdout.write(out.join("\n") + "\n");
}

// --- Errors ---------------------------------------------------------------

export class DispatchError extends Error {
  constructor(exitCode, message, errorCode) {
    super(message);
    this.name = "DispatchError";
    this.exitCode = exitCode;
    if (errorCode !== undefined) this.errorCode = errorCode;
  }
}

// --- Entry point ----------------------------------------------------------

function printHelp() {
  process.stdout.write(`nissth-bridge — cross-binding dispatcher (Phase 08)

Usage:
  nissth-bridge <tool> [--binding <stack>] [tool-specific flags...]
  nissth-bridge --list-bindings
  nissth-bridge --list-tools [--binding <stack>]
  nissth-bridge --describe <tool> [--binding <stack>]
  nissth-bridge --health [--json]     can every tool run here? (exit 1 if not)
  nissth-bridge --help

Routing:
  Bindings are discovered by globbing Bindings/*/*.bridge.json.
  Tool names are unique within the framework; if two bindings register the
  same name, the dispatcher errors with exit code 2 — use --binding <stack>
  to disambiguate.

Per-binding launchers under Bindings/<stack>/scripts/nissth-bridge remain
as escape hatches for direct binding access; they're not expected to be on
PATH alongside the unified launcher.

Pointers:
  CLAUDE.md §11.5 + §11.15
  Tools/nissth-bridge/README.md
`);
}

export function runDispatcher(rawArgv, opts = {}) {
  // Returns an exit code; never calls process.exit() itself (testability).
  const repoRoot = opts.repoRoot ?? findRepoRoot(opts.cwd ?? process.cwd());
  let parsed;
  try {
    parsed = parseArgv(rawArgv);
  } catch (e) {
    if (e instanceof DispatchError) {
      process.stderr.write(`${e.message}\n`);
      return e.exitCode;
    }
    throw e;
  }

  if (parsed.help) {
    printHelp();
    return 0;
  }

  if (parsed.health) {
    // The only async path: the consumer-drift check imports nissth-init.
    return healthReport(repoRoot, opts).then((r) => {
      if (parsed.json) process.stdout.write(JSON.stringify({ ok: r.problems.length === 0, ...r }, null, 2) + "\n");
      else printHealth(r);
      return r.problems.length === 0 ? 0 : 1;
    });
  }

  let frameworkRoot;
  try {
    frameworkRoot = findFrameworkRoot(repoRoot);
  } catch (e) {
    if (e instanceof DispatchError) {
      process.stderr.write(`${e.message}\n`);
      return e.exitCode;
    }
    throw e;
  }
  const manifests = discoverManifests(frameworkRoot);
  if (manifests.length === 0) {
    process.stderr.write(
      `No bindings found at ${join(frameworkRoot, BINDINGS_DIR_NAME)}/*/*.bridge.json. ` +
        `Resolution order: ${FRAMEWORK_ENV_VAR} env var > <repoRoot>/${SUBMODULE_CONVENTION}/ submodule > <repoRoot> fallback. ` +
        `Set ${FRAMEWORK_ENV_VAR}='<path-to-nissth-checkout>' or add the framework as a git submodule at ${SUBMODULE_CONVENTION}/, or install a binding directly under ${BINDINGS_DIR_NAME}/.\n`
    );
    return 4;
  }

  if (parsed.listBindings) {
    const names = manifests.map((m) => m.data.binding).filter(Boolean).sort();
    process.stdout.write(names.join("\n") + "\n");
    return 0;
  }

  if (parsed.listTools) {
    const filtered = parsed.binding
      ? manifests.filter((m) => m.data.binding === parsed.binding)
      : manifests;
    if (parsed.binding && filtered.length === 0) {
      process.stderr.write(`Unknown binding: '${parsed.binding}'. Run --list-bindings.\n`);
      return 4;
    }
    const tools = [];
    for (const m of filtered) {
      for (const t of m.data.tools ?? []) {
        if (t && typeof t.name === "string") tools.push(t.name);
      }
    }
    // Sort and dedupe; flag duplicates (shouldn't happen across our three bindings, but for safety):
    tools.sort();
    const seen = new Set();
    const out = [];
    for (const t of tools) {
      if (!seen.has(t)) {
        out.push(t);
        seen.add(t);
      }
    }
    process.stdout.write(out.join("\n") + "\n");
    return 0;
  }

  const toolMap = buildToolMap(manifests);

  if (parsed.describe) {
    let owner;
    try {
      owner = resolveBindingForTool(toolMap, manifests, parsed.describe, parsed.binding);
    } catch (e) {
      if (e instanceof DispatchError) {
        process.stderr.write(`${e.message}\n`);
        return e.exitCode;
      }
      throw e;
    }
    const tool = (owner.manifest.data.tools ?? []).find((t) => t.name === parsed.describe);
    process.stdout.write(JSON.stringify(tool, null, 2) + "\n");
    process.stdout.write(`\nBinding: ${owner.bindingId} (manifest: ${owner.manifest.path})\n`);
    return 0;
  }

  if (!parsed.tool) {
    process.stderr.write(
      "Usage: nissth-bridge <tool> [--binding <stack>] [tool-flags...] OR nissth-bridge --help\n"
    );
    return 2;
  }

  let owner;
  try {
    owner = resolveBindingForTool(toolMap, manifests, parsed.tool, parsed.binding);
  } catch (e) {
    if (e instanceof DispatchError) {
      process.stderr.write(`${e.message}\n`);
      return e.exitCode;
    }
    throw e;
  }

  let cliEntry;
  try {
    cliEntry = resolveCliEntry(owner.manifest);
  } catch (e) {
    if (e instanceof DispatchError) {
      process.stderr.write(`${e.message}\n`);
      return e.exitCode;
    }
    throw e;
  }

  // Forward the tool name + passthrough args to the binding's CLI.
  // The binding's CLI expects the tool as the first positional arg, same as via its own launcher.
  const fullPassthrough = [parsed.tool, ...parsed.passthrough];
  const spec = buildSpawnSpec(cliEntry, fullPassthrough);

  if (parsed.dryRun) {
    process.stdout.write(`would exec: ${spec.command} ${spec.args.join(" ")}\n`);
    return 0;
  }

  const result = spawnSync(spec.command, spec.args, {
    stdio: "inherit",
    env: process.env,
    timeout: 10 * 60 * 1000, // 10 min
  });
  if (result.error) {
    process.stderr.write(`Failed to spawn ${spec.command}: ${result.error.message}\n`);
    return 3;
  }
  return result.status ?? 1;
}

// --- Main -----------------------------------------------------------------

const __filename = fileURLToPath(import.meta.url);
const isMain = process.argv[1] === __filename;
if (isMain) {
  Promise.resolve()
    .then(() => runDispatcher(process.argv.slice(2)))
    .then((code) => process.exit(code))
    .catch((err) => {
      process.stderr.write(`error: ${err?.stack ?? err}\n`);
      process.exit(1);
    });
}
