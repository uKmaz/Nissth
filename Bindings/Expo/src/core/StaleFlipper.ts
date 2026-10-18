import { readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parse as yamlParse } from "yaml";

export type DriftCheck = (
  dblFrontmatter: Record<string, unknown>,
  dblBody: string
) => boolean;

export interface DBLArtifact {
  path: string;
  frontmatter: Record<string, unknown>;
  body: string;
}

const FRONTMATTER_REGEX = /^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/;

export class StaleFlipper {
  constructor(private readonly repoRoot: string) {}

  /**
   * Scan a DBL sub-directory for artifacts whose `covers` overlaps the given scope path,
   * run the drift-check callback against each, and STALE-flip any that drift.
   * Returns the list of flipped artifact absolute paths.
   * Idempotent: artifacts already STALE are skipped.
   *
   * The drift check receives the artifact's own frontmatter, so a lens can (and should)
   * narrow its evidence to what lives under that artifact's `covers` — flipping an
   * artifact because of a file it does not cover is a false positive (Phase 19).
   */
  flipIfStale(opts: {
    dblSubdir: string;
    scopePath: string;
    driftCheck: DriftCheck;
    reportFileName: string;
  }): string[] {
    const dblDir = join(this.repoRoot, "DBL", opts.dblSubdir);
    let entries: string[];
    try {
      entries = readdirSync(dblDir);
    } catch {
      return [];
    }

    const flipped: string[] = [];
    for (const entry of entries) {
      if (!entry.endsWith(".md")) continue;
      const fullPath = join(dblDir, entry);
      try {
        if (!statSync(fullPath).isFile()) continue;
      } catch {
        continue;
      }

      const artifact = StaleFlipper.readArtifact(fullPath);
      if (!artifact) continue;

      const covers = artifact.frontmatter.covers;
      if (!Array.isArray(covers)) continue;
      const coversArr = covers as string[];

      const overlaps = coversArr.some((c) => StaleFlipper.scopeOverlaps(c, opts.scopePath));
      if (!overlaps) continue;

      const lastRegenerated = String(artifact.frontmatter.last_regenerated ?? "");
      if (lastRegenerated.startsWith("STALE")) continue;

      if (opts.driftCheck(artifact.frontmatter, artifact.body)) {
        const marker = `STALE — superseded by AgentReports/Bridge/${opts.reportFileName}`;
        if (StaleFlipper.setLastRegenerated(fullPath, marker)) flipped.push(fullPath);
      }
    }
    return flipped;
  }

  static readArtifact(path: string): DBLArtifact | null {
    let text: string;
    try {
      text = readFileSync(path, "utf8");
    } catch {
      return null;
    }
    const match = text.match(FRONTMATTER_REGEX);
    if (!match) return null;
    const yaml = match[1];
    const body = match[2];
    try {
      const parsed = yamlParse(yaml);
      if (parsed == null || typeof parsed !== "object") return null;
      return {
        path,
        frontmatter: parsed as Record<string, unknown>,
        body,
      };
    } catch {
      return null;
    }
  }

  /**
   * Replace the artifact's `last_regenerated` value **in place**, as a line edit on the raw
   * text. Every other byte of the file — key order, line wrapping, quoting, line endings —
   * is preserved. (Re-serialising the frontmatter through a YAML writer re-wraps long
   * `name:` / `stale_when:` lines at 80 columns, which `Tools/dbl-check` then reports as
   * `bad-frontmatter`: the Bridge breaking the artifact it just flipped. Phase 19.)
   *
   * Returns false when the file cannot be read, has no frontmatter, or cannot be written.
   */
  static setLastRegenerated(path: string, value: string): boolean {
    let text: string;
    try {
      text = readFileSync(path, "utf8");
    } catch {
      return false;
    }
    const match = text.match(FRONTMATTER_REGEX);
    if (!match) return false;

    const blockStart = text.indexOf("\n") + 1; // just after the opening `---` line
    const block = match[1];
    const blockEnd = blockStart + block.length;

    const eol = block.includes("\r\n") ? "\r\n" : "\n";
    const lines = block.split(/\r?\n/);

    let keyIndex = -1;
    for (let i = 0; i < lines.length; i++) {
      if (/^last_regenerated\s*:/.test(lines[i])) {
        keyIndex = i;
        break;
      }
    }

    const replacement = `last_regenerated: ${value}`;
    if (keyIndex === -1) {
      lines.push(replacement);
    } else {
      // a folded / continued value occupies the following indented, non-key lines
      let end = keyIndex + 1;
      while (end < lines.length && /^\s+\S/.test(lines[end])) end++;
      lines.splice(keyIndex, end - keyIndex, replacement);
    }

    const newBlock = lines.join(eol);
    try {
      writeFileSync(path, text.slice(0, blockStart) + newBlock + text.slice(blockEnd), "utf8");
    } catch {
      return false;
    }
    return true;
  }

  /**
   * Lightweight overlap test: substring match in either direction, plus minimal glob support.
   * `covers` patterns in DBL artifacts are typically directory paths or simple globs.
   * Used to decide whether an artifact is in the *scanned scope* at all.
   */
  static scopeOverlaps(coverPattern: string, scopePath: string): boolean {
    if (!coverPattern || !scopePath) return false;
    if (coverPattern === scopePath) return true;
    if (scopePath.startsWith(coverPattern)) return true;
    if (coverPattern.startsWith(scopePath)) return true;
    if (coverPattern.includes("*")) {
      return StaleFlipper.globMatch(coverPattern, scopePath);
    }
    return false;
  }

  /**
   * Does this `covers` pattern actually contain that file? Stricter than `scopeOverlaps`:
   * a pattern covers paths *under* it, never its parents. This is the test a lens uses to
   * narrow its evidence to one artifact (Phase 19 C1).
   */
  static coversPath(coverPattern: string, filePath: string): boolean {
    if (!coverPattern || !filePath) return false;
    const pattern = coverPattern.replace(/\\/g, "/").replace(/^\.\//, "");
    const file = filePath.replace(/\\/g, "/").replace(/^\.\//, "");
    if (pattern === file) return true;
    if (pattern.includes("*")) return StaleFlipper.globMatch(pattern, file);
    const dir = pattern.endsWith("/") ? pattern : `${pattern}/`;
    return file.startsWith(dir);
  }

  /**
   * The fixed part of a `covers` pattern, before the first wildcard: `src/ui/**` → `src/ui/`.
   * Used to tell a per-module artifact from a whole-tree catch-all (Phase 19 C1).
   */
  static literalPrefix(pattern: string): string {
    const p = pattern.replace(/\\/g, "/").replace(/^\.\//, "");
    const star = p.indexOf("*");
    return star === -1 ? p : p.slice(0, star);
  }

  static globMatch(pattern: string, candidate: string): boolean {
    const escape = (s: string): string => s.replace(/[.+^${}()|[\]\\]/g, "\\$&");
    let re = "";
    let i = 0;
    while (i < pattern.length) {
      if (pattern.startsWith("**/", i)) {
        re += "(?:.*/)?"; // zero or more directories
        i += 3;
      } else if (pattern.startsWith("**", i)) {
        re += ".*";
        i += 2;
      } else if (pattern[i] === "*") {
        re += "[^/]*";
        i += 1;
      } else {
        re += escape(pattern[i]);
        i += 1;
      }
    }
    return new RegExp(`^${re}$`).test(candidate);
  }
}
