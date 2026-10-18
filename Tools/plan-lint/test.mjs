import { test, after } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { lint, lintPlan, parsePlan, pathsIn, stepTargets, boundaryRules, ConfigError } from "./lint.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const CLI = join(HERE, "lint.mjs");
const REPO = resolve(HERE, "..", "..");

const temps = [];
function tmp() {
  const d = mkdtempSync(join(tmpdir(), "nissth-plan-lint-"));
  mkdirSync(join(d, "ImplementationPlans"), { recursive: true });
  temps.push(d);
  return d;
}
after(() => {
  for (const d of temps) rmSync(d, { recursive: true, force: true });
});

const run = (args) => {
  const r = spawnSync(process.execPath, [CLI, ...args], { encoding: "utf8" });
  return { code: r.status, stdout: r.stdout, stderr: r.stderr };
};
const checks = (r) => r.findings.map((f) => f.check).sort();

/** A minimal plan that passes every check; `over` replaces whole sections. */
function PLAN(over = {}) {
  const s = {
    0: [
      "- **Plan ID:** Phase_01_Thing",
      "- **Authored:** 2026-09-25 by Someone",
      "- **Approved:** 2026-09-25 by user",
      "- **Depends on:** none",
      "- **Estimated scope:** small",
    ].join("\n"),
    1: "### 1.1 Inputs to read\n\n- **DBL:** none\n\n### 1.2 Diagnostic actions\n\n| # |\n|:--|\n",
    2: "| Target | Property |\n|:--|:--|\n",
    3: "### 3.1 Step list\n\n- [ ] **Step 1.** Do it. **File:** `src/thing.ts`. **Operation:** add.\n\n### 3.2 Forbidden in this phase\n\n- not the other thing\n",
    4: "### 4.1 Freshness guarantee\n\n- fresh\n",
    5: "- [ ] tidy up\n",
    6: "```\nentry\n```\n",
    ...over,
  };
  const titles = ["Metadata", "Pre-Flight Diagnostic (REPORT)", "Expected State", "Execution (EXECUTE)", "Post-Flight Verification (VERIFY)", "Cleanup", "Status Update Entry"];
  return titles.map((t, i) => (s[i] === null ? "" : `## ${i}. ${t}\n\n${s[i]}\n`)).join("\n");
}

function write(root, name, text) {
  writeFileSync(join(root, "ImplementationPlans", name), text, "utf8");
  return `ImplementationPlans/${name}`;
}

function writeMap(root, name, frontmatterCovers, rulesBlock) {
  mkdirSync(join(root, "DBL", "DependencyMaps"), { recursive: true });
  const text = [
    "---",
    "artifact_type: dependency_map",
    "name: Layers",
    "last_regenerated: 2026-09-25 by Someone",
    "source_state: abc1234",
    "covers:",
    ...frontmatterCovers.map((c) => `  - ${c}`),
    "stale_when:",
    "  - anything moves",
    "---",
    "",
    "# Layers",
    "",
    "## Boundary rules (forbidden imports)",
    rulesBlock,
    "",
    "## Module-to-module relationships",
    "| From | To |",
    "|:--|:--|",
    "| a | b |",
  ].join("\n");
  writeFileSync(join(root, "DBL", "DependencyMaps", name), text, "utf8");
}

// --- parsing -----------------------------------------------------------------

test("pathsIn takes backticked paths and leaves prose alone", () => {
  const got = pathsIn("edit `src/a/b.ts:12-30` and `Tools/x/y.mjs`, not `a — b` or `word` or `https://x/y`");
  assert.deepEqual(got.sort(), ["Tools/x/y.mjs", "src/a/b.ts"]);
});

test("stepTargets reads both the **File:** marker and a bare step line", () => {
  const p = parsePlan(PLAN({ 3: [
    "### 3.1 Step list",
    "",
    "- [ ] **Step 1.** A. **File:** `src/one.ts`. **Operation:** add. **Acceptance:** `not/a/target.md` is irrelevant prose",
    "- [x] **Step 2.** Rewrite `src/two.ts` in place with no marker at all.",
    "- not a step line, `src/three.ts` ignored",
    "",
    "### 3.2 Forbidden in this phase",
    "",
    "- nothing",
  ].join("\n") }));
  const t = stepTargets(p);
  assert.ok(t.includes("src/one.ts"), t.join());
  assert.ok(t.includes("src/two.ts"), t.join());
  assert.ok(!t.includes("src/three.ts"), "a non-step line is not a target");
  // The **File:** clause wins, so the Acceptance prose path is not a target.
  assert.ok(!t.includes("not/a/target.md"), t.join());
});

test("boundaryRules reads both the template's bullets and the table consumers write", () => {
  assert.deepEqual(
    boundaryRules("## Boundary rules\n- `A` MUST NOT import from `B` — reason: layering\n"),
    ["`A` MUST NOT import from `B` — reason: layering"]
  );
  const table = [
    "## Boundary rules (forbidden imports)",
    "| MUST NOT | Reason | Enforcement |",
    "|:---|:---|:---|",
    "| `Core` → `App` | Core is headless | a test |",
    "",
    "## Next section",
    "| not | a rule |",
  ].join("\n");
  assert.deepEqual(boundaryRules(table), ["`Core` → `App` — Core is headless"]);
  assert.deepEqual(boundaryRules("## Boundary rules\n- no enforced boundaries yet\n"), []);
  assert.deepEqual(boundaryRules("# no such section"), []);
});

// --- structural checks --------------------------------------------------------

test("a well-formed plan produces no findings", () => {
  const d = tmp();
  const rel = write(d, "Phase_01_Thing.md", PLAN());
  assert.deepEqual(checks(lintPlan(d, rel)), []);
});

test("missing sections, a mismatched Plan ID, and an empty §3.2 each fire", () => {
  const d = tmp();
  const rel = write(d, "Phase_01_Thing.md", PLAN({ 4: null, 5: null, 0: "- **Plan ID:** Phase_99_Other\n- **Approved:** 2026-09-25 by user\n- **Depends on:** none", 3: "### 3.1 Step list\n\n- [ ] **Step 1.** x. **File:** `a/b.ts`.\n\n### 3.2 Forbidden in this phase\n\n(nothing yet)\n" }));
  const got = checks(lintPlan(d, rel));
  assert.deepEqual(got, ["empty-forbidden", "missing-section", "missing-section", "plan-id-mismatch"]);
});

test("Approved: pending is info, a malformed Approved: is an error", () => {
  const d = tmp();
  const pending = write(d, "Phase_01_Thing.md", PLAN({ 0: "- **Plan ID:** Phase_01_Thing\n- **Approved:** pending\n- **Depends on:** none" }));
  assert.deepEqual(checks(lintPlan(d, pending)), ["approved-pending"]);
  const bad = write(d, "Phase_02_Thing.md", PLAN({ 0: "- **Plan ID:** Phase_02_Thing\n- **Approved:** soon\n- **Depends on:** none" }));
  assert.deepEqual(checks(lintPlan(d, bad)), ["bad-approved"]);
});

test("Depends on resolves loosely: a short id and a reserved wildcard both count", () => {
  const d = tmp();
  write(d, "Phase_07_Real_Name.md", PLAN({ 0: "- **Plan ID:** Phase_07_Real_Name\n- **Approved:** 2026-09-25 by user\n- **Depends on:** none" }));
  const rel = write(d, "Phase_08_Thing.md", PLAN({ 0: "- **Plan ID:** Phase_08_Thing\n- **Approved:** 2026-09-25 by user\n- **Depends on:** Phase_07 (short form), `Phase_07_*` (the reserved number)" }));
  assert.deepEqual(checks(lintPlan(d, rel)), []);
  const bad = write(d, "Phase_09_Thing.md", PLAN({ 0: "- **Plan ID:** Phase_09_Thing\n- **Approved:** 2026-09-25 by user\n- **Depends on:** Phase_42_Nope" }));
  assert.deepEqual(checks(lintPlan(d, bad)), ["unknown-dependency"]);
});

test("a cited artifact that does not exist is an error; one that exists is not", () => {
  const d = tmp();
  const rel = write(d, "Phase_01_Thing.md", PLAN({ 1: "### 1.1 Inputs to read\n\n- **DBL:** `DBL/Summaries/gone.md`\n" }));
  assert.deepEqual(checks(lintPlan(d, rel)), ["missing-cited-artifact"]);
  mkdirSync(join(d, "DBL", "Summaries"), { recursive: true });
  writeFileSync(join(d, "DBL", "Summaries", "gone.md"), "x", "utf8");
  assert.deepEqual(checks(lintPlan(d, rel)), []);
});

// --- the load-bearing check ---------------------------------------------------

test("dependency-map-not-cited: fires on a covered target, silent once cited", () => {
  const d = tmp();
  writeMap(d, "layers.md", ["src/**"], "| MUST NOT | Reason |\n|:--|:--|\n| `Core` → `App` | headless |");
  const rel = write(d, "Phase_01_Thing.md", PLAN()); // §3 targets src/thing.ts, §1.1 says "none"
  const r = lintPlan(d, rel);
  assert.deepEqual(checks(r), ["dependency-map-not-cited"]);
  assert.match(r.findings[0].message, /src\/thing\.ts/);
  assert.match(r.findings[0].message, /1 boundary rule/);
  assert.match(r.findings[0].message, /`Core` → `App`/);

  const cited = write(d, "Phase_02_Thing.md", PLAN({
    0: "- **Plan ID:** Phase_02_Thing\n- **Approved:** 2026-09-25 by user\n- **Depends on:** none",
    1: "### 1.1 Inputs to read\n\n- **DBL:** `DBL/DependencyMaps/layers.md` — the boundary this phase works inside\n",
  }));
  assert.deepEqual(checks(lintPlan(d, cited)), []);
});

test("dependency-map-not-cited stays quiet when the map states no rules, or covers nothing the plan touches", () => {
  const d = tmp();
  writeMap(d, "layers.md", ["src/**"], "- no enforced boundaries yet");
  const rel = write(d, "Phase_01_Thing.md", PLAN());
  assert.deepEqual(checks(lintPlan(d, rel)), []);

  const e = tmp();
  writeMap(e, "layers.md", ["docs/**"], "| MUST NOT | Reason |\n|:--|:--|\n| `A` → `B` | x |");
  const rel2 = write(e, "Phase_01_Thing.md", PLAN());
  assert.deepEqual(checks(lintPlan(e, rel2)), []);
});

test("a waiver silences one named check and nothing else", () => {
  const d = tmp();
  writeMap(d, "layers.md", ["src/**"], "| MUST NOT | Reason |\n|:--|:--|\n| `Core` → `App` | headless |");
  const rel = write(d, "Phase_01_Thing.md",
    PLAN({ 0: "- **Plan ID:** Phase_99_Wrong\n- **Approved:** 2026-09-25 by user\n- **Depends on:** none" }) +
    "\n<!-- plan-lint:allow dependency-map-not-cited - the targets are in another repo -->\n");
  assert.deepEqual(checks(lintPlan(d, rel)), ["plan-id-mismatch"]);
});

// --- CLI + the real corpus ----------------------------------------------------

test("CLI: clean exits 0, findings exit 1, --json parses, bad root exits 2", () => {
  const d = tmp();
  write(d, "Phase_01_Thing.md", PLAN());
  assert.equal(run(["--root", d]).code, 0);

  write(d, "Phase_02_Bad.md", PLAN({ 0: "- **Plan ID:** Phase_77_Nope\n- **Approved:** 2026-09-25 by user\n- **Depends on:** none" }));
  const bad = run(["--root", d]);
  assert.equal(bad.code, 1);
  assert.match(bad.stdout, /plan-id-mismatch/);
  assert.match(bad.stdout, /2 plan\(s\) scanned/);

  const json = JSON.parse(run(["--root", d, "--json"]).stdout);
  assert.equal(json.scanned, 2);
  assert.equal(json.summary.error, 1);

  assert.equal(run(["--root", join(d, "nope")]).code, 2);
  assert.equal(run(["--root"]).code, 2);
  assert.equal(run(["--bogus"]).code, 2);
  assert.equal(run(["--help"]).code, 0);
});

test("--plan lints one plan, and --strict fails on an info-only plan", () => {
  const d = tmp();
  write(d, "Phase_01_Thing.md", PLAN());
  const rel = write(d, "Phase_02_Pending.md", PLAN({ 0: "- **Plan ID:** Phase_02_Pending\n- **Approved:** pending\n- **Depends on:** none" }));
  assert.equal(run(["--root", d, "--plan", rel]).code, 0);
  assert.match(run(["--root", d, "--plan", rel]).stdout, /approved-pending/);
  assert.equal(run(["--root", d, "--plan", rel, "--strict"]).code, 1);
  assert.equal(run(["--root", d, "--plan", "ImplementationPlans/Phase_01_Thing.md", "--strict"]).code, 0);
});

// This is the assertion that matters: a linter that only passes its own fixtures
// is the fixture problem phases 19 and 23 both recorded.
test("this repository's own plans lint clean", () => {
  const r = lint(REPO);
  assert.ok(r.scanned >= 23, `expected the real plan corpus, scanned ${r.scanned}`);
  assert.equal(r.summary.error, 0, JSON.stringify(r.findings.filter((f) => f.severity === "error"), null, 1));
});
