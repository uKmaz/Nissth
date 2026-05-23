import { statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";

const ROOT_MARKER = "CLAUDE.md";
const BINDINGS_DIR_NAME = "Bindings";
const FRAMEWORK_ENV_VAR = "NISSTH_FRAMEWORK_ROOT";
const SUBMODULE_CONVENTION = join("Tools", "Nissth");

/**
 * Walk up from startDir until a directory containing CLAUDE.md is found.
 * That's the repo root. Throws if no ancestor contains the marker.
 */
export function findRepoRoot(startDir: string = process.cwd()): string {
  let dir = resolve(startDir);
  for (let i = 0; i < 32; i++) {
    try {
      if (statSync(join(dir, ROOT_MARKER)).isFile()) {
        return dir;
      }
    } catch {
      // marker not at this level; try parent
    }
    const parent = dirname(dir);
    if (parent === dir) {
      break;
    }
    dir = parent;
  }
  throw new Error(
    `Could not locate ${ROOT_MARKER} in any ancestor of ${startDir}. ` +
      `nissth-bridge must be run from inside a Nissth-bound repository.`
  );
}

/**
 * Resolve the framework root — where the Bindings/ tree actually lives.
 * Mirrors the dispatcher's three-tier resolution (Phase 09; see
 * Tools/nissth-bridge/dispatcher.js findFrameworkRoot, CLAUDE.md §11.15):
 *
 *   1. NISSTH_FRAMEWORK_ROOT env var (must contain a Bindings/ subdir;
 *      throws otherwise — invalid config).
 *   2. <repoRoot>/Tools/Nissth/ (submodule convention for consumer projects
 *      that install Nissth as a git submodule).
 *   3. <repoRoot> (fallback — Nissth's own dogfooding, where repoRoot and
 *      frameworkRoot coincide).
 *
 * Reports still go to <repoRoot>/AgentReports/Bridge/. Only schema lookups
 * and other framework-relative file accesses route through this function.
 * Added by Phase 09.5 — Phase 09 introduced this split in the dispatcher
 * but didn't extend it to the binding layer; consumer-side invocations
 * with NISSTH_FRAMEWORK_ROOT set failed schema loads until this landed.
 */
export function findFrameworkRoot(repoRoot: string): string {
  const fromEnv = process.env[FRAMEWORK_ENV_VAR];
  if (typeof fromEnv === "string" && fromEnv.length > 0) {
    const abs = resolve(fromEnv);
    const bindingsDir = join(abs, BINDINGS_DIR_NAME);
    try {
      if (statSync(bindingsDir).isDirectory()) return abs;
    } catch {
      // fall through to throw
    }
    throw new Error(
      `${FRAMEWORK_ENV_VAR}='${fromEnv}' does not contain a ${BINDINGS_DIR_NAME}/ subdirectory. ` +
        `Set it to the absolute path of a Nissth checkout (the directory that holds CLAUDE.md + ${BINDINGS_DIR_NAME}/).`
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
