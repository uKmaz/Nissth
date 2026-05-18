// Tests for the nissth-bridge cross-binding dispatcher.
// Uses Node's built-in node:test + node:assert (no Jest, no installs).

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  findRepoRoot,
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
