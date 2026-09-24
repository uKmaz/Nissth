import { test, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { due, worksheet, inventory, stamp, RegenError } from "./regen.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const CLI = join(HERE, "regen.mjs");
const REPO = resolve(HERE, "..", "..");

const temps = [];
after(() => {
  for (const d of temps) rmSync(d, { recursive: true, force: true });
});

const run = (args) => {
  const r = spawnSync(process.execPath, [CLI, ...args], { encoding: "utf8" });
  return { code: r.status, stdout: r.stdout, stderr: r.stderr };
};

function artifact(over = {}) {
  const d = {
    artifact_type: "summary",
    name: "A module whose name is long enough that a YAML re-serialiser would be tempted to fold it across two lines",
    last_regenerated: "2026-09-01 by Someone",
    source_state: "PLACEHOLDER",
    covers: ["src/**"],
    stale_when: ["a public export changes", "a file is added or removed"],
    ...over,
  };
  const L = ["---"];
  for (const [k, v] of Object.entries(d)) {
    if (Array.isArray(v)) {
      L.push(`${k}:`);
      for (const i of v) L.push(`  - ${i}`);
    } else L.push(`${k}: ${v}`);
  }
  L.push("---", "", "# A", "", "Body text.", "");
  return L.join("\n");
}

/** A real git repo with one commit, a covered source file, and one artifact. */
function repo({ source = "export const a = 1;\n", over = {} } = {}) {
  const root = mkdtempSync(join(tmpdir(), "nissth-regen-"));
  temps.push(root);
  const g = (...a) => execFileSync("git", a, { cwd: root, stdio: "ignore" });
  g("init", "-q");
  g("config", "user.email", "t@example.com");
  g("config", "user.name", "T");
  mkdirSync(join(root, "src"), { recursive: true });
  writeFileSync(join(root, "src", "a.ts"), source);
  mkdirSync(join(root, "DBL", "Summaries"), { recursive: true });
  g("add", "-A");
  g("commit", "-q", "-m", "one");
  const sha = execFileSync("git", ["rev-parse", "--short", "HEAD"], { cwd: root, encoding: "utf8" }).trim();
  writeFileSync(join(root, "DBL", "Summaries", "a.md"), artifact({ source_state: sha, ...over }));
  g("add", "-A");
  g("commit", "-q", "-m", "artifact");
  return { root, sha, g };
}

function touchSource(root, g) {
  writeFileSync(join(root, "src", "a.ts"), "export const a = 2;\nexport const b = 3;\n");
  writeFileSync(join(root, "src", "new.ts"), "export const n = 1;\n");
  g("add", "-A");
  g("commit", "-q", "-m", "change the covered source");
}

// --- inventory ----------------------------------------------------------------

test("inventory lists what is under covers now, and never DBL/ itself", () => {
  const { root } = repo();
  writeFileSync(join(root, "unrelated.md"), "x");
  const files = inventory(root, ["src/**"]);
  assert.deepEqual(files, ["src/a.ts"]);
  assert.ok(!inventory(root, ["**"]).some((f) => f.startsWith("DBL/")));
});

// --- due ----------------------------------------------------------------------

test("nothing is due when the covered source has not moved", () => {
  const { root } = repo();
  assert.deepEqual(due(root), []);
  assert.equal(run(["--root", root]).code, 0);
  assert.match(run(["--root", root]).stdout, /nothing due/);
});

test("a changed covered file makes the artifact due", () => {
  const { root, g } = repo();
  touchSource(root, g);
  const list = due(root);
  assert.equal(list.length, 1);
  assert.equal(list[0].rel, "DBL/Summaries/a.md");
  assert.equal(list[0].reasons[0].check, "covers-changed-since");
  const cli = run(["--root", root]);
  assert.equal(cli.code, 1, "due artifacts exit 1 so a sweep can gate on it");
  assert.match(cli.stdout, /1 artifact\(s\) due/);
});

test("a STALE flip and a source_state with no ref both count as due", () => {
  const { root } = repo({ over: { last_regenerated: "STALE — superseded by AgentReports/Bridge/x.md" } });
  assert.equal(due(root)[0].reasons[0].check, "stale-marked");

  const b = repo({ over: { source_state: "Phase 06 close" } });
  assert.equal(due(b.root)[0].reasons[0].check, "unrecognised-source-state");
});

// --- worksheet ----------------------------------------------------------------

test("the worksheet names the diff, the current inventory, the stale_when list and the lens", () => {
  const { root, g, sha } = repo();
  touchSource(root, g);
  const w = worksheet(root, "DBL/Summaries/a.md");
  assert.match(w, /# Regeneration worksheet — `DBL\/Summaries\/a\.md`/);
  assert.match(w, new RegExp(`ref \`${sha}\``));
  assert.match(w, /2 file\(s\): 1 added, 1 modified, 0 deleted/);
  assert.match(w, /\| A \| `src\/new\.ts` \|/);
  assert.match(w, /\| M \| `src\/a\.ts` \|/);
  assert.match(w, /## What is under `covers` now\n\n2 file\(s\)/);
  assert.match(w, /- \[ \] a public export changes/);
  assert.match(w, /## Surface/);
  assert.match(w, /last_regenerated: \d{4}-\d{2}-\d{2} by <you>/);
  assert.match(w, /--stamp DBL\/Summaries\/a\.md/);
});

test("the worksheet is honest when there is no ref to diff against", () => {
  const { root } = repo({ over: { source_state: "design-only — SDD §4 approved 2026-09-01; no source yet" } });
  const w = worksheet(root, "DBL/Summaries/a.md");
  assert.match(w, /no git ref in it/);
  assert.match(w, /nothing to diff against/);
});

test("worksheet refuses an artifact that is missing or has no frontmatter", () => {
  const { root } = repo();
  assert.throws(() => worksheet(root, "DBL/Summaries/nope.md"), (e) => e instanceof RegenError && e.code === "no_artifact");
  writeFileSync(join(root, "DBL", "Summaries", "raw.md"), "# no frontmatter\n");
  assert.throws(() => worksheet(root, "DBL/Summaries/raw.md"), (e) => e instanceof RegenError && e.code === "no_frontmatter");
  assert.equal(run(["--root", root, "--artifact", "DBL/Summaries/nope.md"]).code, 2);
});

// --- stamp --------------------------------------------------------------------

test("stamp refuses while the body is unchanged — that is the whole contract", () => {
  const { root, g } = repo();
  touchSource(root, g);
  assert.throws(() => stamp(root, "DBL/Summaries/a.md"), (e) => e instanceof RegenError && e.code === "body_unchanged");
  const cli = run(["--root", root, "--stamp", "DBL/Summaries/a.md"]);
  assert.equal(cli.code, 2);
  assert.match(cli.stderr, /unmodified in the working tree/);
});

test("stamp writes exactly two lines and preserves every other byte", () => {
  const { root, g } = repo();
  touchSource(root, g);
  const file = join(root, "DBL", "Summaries", "a.md");
  const before = readFileSync(file, "utf8");
  writeFileSync(file, before.replace("Body text.", "Body text, rewritten by a human."), "utf8");
  const edited = readFileSync(file, "utf8");

  const r = stamp(root, "DBL/Summaries/a.md", { by: "Someone" });
  assert.equal(r.changed, true);
  const after = readFileSync(file, "utf8");

  const a = edited.split("\n");
  const b = after.split("\n");
  assert.equal(a.length, b.length, "no line was added or removed");
  const differing = a.map((l, i) => (l === b[i] ? null : i)).filter((i) => i !== null);
  assert.equal(differing.length, 2, `expected exactly two changed lines, got ${JSON.stringify(differing.map((i) => [a[i], b[i]]))}`);
  assert.match(b[differing[0]], /^last_regenerated: \d{4}-\d{2}-\d{2} by Someone$/);
  assert.match(b[differing[1]], /^source_state: [0-9a-f]{7,40}$/);
  // The long `name:` line is the one a YAML re-serialiser would have folded (Phase 19).
  assert.ok(after.includes("a YAML re-serialiser would be tempted to fold it across two lines"));
});

test("stamp refuses outside a git work tree, and when the key is absent", () => {
  const plain = mkdtempSync(join(tmpdir(), "nissth-regen-nogit-"));
  temps.push(plain);
  mkdirSync(join(plain, "DBL", "Summaries"), { recursive: true });
  writeFileSync(join(plain, "DBL", "Summaries", "a.md"), artifact());
  assert.throws(() => stamp(plain, "DBL/Summaries/a.md"), (e) => e instanceof RegenError && e.code === "no_git");

  const { root, g } = repo();
  touchSource(root, g);
  const file = join(root, "DBL", "Summaries", "a.md");
  writeFileSync(file, readFileSync(file, "utf8").replace(/^source_state:.*$/m, "# gone"), "utf8");
  assert.throws(() => stamp(root, "DBL/Summaries/a.md"), (e) => e instanceof RegenError && e.code === "missing_key");
});

// --- CLI ----------------------------------------------------------------------

test("CLI: --json, --all, unknown flags, and this repository's own template-only DBL", () => {
  const { root, g } = repo();
  touchSource(root, g);
  const j = run(["--root", root, "--json"]);
  assert.equal(j.code, 1);
  assert.equal(JSON.parse(j.stdout).due.length, 1);

  const all = run(["--root", root, "--all"]);
  assert.equal(all.code, 1);
  assert.match(all.stdout, /Regeneration worksheet/);

  assert.equal(run(["--bogus"]).code, 2);
  assert.equal(run(["--root"]).code, 2);
  assert.equal(run(["--help"]).code, 0);

  // Nissth's own DBL is templates only, so the tool must be quiet here.
  const self = run(["--root", REPO]);
  assert.equal(self.code, 0);
  assert.match(self.stdout, /nothing due/);
});
