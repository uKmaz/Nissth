import { test, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { check, checkArtifact, parseFrontmatter, globToRegExp, firstCoveredFile, budget, sourceRef, refless, ConfigError, WORD_BUDGET } from "./check.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const FIX = join(HERE, "_fixtures");
const CLI = join(HERE, "check.mjs");
// A live consumer checkout, when one is present on this host. Overridable because
// the framework runs on more than one machine and under more than one user name;
// the case skips rather than fails when the path is absent.
const CONSUMER = process.env.NISSTH_CONSUMER_ROOT || "<path>/ExampleFinanceApp";

const temps = [];
function tmp() {
  const d = mkdtempSync(join(tmpdir(), "nissth-dbl-check-"));
  temps.push(d);
  return d;
}
after(() => {
  for (const d of temps) rmSync(d, { recursive: true, force: true });
});

function run(args, opts = {}) {
  const r = spawnSync(process.execPath, [CLI, ...args], { encoding: "utf8", cwd: HERE, ...opts });
  return { code: r.status, stdout: r.stdout, stderr: r.stderr };
}
const checks = (r) => r.findings.map((f) => f.check).sort();

const FM = (over = {}) => {
  const d = {
    artifact_type: "summary",
    name: "X",
    last_regenerated: "2026-09-13 by test",
    source_state: '"uncommitted state at 2026-09-13 10:00"',
    covers: ["src/**"],
    stale_when: ["anything"],
    ...over,
  };
  const lines = ["---"];
  for (const [k, v] of Object.entries(d)) {
    if (v === undefined) continue;
    if (Array.isArray(v)) {
      lines.push(`${k}:`);
      for (const i of v) lines.push(`  - ${i}`);
    } else lines.push(`${k}: ${v}`);
  }
  lines.push("---", "", "# X", "", "Body.", "");
  return lines.join("\n");
};

function repo(files) {
  const root = tmp();
  for (const [rel, content] of Object.entries(files)) {
    mkdirSync(dirname(join(root, rel)), { recursive: true });
    writeFileSync(join(root, rel), content);
  }
  return root;
}

// ------------------------------------------------------------- units

test("parseFrontmatter reads scalars, lists, quotes, and free text with — and :", () => {
  const fm = parseFrontmatter('---\nname: A: b — c\nsource_state: "q: v"\ncovers:\n  - a/**\n  - b\n---\nbody');
  assert.deepEqual(fm.data, { name: "A: b — c", source_state: "q: v", covers: ["a/**", "b"] });
  assert.equal(fm.errors.length, 0);
  assert.equal(parseFrontmatter("# no fm\n"), null);
});

test("parseFrontmatter reports unparseable lines and an unclosed block", () => {
  const fm = parseFrontmatter("---\nname: A\n?? junk\n  - orphan\n");
  assert.deepEqual(fm.errors.map((e) => e.message.split(":")[0]), ["unparseable frontmatter line", "list item outside a list key", "frontmatter block never closed with `---`"]);
});

test("globToRegExp handles **, *, ?, plain paths and trailing slash", () => {
  const m = (g, p) => globToRegExp(g).test(p);
  assert.ok(m("src/**", "src/a/b.ts"));
  assert.ok(m("src/**", "src/x.ts"));
  assert.ok(!m("src/**", "srcx/x.ts"));
  assert.ok(m("app/**/*.tsx", "app/(tabs)/index.tsx"));
  assert.ok(m("app/**/*.tsx", "app/index.tsx"));
  assert.ok(!m("app/**/*.tsx", "app/index.ts"));
  assert.ok(m("app.json", "app.json"));
  assert.ok(!m("app.json", "xapp.json"));
  assert.ok(m("migrations/", "migrations/0001.sql"));
  assert.ok(m("src/db/schema.?s", "src/db/schema.ts"));
});

test("firstCoveredFile skips DBL/ itself and stops at the first hit", () => {
  const root = repo({ "DBL/Summaries/a.md": "x", "src/deep/y.ts": "y" });
  assert.equal(firstCoveredFile(root, ["DBL/**"]), null);
  assert.equal(firstCoveredFile(root, ["src/**"]), "src/deep/y.ts");
  assert.equal(firstCoveredFile(root, ["lib/**"]), null);
});

// ------------------------------------------------------------- fixtures

test("clean fixture: 4 artifacts, template ignored, 0 findings", () => {
  const r = check(join(FIX, "clean"));
  assert.equal(r.scanned, 4);
  assert.ok(!r.artifacts.some((a) => a.includes("_TEMPLATE")));
  assert.deepEqual(checks(r), []);
});

test("stale fixture: exactly one stale-marked info; exit 0, exit 1 under --strict", () => {
  const r = check(join(FIX, "stale"));
  assert.deepEqual(checks(r), ["stale-marked"]);
  assert.equal(r.findings[0].severity, "info");
  assert.match(r.findings[0].message, /route_lens_2026-09-13T1200Z\.md/);
  assert.equal(run(["--root", join(FIX, "stale")]).code, 0);
  assert.equal(run(["--root", join(FIX, "stale"), "--strict"]).code, 1);
});

test("design-only fixture: source under covers → error; absent → clean", () => {
  const r = check(join(FIX, "design-only"));
  assert.equal(r.scanned, 2);
  assert.deepEqual(checks(r), ["design-only-source-exists"]);
  assert.equal(r.findings[0].file, "DBL/Summaries/with-source.md");
  assert.match(r.findings[0].message, /src\/x\.ts/);
  assert.equal(run(["--root", join(FIX, "design-only")]).code, 1);
});

// ------------------------------------------------------------- broken cases (built at test time)

test("missing frontmatter", () => {
  const root = repo({ "DBL/Summaries/a.md": "# no frontmatter\n" });
  assert.deepEqual(checks(check(root)), ["missing-frontmatter"]);
});

test("missing keys — one finding per key, empty lists count as missing", () => {
  const root = repo({ "DBL/Summaries/a.md": FM({ name: undefined, stale_when: undefined }), "DBL/Summaries/b.md": FM({ covers: [] }) });
  const r = check(root);
  const a = r.findings.filter((f) => f.file.endsWith("a.md"));
  assert.deepEqual(a.map((f) => f.check), ["missing-key", "missing-key"]);
  assert.ok(a.some((f) => f.message.includes("`name`")) && a.some((f) => f.message.includes("`stale_when`")));
  const b = r.findings.filter((f) => f.file.endsWith("b.md"));
  assert.equal(b.length, 1);
  assert.match(b[0].message, /`covers` is empty/);
});

test("type/dir mismatch and unknown dir", () => {
  const root = repo({ "DBL/APIIndex/a.md": FM({ artifact_type: "summary" }), "DBL/Notes/n.md": FM() });
  const r = check(root);
  assert.deepEqual(checks(r), ["type-dir-mismatch", "unknown-dir"]);
  assert.equal(r.findings.find((f) => f.check === "unknown-dir").severity, "warn");
});

test("bad last_regenerated format is an error; valid date and STALE forms are not", () => {
  const root = repo({
    "DBL/Summaries/a.md": FM({ last_regenerated: "yesterday" }),
    "DBL/Summaries/b.md": FM({ last_regenerated: "2026-09-13 by Claude (Opus 5)" }),
    "DBL/Summaries/c.md": FM({ last_regenerated: "STALE — superseded by AgentReports/Bridge/x.md" }),
  });
  const r = check(root);
  assert.deepEqual(r.findings.map((f) => [f.file.slice(-4), f.check]), [["a.md", "bad-regenerated-format"], ["c.md", "stale-marked"]]);
});

test("CRLF is an error", () => {
  const root = repo({ "DBL/Summaries/a.md": FM().replace(/\n/g, "\r\n") });
  assert.ok(checks(check(root)).includes("crlf"));
});

test("over budget is a warn and does not fail the exit code", () => {
  const root = repo({ "DBL/Summaries/a.md": FM() + "word ".repeat(WORD_BUDGET + 10) });
  const r = check(root);
  assert.deepEqual(checks(r), ["over-budget"]);
  assert.equal(r.findings[0].severity, "warn");
  assert.equal(run(["--root", root]).code, 0);
  assert.equal(run(["--root", root, "--strict"]).code, 1);
});

test("nested artifact directories are scanned; _TEMPLATE.md excluded at any depth", () => {
  const root = repo({ "DBL/Summaries/sub/a.md": FM(), "DBL/Summaries/sub/_TEMPLATE.md": "x" });
  const r = check(root);
  assert.deepEqual(r.artifacts, ["DBL/Summaries/sub/a.md"]);
});

// ------------------------------------------------------------- git ref

function haveGit() {
  return spawnSync("git", ["--version"]).status === 0;
}

test("covers-changed-since: changed → warn, unchanged → clean", { skip: !haveGit() && "git not on PATH" }, () => {
  const root = repo({ "src/a.ts": "1\n", "other/b.ts": "1\n" });
  const git = (...a) => execFileSync("git", ["-C", root, ...a], { encoding: "utf8", env: { ...process.env, GIT_AUTHOR_NAME: "t", GIT_AUTHOR_EMAIL: "t@t", GIT_COMMITTER_NAME: "t", GIT_COMMITTER_EMAIL: "t@t" } });
  git("init", "-q");
  git("add", "-A");
  git("commit", "-q", "-m", "base");
  const sha = git("rev-parse", "HEAD").trim();
  mkdirSync(join(root, "DBL", "Summaries"), { recursive: true });
  writeFileSync(join(root, "DBL/Summaries/a.md"), FM({ source_state: sha, covers: ["src/**"] }));
  writeFileSync(join(root, "DBL/Summaries/b.md"), FM({ source_state: sha, covers: ["other/**"] }));
  writeFileSync(join(root, "src/a.ts"), "2\n"); // change only under a.md's covers
  const r = check(root);
  assert.deepEqual(r.findings.map((f) => [f.file.slice(-4), f.check, f.severity]), [["a.md", "covers-changed-since", "warn"]]);
  assert.match(r.findings[0].message, /src\/a\.ts/);
  assert.equal(run(["--root", root]).code, 0);
});

test("covers-changed-since: no git work tree → skip note, exit 0", () => {
  const root = repo({ "DBL/Summaries/a.md": FM({ source_state: "0123456789abcdef0123456789abcdef01234567" }) });
  const r = check(root);
  assert.deepEqual(checks(r), []);
  assert.equal(r.notes.length, 1);
  assert.match(r.notes[0], /skipped/);
});

// ------------------------------------------------------------- CLI

test("no DBL/ dir → exit 2; templates-only → exit 0 with message", () => {
  const r = run(["--root", tmp()]);
  assert.equal(r.code, 2);
  assert.match(r.stderr, /no DBL\/ directory/);
  assert.throws(() => check(tmp()), ConfigError);
  const root = repo({ "DBL/Summaries/_TEMPLATE.md": "x" });
  const t = run(["--root", root]);
  assert.equal(t.code, 0);
  assert.match(t.stdout, /templates only/);
});

test("--json shape; unknown flag → exit 2; --help → exit 0", () => {
  const j = JSON.parse(run(["--root", join(FIX, "clean"), "--json"]).stdout);
  for (const k of ["root", "scanned", "artifacts", "findings", "notes", "summary", "words"]) assert.ok(k in j, k);
  assert.deepEqual(j.summary, { error: 0, warn: 0, info: 0 });
  assert.equal(run(["--bogus"]).code, 2);
  assert.equal(run(["--help"]).code, 0);
});

test("text reporter groups findings by file and prints the summary line", () => {
  const r = run(["--root", join(FIX, "design-only")]);
  assert.equal(r.code, 1);
  assert.match(r.stdout, /^DBL\/Summaries\/with-source\.md\n  \[error\] design-only-source-exists/m);
  assert.match(r.stdout, /2 artifact\(s\) scanned — 1 error, 0 warn, 0 info/);
});

test("Nissth's own DBL is templates only", () => {
  const r = check(resolve(HERE, "..", ".."));
  assert.equal(r.scanned, 0);
});

// Asserts only what this repository owns: every artifact in a live consumer
// *parses* and satisfies the frontmatter contract. Not how many there are — that
// assertion read 11, the consumer grew to 17, and the suite went red for a change
// in another repo (Phase 21). And not that it is free of warnings either: Phase 25
// taught the freshness check to fire on annotated refs, four of that consumer's
// artifacts turned out to be genuinely stale, and this case went red again for a
// fact about someone else's project. An `error` here means dbl-check or the
// contract is wrong; a `warn` means the consumer has regeneration to do.
test("a live consumer checkout satisfies the frontmatter contract", { skip: !existsSync(join(CONSUMER, "DBL")) && "consumer checkout not present" }, () => {
  const r = check(CONSUMER);
  assert.ok(r.scanned > 0, `expected artifacts under ${CONSUMER}/DBL`);
  const errors = r.findings.filter((x) => x.severity === "error");
  assert.deepEqual(errors, [], JSON.stringify(errors, null, 1));
});

test("budget: counts words against WORD_BUDGET and flags over", () => {
  const d = tmp();
  const under = join(d, "under.md");
  const over = join(d, "over.md");
  writeFileSync(under, "word ".repeat(10), "utf8");
  writeFileSync(over, "word ".repeat(WORD_BUDGET + 5), "utf8");

  const u = budget(under);
  assert.equal(u.words, 10);
  assert.equal(u.budget, WORD_BUDGET);
  assert.equal(u.over, false);

  const o = budget(over);
  assert.equal(o.words, WORD_BUDGET + 5);
  assert.equal(o.over, true);

  assert.throws(() => budget(join(d, "nope.md")), ConfigError);
  assert.throws(() => budget(d), ConfigError);
});

test("budget agrees with the over-budget finding on the same file", () => {
  const d = tmp();
  mkdirSync(join(d, "DBL", "Summaries"), { recursive: true });
  const rel = "DBL/Summaries/big.md";
  writeFileSync(join(d, rel), FM() + "\n" + "word ".repeat(WORD_BUDGET + 1), "utf8");
  const scan = check(d);
  const over = scan.findings.find((f) => f.check === "over-budget");
  assert.ok(over, "expected an over-budget finding");
  assert.equal(budget(join(d, rel)).words, scan.words[rel]);
});

test("--budget CLI: under → 0, over → 1, repeatable, --json, bad path → 2", () => {
  const d = tmp();
  const under = join(d, "under.md");
  const over = join(d, "over.md");
  writeFileSync(under, "word ".repeat(10), "utf8");
  writeFileSync(over, "word ".repeat(WORD_BUDGET + 5), "utf8");

  const ok = run(["--budget", under]);
  assert.equal(ok.code, 0);
  assert.match(ok.stdout, /10 words \/ 1100 — ok, 1090 to spare/);

  const bad = run(["--budget", over]);
  assert.equal(bad.code, 1);
  assert.match(bad.stdout, /OVER by 5/);
  assert.match(bad.stdout, /§7\.4/);

  const both = run(["--budget", under, "--budget", over]);
  assert.equal(both.code, 1);
  assert.equal(both.stdout.split("\n").filter((l) => l.includes("words /")).length, 2);

  const json = run(["--budget", under, "--json"]);
  assert.equal(json.code, 0);
  const parsed = JSON.parse(json.stdout);
  assert.equal(parsed.budget, WORD_BUDGET);
  assert.equal(parsed.files[0].words, 10);
  assert.equal(parsed.files[0].over, false);

  assert.equal(run(["--budget", join(d, "nope.md")]).code, 2);
  assert.equal(run(["--budget"]).code, 2);
  assert.equal(run(["--budget", "--json"]).code, 2);
  // --root still works, and still rejects a missing value.
  assert.equal(run(["--root"]).code, 2);
});

// --- Phase 25: the freshness check used to be unable to fire ------------------
// `HEX_REF` was anchored, so `covers-changed-since` ran only when source_state was
// a bare hash. A consumer wrote `git c5e6a34 (Phase 06 …)` on all 17 of its
// artifacts; four were stale, one by 74 files, while --strict reported 0/0/0.

test("sourceRef takes the ref out of every form that carries one", () => {
  assert.equal(sourceRef("ec8d033"), "ec8d033");
  assert.equal(sourceRef("git c5e6a34 (Phase 06 M5 feature commit — reports)"), "c5e6a34");
  assert.equal(sourceRef("90f25e2 (Phase 08 C13 fix-forward; reports.ts unchanged since 1e751ac)"), "90f25e2");
  assert.equal(sourceRef("  b51913b  "), "b51913b");
  assert.equal(sourceRef("uncommitted state at 2026-09-13 10:00"), null);
  assert.equal(sourceRef("design-only — SDD.md §3 approved 2026-09-13; no source yet"), null);
  assert.equal(sourceRef('<git commit hash | "uncommitted state at YYYY-MM-DD HH:MM">'), null);
  assert.equal(sourceRef(""), null);
  // A hex-looking word with no digit is prose, not a short hash.
  assert.equal(sourceRef("added after the facade was decided"), null);
});

test("refless names the forms that deliberately carry no ref", () => {
  assert.equal(refless("design-only — x"), true);
  assert.equal(refless("uncommitted state at 2026-09-13 10:00"), true);
  assert.equal(refless('<git commit hash | "…">'), true);
  assert.equal(refless("ec8d033"), false);
  assert.equal(refless("Phase 06 close"), false);
});

test("covers-changed-since fires on an annotated ref, not only a bare one", () => {
  const root = tmp();
  const git = (...a) => execFileSync("git", a, { cwd: root, stdio: "ignore" });
  git("init", "-q");
  git("config", "user.email", "t@example.com");
  git("config", "user.name", "T");
  mkdirSync(join(root, "src"), { recursive: true });
  writeFileSync(join(root, "src", "a.ts"), "export const a = 1;\n");
  git("add", "-A");
  git("commit", "-q", "-m", "one");
  const sha = execFileSync("git", ["rev-parse", "--short", "HEAD"], { cwd: root, encoding: "utf8" }).trim();
  writeFileSync(join(root, "src", "a.ts"), "export const a = 2;\n");
  git("add", "-A");
  git("commit", "-q", "-m", "two");

  mkdirSync(join(root, "DBL", "Summaries"), { recursive: true });
  const write = (name, ss) => writeFileSync(join(root, "DBL", "Summaries", name), FM({ source_state: ss }));
  write("bare.md", sha);
  write("annotated.md", `git ${sha} (Phase 06 close — the shape one consumer actually writes)`);
  write("prose.md", "Phase 06 close");

  const r = check(root);
  const by = (n) => r.findings.filter((x) => x.file.endsWith(n)).map((x) => x.check);
  assert.deepEqual(by("bare.md"), ["covers-changed-since"]);
  assert.deepEqual(by("annotated.md"), ["covers-changed-since"], "an annotated ref must be checked, not skipped");
  assert.deepEqual(by("prose.md"), ["unrecognised-source-state"], "a source_state with no ref must be reported, never silently skipped");
  assert.match(r.findings.find((x) => x.file.endsWith("annotated.md")).message, /dbl-regen --artifact/);
});
