---
report_type: snapshot
title: Phase 08 — Unified `nissth-bridge` Dispatcher — close snapshot
authored: 2026-05-18 by Claude (Opus 4.7)
last_updated: 2026-05-18 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-18 — Phase 08: Unified `nissth-bridge` Dispatcher — CLOSED
related_plans:
  - Phase_08_Unified_Bridge_Dispatcher
covers:
  - Tools/nissth-bridge/**
  - nissth-bridge (repo root)
  - nissth-bridge.ps1 (repo root)
  - CLAUDE.md §11.15
  - cli_entry field in all three .bridge.json manifests
supersedes:
  - none
---

# Phase 08 close snapshot — Unified `nissth-bridge` dispatcher

The PATH-collision callout that lived in the Phase 05–07 snapshot Reports is **resolved**. A single canonical `nissth-bridge` at the repo root dispatches across all installed bindings; per-binding launchers stay as escape hatches. Plain JavaScript (no TypeScript, no build step), zero runtime dependencies, ~330 lines.

## What shipped

| Artifact | Lines | Purpose |
|:---|:---|:---|
| `Tools/nissth-bridge/dispatcher.js` | ~330 | The dispatcher. Pure Node stdlib (`fs`, `path`, `url`, `child_process`). Exports tested functions for `node --test` use. |
| `Tools/nissth-bridge/package.json` | ~15 | Zero deps; `npm test` runs `node --test test.mjs`. |
| `Tools/nissth-bridge/README.md` | ~180 | Discovery model, flag reference, worked examples, conflict policy, adding-a-new-binding recipe. |
| `Tools/nissth-bridge/test.mjs` | ~280 | 24 test cases across parseArgv, discoverManifests, buildToolMap, resolveBindingForTool, buildSpawnSpec, runDispatcher (real + synthetic). |
| `Tools/nissth-bridge/_fixtures/BindingA + BindingB` | 2 files | Synthetic manifests used by test.mjs for conflict simulation against a tmp repo root. |
| `nissth-bridge` (repo root, POSIX) | ~22 | Shell launcher. Resolves `Tools/nissth-bridge/dispatcher.js` relative to itself; works from any cwd. |
| `nissth-bridge.ps1` (repo root, PowerShell) | ~20 | PowerShell equivalent. |
| `cli_entry` field in 3 manifests | +1 line each | Per-binding metadata: `{runtime: "node" \| "java-jar", path: "<rel-path>"}`. |
| `CLAUDE.md` §11.15 | ~25 lines added | New sub-section documenting the unified dispatcher. §11.5 byte-equal to its pre-Phase-08 state. |
| `README.md` (top-level) "Installing & using a binding" | rewritten | Repo-root dispatcher examples; per-binding launcher kept as escape hatch; old "Cross-binding collision (heads-up)" subsection replaced with a "Phase 08 — closed" note. |
| `Bindings/README.md` | +H2 + Step 7 in "Adding a new binding" | New "Cross-binding dispatcher" section; new-binding recipe includes `cli_entry` add. |

## Verification results

| Check | Status | Detail |
|:---|:---|:---|
| Dispatcher tests | ✅ 24/24 | `node --test`, ~106ms total. Covers parseArgv, discovery, buildToolMap, resolveBindingForTool, buildSpawnSpec, runDispatcher (real-repo + synthetic-fixtures). |
| `./nissth-bridge --list-bindings` | ✅ | Returns `expo`, `postgres`, `spring-boot` (alphabetical). |
| `./nissth-bridge --list-tools \| wc -l` | ✅ 14 | 15 total registrations; 1 deduped (`migration_status`). |
| `./nissth-bridge --describe entity_field_add` | ✅ | Returns the SpringBoot manifest entry. |
| Live dispatch: `./nissth-bridge route_lens --scope.root_path Bindings/Expo/tests/fixture` | ✅ | Produced `AgentReports/Bridge/route_lens_2026-05-18T170113Z.md`. Full exec path through the dispatcher validated end-to-end. |
| Conflict simulation (real `migration_status`) | ✅ | Exit 2 with `Tool 'migration_status' is registered by multiple bindings: postgres, spring-boot. Use --binding <stack> to disambiguate.` |
| `--binding spring-boot` disambiguation (real) | ✅ | Routes to `java -jar Bindings/SpringBoot/target/nissth-bridge-0.1.0.jar`. |
| `--binding postgres` disambiguation (real) | ✅ | Routes to `node Bindings/Postgres/dist/cli/index.js`. |
| Unknown tool | ✅ Exit 4 | `Unknown tool: 'ghost_tool'. Run --list-tools for the catalog.` |
| Unknown binding | ✅ Exit 4 | `Unknown binding: 'nonexistent'. Run --list-bindings to see installed bindings.` |
| Phase 05 regression | ✅ 104/104 | `./mvnw clean test -U -B`; BUILD SUCCESS at 2026-05-18T20:00:58+03:00; 10.249s. |
| Phase 06 regression | ✅ 51/51 | `npm test`; 12 suites, 8.623s. |
| Phase 07 regression | ✅ 76 pass / 18 skip | `npm test`; 5.522s. Strategy C carry-over. |

## The naturally-occurring conflict — a real-world stress test

Authoring the plan assumed `Bindings/_schemas/bridge-command.schema.json` was the only frozen contract and that **tool names would generally not collide across bindings**. The first end-to-end smoke after Step 2 revealed otherwise:

```
$ node Tools/nissth-bridge/dispatcher.js --list-tools | wc -l
14    # not 15
```

Both **Spring Boot** (Phase 05) and **Postgres** (Phase 07) register a tool named `migration_status`. The implementations are intentional and correct:

- SpringBoot's `migration_status` reads the Flyway/Liquibase history table of the Spring Boot project's bound database; scope is `--scope.root_path <project-dir>`.
- Postgres's `migration_status` reads the same tables but against any reachable Postgres database; scope is `--scope.extra.connection_string` or `NISSTH_PG_URL`.

Different freshness models, different scope keys, different invocation patterns — same tool name. Renaming either would break a published Phase 05 or Phase 07 contract; the §3.2 forbidden list explicitly prohibits per-binding source changes. **The conflict-resolution design was the right call**: refuse to guess, exit 2, suggest `--binding <stack>`. The framework now has a real example of when disambiguation is needed, and the README + CLAUDE.md §11.15 both document it.

This is actually a small framework-design lesson: **the bindings ARE the test suite for the dispatcher**. Synthetic fixtures (Tools/nissth-bridge/_fixtures/) are still useful for unit-test isolation, but the real cross-binding interactions surface only when real bindings sit side-by-side. Phase 08's `migration_status` conflict is the first signal of this; future bindings (Postgres action tools, Django, Rails, Redis, etc.) will surface more.

## `cli_entry` field — per-binding manifest metadata

The dispatcher needed a way to know how to spawn each binding's CLI. The cleanest landing was a new `cli_entry` field in each binding's `.bridge.json`:

```json
// Bindings/Expo/expo.bridge.json (excerpt)
{
  "binding": "expo",
  "binding_version": "0.1.0",
  ...
  "cli_entry": { "runtime": "node", "path": "dist/cli/index.js" },
  ...
}
```

**Schema-impact analysis (recorded in plan §3.1 Step 3):** `cli_entry` is per-binding manifest metadata, NOT part of `Bindings/_schemas/bridge-command.schema.json` (which is the command-input contract — the JSON shape the dispatcher sends INTO a binding, not the manifest shape). The per-binding manifest schema is per-binding by design; adding fields is plan-exempt under HR#12's framing of "real product code." The frozen cross-stack contract is untouched.

Supported `cli_entry.runtime` values:

| Runtime | Spawn command | Used by |
|:---|:---|:---|
| `"node"` | `node <abs-path-to-cli-entry>` | Expo, Postgres |
| `"java-jar"` | `java -jar <abs-path-to-cli-entry>` | Spring Boot |

Adding a new runtime (e.g., `"python"`, `"go-binary"`, `"shell"`) is a one-line change to `buildSpawnSpec()` in `dispatcher.js`.

## Discovery model

```javascript
// Tools/nissth-bridge/dispatcher.js — discoverManifests()
const bindingsDir = join(repoRoot, "Bindings");
for (const entry of readdirSync(bindingsDir)) {
  if (entry.startsWith("_") || entry === "_schemas") continue;   // skip _schemas/, _draft/, etc.
  // scan <binding-dir>/*.bridge.json files
  ...
}
```

Three rules:

1. **Walk-up repo-root resolution.** Same logic as the per-binding `repoRoot.ts` (Phase 06/07): walk up from `__dirname` until a directory containing `CLAUDE.md` is found. The dispatcher works from any cwd.
2. **Skip `_`-prefixed directories.** `_schemas/` (the cross-stack contract dir) is skipped, but so is any other future `_`-prefixed sibling — useful for `_draft/`, `_archived/`, etc.
3. **One `.bridge.json` per binding directory.** The dispatcher picks the first match; conventionally there's exactly one per binding (`<stack>.bridge.json`).

**No caching.** Every dispatcher invocation re-globs and re-parses. The cost is ~5ms per invocation (three small JSON files); the benefit is zero stale-cache bugs when adding/upgrading bindings.

## How the dispatcher tests work

`Tools/nissth-bridge/test.mjs` uses Node's built-in `node:test` + `node:assert`. Two test strategies:

1. **Real-repo tests** — run against the actual `Bindings/` directory. Validate that the three current bindings are discoverable, that `migration_status` is a real conflict, that `schema_lens`/`route_lens`/`entity_lens` route to the expected bindings.
2. **Synthetic-fixture tests** — `mkdtempSync(os.tmpdir())` → write a fake `CLAUDE.md` (so `findRepoRoot` resolves) + fake `Bindings/BindingA/stack-a.bridge.json` + similar for `BindingB`. Used for the conflict-simulation test (cleanest way to verify the policy without depending on the real `migration_status` collision continuing to exist).

The fixtures at `Tools/nissth-bridge/_fixtures/BindingA + BindingB` are static files referenced by the synthetic-conflict test. They live in version control so the tests are reproducible across hosts.

Tests do NOT spawn real binding CLIs. Phase 08 §3.1 Step 11's integration sanity-check (`./nissth-bridge route_lens --scope.root_path ...`) covers the live exec path — that's the one place we pay the binding-startup cost end-to-end. Result of that check: ✅ produced `route_lens_2026-05-18T170113Z.md`.

## Adding a new binding — recipe

The dispatcher requires zero changes when a new binding lands. The recipe (now documented in `Bindings/README.md`):

1. Author the binding under `Bindings/<NewStack>/`.
2. Drop a `<stack>.bridge.json` at the binding root with `binding`, `binding_version`, `contract_version`, `language`, `build_tool`, `tools[]`, and **`cli_entry: {"runtime": "...", "path": "..."}`**.
3. Build the binding so `cli_entry.path` resolves to a real artifact.
4. Pick tool names that don't collide with the current 14. (Run `./nissth-bridge --list-tools` to check.)
5. The next `./nissth-bridge --list-bindings` picks up the new binding automatically. No dispatcher rebuild, no manifest cache to invalidate.

If collisions are unavoidable (e.g., a new Django binding's `migration_status` that legitimately overlaps both SpringBoot's and Postgres's), the dispatcher already handles the case: exit 2 + `--binding <stack>` disambiguator.

## Divergences from Phase_08 plan §2

| Plan §2 row | Plan said | Actually did | Why |
|:---|:---|:---|:---|
| `./nissth-bridge --list-tools` count | "~15 — 5 Postgres + 5 Expo + 5 SpringBoot" | **14** (one deduped) | `migration_status` is registered by both SpringBoot and Postgres. Plan §2 assumed no naturally-occurring conflict. Result is actually more interesting — it gave us a real test case for the conflict policy. |

No other divergences. The dispatcher behaves exactly as §2's After table specifies for every other row.

## Known limitations / follow-ups

1. **`migration_status` conflict requires `--binding <stack>`.** Documented in CLAUDE.md §11.15, top-level README, dispatcher README. Won't be resolved by renaming (would break Phase 05 / Phase 07 contracts). Future Django/Rails bindings registering their own `migration_status` would add to the disambiguator list, not break anything.
2. **No multiplexing MCP shim.** Each binding's MCP shim stays per-binding. A unified MCP shim that dispatches across bindings (the MCP-layer analog of this CLI dispatcher) is a candidate for a future framework-hardening plan if MCP usage grows.
3. **No manifest caching.** Every dispatcher invocation re-globs `Bindings/*/*.bridge.json` and re-parses. ~5ms overhead is acceptable for a CLI tool; if dispatch becomes hot-path (unlikely), in-memory caching with mtime invalidation is straightforward.
4. **No environment-variable routing.** `--binding <stack>` is the only escape-hatch flag. No `NISSTH_BINDING` env var; deliberate per plan §3.2.
5. **Synthetic fixtures are slightly stale-prone.** `Tools/nissth-bridge/_fixtures/BindingA/stack-a.bridge.json` declares its own `contract_version: 1`; if the contract version ever bumps, both fixtures need updating. Mitigation: the contract version is intentionally pinned to 1 across the framework; a bump is a major event that touches every binding and would naturally update these fixtures too.
6. **No automated PATH-installation.** Users still manually symlink or add the repo root to PATH. A `npm run install-launcher` script or `make install` target could automate this; deferred.

## Implications for future work

- **Example consumer project** (`Desktop/ExampleApp/`) — now fully unblocked. The Example project can install one `nissth-bridge` on PATH and use any of the three Nissth bindings via a single command surface.
- **Future bindings** (Django, Rails, Redis, S3, etc.) — drop-in. Author the binding, add `cli_entry` to its manifest, done.
- **Future cross-binding MCP shim** — Phase 08 establishes the discovery + routing pattern at the CLI layer; the MCP analog would reuse `discoverManifests()` + `buildToolMap()` + `resolveBindingForTool()` and forward to per-binding shims (or directly to per-binding CLIs).
- **Future framework health checks** — the dispatcher knows where every binding lives + how to spawn its CLI. A `nissth-bridge --doctor` flag could chain through each binding's smoke test as a single command. Deferred.

## Pointers

- **Plan:** `ImplementationPlans/Phase_08_Unified_Bridge_Dispatcher.md`
- **Stack rules:** dispatcher is cross-cutting; no §8.x. New §11.15 added.
- **Discovery + routing source:** `Tools/nissth-bridge/dispatcher.js`
- **Flag reference:** `Tools/nissth-bridge/README.md`
- **Phase 05–07 snapshots (carry-over context):** `AgentReports/Reports/2026-05-{17,18}_phase-0{5,6,7}-*.md`

## Revision history

- 2026-05-18 — initial authoring on phase close.
