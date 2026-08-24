// Shared helpers for Bindings/Expo integration tests.
// Each IT copies the in-repo fixture to a tmpdir so mutations
// (route_scaffold writes, StaleFlipper rewrites of DBL artifacts, etc.)
// don't pollute the canonical fixture between runs.

import {
  cpSync,
  mkdtempSync,
  readFileSync,
  rmSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { parse as yamlParse } from "yaml";
import { findRepoRoot } from "../../src/core/repoRoot";
import { ReportWriter } from "../../src/core/ReportWriter";
import { StaleFlipper } from "../../src/core/StaleFlipper";
import { BindingManifest } from "../../src/core/BindingManifest";
import { ToolDispatcher } from "../../src/core/ToolDispatcher";
import type { ToolHandler } from "../../src/core/types";

export const REPO_ROOT = findRepoRoot();
export const FIXTURE_ROOT = join(
  REPO_ROOT,
  "Bindings",
  "Expo",
  "tests",
  "fixture"
);

export function copyFixtureToTmp(prefix = "nissth-it-"): {
  tmpRoot: string;
  cleanup: () => void;
} {
  const tmpRoot = mkdtempSync(join(tmpdir(), prefix));
  cpSync(FIXTURE_ROOT, tmpRoot, { recursive: true });
  return {
    tmpRoot,
    cleanup: () => rmSync(tmpRoot, { recursive: true, force: true }),
  };
}

export function buildDispatcher(
  repoRoot: string,
  handlers: (writer: ReportWriter, flipper: StaleFlipper) => ToolHandler[]
): {
  dispatcher: ToolDispatcher;
  writer: ReportWriter;
  flipper: StaleFlipper;
} {
  const manifest = BindingManifest.load();
  const writer = new ReportWriter({
    binding: manifest.binding,
    bindingVersion: manifest.bindingVersion,
    repoRoot,
  });
  const flipper = new StaleFlipper(repoRoot);
  const dispatcher = new ToolDispatcher();
  for (const h of handlers(writer, flipper)) dispatcher.register(h);
  return { dispatcher, writer, flipper };
}

export function readReportFrontmatter(
  reportPath: string
): { frontmatter: Record<string, unknown>; body: string } | null {
  const text = readFileSync(reportPath, "utf8");
  const m = text.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/);
  if (!m) return null;
  return {
    frontmatter: yamlParse(m[1]) as Record<string, unknown>,
    body: m[2],
  };
}

export function readDBLFrontmatter(
  artifactPath: string
): Record<string, unknown> | null {
  try {
    const text = readFileSync(artifactPath, "utf8");
    const m = text.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
    if (!m) return null;
    return yamlParse(m[1]) as Record<string, unknown>;
  } catch {
    return null;
  }
}

export { dirname };
