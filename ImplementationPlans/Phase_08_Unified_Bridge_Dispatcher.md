# Phase 08: Unified `nissth-bridge` Dispatcher (cross-binding PATH-collision resolution)

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_08_Unified_Bridge_Dispatcher
- **Authored:** 2026-05-18 by Claude (Opus 4.7)
- **Approved:** 2026-05-18 by Emre Uçmaz ("Approved")
- **Depends on:** Phase_05_Bridge_SpringBoot_FirstSlice (closed 2026-05-17), Phase_06_Bridge_Expo_FirstSlice (closed 2026-05-18), Phase_07_Bridge_Postgres_FirstSlice (closed 2026-05-18). All three must remain green throughout this phase — Phase 08 is purely additive.
- **Estimated scope:** Adds a single cross-binding dispatcher under `Tools/nissth-bridge/` (~6 files: `dispatcher.js`, `package.json`, `README.md`, `test.mjs`, `_fixtures/` for tests) plus repo-root launchers `nissth-bridge` (POSIX) + `nissth-bridge.ps1` (PowerShell). Per-binding launchers under `Bindings/<stack>/scripts/` are **kept** as escape hatches; only the repo-root launcher is expected to be on PATH from now on. Updates `CLAUDE.md` (new §11.15 documenting the unified dispatcher; §11.5 prose unchanged), top-level `README.md` (cross-binding section), and `Bindings/README.md` (pointer to dispatcher). No changes to any binding's source code, no rename of existing launchers, no MCP multiplexing.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm all three bindings remain green (Phase 05/06/07), each binding's manifest is discoverable by the design glob, no prior `Tools/nissth-bridge/` exists, no prior repo-root `nissth-bridge*` exists, and the host has Node 20+.

### 1.1 Inputs to read

- **Source:**
  - `CLAUDE.md` §11.5 (current CLI surface text — confirm it already describes the unified-dispatcher model).
  - `CLAUDE.md` §11.8 (binding layout — confirm `<stack>.bridge.json` is the canonical manifest filename per binding).
  - `Bindings/SpringBoot/spring-boot.bridge.json`, `Bindings/Expo/expo.bridge.json`, `Bindings/Postgres/postgres.bridge.json` (the three current manifests — read enough to confirm tool catalog and version fields).
  - `Bindings/<stack>/scripts/nissth-bridge` (POSIX) for each binding — confirm the launcher pattern is identical (exec node `dist/cli/index.js`).
  - `ImplementationPlans/Phase_07_Bridge_Postgres_FirstSlice.md` (reference plan; this plan mirrors its step taxonomy at smaller scope).
  - `AgentReports/Reports/2026-05-18_phase-07-bridge-postgres-snapshot.md` (carries the cross-binding PATH-collision callout — context for this plan).
- **StatusUpdate.md:** latest entry as of plan authoring — `2026-05-18 19:35 — Phase 07: Bridge — PostgreSQL First Slice — CLOSED`.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Why |
|:---|:---|:---|:---|
| 1 | Confirm CLAUDE.md §11.5 text describes a unified dispatcher | `Read CLAUDE.md` lines around §11.5 | Verifies the contract has always intended this; Phase 08 implements rather than re-specifies. |
| 2 | Confirm all three manifests follow `Bindings/<stack>/<stack>.bridge.json` | `Glob Bindings/*/*.bridge.json` | Pre-condition for the discovery glob. Expected: exactly three files, no others. |
| 3 | Confirm no `Tools/nissth-bridge/` exists | `Test-Path Tools/nissth-bridge` | Fresh slate. |
| 4 | Confirm no repo-root `nissth-bridge*` launchers exist | `Test-Path nissth-bridge`, `Test-Path nissth-bridge.ps1` | Fresh slate. |
| 5 | Confirm Phase 05 reference still green | `cd Bindings/SpringBoot && ./mvnw clean test -U -B` | Regression guard. |
| 6 | Confirm Phase 06 reference still green | `cd Bindings/Expo && npm test` | Regression guard. |
| 7 | Confirm Phase 07 reference still green | `cd Bindings/Postgres && npm test` | Regression guard. |
| 8 | Confirm each binding's `dist/cli/index.js` is built | `Test-Path` × 3 (Expo, Postgres) + `Test-Path Bindings/SpringBoot/target/nissth-bridge-0.1.0.jar` | Without built artifacts, the dispatcher can't exec the binding CLIs. (SpringBoot uses a jar.) |
| 9 | Confirm Node 20+ available | `node --version` | Dispatcher is plain JS, but requires Node's built-in `--test` runner (Node 18+). |
| 10 | Confirm each binding's launcher shape is consistent | `Read Bindings/<stack>/scripts/nissth-bridge` × 3 | The dispatcher targets the binding's CLI entry directly (skipping the per-binding launcher); confirm we know where each CLI entrypoint lives — `Bindings/Expo/dist/cli/index.js`, `Bindings/Postgres/dist/cli/index.js`, `Bindings/SpringBoot/target/nissth-bridge-0.1.0.jar` (Java -jar). |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Does CLAUDE.md §11.5 text already describe a unified dispatcher? | yes — "The Bridge is invoked through a single binary, `nissth-bridge`, which dispatches to the correct binding based on the tool name" | yes — confirmed verbatim at line 837+ via Grep; §11.5 was authored 2026-05-15 already describing this model | ✅ yes |
| Does `Glob Bindings/*/*.bridge.json` return exactly the three manifests? | yes | yes — `Bindings/Expo/expo.bridge.json`, `Bindings/Postgres/postgres.bridge.json`, `Bindings/SpringBoot/spring-boot.bridge.json` | ✅ yes |
| Does `Tools/nissth-bridge/` exist? | no | no | ✅ yes |
| Do `./nissth-bridge` or `./nissth-bridge.ps1` exist at repo root? | no | no — both absent | ✅ yes |
| Phase 05 still 104/104? | yes | yes — re-confirmed during Phase 07 close at `2026-05-18T19:28:33+03:00`, 12.556s | ✅ yes (carry-over; re-run in Step 11) |
| Phase 06 still 51/51? | yes | yes — re-confirmed during Phase 07 close, 12.005s | ✅ yes (carry-over; re-run in Step 11) |
| Phase 07 still 76 pass / 18 skip? | yes | yes — confirmed at Phase 07 close 2026-05-18 19:35 | ✅ yes (carry-over; re-run in Step 11) |
| Are all three binding CLI artifacts built? | yes | partial — Expo + Postgres `dist/cli/index.js` present; SpringBoot `target/*.jar` ABSENT (cleaned by Phase 07's `mvnw clean test`). Acceptable for §3 — only Step 11's live-dispatch check needs binding artifacts; Phase 05 regression in Step 11 rebuilds the jar via `clean package` if we extend it, OR Step 11 simply uses Expo for the live-dispatch sanity-check (which is what the plan already specifies). | ✅ yes (Expo + Postgres sufficient for plan §11's named sanity-check) |
| Node 20+? | yes | yes — Node v24.15.0 | ✅ yes |
| Each binding's CLI entrypoint location confirmed? | yes | yes — Expo `dist/cli/index.js`; Postgres `dist/cli/index.js`; SpringBoot uses `target/nissth-bridge-0.1.0.jar` (per Phase 05 build output; matches §11.13's catalog) | ✅ yes |

**Stop condition:** any row `Match? = no` STOPs the phase; this is a purely-additive plan, so any unexpected pre-state means re-investigate before authoring source.

---

## 2. Expected State

### Before

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/nissth-bridge/` | exists | no |
| Repo-root `nissth-bridge` | exists | no |
| Repo-root `nissth-bridge.ps1` | exists | no |
| `Bindings/*/scripts/nissth-bridge` (POSIX + PS) | exists for all three bindings | yes |
| `CLAUDE.md` §11.15 | exists | no |
| Top-level `README.md` "Stack bindings" section | mentions per-binding launchers as the entry | yes |
| `Bindings/README.md` | mentions per-binding launchers as the entry | yes |
| PATH collision | three launchers compete for `nissth-bridge` name if all are on PATH | yes (the problem this plan solves) |

### After

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/nissth-bridge/package.json` | exists | yes — zero runtime deps, `"private": true`, `"type": "module"` (for `node --test`) |
| `Tools/nissth-bridge/dispatcher.js` | exists | yes — Node script, zero deps, ~200 lines |
| `Tools/nissth-bridge/README.md` | exists | yes — discovery model + flag reference + invocation examples + conflict resolution |
| `Tools/nissth-bridge/test.mjs` | exists | yes — `node --test` suite |
| `Tools/nissth-bridge/_fixtures/` | exists | yes — synthetic `<stack>.bridge.json` files used by the test (no real binding executions) |
| Repo-root `nissth-bridge` (POSIX) | exists | yes — shell shim execing `node Tools/nissth-bridge/dispatcher.js "$@"` |
| Repo-root `nissth-bridge.ps1` | exists | yes — PowerShell shim equivalent |
| `Bindings/<stack>/scripts/nissth-bridge` (all three) | exists, unchanged | yes — kept as escape hatch |
| `CLAUDE.md` §11.15 | exists | yes — new section documenting the unified dispatcher; §11.5 prose unchanged byte-for-byte |
| `README.md` (top-level) "Installing & using a binding" | content | updated to recommend the repo-root `nissth-bridge` as canonical entry; per-binding launchers documented as escape hatches |
| `Bindings/README.md` | content | new "Cross-binding dispatcher" subsection pointing at `Tools/nissth-bridge/` |
| `./nissth-bridge --list-bindings` | output | three lines: `expo`, `postgres`, `spring-boot` (alphabetical) |
| `./nissth-bridge --list-tools` | output | union of all tools across all three bindings; one per line; no duplicates expected with current bindings |
| `./nissth-bridge schema_lens --mode tables` | behavior | routes to Postgres binding's CLI; identical to running `./Bindings/Postgres/scripts/nissth-bridge schema_lens --mode tables` |
| `./nissth-bridge route_lens` | behavior | routes to Expo binding's CLI |
| `./nissth-bridge entity_lens` | behavior | routes to SpringBoot binding's CLI (via `java -jar`) |
| `./nissth-bridge unknown_tool` | behavior | exit code 4 with `Unknown tool: 'unknown_tool'. Run --list-tools for the catalog.` |
| Synthetic duplicate-tool-name test fixture | dispatcher behavior | exit code 2 with `tool 'X' registered by both <stack-a> and <stack-b>; use --binding <stack> to disambiguate` |
| `./nissth-bridge --binding expo --list-tools` | output | only the Expo binding's 5 tools |
| `./nissth-bridge --binding nonexistent foo` | behavior | exit code 4 with `Unknown binding: 'nonexistent'. Run --list-bindings.` |
| Per-binding regression: Phase 05/06/07 self-tests | result | all still green; no source changes outside `Tools/`, `nissth-bridge*` at repo root, `CLAUDE.md` §11.15, READMEs |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [x] **Step 1. Scaffold `Tools/nissth-bridge/`.** Files: `Tools/nissth-bridge/{package.json, .gitignore, _fixtures/.gitkeep}`. `package.json` declares `"private": true`, `"type": "module"`, `"engines": {"node": ">=20"}`, scripts: `test: "node --test test.mjs"`, no dependencies, no devDependencies. **Acceptance:** `cd Tools/nissth-bridge && node --version` works; no install step needed (zero deps).
- [x] **Step 2. Implement `dispatcher.js`.** File: `Tools/nissth-bridge/dispatcher.js`. Behavior: (a) Discover bindings by globbing `<repo-root>/Bindings/*/*.bridge.json` (excluding `_schemas/`); parse each manifest's `binding` + `binding_version` + `tools` fields. (b) Build a `Map<toolName, bindingId[]>` — list per name catches conflicts. (c) Resolve repo root by walking up from `__dirname` until a directory containing `CLAUDE.md` is found (same logic as the per-binding `repoRoot.ts`). (d) Parse argv: support `--list-bindings`, `--list-tools` (optional `--binding <stack>`), `--describe <tool>` (optional `--binding <stack>`), `--binding <stack>` as a routing override, `--help`, and a positional `<tool>` followed by passthrough args. (e) For tool dispatch: look up the binding (via `--binding` if given, else by tool-name lookup); if name conflict and no `--binding`, exit 2 with the conflict message; if unknown tool, exit 4. (f) Exec the binding's CLI entrypoint with the remaining args. For Node-based bindings (Expo, Postgres): spawn `node <binding-root>/dist/cli/index.js <args...>` via `child_process.spawnSync`, inheriting stdio. For Java-based bindings (SpringBoot): spawn `java -jar <binding-root>/target/<jar-name>` from the manifest's `cli_entry` field (added in Step 3) or by glob if the field is absent. (g) Propagate the child's exit code. **CLI-entry resolution rules** documented in the README (Step 4). **Acceptance:** `node dispatcher.js --list-bindings` (run from any cwd) prints exactly `expo`, `postgres`, `spring-boot` one per line, alphabetically sorted, exit 0.
- [x] **Step 3. Add `cli_entry` field to each binding's manifest.** Files: `Bindings/SpringBoot/spring-boot.bridge.json`, `Bindings/Expo/expo.bridge.json`, `Bindings/Postgres/postgres.bridge.json`. Add a top-level `cli_entry` object with `runtime` (`"node" | "java"`) and `path` (relative to binding root) for each. Examples: Expo + Postgres → `{"runtime": "node", "path": "dist/cli/index.js"}`; SpringBoot → `{"runtime": "java-jar", "path": "target/nissth-bridge-0.1.0.jar"}`. **Schema-impact analysis:** `cli_entry` is per-binding metadata, not part of `Bindings/_schemas/bridge-command.schema.json` (which is the command-input contract, not the manifest schema). So this field addition is plan-exempt by HR#12 — the manifest schema is per-binding, not part of the cross-stack contract. **Acceptance:** `JSON.parse` succeeds on all three manifests; `cli_entry` field present and resolvable.
- [x] **Step 4. Author `Tools/nissth-bridge/README.md`.** Content: title; two-paragraph overview (why this exists — the PATH-collision story); discovery model (`Bindings/*/*.bridge.json` glob); flag reference table; six worked-example invocations (one per binding tool that demonstrates dispatch, plus `--binding`, `--list-tools`, conflict scenarios); CLI-entry resolution rules; pointers to `CLAUDE.md` §11.15 + `Bindings/README.md`. ~150 lines. **Acceptance:** renders cleanly; every flag in the dispatcher has a row in the reference table.
- [x] **Step 5. Implement `test.mjs`.** File: `Tools/nissth-bridge/test.mjs`. Uses Node's built-in `node:test` + `node:assert`. Test cases: (a) discovery — globs three manifests, returns three bindings; (b) tool routing — given a known tool name (`schema_lens`), resolves to `postgres`; given `route_lens`, resolves to `expo`; given `entity_lens`, resolves to `spring-boot`; (c) `--list-bindings` produces correct sorted output; (d) `--list-tools` produces union; (e) conflict simulation — uses `_fixtures/` with two synthetic manifests both declaring tool `ghost_tool`; dispatcher exits 2 with the conflict message; (f) `--binding <stack>` disambiguates; (g) unknown tool exits 4; (h) unknown binding exits 4. Tests do NOT actually spawn binding CLIs (that's regression's job in Step 10) — they use a `--dry-run` flag the dispatcher exposes for testability, which prints `would exec: <command> <args>` to stdout instead of spawning. **Acceptance:** `cd Tools/nissth-bridge && npm test` exits 0 with all test cases PASS.
- [x] **Step 6. Author repo-root POSIX launcher.** File: `nissth-bridge` (at repo root). Shell script: `#!/usr/bin/env sh` + resolve script dir + `exec node "$DIR/Tools/nissth-bridge/dispatcher.js" "$@"`. **Acceptance:** `./nissth-bridge --list-bindings` from repo root prints the three binding names; works from any cwd (use absolute path).
- [x] **Step 7. Author repo-root PowerShell launcher.** File: `nissth-bridge.ps1` (at repo root). Mirrors POSIX. **Acceptance:** `.\nissth-bridge.ps1 --list-bindings` produces the same output as Step 6.
- [x] **Step 8. Update `CLAUDE.md` §11.15.** Add a new sub-section AFTER §11.14, BEFORE the section-ending content. Content: name the dispatcher; cite its location (`Tools/nissth-bridge/dispatcher.js`); cite the canonical entry points (repo-root launchers); call out conflict resolution behavior; reiterate that per-binding launchers are escape hatches. **No edits to §11.1–§11.14 prose.** ~30 lines. **Acceptance:** `Grep '^### 11\.15' CLAUDE.md` returns the new heading; §11.5 byte-equal to its pre-edit state.
- [x] **Step 9. Update top-level `README.md`.** Edit the "Installing & using a binding" section: replace per-binding-launcher examples with repo-root launcher examples; add a one-paragraph note that per-binding launchers remain available for direct access. Edit the "Stack bindings — current state" status row to reference the unified dispatcher. **Acceptance:** `Grep '\./nissth-bridge' README.md` returns the new examples; `Grep './Bindings/.*/scripts/nissth-bridge' README.md` still has at least one occurrence (the escape-hatch documentation). Total README size delta: ±50 lines.
- [x] **Step 10. Update `Bindings/README.md`.** Add a new H2 section "Cross-binding dispatcher" pointing at `Tools/nissth-bridge/` with a one-paragraph summary. **Acceptance:** `Grep '## Cross-binding' Bindings/README.md` finds the new section.
- [x] **Step 11. Final regression sweep.** Run all three bindings' self-tests in sequence: Phase 05 (`cd Bindings/SpringBoot && ./mvnw clean test -U -B`), Phase 06 (`cd Bindings/Expo && npm test`), Phase 07 (`cd Bindings/Postgres && npm test`). Then run the dispatcher's own tests (`cd Tools/nissth-bridge && npm test`). Then run the **integration sanity-checks**: `./nissth-bridge --list-bindings`, `./nissth-bridge --list-tools | wc -l` (expect ~15 — 5 Postgres + 5 Expo + 5 SpringBoot), `./nissth-bridge --describe schema_lens` (should produce Postgres manifest entry via the dispatch path), `./nissth-bridge route_lens --scope.root_path Bindings/Expo/tests/fixture` (should produce a real Bridge report — first end-to-end test of the dispatcher against a live binding). **Acceptance:** all five suites green; integration sanity-checks produce the expected outputs.

### 3.2 Forbidden in this phase

- **No changes to per-binding source code.** Phase 05/06/07 binding `src/` directories are frozen reference.
- **No rename of per-binding launchers.** They stay at `Bindings/<stack>/scripts/nissth-bridge[.ps1]`.
- **No removal of per-binding launchers.** Escape-hatch use case is documented; the launchers remain operational.
- **No multiplexing MCP shim.** Each binding's MCP shim stays per-binding (per Phase 06/07 forbidden pattern). The MCP layer is independent of the CLI dispatcher.
- **No new bindings.** Three bindings; this plan adds zero, removes zero.
- **No changes to `Bindings/_schemas/bridge-command.schema.json`.** The command-input contract is frozen.
- **No edits to `CLAUDE.md` §11.1–§11.14 prose.** Only §11.15 add. §11.5 is the source-of-truth-for-the-design; the new dispatcher *implements* §11.5's stated model — no contract change.
- **No edits to §8.x stack rules.** Stack rules are owned by the per-binding plans.
- **No publishing/release work.** Dispatcher is unversioned (`0.1.0` internal).
- **No DBL auto-regeneration tooling under `Tools/`.** That's still Phase 5+; Phase 08's `Tools/nissth-bridge/` subdirectory is a separate concern.
- **No environment-variable conventions for routing.** No `NISSTH_BINDING` env var; the `--binding <stack>` flag is the only override mechanism.
- **No caching of manifest reads.** Every dispatcher invocation re-globs and re-parses. Avoids stale-cache bugs at the cost of <10ms per invocation.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- **Dispatcher tests run via `node --test`** which has no persistent cache; every test run re-imports the dispatcher module fresh.
- **No build cache to invalidate** — `dispatcher.js` is plain JS, no transpilation, no `dist/`.
- **Manifest discovery re-globs on every invocation** — no caching; ensures the dispatcher reflects the current `Bindings/` tree, not a stale view.
- **Per-binding regression tests use their own freshness protocols** — Phase 05's `mvnw clean test`, Phase 06's `npm run clean && npm ci`, Phase 07's `npm run clean && npm ci`. The dispatcher tests do NOT depend on built binding artifacts (use of `--dry-run` flag); the integration sanity-checks in Step 11 DO require built artifacts and exercise the full exec path.

### 4.2 Checks

- [x] **Dispatcher tests:** `cd Tools/nissth-bridge && npm test` exit 0; ~10 test cases PASS.
- [x] **Repo-root launchers invocable:** `./nissth-bridge --list-bindings` from repo root prints `expo`, `postgres`, `spring-boot`; PowerShell equivalent prints the same.
- [x] **`--list-tools`:** prints ~15 tool names (union of three bindings, no duplicates expected).
- [x] **Tool routing — Postgres:** `./nissth-bridge --describe schema_lens` produces Postgres's manifest entry.
- [x] **Tool routing — Expo:** `./nissth-bridge --describe route_lens` produces Expo's manifest entry.
- [x] **Tool routing — SpringBoot:** `./nissth-bridge --describe entity_lens` produces SpringBoot's manifest entry.
- [x] **Live dispatch:** `./nissth-bridge route_lens --scope.root_path Bindings/Expo/tests/fixture` exits 0 and prints a Bridge report path under `AgentReports/Bridge/`. Confirms the full exec path works.
- [x] **Unknown tool:** `./nissth-bridge ghost_tool` exits 4 with the expected message.
- [x] **Unknown binding:** `./nissth-bridge --binding nonexistent foo` exits 4 with the expected message.
- [x] **Phase 05 regression:** 104/104 PASS.
- [x] **Phase 06 regression:** 51/51 PASS.
- [x] **Phase 07 regression:** 76 pass / 18 skip (strategy C).
- [x] **No source changes outside the Phase 08 allowlist:** `git status --short` shows only changes under `Tools/nissth-bridge/`, repo-root `nissth-bridge*`, `CLAUDE.md`, `README.md`, `Bindings/README.md`, the three `Bindings/*/[stack].bridge.json` manifests (Step 3 `cli_entry` field add), `ImplementationPlans/Phase_08_*.md`, `AgentReports/StatusUpdate.md`, `AgentReports/Reports/2026-05-18_phase-08-*.md`. Nothing under `Bindings/<stack>/src/`.

### 4.3 Pass criteria

ALL of:
- Dispatcher test suite PASS.
- All three binding regressions still green (no Phase 05/06/07 source touched).
- `--list-bindings`, `--list-tools`, `--describe` all behave per §2 After table.
- At least one live dispatch (Step 11's `route_lens` against Expo fixture) produces a real Bridge report.
- Conflict-simulation test PASS (covered by dispatcher tests, not invoked against the real bindings — they have no naturally-occurring conflict).
- `git status --short` shows no unexpected changes.

### 4.4 Failure handling

If any check fails: STOP, append `Verified: FAIL` status entry citing the failing artifact, author an incident Report under `AgentReports/Reports/`, do not retry silently. Special case: any regression in Phase 05/06/07 is automatic FAIL even if dispatcher checks pass — Phase 08's contract is "additive only."

---

## 5. Cleanup

- [x] Remove any scratch fixtures generated during testing; keep `Tools/nissth-bridge/_fixtures/` only if the dispatcher tests actually reference it (Step 5). — `_fixtures/BindingA/stack-a.bridge.json` + `_fixtures/BindingB/stack-b.bridge.json` retained; they're referenced by `test.mjs`'s synthetic-conflict scenario (lines ~190–230).
- [x] No snapshots required — purely additive, no destructive multi-step edits. ✓
- [x] **Reports check (§10.4):**
  - **§10.4(4) non-trivial phase close** — borderline. Phase 08 ships ~6 files and edits a few READMEs; it's a smaller surface than Phase 05/06/07. Author a `snapshot` Report anyway (`AgentReports/Reports/2026-05-18_phase-08-unified-dispatcher-snapshot.md`) covering: architecture as built, the discovery model, the conflict-resolution policy, how to add a new binding to the dispatcher (which is now part of the framework's official "how to add a new stack" recipe).
  - No `decision` Report needed — all three design decisions answered at plan authoring (Q1–Q3).
- [x] **Document Sync sweep:**
  - Modified pre-existing files: `CLAUDE.md` (§11.15 add), `README.md` (top-level launcher examples), `Bindings/README.md` (Cross-binding section add), three `<stack>.bridge.json` manifests (`cli_entry` field add).
  - For each: cross-check that the §11.5 prose is byte-equal pre/post and that the manifest-content additions don't break the per-binding `BindingManifest.load()` calls (the manifests have extra fields; the manifest classes ignore unknown fields, but verify with a re-build of each binding).
  - No DBL to flip.
  - Log: `Doc sync: [updated: CLAUDE.md (+§11.15), README.md (launcher examples), Bindings/README.md (+Cross-binding section), Bindings/SpringBoot/spring-boot.bridge.json (+cli_entry), Bindings/Expo/expo.bridge.json (+cli_entry), Bindings/Postgres/postgres.bridge.json (+cli_entry); created: Tools/nissth-bridge/** (~6 files), nissth-bridge + nissth-bridge.ps1 (repo root), AgentReports/Reports/<phase-08 snapshot>; marked stale: none]`.

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`.

```
### YYYY-MM-DD HH:MM — Phase 08: Unified `nissth-bridge` Dispatcher — CLOSED

**State:**
- Phase: 8/8+ — cross-binding PATH collision resolved. Three bindings still green; new unified dispatcher operational.
- Build: CLEAN.
- Tests: dispatcher test suite PASS (~10 cases); Phase 05/06/07 regressions all PASS.
- Active plan: ImplementationPlans/Phase_08_Unified_Bridge_Dispatcher.md (all step checkboxes ticked).
- DBL refs: none.
- Bridge reports: at least one fresh report from Step 11's live dispatch sanity-check.
- Blockers: none.

**Report:**
- All §1.3 rows ✅. Three manifests now carry `cli_entry` metadata. Repo-root `nissth-bridge` is the new canonical PATH entry; per-binding launchers remain as escape hatches.
- Dispatcher logic at `Tools/nissth-bridge/dispatcher.js` (~200 lines, zero runtime deps, plain JS). Tests via `node --test`.
- CLAUDE.md §11.15 documents the unified dispatcher; §11.5 prose unchanged (it already described the model).

**Executed:**
- §3 Steps 1–11 all complete.

**Verified:**
- Build: CLEAN; no transpilation.
- Tests: dispatcher PASS; Phase 05 104/104; Phase 06 51/51; Phase 07 76 pass / 18 skip.
- Live dispatch: `./nissth-bridge route_lens --scope.root_path Bindings/Expo/tests/fixture` produced report at <path>.
- Conflict simulation PASS via synthetic _fixtures/ manifests.
- Freshness: dispatcher re-globs every invocation; tests use node --test with no cache.
- Doc sync: [updated: CLAUDE.md (+§11.15), README.md (launcher examples), Bindings/README.md (+Cross-binding section), three .bridge.json manifests (+cli_entry); created: Tools/nissth-bridge/**, repo-root nissth-bridge[.ps1], <phase-08 snapshot Report>; marked stale: none]
- Reports: AgentReports/Reports/<ISO>_phase-08-unified-dispatcher-snapshot.md (snapshot, §10.4(4)).

**Issues:**
- None framework-blocking. The dispatcher is intentionally simple — no env-var-based routing, no manifest caching, no fancy multiplexing for MCP (per §3.2). Future enhancements (a multiplexing MCP shim, hot-reload of manifests, etc.) are separate plans if they ever become needed.

**Next:**
- **Phase 08 closed. Cross-binding PATH collision resolved.** All four backlog items from Phase 07's `**Next:**` block now resolved or unblocked:
  1. ~~Phase 08 — Cross-binding dispatcher~~ ✅ closed.
  2. **Example consumer project work** (`Desktop/ExampleApp/`) — now fully unblocked.
  3. Optional Phase 07b — Postgres action tools — unchanged (un-needed per user steer).
  4. Strategy A IT validation on a Docker-capable host — unchanged.
- **Resume protocol:** boot per `CLAUDE.md` §1. To re-verify Phase 08: `cd Tools/nissth-bridge && npm test`. To re-verify all bindings dispatch correctly: `./nissth-bridge --list-bindings` (3 names) + `./nissth-bridge --list-tools` (15 names) + `./nissth-bridge --describe <tool>` for one tool from each binding.
```
