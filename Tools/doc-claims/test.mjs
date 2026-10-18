import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { validate, loadBindings, ConfigError } from "./validate.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const FIX = join(HERE, "_fixtures");
const CLI = join(HERE, "validate.mjs");

/** Run the CLI, returning { code, stdout, stderr }. */
function run(args) {
  try {
    const stdout = execFileSync(process.execPath, [CLI, ...args], {
      cwd: HERE,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    });
    return { code: 0, stdout, stderr: "" };
  } catch (err) {
    return {
      code: err.status ?? -1,
      stdout: err.stdout ?? "",
      stderr: err.stderr ?? "",
    };
  }
}

const fixture = (name) => validate(join(FIX, name), HERE);

// ------------------------------------------------------------ ground truth

test("loadBindings reads every manifest under Bindings/", () => {
  const b = loadBindings(join(FIX, "clean"));
  assert.equal(b.length, 1);
  assert.equal(b[0].id, "postgres");
  assert.equal(b[0].tools.size, 5);
  assert.ok(b[0].tools.has("schema_lens"));
});

test("loadBindings skips _schemas and underscore-prefixed dirs", () => {
  const b = loadBindings(join(FIX, "clean"));
  assert.ok(!b.some((x) => x.dirName.startsWith("_")));
});

test("a directory without a manifest is not a binding", () => {
  // _fixtures/unbuilt has no Bindings/Postgres at all
  const b = loadBindings(join(FIX, "unbuilt"));
  assert.ok(!b.some((x) => x.dirName === "Postgres"));
});

// ------------------------------------------------- check 1: stale status

test("check 1 fires on a shipped binding described as queued", () => {
  const { findings } = fixture("stale");
  const f = findings.filter((x) => x.check === "stale-binding-status");
  assert.equal(f.length, 1);
  assert.equal(f[0].file, "README.md");
  assert.match(f[0].message, /Postgres/);
});

test("check 1 does NOT fire for a binding that genuinely has no directory", () => {
  const { findings } = fixture("unbuilt");
  assert.deepEqual(
    findings.filter((x) => x.check === "stale-binding-status"),
    []
  );
});

test("check 1 ignores stack names embedded in plan filenames", () => {
  // The CLAUDE.md §11.12 case: "Phase_05_Bridge_SpringBoot_FirstSlice.md
  // (not yet authored at the time §11 is written)" is accurate historical
  // prose about a plan, not a claim that the binding is missing.
  const { findings } = fixture("planname");
  assert.deepEqual(findings, []);
});

// ------------------------------------------------ check 2: fictional tool

test("check 2 catches a fictional tool in an enumeration line", () => {
  const { findings } = fixture("fiction");
  const f = findings.filter((x) => x.check === "fictional-tool");
  assert.equal(f.length, 1);
  assert.match(f[0].message, /index_drift/);
});

test("check 2 honours the allowlist", () => {
  const { findings } = fixture("allowlisted");
  assert.deepEqual(
    findings.filter((x) => x.check === "fictional-tool"),
    []
  );
});

test("check 2 ignores lines with no shipping verb", () => {
  const { findings } = fixture("noverb");
  assert.deepEqual(findings, []);
});

test("check 2 ignores a single identifier (a mention, not a list)", () => {
  const { findings } = fixture("single");
  assert.deepEqual(findings, []);
});

// -------------------------------------------------- check 3: count drift

test("check 3 fires when the table count disagrees with the manifest", () => {
  const { findings } = fixture("count");
  const f = findings.filter((x) => x.check === "tool-count-drift");
  assert.equal(f.length, 1);
  assert.match(f[0].message, /claims 3 .*registers 5/);
});

test("check 3 is silent when counts agree", () => {
  const { findings } = fixture("clean");
  assert.deepEqual(
    findings.filter((x) => x.check === "tool-count-drift"),
    []
  );
});

// ----------------------------------------------------------- clean tree

test("a clean fixture produces no findings at all", () => {
  const { findings } = fixture("clean");
  assert.deepEqual(findings, []);
});

test("the real repository passes", () => {
  const { findings } = validate(join(HERE, "..", ".."), HERE);
  assert.deepEqual(
    findings,
    [],
    "the repo's own docs must pass; a finding here means the tool is noisy or the docs regressed"
  );
});

// ---------------------------------------------------------------- CLI

test("CLI exits 0 and says so on a clean tree", () => {
  const r = run(["--root", join(FIX, "clean")]);
  assert.equal(r.code, 0);
  assert.match(r.stdout, /no findings/);
});

test("CLI exits 1 and prints file:line on findings", () => {
  const r = run(["--root", join(FIX, "fiction")]);
  assert.equal(r.code, 1);
  assert.match(r.stdout, /README\.md:2\s+\[fictional-tool\]/);
});

test("CLI --json emits parseable output and keeps the exit code", () => {
  const r = run(["--root", join(FIX, "fiction"), "--json"]);
  assert.equal(r.code, 1);
  const parsed = JSON.parse(r.stdout);
  assert.equal(parsed.findings.length, 1);
  assert.equal(parsed.findings[0].check, "fictional-tool");
  assert.ok(Array.isArray(parsed.bindings));
});

test("CLI exits 2 when --root has no bindings", () => {
  const r = run(["--root", join(FIX, "notarepo")]);
  assert.equal(r.code, 2);
  assert.match(r.stderr, /no binding manifests/);
});

test("CLI exits 2 when --root is given no value", () => {
  const r = run(["--root"]);
  assert.equal(r.code, 2);
});

test("CLI --help exits 0", () => {
  const r = run(["--help"]);
  assert.equal(r.code, 0);
  assert.match(r.stdout, /usage/);
});

test("a malformed manifest raises ConfigError, not a crash", () => {
  assert.throws(() => loadBindings(join(FIX, "badjson")), ConfigError);
});

// ------------------------------------------------- inline suppression

test("a doc-claims:allow marker on the preceding line suppresses that check", () => {
  const { findings } = fixture("suppressed");
  assert.deepEqual(findings, []);
});

test("suppression naming a different check does NOT silence the finding", () => {
  const { findings } = fixture("wrongsuppress");
  const f = findings.filter((x) => x.check === "stale-binding-status");
  assert.equal(f.length, 1, "a tool-count-drift waiver must not suppress a status finding");
});
