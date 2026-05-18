import { statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";

const ROOT_MARKER = "CLAUDE.md";

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
