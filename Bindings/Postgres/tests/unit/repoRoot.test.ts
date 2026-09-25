// Unit tests for Phase 09.5 — findFrameworkRoot three-tier resolution and
// ReportWriter two-root divergence wiring. Mirror of dispatcher's
// Tools/nissth-bridge/test.mjs cases; ensures the binding-side honors
// NISSTH_FRAMEWORK_ROOT for schema lookups while keeping repoRoot for
// AgentReports/Bridge/ writes.

import {
  mkdtempSync,
  rmSync,
  mkdirSync,
  writeFileSync,
  copyFileSync,
  readFileSync,
  statSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { findFrameworkRoot, findRepoRoot } from "../../src/core/repoRoot";
import { ReportWriter } from "../../src/core/ReportWriter";

const FRAMEWORK_ENV_VAR = "NISSTH_FRAMEWORK_ROOT";

function withEnv<T>(name: string, value: string | undefined, fn: () => T): T {
  const prev = process.env[name];
  if (value === undefined) {
    delete process.env[name];
  } else {
    process.env[name] = value;
  }
  try {
    return fn();
  } finally {
    if (prev === undefined) {
      delete process.env[name];
    } else {
      process.env[name] = prev;
    }
  }
}

function findRealRepoRoot(startDir: string): string {
  let dir = resolve(startDir);
  for (let i = 0; i < 32; i++) {
    try {
      if (statSync(join(dir, "CLAUDE.md")).isFile()) return dir;
    } catch {
      // ignore
    }
    const parent = dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error(`could not find real repo root from ${startDir}`);
}

describe("findFrameworkRoot — Phase 09.5 binding-side three-tier resolution", () => {
  let tmpFramework: string;
  let tmpRepo: string;
  let tmpSubmoduleRepo: string;

  beforeEach(() => {
    tmpFramework = mkdtempSync(join(tmpdir(), "nissth-fw-"));
    tmpRepo = mkdtempSync(join(tmpdir(), "nissth-repo-"));
    tmpSubmoduleRepo = mkdtempSync(join(tmpdir(), "nissth-submodule-"));

    // Framework root has Bindings/ subdir.
    mkdirSync(join(tmpFramework, "Bindings"), { recursive: true });
    // Submodule-style repo: <repoRoot>/Tools/Nissth/Bindings/
    mkdirSync(join(tmpSubmoduleRepo, "Tools", "Nissth", "Bindings"), {
      recursive: true,
    });
  });

  afterEach(() => {
    rmSync(tmpFramework, { recursive: true, force: true });
    rmSync(tmpRepo, { recursive: true, force: true });
    rmSync(tmpSubmoduleRepo, { recursive: true, force: true });
  });

  it("tier 1: NISSTH_FRAMEWORK_ROOT env var wins when path contains Bindings/", () => {
    withEnv(FRAMEWORK_ENV_VAR, tmpFramework, () => {
      expect(findFrameworkRoot(tmpRepo)).toBe(resolve(tmpFramework));
    });
  });

  it("tier 1 error: env var set but missing Bindings/ subdir throws", () => {
    const badFramework = mkdtempSync(join(tmpdir(), "nissth-bad-fw-"));
    try {
      withEnv(FRAMEWORK_ENV_VAR, badFramework, () => {
        expect(() => findFrameworkRoot(tmpRepo)).toThrow(/Bindings\//);
      });
    } finally {
      rmSync(badFramework, { recursive: true, force: true });
    }
  });

  it("tier 2: submodule convention <repoRoot>/Tools/Nissth/ wins when no env var", () => {
    withEnv(FRAMEWORK_ENV_VAR, undefined, () => {
      expect(findFrameworkRoot(tmpSubmoduleRepo)).toBe(
        join(tmpSubmoduleRepo, "Tools", "Nissth")
      );
    });
  });

  it("tier 3 fallback: no env var + no submodule → returns repoRoot itself", () => {
    withEnv(FRAMEWORK_ENV_VAR, undefined, () => {
      expect(findFrameworkRoot(tmpRepo)).toBe(tmpRepo);
    });
  });

  it("env var precedence over submodule: both present → env var wins", () => {
    withEnv(FRAMEWORK_ENV_VAR, tmpFramework, () => {
      expect(findFrameworkRoot(tmpSubmoduleRepo)).toBe(resolve(tmpFramework));
    });
  });
});

describe("ReportWriter — Phase 09.5 two-root divergence integration", () => {
  let tmpFramework: string;
  let tmpConsumer: string;

  beforeEach(() => {
    tmpFramework = mkdtempSync(join(tmpdir(), "nissth-fw-rw-"));
    tmpConsumer = mkdtempSync(join(tmpdir(), "nissth-cons-rw-"));

    // Consumer repo: CLAUDE.md only — NO Bindings/, NO _schemas/. Diverges from framework.
    writeFileSync(join(tmpConsumer, "CLAUDE.md"), "# consumer\n");

    // Framework: holds the canonical bridge-command.schema.json under Bindings/_schemas/.
    mkdirSync(join(tmpFramework, "Bindings", "_schemas"), { recursive: true });
    const realRoot = findRealRepoRoot(__dirname);
    copyFileSync(
      join(realRoot, "Bindings", "_schemas", "bridge-command.schema.json"),
      join(tmpFramework, "Bindings", "_schemas", "bridge-command.schema.json")
    );
  });

  afterEach(() => {
    rmSync(tmpFramework, { recursive: true, force: true });
    rmSync(tmpConsumer, { recursive: true, force: true });
  });

  it("schema validator loads from tmpFramework via NISSTH_FRAMEWORK_ROOT; report lands under tmpConsumer", () => {
    withEnv(FRAMEWORK_ENV_VAR, tmpFramework, () => {
      // ReportWriter's default loadFrontmatterValidator() now goes through
      // findFrameworkRoot(findRepoRoot()) — so the schema lookup honors env var.
      // The write() destination uses constructor-injected repoRoot = tmpConsumer.
      const writer = new ReportWriter({
        binding: "postgres",
        bindingVersion: "0.1.1",
        repoRoot: tmpConsumer,
      });
      const reportPath = writer.write({
        tool: "schema_lens",
        freshness: {
          source: "test fixture",
          source_state: "synthetic two-root",
          guarantee: "frameworkRoot diverges from repoRoot",
        },
        body: "## OK\n- two-root path exercised",
      });

      // Destination MUST be under the consumer repo, NOT the framework
      const consumerAbs = resolve(tmpConsumer);
      expect(reportPath.startsWith(consumerAbs)).toBe(true);
      expect(reportPath).toMatch(
        /AgentReports[\/\\]Bridge[\/\\]schema_lens_.*\.md$/
      );

      // Report content sane
      const content = readFileSync(reportPath, "utf8");
      expect(content).toContain("binding: postgres");
      expect(content).toContain("binding_version: 0.1.1");
      expect(content).toContain("tool: schema_lens");
    });
  });

  it("schema validator falls back to repoRoot when env var unset (Nissth-style dogfooding)", () => {
    // Without NISSTH_FRAMEWORK_ROOT, findFrameworkRoot falls through:
    //   tier 2 (submodule) — tmpConsumer has no Tools/Nissth/Bindings/, skip
    //   tier 3 (repoRoot fallback) — returns tmpConsumer
    // Schema lookup would then fail (no Bindings/_schemas/ in tmpConsumer).
    // We assert the error surfaces clearly — this is the OLD (pre-Phase-09.5)
    // behavior, preserved when the env var is intentionally unset.
    //
    // Note: findRepoRoot() walks from process.cwd(), which during this test
    // run is inside the framework checkout, so it actually finds the real
    // Nissth root. That means findFrameworkRoot returns the real Nissth root
    // via tier 3 fallback, and the schema loads successfully. This
    // demonstrates the dogfooding case: repoRoot === frameworkRoot.
    withEnv(FRAMEWORK_ENV_VAR, undefined, () => {
      const writer = new ReportWriter({
        binding: "postgres",
        bindingVersion: "0.1.1",
        repoRoot: tmpConsumer,
      });
      // No throw — schema resolves via dogfooding fallback. Sanity check only.
      const reportPath = writer.write({
        tool: "schema_lens",
        freshness: { source: "x", source_state: "y", guarantee: "z" },
        body: "ok",
      });
      expect(reportPath).toMatch(/schema_lens_.*\.md$/);
    });
  });
});

// Reference findRepoRoot to keep the import linter happy.
void findRepoRoot;
