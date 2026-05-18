import { readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parse as yamlParse, stringify as yamlStringify } from "yaml";

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
        artifact.frontmatter.last_regenerated = `STALE — superseded by AgentReports/Bridge/${opts.reportFileName}`;
        StaleFlipper.writeArtifact(artifact);
        flipped.push(fullPath);
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

  static writeArtifact(a: DBLArtifact): void {
    const yaml = yamlStringify(a.frontmatter);
    const content = `---\n${yaml}---\n${a.body}`;
    writeFileSync(a.path, content, "utf8");
  }

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

  static globMatch(pattern: string, candidate: string): boolean {
    const re = pattern
      .replace(/[.+^${}()|[\]\\]/g, "\\$&")
      .replace(/\*\*/g, ".*")
      .replace(/\*/g, "[^/]*");
    return new RegExp(`^${re}$`).test(candidate);
  }
}
