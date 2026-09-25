import { test, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, readdirSync, statSync, rmSync, cpSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { plan, apply, parseArgs, frameworkBody, checkConsumer, openFeedback, InitError, STACKS, VERSION } from "./init.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const CLI = join(HERE, "init.mjs");
const FRAMEWORK = resolve(HERE, "..", "..");

const temps = [];
function tmp(prefix = "nissth-init-") {
  const d = mkdtempSync(join(tmpdir(), prefix));
  temps.push(d);
  return d;
}
after(() => {
  for (const d of temps) rmSync(d, { recursive: true, force: true });
});

/** Run the CLI; returns { code, stdout, stderr }. */
function run(args, env = {}) {
  const r = spawnSync(process.execPath, [CLI, ...args], { encoding: "utf8", env: { ...process.env, ...env } });
  return { code: r.status, stdout: r.stdout, stderr: r.stderr };
}

function walk(dir, base = dir, out = []) {
  for (const e of readdirSync(dir)) {
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walk(p, base, out);
    else out.push(p.slice(base.length + 1).replace(/\\/g, "/"));
  }
  return out.sort();
}

const EXPECTED = [
  ".claude/settings.json",
  ".gitattributes",
  ".gitignore",
  "AGENTS.md",
  "AgentReports/Archive/README.md",
  "AgentReports/Bridge/.gitkeep",
  "AgentReports/Reports/.gitkeep",
  "AgentReports/Snapshots/.gitkeep",
  "AgentReports/StatusUpdate.md",
  "CLAUDE.md",
  "DBL/APIIndex/_TEMPLATE.md",
  "DBL/DependencyMaps/_TEMPLATE.md",
  "DBL/SchemaIndex/_TEMPLATE.md",
  "DBL/Summaries/_TEMPLATE.md",
  "ImplementationPlans/_TEMPLATE.md",
  "Tests/README.md",
  "Tools/.gitkeep",
  "nissth-bridge",
  "nissth-bridge.ps1",
];

/** A minimal fake framework root built from the real one, with every text file converted to CRLF. */
function crlfFramework() {
  const root = tmp("nissth-fake-fw-");
  mkdirSync(join(root, "Bindings"));
  const rels = [
    "CLAUDE.md",
    "AGENTS.md",
    "ImplementationPlans/_TEMPLATE.md",
    "DBL/Summaries/_TEMPLATE.md",
    "DBL/DependencyMaps/_TEMPLATE.md",
    "DBL/APIIndex/_TEMPLATE.md",
    "DBL/SchemaIndex/_TEMPLATE.md",
    "Tools/nissth-bridge/dispatcher.js",
    "Tools/nissth-bridge/consumer-launcher/nissth-bridge",
    "Tools/nissth-bridge/consumer-launcher/nissth-bridge.ps1",
  ];
  for (const rel of rels) {
    const src = readFileSync(join(FRAMEWORK, rel), "utf8").replace(/\r?\n/g, "\r\n");
    mkdirSync(dirname(join(root, rel)), { recursive: true });
    writeFileSync(join(root, rel), src);
  }
  return root;
}

// ------------------------------------------------------------------ plan()

test("plan lists the full file set for expo, in a fixed order", () => {
  const p = plan({ target: tmp(), name: "Alpha", stack: "expo" });
  assert.deepEqual(p.files.map((f) => f.rel).sort(), EXPECTED);
  assert.equal(p.wiring, "local");
});

test("every stack plans the same file set; gitignore content differs per stack", () => {
  const byStack = Object.fromEntries(STACKS.map((s) => [s, plan({ target: tmp(), name: "Beta", stack: s })]));
  for (const s of STACKS) assert.deepEqual(byStack[s].files.map((f) => f.rel).sort(), EXPECTED, s);
  const ign = (s) => byStack[s].files.find((f) => f.rel === ".gitignore").content;
  assert.notEqual(ign("expo"), ign("spring-boot"));
  assert.match(ign("expo"), /\.expo\//);
  assert.match(ign("spring-boot"), /target\//);
  assert.doesNotMatch(ign("none"), /\.expo\/|target\//);
  for (const s of STACKS) assert.match(ign(s), /AgentReports\/Bridge\//, `${s} must ignore the Bridge dir`);
});

test("Tests/README.md states the test-sources rule (CLAUDE.md §5) and replaces the keeper", () => {
  const files = plan({ target: tmp(), name: "Gamma", stack: "none" }).files;
  assert.equal(files.find((f) => f.rel === "Tests/.gitkeep"), undefined, "no Tests/.gitkeep any more");
  const readme = files.find((f) => f.rel === "Tests/README.md");
  assert.ok(readme, "Tests/README.md planned");
  assert.match(readme.content, /test sources/i);
  assert.match(readme.content, /verification artifacts/i);
  assert.match(readme.content, /never create `tests\/` or `test\/`/i);
  assert.match(readme.content, /case-insensitive/i);
  assert.doesNotMatch(readme.content, /\r/);
});

test("unknown stack, wiring, and missing name are usage errors", () => {
  for (const bad of [{ stack: "django" }, { stack: "expo", wiring: "vendored" }, { stack: "expo", name: "" }]) {
    assert.throws(() => plan({ target: tmp(), name: "Gamma", ...bad }), (e) => e instanceof InitError && e.code === "usage");
  }
});

test("already-initialised target is refused before any collision check", () => {
  const t = tmp();
  mkdirSync(join(t, "AgentReports"), { recursive: true });
  writeFileSync(join(t, "AgentReports", "StatusUpdate.md"), "# ledger\n");
  assert.throws(() => plan({ target: t, name: "Delta", stack: "none" }), (e) => e.code === "already_initialized");
});

test("a single pre-existing file aborts with file_exists and lists it", () => {
  const t = tmp();
  writeFileSync(join(t, "CLAUDE.md"), "mine\n");
  writeFileSync(join(t, ".gitignore"), "mine\n");
  assert.throws(
    () => plan({ target: t, name: "Epsilon", stack: "expo" }),
    (e) => e.code === "file_exists" && e.details.length === 2 && e.details.includes("CLAUDE.md") && e.details.includes(".gitignore")
  );
  assert.deepEqual(walk(t), [".gitignore", "CLAUDE.md"], "nothing else was written");
});

test("target equal to the framework root is refused", () => {
  assert.throws(() => plan({ target: FRAMEWORK, name: "Zeta", stack: "none" }), (e) => e.code === "target_is_framework");
});

test("--framework-root without Bindings/ is refused", () => {
  const fake = tmp();
  writeFileSync(join(fake, "CLAUDE.md"), "# x\n");
  assert.throws(() => plan({ target: tmp(), name: "Eta", stack: "none", frameworkRoot: fake }), (e) => e.code === "invalid_framework_root");
});

test("CLAUDE.md = rendered banner + framework body identical from the first rule line", () => {
  const p = plan({ target: tmp(), name: "Theta Project", stack: "expo", now: new Date(2026, 8, 13, 10, 5) });
  const out = p.files.find((f) => f.rel === "CLAUDE.md").content;
  assert.match(out, /^# Theta Project — Claude Code Project Instructions\n/);
  assert.match(out, /Bootstrapped 2026-09-13 by `nissth-init`/);
  assert.match(out, /Stack binding: \*\*Expo\*\*/);
  const body = frameworkBody(readFileSync(join(FRAMEWORK, "CLAUDE.md"), "utf8").replace(/\r\n?/g, "\n"));
  assert.ok(out.endsWith(body), "framework body is appended verbatim");
  assert.equal(out.indexOf(body), out.indexOf("\n---\n") + 1, "banner is followed directly by the framework body");
});

test("AGENTS.md carries the project name", () => {
  const p = plan({ target: tmp(), name: "Iota", stack: "none" });
  assert.match(p.files.find((f) => f.rel === "AGENTS.md").content, /This project \(\*\*Iota\*\*, a Nissth consumer\)/);
});

test("StatusUpdate.md has the schema preamble and a complete Bootstrap entry", () => {
  const p = plan({ target: tmp(), name: "Kappa", stack: "postgres", now: new Date(2026, 8, 13, 10, 5) });
  const s = p.files.find((f) => f.rel === "AgentReports/StatusUpdate.md").content;
  assert.match(s, /^# Kappa Status Update — Append-Only Log\n/);
  assert.match(s, /ENTRY SCHEMA — copy this block/);
  assert.match(s, /### 2026-09-13 10:05 — Bootstrap\n/);
  for (const field of ["**State:**", "**Report:**", "**Executed:**", "**Verified:**", "**Issues:**", "**Next:**", "Doc sync: none", "Bridge reports: none", "`nissth-init` v" + VERSION, "Stack: `postgres`"]) {
    assert.ok(s.includes(field), `entry lacks ${field}`);
  }
  for (const rel of EXPECTED) assert.ok(s.includes("`" + rel + "`"), `entry does not list ${rel}`);
  assert.match(s, /SRS\.md, SDD\.md\) — author and get approval/);
});

test("Bootstrap entry reports pre-existing SRS/SDD as present", () => {
  const t = tmp();
  mkdirSync(join(t, "ImplementationPlans"));
  writeFileSync(join(t, "ImplementationPlans", "SRS.md"), "# srs\n");
  writeFileSync(join(t, "ImplementationPlans", "SDD.md"), "# sdd\n");
  const s = plan({ target: t, name: "Lambda", stack: "none" }).files.find((f) => f.rel === "AgentReports/StatusUpdate.md").content;
  assert.match(s, /both present at init time/);
});

test("local wiring fills DEFAULT_ROOT in both launchers; submodule wiring leaves them empty", () => {
  const local = plan({ target: tmp(), name: "Mu", stack: "none" });
  const sub = plan({ target: tmp(), name: "Mu", stack: "none", wiring: "submodule" });
  const sh = (p) => p.files.find((f) => f.rel === "nissth-bridge").content;
  const ps = (p) => p.files.find((f) => f.rel === "nissth-bridge.ps1").content;
  assert.match(sh(local), new RegExp(`^DEFAULT_ROOT="${FRAMEWORK.replace(/\\/g, "/").replace(/[.*+?^${}()|[\]]/g, "\\$&")}"$`, "m"));
  assert.match(ps(local), /^\$DefaultRoot = '.+'$/m);
  assert.match(sh(sub), /^DEFAULT_ROOT=""$/m);
  assert.match(ps(sub), /^\$DefaultRoot = ''$/m);
  const ledger = sub.files.find((f) => f.rel === "AgentReports/StatusUpdate.md").content;
  assert.match(ledger, /git submodule at `Tools\/Nissth\/`/);
});

test(".claude/settings.json is a narrow allow-list with stack additions and no bypass keys", () => {
  for (const s of STACKS) {
    const j = JSON.parse(plan({ target: tmp(), name: "Nu", stack: s }).files.find((f) => f.rel === ".claude/settings.json").content);
    assert.ok(Array.isArray(j.permissions.allow) && j.permissions.allow.length >= 5, s);
    assert.equal(j.permissions.defaultMode, undefined);
    assert.equal(JSON.stringify(j).includes("bypassPermissions"), false);
    assert.ok(j.permissions.allow.includes("Bash(./nissth-bridge *)"));
  }
  const expo = JSON.parse(plan({ target: tmp(), name: "Nu", stack: "expo" }).files.find((f) => f.rel === ".claude/settings.json").content);
  assert.ok(expo.permissions.allow.includes("Bash(npx expo-doctor)"));
});

test("every planned file is LF even when the framework source is CRLF", () => {
  const fw = crlfFramework();
  const p = plan({ target: tmp(), name: "Xi", stack: "expo", frameworkRoot: fw });
  for (const f of p.files) assert.equal(f.content.includes("\r"), false, `${f.rel} contains CR`);
  // and the framework body still matches the real (LF) one
  const body = frameworkBody(readFileSync(join(FRAMEWORK, "CLAUDE.md"), "utf8").replace(/\r\n?/g, "\n"));
  assert.ok(p.files.find((f) => f.rel === "CLAUDE.md").content.endsWith(body));
});

// ------------------------------------------------------------------ apply() + CLI

test("apply writes exactly the planned files; a second run is refused", () => {
  const t = tmp();
  const created = apply(plan({ target: t, name: "Omicron", stack: "expo" }));
  assert.deepEqual([...created].sort(), EXPECTED);
  assert.deepEqual(walk(t), EXPECTED);
  for (const rel of EXPECTED) assert.equal(readFileSync(join(t, rel), "utf8").includes("\r"), false, rel);
  const again = run(["--target", t, "--name", "Omicron", "--stack", "expo", "--json"]);
  assert.equal(again.code, 2);
  assert.equal(JSON.parse(again.stdout).error_code, "already_initialized");
});

test("--dry-run writes nothing and lists the file set; --json is parseable", () => {
  const t = tmp();
  const r = run(["--target", t, "--name", "Pi", "--stack", "spring-boot", "--dry-run", "--json"]);
  assert.equal(r.code, 0, r.stderr);
  const j = JSON.parse(r.stdout);
  assert.equal(j.dry_run, true);
  assert.deepEqual([...j.would_create].sort(), EXPECTED);
  assert.deepEqual(walk(t), []);
});

test("CLI: usage errors exit 2 with the usage text; --help exits 0", () => {
  const r = run(["--target"]);
  assert.equal(r.code, 2);
  assert.match(r.stderr, /needs a value/);
  const h = run(["--help"]);
  assert.equal(h.code, 0);
  assert.match(h.stdout, /nissth-init v/);
  assert.throws(() => parseArgs(["--bogus"]), (e) => e.code === "usage");
});

test("CLI: real run prints the HR#13 reminder and the --json shape has created[]", () => {
  const t1 = tmp();
  const r = run(["--target", t1, "--name", "Rho", "--stack", "none"]);
  assert.equal(r.code, 0, r.stderr);
  assert.match(r.stdout, /HR#13/);
  const t2 = tmp();
  const j = JSON.parse(run(["--target", t2, "--name", "Rho", "--stack", "none", "--json"]).stdout);
  assert.equal(j.ok, true);
  assert.deepEqual([...j.created].sort(), EXPECTED);
});

// ------------------------------------------------------------------ launchers (field test)

function have(cmd, args) {
  const r = spawnSync(cmd, args, { encoding: "utf8" });
  return r.status === 0;
}

test("generated POSIX launcher resolves the dispatcher via DEFAULT_ROOT and via the env var", { skip: !have("sh", ["-c", "exit 0"]) && "sh not on PATH" }, () => {
  const t = tmp();
  apply(plan({ target: t, name: "Sigma", stack: "expo" }));
  const env = { ...process.env };
  delete env.NISSTH_FRAMEWORK_ROOT;
  const a = spawnSync("sh", ["./nissth-bridge", "--list-bindings"], { cwd: t, encoding: "utf8", env });
  assert.equal(a.status, 0, a.stderr);
  assert.match(a.stdout, /expo/);
  // submodule-wired launcher with no submodule and no env var → exit 3
  const t2 = tmp();
  apply(plan({ target: t2, name: "Sigma", stack: "expo", wiring: "submodule" }));
  const b = spawnSync("sh", ["./nissth-bridge", "--list-bindings"], { cwd: t2, encoding: "utf8", env });
  assert.equal(b.status, 3);
  assert.match(b.stderr, /dispatcher not found/);
  // same launcher, env var set → works
  const c = spawnSync("sh", ["./nissth-bridge", "--list-bindings"], { cwd: t2, encoding: "utf8", env: { ...env, NISSTH_FRAMEWORK_ROOT: FRAMEWORK } });
  assert.equal(c.status, 0, c.stderr);
  assert.match(c.stdout, /spring-boot/);
});

const PS = ["pwsh", "powershell"].find((c) => have(c, ["-NoProfile", "-Command", "exit 0"]));

test("generated PowerShell launcher resolves the dispatcher via DEFAULT_ROOT and via the env var", { skip: !PS && "no pwsh/powershell on PATH" }, () => {
  const t = tmp();
  apply(plan({ target: t, name: "Tau", stack: "expo" }));
  const env = { ...process.env };
  delete env.NISSTH_FRAMEWORK_ROOT;
  const ps = (cwd, e) => spawnSync(PS, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", join(cwd, "nissth-bridge.ps1"), "--list-bindings"], { cwd, encoding: "utf8", env: e });
  const a = ps(t, env);
  assert.equal(a.status, 0, a.stderr);
  assert.match(a.stdout, /postgres/);
  const t2 = tmp();
  apply(plan({ target: t2, name: "Tau", stack: "expo", wiring: "submodule" }));
  const b = ps(t2, env);
  assert.equal(b.status, 3);
  assert.match(b.stderr, /dispatcher not found/);
  const c = ps(t2, { ...env, NISSTH_FRAMEWORK_ROOT: FRAMEWORK });
  assert.equal(c.status, 0, c.stderr);
  assert.match(c.stdout, /expo/);
});

// ---------------------------------------------------------------- Phase 22

test("the skeleton carries AgentReports/Archive/README.md with the rotation procedure", () => {
  const t = tmp();
  const p = plan({ target: t, name: "Rho", stack: "none" });
  const rel = "AgentReports/Archive/README.md";
  assert.ok(p.files.some((f) => f.rel === rel), "Archive README not planned");
  apply(p);
  const text = readFileSync(join(t, "AgentReports", "Archive", "README.md"), "utf8");
  assert.match(text, /100 KB/);
  assert.match(text, /Hard Rule #3/);
  assert.match(text, /StatusUpdate_<first-date>_<last-date>_<slug>\.md/);
  assert.ok(!text.includes("\r"), "template must be LF");
});

test("a successful init ends with the open-a-session-in-the-target handoff", () => {
  const t = tmp();
  const r = run(["--target", t, "--name", "Sigma", "--stack", "none"]);
  assert.equal(r.code, 0);
  assert.match(r.stdout, /HANDOFF/);
  assert.match(r.stdout, /Open the next session IN the new project/);
  assert.ok(r.stdout.includes(t), "handoff must name the target path");
});

test("--check: a freshly initialised project is in sync", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Upsilon", stack: "none" }));
  const r = checkConsumer(t, FRAMEWORK);
  assert.equal(r.inSync, true);
  assert.equal(r.driftLines, 0);
  assert.deepEqual(r.missing, []);
  const cli = run(["--check", t]);
  assert.equal(cli.code, 0);
  assert.match(cli.stdout, /in sync/);
});

test("--check: an edited framework body is drift, and the banner is not", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Phi", stack: "none" }));
  const md = join(t, "CLAUDE.md");
  const before = readFileSync(md, "utf8");

  // Editing the banner (above the first rule) is the consumer's own business.
  writeFileSync(md, before.replace("**Status:**", "**Status:** phase 3 —"), "utf8");
  assert.equal(checkConsumer(t, FRAMEWORK).inSync, true);

  // Editing the framework body is not.
  writeFileSync(md, before.replace("**Agents must never explore — they must operate.**", "**Agents may explore a bit.**"), "utf8");
  const r = checkConsumer(t, FRAMEWORK);
  assert.equal(r.inSync, false);
  assert.equal(r.driftLines, 1);
  assert.match(r.firstDrift.expected, /never explore/);
  assert.match(r.firstDrift.actual, /explore a bit/);

  const cli = run(["--check", t]);
  assert.equal(cli.code, 1);
  assert.match(cli.stdout, /framework body differs on 1 line/);
  assert.match(cli.stdout, /keeping the consumer's banner/);
});

test("--check: a CRLF checkout is not drift", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Chi", stack: "none" }));
  const md = join(t, "CLAUDE.md");
  writeFileSync(md, readFileSync(md, "utf8").replace(/\n/g, "\r\n"), "utf8");
  assert.equal(checkConsumer(t, FRAMEWORK).inSync, true);
});

test("--check: a missing skeleton directory is reported", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Psi", stack: "none" }));
  rmSync(join(t, "AgentReports", "Archive"), { recursive: true, force: true });
  const r = checkConsumer(t, FRAMEWORK);
  assert.equal(r.inSync, false);
  assert.deepEqual(r.missing, ["AgentReports/Archive"]);
  assert.equal(r.driftLines, 0);
  assert.equal(run(["--check", t]).code, 1);
});

test("--check: a directory that is not a Nissth project exits 2", () => {
  const t = tmp();
  const r = run(["--check", t]);
  assert.equal(r.code, 2);
  assert.match(r.stderr, /not an initialised Nissth project/);
  assert.equal(run(["--check", join(t, "nope")]).code, 2);
  assert.throws(() => checkConsumer(t, FRAMEWORK), InitError);
});

test("--check --json reports the shape and the exit code follows inSync", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Omega", stack: "none" }));
  const ok = run(["--check", t, "--json"]);
  assert.equal(ok.code, 0);
  const parsed = JSON.parse(ok.stdout);
  assert.equal(parsed.ok, true);
  assert.equal(parsed.driftLines, 0);
  rmSync(join(t, "Tests", "README.md"), { force: true });
  const bad = run(["--check", t, "--json"]);
  assert.equal(bad.code, 1);
  assert.deepEqual(JSON.parse(bad.stdout).missing, ["Tests/README.md"]);
});

// --- Phase 24: a consumer's open feedback, readable from here ----------------
// One digest carried ten open items for ten days while Nissth sessions came and
// went, because the only copy lived in a repo no framework session opens.

test("--check reports open feedback rows without treating them as drift", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Iota", stack: "none" }));
  const digest = join(t, "AgentReports", "Reports", "2026-09-25_nissth-feedback-digest.md");
  writeFileSync(digest, [
    "# Nissth feedback digest",
    "",
    "| # | Where | Finding | Status |",
    "|:---|:---|:---|:---|",
    "| A1 | §8.2 | the verification sequence lacks a bundle check that only Metro can do | open |",
    "| A2 | §7.6 | greenfield artifacts had no design-only mode, improvised in Phase 00 | **applied** |",
    "| B1 | `component_lens` | flips artifacts that cover no component at all, every single run | open |",
  ].join("\n"), "utf8");

  const r = checkConsumer(t, FRAMEWORK);
  assert.equal(r.inSync, true, "a feedback row is not drift");
  assert.equal(r.openFeedback.length, 2);
  assert.deepEqual(r.openFeedback.map((x) => x.id), ["A1", "B1"]);
  assert.match(r.openFeedback[0].note, /bundle check/);
  assert.match(r.openFeedback[0].file, /nissth-feedback-digest\.md$/);

  const cli = run(["--check", t]);
  assert.equal(cli.code, 0, "open feedback must not change the exit code");
  assert.match(cli.stdout, /open framework feedback raised by this consumer: 2 row\(s\)/);
  assert.match(cli.stdout, /A1/);
  assert.match(cli.stdout, /B1/);
  assert.doesNotMatch(cli.stdout, /A2/, "an applied row is not open");
});

test("--check is silent about feedback when there is none, and when there is no digest", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Kappa", stack: "none" }));
  assert.deepEqual(checkConsumer(t, FRAMEWORK).openFeedback, []);
  assert.doesNotMatch(run(["--check", t]).stdout, /open framework feedback/);

  writeFileSync(join(t, "AgentReports", "Reports", "2026-09-25_nissth-feedback-digest.md"),
    "| # | Finding | Status |\n|:--|:--|:--|\n| A1 | all done | **applied** |\n", "utf8");
  assert.deepEqual(checkConsumer(t, FRAMEWORK).openFeedback, []);
});

test("open feedback is also reported next to drift, not instead of it", () => {
  const t = tmp();
  apply(plan({ target: t, name: "Lambda", stack: "none" }));
  writeFileSync(join(t, "AgentReports", "Reports", "feedback.md"),
    "| # | Finding | Status |\n|:--|:--|:--|\n| C9 | the launcher ignores the framework root env var entirely | open |\n", "utf8");
  const md = join(t, "CLAUDE.md");
  writeFileSync(md, readFileSync(md, "utf8").replace("**Agents must never explore — they must operate.**", "**Agents may explore.**"), "utf8");
  const cli = run(["--check", t]);
  assert.equal(cli.code, 1, "drift still fails");
  assert.match(cli.stdout, /framework body differs/);
  assert.match(cli.stdout, /C9/);
});
