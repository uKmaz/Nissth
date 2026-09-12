import { test, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, readdirSync, statSync, rmSync, cpSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { plan, apply, parseArgs, frameworkBody, InitError, STACKS, VERSION } from "./init.mjs";

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
  "Tests/.gitkeep",
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
