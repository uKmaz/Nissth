// Tests for the nissth-bridge cross-binding dispatcher.
// Uses Node's built-in node:test + node:assert (no Jest, no installs).

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  findRepoRoot,
  findFrameworkRoot,
  discoverManifests,
  buildToolMap,
  parseArgv,
  resolveBindingForTool,
  resolveCliEntry,
  buildSpawnSpec,
  runDispatcher,
  DispatchError,
} from "./dispatcher.js";

// --- Fixture builders ------------------------------------------------------

function makeSyntheticRepo(manifests) {
  // manifests: Array<{ dir, fileName, json }>
  const root = mkdtempSync(join(tmpdir(), "nissth-disp-"));
  // Write the root marker so findRepoRoot picks this up.
  writeFileSync(join(root, "CLAUDE.md"), "# fake\n");
  mkdirSync(join(root, "Bindings"), { recursive: true });
  for (const m of manifests) {
    const bDir = join(root, "Bindings", m.dir);
    mkdirSync(bDir, { recursive: true });
    writeFileSync(join(bDir, m.fileName), JSON.stringify(m.json, null, 2));
  }
  return root;
}

function cleanup(root) {
  try {
    rmSync(root, { recursive: true, force: true });
  } catch {
    // best-effort
  }
}

// --- parseArgv -------------------------------------------------------------

test("parseArgv: empty argv -> no tool", () => {
  const p = parseArgv([]);
  assert.equal(p.tool, null);
  assert.equal(p.listBindings, false);
});

test("parseArgv: --list-bindings", () => {
  const p = parseArgv(["--list-bindings"]);
  assert.equal(p.listBindings, true);
});

test("parseArgv: --describe + tool name", () => {
  const p = parseArgv(["--describe", "schema_lens"]);
  assert.equal(p.describe, "schema_lens");
});

test("parseArgv: --binding + tool name + passthrough flags", () => {
  const p = parseArgv(["schema_lens", "--binding", "postgres", "--mode", "tables"]);
  assert.equal(p.tool, "schema_lens");
  assert.equal(p.binding, "postgres");
  assert.deepEqual(p.passthrough, ["--mode", "tables"]);
});

test("parseArgv: --describe without value raises DispatchError(2)", () => {
  assert.throws(() => parseArgv(["--describe"]), DispatchError);
});

test("parseArgv: --binding without value raises DispatchError(2)", () => {
  assert.throws(() => parseArgv(["foo", "--binding"]), DispatchError);
});

test("parseArgv: unknown --flag with value gets passthrough'd", () => {
  const p = parseArgv(["my_tool", "--custom-flag", "hello"]);
  assert.equal(p.tool, "my_tool");
  assert.deepEqual(p.passthrough, ["--custom-flag", "hello"]);
});

// --- discoverManifests (real repo) ----------------------------------------

test("discoverManifests: real Nissth repo returns 3 bindings", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  const names = manifests.map((m) => m.data.binding).sort();
  assert.deepEqual(names, ["expo", "postgres", "spring-boot"]);
});

test("discoverManifests: every real manifest has cli_entry with valid runtime", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  for (const m of manifests) {
    const e = m.data.cli_entry;
    assert.ok(e, `binding '${m.data.binding}' missing cli_entry`);
    assert.ok(["node", "java-jar"].includes(e.runtime));
    assert.equal(typeof e.path, "string");
  }
});

test("discoverManifests: _schemas/ subdir is skipped", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  for (const m of manifests) {
    assert.ok(!m.dir.startsWith("_"));
  }
});

// --- buildToolMap (real repo) ---------------------------------------------

test("buildToolMap: real repo flags migration_status as conflicting", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  const map = buildToolMap(manifests);
  const owners = map.get("migration_status");
  assert.ok(Array.isArray(owners));
  assert.equal(owners.length, 2);
  const bindings = owners.map((o) => o.bindingId).sort();
  assert.deepEqual(bindings, ["postgres", "spring-boot"]);
});

test("buildToolMap: schema_lens belongs to exactly postgres", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  const map = buildToolMap(manifests);
  const owners = map.get("schema_lens");
  assert.equal(owners.length, 1);
  assert.equal(owners[0].bindingId, "postgres");
});

// --- resolveBindingForTool ------------------------------------------------

test("resolveBindingForTool: conflict throws DispatchError(2) with helpful message", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  const map = buildToolMap(manifests);
  try {
    resolveBindingForTool(map, manifests, "migration_status", null);
    assert.fail("expected DispatchError");
  } catch (e) {
    assert.ok(e instanceof DispatchError);
    assert.equal(e.exitCode, 2);
    assert.match(e.message, /multiple bindings/);
    assert.match(e.message, /postgres, spring-boot|spring-boot, postgres/);
    assert.match(e.message, /--binding/);
  }
});

test("resolveBindingForTool: --binding disambiguates conflicting tool", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  const map = buildToolMap(manifests);
  const owner = resolveBindingForTool(map, manifests, "migration_status", "postgres");
  assert.equal(owner.bindingId, "postgres");
});

test("resolveBindingForTool: unknown tool throws DispatchError(4)", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  const map = buildToolMap(manifests);
  try {
    resolveBindingForTool(map, manifests, "nonexistent_tool", null);
    assert.fail("expected DispatchError");
  } catch (e) {
    assert.equal(e.exitCode, 4);
  }
});

test("resolveBindingForTool: unknown binding throws DispatchError(4)", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const manifests = discoverManifests(repoRoot);
  const map = buildToolMap(manifests);
  try {
    resolveBindingForTool(map, manifests, "schema_lens", "ghost_binding");
    assert.fail("expected DispatchError");
  } catch (e) {
    assert.equal(e.exitCode, 4);
  }
});

// --- resolveCliEntry + buildSpawnSpec -------------------------------------

test("buildSpawnSpec: runtime=node uses process.execPath", () => {
  const cliEntry = { runtime: "node", absPath: "/abs/dist/cli/index.js", bindingDir: "/abs" };
  const spec = buildSpawnSpec(cliEntry, ["schema_lens", "--mode", "tables"]);
  assert.equal(spec.command, process.execPath);
  assert.deepEqual(spec.args, ["/abs/dist/cli/index.js", "schema_lens", "--mode", "tables"]);
});

test("buildSpawnSpec: runtime=java-jar uses java -jar", () => {
  const cliEntry = { runtime: "java-jar", absPath: "/abs/target/x.jar", bindingDir: "/abs" };
  const spec = buildSpawnSpec(cliEntry, ["entity_lens"]);
  assert.equal(spec.command, "java");
  assert.deepEqual(spec.args, ["-jar", "/abs/target/x.jar", "entity_lens"]);
});

// --- runDispatcher: --list-bindings / --list-tools / --describe (real) ---

test("runDispatcher: --list-bindings against real repo (captures stdout via spawn proxy)", async () => {
  // Use the same process; runDispatcher writes to process.stdout. We assert it returns 0.
  // The actual content is exercised by the shell-level Step 11 integration check.
  const repoRoot = findRepoRoot(import.meta.dirname);
  // Save & swap stdout.write to capture.
  const chunks = [];
  const orig = process.stdout.write.bind(process.stdout);
  process.stdout.write = (s) => { chunks.push(s); return true; };
  try {
    const code = await runDispatcher(["--list-bindings"], { repoRoot });
    assert.equal(code, 0);
    const out = chunks.join("");
    assert.match(out, /expo/);
    assert.match(out, /postgres/);
    assert.match(out, /spring-boot/);
  } finally {
    process.stdout.write = orig;
  }
});

test("runDispatcher: --dry-run dispatch to a unique tool exits 0 with would-exec line", async () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const chunks = [];
  const orig = process.stdout.write.bind(process.stdout);
  process.stdout.write = (s) => { chunks.push(s); return true; };
  try {
    const code = await runDispatcher(["schema_lens", "--dry-run", "--mode", "tables"], { repoRoot });
    assert.equal(code, 0);
    const out = chunks.join("");
    assert.match(out, /would exec/);
    assert.match(out, /dist[\\/]cli[\\/]index\.js/);
    assert.match(out, /schema_lens/);
    assert.match(out, /--mode\s+tables/);
  } finally {
    process.stdout.write = orig;
  }
});

test("runDispatcher: conflict on real migration_status returns exit 2", async () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const chunks = [];
  const origErr = process.stderr.write.bind(process.stderr);
  process.stderr.write = (s) => { chunks.push(s); return true; };
  try {
    const code = await runDispatcher(["migration_status", "--dry-run"], { repoRoot });
    assert.equal(code, 2);
    const err = chunks.join("");
    assert.match(err, /multiple bindings/);
  } finally {
    process.stderr.write = origErr;
  }
});

test("runDispatcher: unknown tool returns exit 4", async () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  const chunks = [];
  const origErr = process.stderr.write.bind(process.stderr);
  process.stderr.write = (s) => { chunks.push(s); return true; };
  try {
    const code = await runDispatcher(["ghost_tool"], { repoRoot });
    assert.equal(code, 4);
    const err = chunks.join("");
    assert.match(err, /Unknown tool/);
  } finally {
    process.stderr.write = origErr;
  }
});

// --- runDispatcher with synthetic fixtures --------------------------------

test("runDispatcher (synthetic): two fixtures with conflicting tool -> exit 2", async () => {
  const fakeRoot = makeSyntheticRepo([
    {
      dir: "BindingA",
      fileName: "stack-a.bridge.json",
      json: {
        binding: "stack-a",
        binding_version: "0.0.1",
        contract_version: 1,
        language: "node",
        node_min: 20,
        build_tool: "npm",
        cli_entry: { runtime: "node", path: "dist/cli/index.js" },
        description: "Synthetic A.",
        tools: [{ name: "ghost_tool", kind: "diagnostic", modes: ["default"], scope_keys: [], scope_extra_keys: [], description: "A" }],
      },
    },
    {
      dir: "BindingB",
      fileName: "stack-b.bridge.json",
      json: {
        binding: "stack-b",
        binding_version: "0.0.1",
        contract_version: 1,
        language: "node",
        node_min: 20,
        build_tool: "npm",
        cli_entry: { runtime: "node", path: "dist/cli/index.js" },
        description: "Synthetic B.",
        tools: [{ name: "ghost_tool", kind: "diagnostic", modes: ["default"], scope_keys: [], scope_extra_keys: [], description: "B" }],
      },
    },
  ]);
  const chunks = [];
  const origErr = process.stderr.write.bind(process.stderr);
  process.stderr.write = (s) => { chunks.push(s); return true; };
  try {
    const code = await runDispatcher(["ghost_tool"], { repoRoot: fakeRoot });
    assert.equal(code, 2);
    assert.match(chunks.join(""), /multiple bindings.*stack-a.*stack-b|multiple bindings.*stack-b.*stack-a/s);
  } finally {
    process.stderr.write = origErr;
    cleanup(fakeRoot);
  }
});

test("runDispatcher (synthetic): empty Bindings -> exit 4", async () => {
  const fakeRoot = mkdtempSync(join(tmpdir(), "nissth-disp-empty-"));
  writeFileSync(join(fakeRoot, "CLAUDE.md"), "# fake\n");
  mkdirSync(join(fakeRoot, "Bindings"), { recursive: true });
  const chunks = [];
  const origErr = process.stderr.write.bind(process.stderr);
  process.stderr.write = (s) => { chunks.push(s); return true; };
  try {
    const code = await runDispatcher(["foo"], { repoRoot: fakeRoot });
    assert.equal(code, 4);
    assert.match(chunks.join(""), /No bindings found/);
  } finally {
    process.stderr.write = origErr;
    cleanup(fakeRoot);
  }
});

// --- Phase 09: framework-root resolution ----------------------------------

const FRAMEWORK_ENV = "NISSTH_FRAMEWORK_ROOT";

function withEnv(key, value, fn) {
  const original = process.env[key];
  if (value === null) {
    delete process.env[key];
  } else {
    process.env[key] = value;
  }
  try {
    return fn();
  } finally {
    if (original === undefined) {
      delete process.env[key];
    } else {
      process.env[key] = original;
    }
  }
}

function makeFrameworkOnlyDir(bindings) {
  // bindings: Array<{ dir, fileName, json }> — same shape as makeSyntheticRepo's manifests.
  // Builds a directory with just CLAUDE.md + Bindings/ (no AgentReports/, no Tools/, etc.).
  // Suitable as an NISSTH_FRAMEWORK_ROOT target.
  const root = mkdtempSync(join(tmpdir(), "nissth-fwroot-"));
  writeFileSync(join(root, "CLAUDE.md"), "# fake framework root\n");
  mkdirSync(join(root, "Bindings"), { recursive: true });
  for (const m of bindings) {
    const bDir = join(root, "Bindings", m.dir);
    mkdirSync(bDir, { recursive: true });
    writeFileSync(join(bDir, m.fileName), JSON.stringify(m.json, null, 2));
  }
  return root;
}

const TEST_STACK_MANIFEST = {
  dir: "TestStack",
  fileName: "test-stack.bridge.json",
  json: {
    binding: "test-stack",
    binding_version: "0.0.1",
    contract_version: 1,
    language: "node",
    node_min: 20,
    build_tool: "npm",
    cli_entry: { runtime: "node", path: "dist/cli/index.js" },
    description: "Synthetic framework-root test binding.",
    tools: [{ name: "test_lens", kind: "diagnostic", modes: ["default"], scope_keys: [], scope_extra_keys: [], description: "T" }],
  },
};

test("findFrameworkRoot: NISSTH_FRAMEWORK_ROOT env var resolves correctly when path is valid", () => {
  const fwRoot = makeFrameworkOnlyDir([TEST_STACK_MANIFEST]);
  const repoRoot = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  writeFileSync(join(repoRoot, "CLAUDE.md"), "# consumer\n");
  try {
    withEnv(FRAMEWORK_ENV, fwRoot, () => {
      const resolved = findFrameworkRoot(repoRoot);
      assert.equal(resolved, fwRoot);
    });
  } finally {
    cleanup(fwRoot);
    cleanup(repoRoot);
  }
});

test("findFrameworkRoot: NISSTH_FRAMEWORK_ROOT pointing at invalid path throws DispatchError(invalid_framework_root)", () => {
  const repoRoot = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  writeFileSync(join(repoRoot, "CLAUDE.md"), "# consumer\n");
  const bogus = mkdtempSync(join(tmpdir(), "nissth-bogus-"));
  // bogus exists but has no Bindings/ subdir
  try {
    withEnv(FRAMEWORK_ENV, bogus, () => {
      try {
        findFrameworkRoot(repoRoot);
        assert.fail("expected DispatchError");
      } catch (e) {
        assert.ok(e instanceof DispatchError);
        assert.equal(e.exitCode, 2);
        assert.equal(e.errorCode, "invalid_framework_root");
        assert.match(e.message, /does not contain a Bindings\/ subdirectory/);
      }
    });
  } finally {
    cleanup(repoRoot);
    cleanup(bogus);
  }
});

test("findFrameworkRoot: submodule convention <repoRoot>/Tools/Nissth/ detected when Bindings/ exists there", () => {
  const repoRoot = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  writeFileSync(join(repoRoot, "CLAUDE.md"), "# consumer\n");
  // Build a fake submodule structure at repoRoot/Tools/Nissth/Bindings/
  const submodulePath = join(repoRoot, "Tools", "Nissth");
  mkdirSync(join(submodulePath, "Bindings", "TestStack"), { recursive: true });
  writeFileSync(
    join(submodulePath, "Bindings", "TestStack", "test-stack.bridge.json"),
    JSON.stringify(TEST_STACK_MANIFEST.json)
  );
  try {
    withEnv(FRAMEWORK_ENV, null, () => {
      const resolved = findFrameworkRoot(repoRoot);
      assert.equal(resolved, submodulePath);
    });
  } finally {
    cleanup(repoRoot);
  }
});

test("findFrameworkRoot: fallback to repoRoot when neither env var nor submodule present (Nissth dogfooding)", () => {
  const repoRoot = findRepoRoot(import.meta.dirname);
  withEnv(FRAMEWORK_ENV, null, () => {
    const resolved = findFrameworkRoot(repoRoot);
    // Real Nissth repo: no Tools/Nissth/ submodule of itself; falls back to repoRoot.
    assert.equal(resolved, repoRoot);
  });
});

test("findFrameworkRoot: env var beats submodule convention (precedence)", () => {
  const fwRoot = makeFrameworkOnlyDir([TEST_STACK_MANIFEST]);
  const repoRoot = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  writeFileSync(join(repoRoot, "CLAUDE.md"), "# consumer\n");
  // Also build a Tools/Nissth/ submodule path (would otherwise win without env var).
  mkdirSync(join(repoRoot, "Tools", "Nissth", "Bindings", "SubmoduleStack"), { recursive: true });
  writeFileSync(
    join(repoRoot, "Tools", "Nissth", "Bindings", "SubmoduleStack", "sub.bridge.json"),
    JSON.stringify({ ...TEST_STACK_MANIFEST.json, binding: "submodule-stack" })
  );
  try {
    withEnv(FRAMEWORK_ENV, fwRoot, () => {
      const resolved = findFrameworkRoot(repoRoot);
      assert.equal(resolved, fwRoot, "env var should beat submodule convention");
    });
  } finally {
    cleanup(fwRoot);
    cleanup(repoRoot);
  }
});

test("findFrameworkRoot: submodule convention beats fallback when env var unset", () => {
  // Build a consumer repo with BOTH a fake submodule at Tools/Nissth/Bindings/ AND a top-level Bindings/.
  // Resolution should pick the submodule.
  const repoRoot = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  writeFileSync(join(repoRoot, "CLAUDE.md"), "# consumer\n");
  // Submodule-style Bindings:
  mkdirSync(join(repoRoot, "Tools", "Nissth", "Bindings", "SubmoduleStack"), { recursive: true });
  writeFileSync(
    join(repoRoot, "Tools", "Nissth", "Bindings", "SubmoduleStack", "sub.bridge.json"),
    JSON.stringify({ ...TEST_STACK_MANIFEST.json, binding: "submodule-stack" })
  );
  // Fallback-style Bindings at repoRoot:
  mkdirSync(join(repoRoot, "Bindings", "LocalStack"), { recursive: true });
  writeFileSync(
    join(repoRoot, "Bindings", "LocalStack", "local.bridge.json"),
    JSON.stringify({ ...TEST_STACK_MANIFEST.json, binding: "local-stack" })
  );
  try {
    withEnv(FRAMEWORK_ENV, null, () => {
      const resolved = findFrameworkRoot(repoRoot);
      assert.equal(resolved, join(repoRoot, "Tools", "Nissth"), "submodule should beat fallback");
    });
  } finally {
    cleanup(repoRoot);
  }
});

test("runDispatcher (Phase 09): NISSTH_FRAMEWORK_ROOT routes --list-bindings to env-var target", async () => {
  const fwRoot = makeFrameworkOnlyDir([TEST_STACK_MANIFEST]);
  const repoRoot = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  writeFileSync(join(repoRoot, "CLAUDE.md"), "# consumer\n");
  const chunks = [];
  const orig = process.stdout.write.bind(process.stdout);
  process.stdout.write = (s) => { chunks.push(s); return true; };
  try {
    await withEnv(FRAMEWORK_ENV, fwRoot, async () => {
      const code = await runDispatcher(["--list-bindings"], { repoRoot });
      assert.equal(code, 0);
    });
    const out = chunks.join("");
    assert.match(out, /test-stack/);
    // Crucially: Nissth's own three bindings (expo, postgres, spring-boot) should NOT appear.
    assert.doesNotMatch(out, /^expo$/m);
    assert.doesNotMatch(out, /^postgres$/m);
    assert.doesNotMatch(out, /^spring-boot$/m);
  } finally {
    process.stdout.write = orig;
    cleanup(fwRoot);
    cleanup(repoRoot);
  }
});

test("runDispatcher (Phase 09): invalid NISSTH_FRAMEWORK_ROOT exits 2 with invalid_framework_root error", async () => {
  const repoRoot = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  writeFileSync(join(repoRoot, "CLAUDE.md"), "# consumer\n");
  const bogus = mkdtempSync(join(tmpdir(), "nissth-bogus-"));
  const chunks = [];
  const origErr = process.stderr.write.bind(process.stderr);
  process.stderr.write = (s) => { chunks.push(s); return true; };
  try {
    await withEnv(FRAMEWORK_ENV, bogus, async () => {
      const code = await runDispatcher(["--list-bindings"], { repoRoot });
      assert.equal(code, 2);
    });
    const err = chunks.join("");
    assert.match(err, /does not contain a Bindings\/ subdirectory/);
  } finally {
    process.stderr.write = origErr;
    cleanup(repoRoot);
    cleanup(bogus);
  }
});

// --- Phase 26: --health ----------------------------------------------------
// `--list-bindings` exits 0 on a tree where nothing is built. These cases pin
// that `--health` fails on each way a tool can be unable to run, and passes on
// a tree where every one could.

import { healthReport } from "./dispatcher.js";
import { utimesSync } from "node:fs";
import { dirname as _dirname, resolve as _resolve } from "node:path";
import { fileURLToPath as _fileURLToPath } from "node:url";

const REAL_FRAMEWORK = _resolve(_dirname(_fileURLToPath(import.meta.url)), "..", "..");

const NODE_BINDING = (id) => ({
  dir: id,
  fileName: `${id}.bridge.json`,
  json: { binding: id, cli_entry: { runtime: "node", path: "dist/cli/index.js" }, tools: [{ name: `${id}_tool` }] },
});

function build(root, id, { srcNewer = false } = {}) {
  const b = join(root, "Bindings", id);
  mkdirSync(join(b, "dist", "cli"), { recursive: true });
  mkdirSync(join(b, "src"), { recursive: true });
  const entry = join(b, "dist", "cli", "index.js");
  const src = join(b, "src", "index.ts");
  writeFileSync(entry, "// built\n");
  writeFileSync(src, "// source\n");
  const old = new Date(Date.now() - 3_600_000);
  const now = new Date();
  // srcNewer: the build predates the source — what a pull without a rebuild leaves.
  utimesSync(entry, srcNewer ? old : now, srcNewer ? old : now);
  utimesSync(src, srcNewer ? now : old, srcNewer ? now : old);
}

async function withEnvAsync(key, value, fn) {
  const original = process.env[key];
  if (value === null) delete process.env[key];
  else process.env[key] = value;
  try {
    return await fn();
  } finally {
    if (original === undefined) delete process.env[key];
    else process.env[key] = original;
  }
}

function quiet(fn) {
  const orig = process.stdout.write;
  let out = "";
  process.stdout.write = (s) => ((out += s), true);
  return Promise.resolve()
    .then(fn)
    .then((v) => ({ v, out }))
    .finally(() => (process.stdout.write = orig));
}

test("parseArgv: --health, with --json in either order", () => {
  assert.equal(parseArgv(["--health"]).health, true);
  assert.deepEqual([parseArgv(["--health", "--json"]).json, parseArgv(["--json", "--health"]).json], [true, true]);
  // --json without --health is a binding's flag, not ours.
  assert.deepEqual(parseArgv(["alpha_tool", "--json"]).passthrough, ["--json"]);
});

test("--health: every binding built and current → healthy, exit 0", async () => {
  const root = makeSyntheticRepo([NODE_BINDING("alpha")]);
  try {
    build(root, "alpha");
    const r = await withEnvAsync(FRAMEWORK_ENV, null, () => healthReport(root));
    assert.deepEqual(r.problems, []);
    assert.equal(r.bindings[0].status, "ok");
    assert.equal(r.body.status, "n/a");
    const { v, out } = await quiet(() => withEnvAsync(FRAMEWORK_ENV, null, () => runDispatcher(["--health"], { repoRoot: root })));
    assert.equal(v, 0);
    assert.match(out, /HEALTHY/);
  } finally {
    cleanup(root);
  }
});

test("--health: an unbuilt binding is a problem — the case --list-bindings passes", async () => {
  const root = makeSyntheticRepo([NODE_BINDING("alpha"), NODE_BINDING("beta")]);
  try {
    build(root, "alpha");
    const r = await withEnvAsync(FRAMEWORK_ENV, null, () => healthReport(root));
    assert.deepEqual(r.bindings.map((b) => [b.binding, b.status]), [["alpha", "ok"], ["beta", "not-built"]]);
    assert.equal(r.problems.length, 1);
    assert.match(r.problems[0], /beta: not-built — build: cd ".*beta" && npm ci && npm run build/);
    const { v } = await quiet(() => withEnvAsync(FRAMEWORK_ENV, null, () => runDispatcher(["--health"], { repoRoot: root })));
    assert.equal(v, 1);
    const listed = await quiet(() => withEnvAsync(FRAMEWORK_ENV, null, () => runDispatcher(["--list-bindings"], { repoRoot: root })));
    assert.equal(listed.v, 0, "--list-bindings still passes, which is why --health exists");
  } finally {
    cleanup(root);
  }
});

test("--health: a build older than its source is a problem", async () => {
  const root = makeSyntheticRepo([NODE_BINDING("alpha")]);
  try {
    build(root, "alpha", { srcNewer: true });
    const r = await withEnvAsync(FRAMEWORK_ENV, null, () => healthReport(root));
    assert.equal(r.bindings[0].status, "build-older-than-source");
    assert.match(r.problems[0], /rebuild:/);
  } finally {
    cleanup(root);
  }
});

test("--health: a java-jar binding without java on PATH is a problem", async () => {
  const root = makeSyntheticRepo([
    { dir: "jvm", fileName: "jvm.bridge.json", json: { binding: "jvm", cli_entry: { runtime: "java-jar", path: "target/x.jar" }, tools: [] } },
  ]);
  try {
    mkdirSync(join(root, "Bindings", "jvm", "target"), { recursive: true });
    writeFileSync(join(root, "Bindings", "jvm", "target", "x.jar"), "jar");
    const r = await withEnvAsync(FRAMEWORK_ENV, null, () => healthReport(root, { javaAvailable: false }));
    assert.equal(r.bindings[0].status, "runtime-missing");
    const ok = await withEnvAsync(FRAMEWORK_ENV, null, () => healthReport(root, { javaAvailable: true }));
    assert.deepEqual(ok.problems, []);
  } finally {
    cleanup(root);
  }
});

test("--health: an unusable framework root is reported, not thrown", async () => {
  const repo = makeSyntheticRepo([]);
  const bogus = mkdtempSync(join(tmpdir(), "nissth-bogus-"));
  try {
    const r = await withEnvAsync(FRAMEWORK_ENV, bogus, () => healthReport(repo));
    assert.equal(r.frameworkRoot, null);
    assert.match(r.problems[0], /framework root unresolved/);
    const { v } = await quiet(() => withEnvAsync(FRAMEWORK_ENV, bogus, () => runDispatcher(["--health"], { repoRoot: repo })));
    assert.equal(v, 1);
  } finally {
    cleanup(repo);
    cleanup(bogus);
  }
});

test("--health: from a consumer, framework-body drift is a problem", async () => {
  // A consumer whose CLAUDE.md is not the framework body, checked against this
  // real checkout (the only tree that carries Tools/nissth-init).
  const consumer = mkdtempSync(join(tmpdir(), "nissth-consumer-"));
  try {
    writeFileSync(join(consumer, "CLAUDE.md"), "# Consumer\n\n> **Status:** old\n\n---\n\n## 1. Something else\n");
    mkdirSync(join(consumer, "AgentReports"), { recursive: true });
    writeFileSync(join(consumer, "AgentReports", "StatusUpdate.md"), "# ledger\n");
    const r = await withEnvAsync(FRAMEWORK_ENV, REAL_FRAMEWORK, () => healthReport(consumer, { javaAvailable: true }));
    assert.equal(r.via, "NISSTH_FRAMEWORK_ROOT env var");
    assert.equal(r.body.status, "drift");
    assert.match(r.body.detail, /framework body differs/);
    assert.ok(r.problems.some((p) => /consumer drift/.test(p) && /--check/.test(p)));
  } finally {
    cleanup(consumer);
  }
});

test("--health --json: machine-readable, ok mirrors the exit code", async () => {
  const root = makeSyntheticRepo([NODE_BINDING("alpha")]);
  try {
    const { v, out } = await quiet(() => withEnvAsync(FRAMEWORK_ENV, null, () => runDispatcher(["--health", "--json"], { repoRoot: root })));
    const j = JSON.parse(out);
    assert.equal(v, 1);
    assert.equal(j.ok, false);
    assert.equal(j.bindings[0].status, "not-built");
    assert.ok(Array.isArray(j.problems) && Array.isArray(j.notes));
  } finally {
    cleanup(root);
  }
});
