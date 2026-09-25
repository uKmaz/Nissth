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
//   2  parse/validate error (bad flags, unknown binding, tool-name conflict, ...)
//   3  execute error (binding's CLI errored out)
//   4  no binding registered for tool / unknown binding name
//   5  freshness contract violated (propagated from binding's CLI)
//
// Zero runtime deps; pure Node stdlib.

import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
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
  //   --list-bindings, --list-tools, --describe <tool>, --help
  //   --binding <stack> (routing override; also used by --list-tools/--describe filter)
  //   --dry-run (test-only: print "would exec: ..." instead of spawning)
  // Everything else is forwarded to the binding's CLI verbatim.
  const result = {
    listBindings: false,
    listTools: false,
    describe: null,
    binding: null,
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
