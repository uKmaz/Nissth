# Phase 09.5: Binding Framework-Root Awareness — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. Once approved, this plan is a contract; the executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_09_5_Binding_Framework_Root
- **Authored:** 2026-05-23 by Claude (Opus 4.7) in the Nissth framework observation session, after the UniHub-Frontend consumer session escalated a `Verified: HALTED`-class failure per HR#2 on Phase 00 §3 Step 2.
- **Approved:** 2026-05-23 by Emre Uçmaz
- **Depends on:** Phase_09_Framework_Root_Resolution (closed 2026-05-18 21:00). Phase 09 added two-root awareness to the dispatcher; this plan extends the same model to the binding implementations that Phase 09 missed.
- **Estimated scope:** Each JS binding (Expo, Postgres) gains a `findFrameworkRoot()` mirroring `Tools/nissth-bridge/dispatcher.js:65-90`. Repoint every framework-relative path constructor (currently `join(findRepoRoot(), "Bindings", "_schemas", …)`) to use `frameworkRoot`; keep `repoRoot` for `AgentReports/Bridge/` writes. SpringBoot binding is **exempt** — its schema loads as a classpath resource (`/bridge-command.schema.json` copied into the JAR via `pom.xml:148`), which is immune to the two-root divergence. Phase 09.5 touches **0 SpringBoot source files**, **~5 Expo source files** (`core/repoRoot.ts`, `core/JsonCommandParser.ts`, `core/ReportWriter.ts`, possibly `cli/index.ts`, plus test updates), and **~9 Postgres source files** (same 3 core files + 5 tools that call `findRepoRoot()` + test updates). Each binding's `package.json` patch-version bumps. **No contract change** — `Bindings/_schemas/bridge-command.schema.json` is untouched.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the bug surface and the exact callsite classification before any edit.

### 1.1 Inputs to read

- **DBL:** none — Nissth core has no DBL.
- **Bridge reports:** none produced by Phase 09.5 itself (this plan modifies binding source; live Bridge invocations happen at §4 verification).
- **Source (audit + change):**
  - `Tools/nissth-bridge/dispatcher.js:30-90` — canonical `findFrameworkRoot` model (env var → submodule convention → `repoRoot` fallback). Bindings will mirror this.
  - `Bindings/Expo/src/core/repoRoot.ts` (entire — currently single function, 30 LOC).
  - `Bindings/Expo/src/core/JsonCommandParser.ts:9-13` (`loadSchema()` callsite — schema lookup, BROKEN).
  - `Bindings/Expo/src/core/ReportWriter.ts:10-12` (`loadFrontmatterValidator()` callsite — schema lookup, BROKEN); also lines 79-86 (`write()` uses constructor-injected `repoRoot` for `AgentReports/Bridge/` — CORRECT, keep).
  - `Bindings/Expo/src/cli/index.ts:67` — audit what `findRepoRoot()` resolves there.
  - `Bindings/Expo/tests/unit/ReportWriter.test.ts:13-34, 140` — pre-fix tests use single-root fixture; will need a two-root variant.
  - `Bindings/Expo/tests/integration/_support.ts:15, 22` — `REPO_ROOT = findRepoRoot()` baked in at module load; revisit.
  - `Bindings/Expo/tests/contract/SchemaValidation.test.ts:32` — schema-load callsite in tests.
  - `Bindings/Postgres/src/core/repoRoot.ts` (entire — byte-equivalent to Expo's).
  - `Bindings/Postgres/src/core/JsonCommandParser.ts:9-13` (BROKEN).
  - `Bindings/Postgres/src/core/ReportWriter.ts:10-12` (BROKEN, same shape as Expo).
  - `Bindings/Postgres/src/tools/{IndexAudit,LockAudit,MigrationStatus,QueryPlan,SchemaLens}.ts` — each imports `findRepoRoot`; classify each line.
  - `Bindings/Postgres/tests/unit/ReportWriter.test.ts:12`, `tests/contract/SchemaValidation.test.ts:16` — schema-load callsites.
  - `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/JsonCommandParser.java:22-47` (CONFIRM EXEMPT — classpath resource, no filesystem `Bindings/` lookup).
  - `Bindings/Expo/package.json`, `Bindings/Postgres/package.json` (version field).
  - `Bindings/Expo/expo.bridge.json`, `Bindings/Postgres/postgres.bridge.json` (binding_version field — verify whether duplicated or sourced from package.json).
- **Reports:** `AgentReports/Reports/2026-05-23_phase-09-binding-frameworkroot-gap.md` (Nissth-side incident, authored alongside this plan); `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Reports\2026-05-23_phase-09-binding-frameworkroot-gap.md` (consumer-side discovery report).
- **StatusUpdate.md:** latest entry — `2026-05-23 01:42 — First real consumer install (UniHub) — Phase 09 recipe field test` (this Nissth tree) and `2026-05-23 02:51 — Phase 00 §3 attempt blocked by binding-side framework-root gap` (UniHub-Frontend tree).

### 1.2 Diagnostic actions

> Bridge tools first per HR#4, but Phase 09.5 is a framework-side fix; the bindings ARE the artifact being fixed, so Bridge tooling against them is meta-circular. Pre-Flight uses Read + Grep + Bash, not Bridge.

| # | Action | Tool / command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm baseline test suites green pre-fix | `cd Bindings/Expo && npm run clean && npm ci && npm test`; same for `Bindings/Postgres`; `cd Bindings/SpringBoot && ./mvnw clean test -U -B`; `cd Tools/nissth-bridge && npm test` | each binding + dispatcher | Establish that the bug doesn't show up in current test suites (it doesn't — proves the test gap that §3 closes). |
| 2 | Enumerate every `findRepoRoot()` callsite | `Grep -n 'findRepoRoot\b' Bindings/Expo/src Bindings/Postgres/src` | both JS binding src/ trees | Classify each line: schema-load (broken; repoint to `frameworkRoot`) vs report-destination (correct; keep `repoRoot`). |
| 3 | Enumerate every `Bindings/_schemas/` or `Bindings/` literal-path constructor | `Grep -n 'Bindings\\/_schemas\\|join.*Bindings.*_schemas' Bindings/Expo/src Bindings/Postgres/src` | both JS bindings | Catch any other framework-relative path constructor the §3 step list might miss. |
| 4 | Confirm SpringBoot exempt | re-read `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/JsonCommandParser.java:22-47` + grep `Bindings/SpringBoot/src/main/java` for filesystem `Bindings/` joins | SpringBoot main src | If any non-classpath `Bindings/_schemas/` join exists, scope expands; otherwise SpringBoot stays exempt. |
| 5 | Confirm `bridge.json` manifest version coupling | read both `<stack>.bridge.json` files | manifests | Determine whether `binding_version` is sourced from `package.json` or duplicated; if duplicated, both update in §3 step. |
| 6 | Confirm dispatcher spawns bindings with `process.env` inheritance | re-read `Tools/nissth-bridge/dispatcher.js` (spawn site around line 450) | dispatcher | `NISSTH_FRAMEWORK_ROOT` MUST reach the binding CLI's `process.env` for the fix to work. (Frontend agent's report cites lines 452-456 as `env: process.env` — verify in this turn.) |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Baseline Expo tests | 51 pass / 0 fail | _to be filled_ | _to be filled_ |
| Baseline Postgres tests | 76 pass / 18 skip / 0 fail | _to be filled_ | _to be filled_ |
| Baseline SpringBoot tests | 104 pass / 0 fail / 0 error | _to be filled_ | _to be filled_ |
| Baseline dispatcher tests | 32 pass / 0 fail | _to be filled_ | _to be filled_ |
| `findRepoRoot()` callsites in `Bindings/Expo/src/` | 3 broken (core/JsonCommandParser, core/ReportWriter, audit cli/index.ts) | _to be filled_ | _to be filled_ |
| `findRepoRoot()` callsites in `Bindings/Postgres/src/` | 7+ — classify: core/JsonCommandParser + core/ReportWriter (broken); tools/*.ts (audit — likely report-write related, keep) | _to be filled_ | _to be filled_ |
| `Bindings/_schemas/` literal in JS binding main src | 4 (2 per binding: schema-load in JsonCommandParser + ReportWriter) | _to be filled_ | _to be filled_ |
| SpringBoot main src has any filesystem `Bindings/` join | NO (classpath-only) | _to be filled_ | _to be filled_ |
| `binding_version` in `*.bridge.json` matches `package.json` version | YES (duplicated; both must bump) | _to be filled_ | _to be filled_ |
| Dispatcher spawns binding CLI with `env: process.env` | YES (confirmed by frontend agent's report; reverify) | _to be filled_ | _to be filled_ |

**Stop condition:** Any `Match? = no` row → STOP, append `Verified: FAIL`, request re-plan. Particularly: if SpringBoot's main src DOES have a filesystem `Bindings/` join, the scope expands by one binding and §3 step list must be extended in a re-plan before execution proceeds.

---

## 2. Expected State

### Before

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Expo/src/core/repoRoot.ts` | exports | `findRepoRoot` only |
| `Bindings/Expo/src/core/JsonCommandParser.ts:9-13` | schema path | `join(findRepoRoot(), "Bindings", "_schemas", "bridge-command.schema.json")` |
| `Bindings/Expo/src/core/ReportWriter.ts:10-12` | schema path | same (broken) |
| `Bindings/Postgres/src/core/repoRoot.ts` | exports | `findRepoRoot` only |
| `Bindings/Postgres/src/core/JsonCommandParser.ts` | schema path | same broken pattern |
| `Bindings/Postgres/src/core/ReportWriter.ts` | schema path | same broken pattern |
| Live invocation of `expo_doctor_lens` / `query_plan` / etc. from a two-root consumer repo | result | ENOENT on `<consumerRepo>/Bindings/_schemas/bridge-command.schema.json` |
| `Bindings/SpringBoot/src/main/java/.../JsonCommandParser.java` | schema load | classpath resource `/bridge-command.schema.json` (already correct) |

### After

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Expo/src/core/repoRoot.ts` | exports | `findRepoRoot` AND `findFrameworkRoot` (latter mirrors dispatcher's three-tier model) |
| `Bindings/Expo/src/core/JsonCommandParser.ts` | schema path | `join(findFrameworkRoot(findRepoRoot()), "Bindings", "_schemas", "bridge-command.schema.json")` |
| `Bindings/Expo/src/core/ReportWriter.ts` | schema path | same — uses `findFrameworkRoot` |
| `Bindings/Expo/src/core/ReportWriter.ts` | `write()` destination | UNCHANGED — `join(this.repoRoot, "AgentReports", "Bridge", …)` |
| `Bindings/Postgres/src/core/repoRoot.ts` | exports | `findRepoRoot` AND `findFrameworkRoot` |
| `Bindings/Postgres/src/core/JsonCommandParser.ts` | schema path | uses `findFrameworkRoot` |
| `Bindings/Postgres/src/core/ReportWriter.ts` | schema path | uses `findFrameworkRoot`; `write()` destination unchanged |
| Live invocation of `expo_doctor_lens` from UniHub-Frontend (two-root) | result | success; report at `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Bridge\expo_doctor_lens_<ts>.md` |
| `Bindings/Expo/package.json` `version` | bump | next patch (e.g., `0.2.0` → `0.2.1`) |
| `Bindings/Postgres/package.json` `version` | bump | next patch |
| `Bindings/{Expo,Postgres}/<stack>.bridge.json` `binding_version` | bump | matches `package.json` (if duplicated; verified in §1.3) |
| `Bindings/Expo/dist/`, `Bindings/Postgres/dist/` | rebuild | fresh `npm run build` outputs reflecting source changes |
| Per-binding test count | net | Expo +2 to +3 tests; Postgres +2 to +3 tests; SpringBoot unchanged at 104; dispatcher unchanged at 32 |

---

## 3. Execution (EXECUTE)

> Each step is atomic and verifiable.

**Branch:** Create and check out `nissth/phase-09-5-binding-framework-root` off `master` before any modification.

### 3.1 Step list

- [x] **Step 1 — Snapshot.** Per HR#9, copy the 6 about-to-be-edited files to `AgentReports/Snapshots/before_phase09_5/`: `Bindings/Expo/src/core/{repoRoot,JsonCommandParser,ReportWriter}.ts`, `Bindings/Postgres/src/core/{repoRoot,JsonCommandParser,ReportWriter}.ts`. **Acceptance:** 6 snapshot files exist.
- [x] **Step 2 — Pre-Flight baseline.** Run §1.2 actions 1-6. Fill §1.3 Findings. **Acceptance:** all rows have `Match? = yes`; any `no` → STOP per §1.3 Stop condition.
- [x] **Step 3 — Expo: extend `repoRoot.ts`.** Add `findFrameworkRoot(repoRoot)` function mirroring `dispatcher.js:65-90` exactly: tier 1 = `NISSTH_FRAMEWORK_ROOT` env var (validates `Bindings/` subdir exists, else throws `Error` with the same message shape as the dispatcher's `DispatchError(2, …, "invalid_framework_root")`); tier 2 = `<repoRoot>/Tools/Nissth/`; tier 3 = `repoRoot` fallback. Export both functions. **File:** `Bindings/Expo/src/core/repoRoot.ts`. **Acceptance:** the file exports `findRepoRoot` AND `findFrameworkRoot`; signature `findFrameworkRoot(repoRoot: string): string`; no other change.
- [x] **Step 4 — Expo: update `JsonCommandParser.ts`.** In `loadSchema()` (lines 9-13), change `findRepoRoot()` → `findFrameworkRoot(findRepoRoot())`. Add the corresponding import. **Acceptance:** the schema path now resolves under the framework root; no other change.
- [x] **Step 5 — Expo: update `ReportWriter.ts`.** In `loadFrontmatterValidator()` (lines 10-12), same change as Step 4. **Crucially, do NOT touch `write()` at lines 79-86** — `this.repoRoot` there stays correct for `AgentReports/Bridge/` destination. **Acceptance:** schema path resolves under framework root; report-write destination unchanged (cite line 79-86 in commit message).
- [x] **Step 6 — Expo: audit `cli/index.ts` and any other `findRepoRoot()` callsite.** From §1.3 Findings, classify any remaining callsite. If it's a schema/framework-relative lookup, repoint. If it's a report-destination, leave alone. **Acceptance:** every callsite in `Bindings/Expo/src/` is either (a) using `findFrameworkRoot` for framework-relative paths, or (b) using `findRepoRoot` for report destinations, with a one-line comment justifying.
- [x] **Step 7 — Postgres: extend `repoRoot.ts`.** Mirror of Step 3. **Note:** the two `repoRoot.ts` files are byte-equivalent; the diff applied to both should be identical. (DRY refactor to a shared module is OUT OF SCOPE per §3.2.)
- [x] **Step 8 — Postgres: update `JsonCommandParser.ts` + `ReportWriter.ts`.** Mirror of Steps 4-5.
- [x] **Step 9 — Postgres: audit `tools/*.ts` callsites.** Per §1.3 Findings classification, each of `IndexAudit`, `LockAudit`, `MigrationStatus`, `QueryPlan`, `SchemaLens` calls `findRepoRoot()` at one line. If a tool uses `repoRoot` to construct an `AgentReports/Bridge/`-bound path, leave alone. If it uses `repoRoot` to read `Bindings/...` or similar, repoint. **Acceptance:** each callsite classified + handled per §3.1 Step 6's rule.
- [x] **Step 10 — Update Expo tests.**
  - `tests/unit/ReportWriter.test.ts`: add a new test case `frameworkRoot diverges from repoRoot — schema validates from frameworkRoot, report writes to repoRoot`. Sets up two `mkdtempSync` dirs, copies the schema into the framework dir, sets `NISSTH_FRAMEWORK_ROOT`, invokes `ReportWriter`, asserts report lands in the repo dir.
  - `tests/contract/SchemaValidation.test.ts`: confirm still PASS with the new resolution path (schema-load now via `findFrameworkRoot`).
  - `tests/integration/_support.ts`: review `REPO_ROOT = findRepoRoot()` baked-in resolution; if the integration test setup expects single-root, add a `FRAMEWORK_ROOT` companion + `withFrameworkRoot()` helper.
  - **Acceptance:** Expo unit tests run +2 cases (net) and all PASS.
- [x] **Step 11 — Update Postgres tests.** Same shape: add 2 new unit/contract cases covering two-root divergence. **Acceptance:** Postgres unit + contract tests run +2 (or +3) cases and all PASS.
- [x] **Step 12 — Bump `package.json` version.** In both `Bindings/Expo/package.json` and `Bindings/Postgres/package.json`, increment the patch version. **Acceptance:** versions bumped (e.g., 0.2.0 → 0.2.1 if that's current; verify in §1.3 first).
- [x] **Step 13 — Bump `<stack>.bridge.json` `binding_version` if duplicated.** Per §1.3 Findings row 5 — if the manifest's `binding_version` field is independent of `package.json`'s, bump it to match.
- [x] **Step 14 — Rebuild dist.** `cd Bindings/Expo && npm run clean && npm ci && npm run build`. Same for `Bindings/Postgres`. **Acceptance:** both `dist/cli/index.js` files updated; mtime newer than source.
- [x] **Step 15 — Per-binding regression sweep.** Run each binding's full test suite from scratch:
  - `cd Bindings/Expo && npm test` → Expo 51 + new tests, all PASS.
  - `cd Bindings/Postgres && npm test` → Postgres 76 pass + new tests + 18 skip (skips unchanged).
  - `cd Bindings/SpringBoot && ./mvnw clean test -U -B` → 104/104 PASS (no source changes; this is a regression-protection check).
  - `cd Tools/nissth-bridge && npm test` → 32/32 PASS (dispatcher untouched).
  - **Acceptance:** all four suites green; numbers match expectations from §1.3 + new-test count.
- [x] **Step 16 — Synthetic two-root smoke (per binding).** Create a tmpdir `consumerRepo/` containing only a `CLAUDE.md` marker. Set `NISSTH_FRAMEWORK_ROOT=<absolute path to this Nissth checkout>`. From `consumerRepo/`, invoke each JS binding's CLI directly (`node $NISSTH_FRAMEWORK_ROOT/Bindings/Expo/dist/cli/index.js --json-stdin <<<'{"tool":"expo_doctor_lens"}'` and analogous for Postgres). **Acceptance:** binding spawns, schema validates, report writes to `consumerRepo/AgentReports/Bridge/`. Failure = §4.4.
- [x] **Step 17 — LIVE smoke against the UniHub-Frontend consumer repo.** `cd C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend && ./nissth-bridge.ps1 expo_doctor_lens`. **Acceptance:** exit 0; report file path printed by the launcher; report exists at `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Bridge\expo_doctor_lens_<ts>.md`; report frontmatter `freshness` field present.
- [x] **Step 18 — Append a Nissth status entry per §6.** This closes the framework-side work.
- [x] **Step 19 — Cross-repo handoff status entries.** Append a status entry to `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\StatusUpdate.md` saying "Phase 09.5 closed; resume Phase 00 §3 from Step 2." Same for `unihub-backend` if its earlier paused state warrants it (verify whether the backend session recorded a pause; if not, just leave a note explaining the unblock is available). **Acceptance:** the consumer repos' `Next:` fields point at "resume Phase 00 §3" rather than "blocked on Phase 09.5."

### 3.2 Forbidden in this phase

- **No SpringBoot source changes.** Classpath schema loading already works correctly under two-root divergence.
- **No `Tools/nissth-bridge/dispatcher.js` changes.** Phase 09's dispatcher logic stays exactly as shipped 2026-05-18.
- **No changes to `Bindings/_schemas/bridge-command.schema.json`.** The contract is stable across this fix.
- **No DRY refactor.** The two TS `repoRoot.ts` files staying byte-equivalent is fine for this phase; extracting a shared `core/roots.ts` module across `Bindings/Expo` and `Bindings/Postgres` is OUT OF SCOPE (would require restructuring each binding's package; defer to a future phase).
- **No CLAUDE.md changes.** A future Phase 09.6 (doc-only) may add a sentence to §11.15 noting that bindings honor `NISSTH_FRAMEWORK_ROOT` too; this phase is code-only and CLAUDE.md edits are not plan-exempt under HR#12.
- **No `Bindings/<stack>/README.md` content changes** beyond a one-line note about framework-root handling, if any. The READMEs are documentation; substantial doc rewriting is a separate phase.
- **No node / Java version bumps. No major-version dep bumps (`ajv`, `Jackson`, etc.).** Only patch-bump the binding packages themselves.
- **No new bindings, no new tools.** Phase 09.5 closes a regression; it does not extend the surface.
- **No edits to UniHub-Frontend / unihub-backend product code.** The §3.19 status-entry appends are framework-administrative; product code stays untouched (those repos' Phase 00s are paused, not modified).

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Each binding's test suite runs after `npm run clean && npm ci` (or `mvnw clean test -U -B` for SpringBoot) — no cached state from the pre-fix build.
- `dist/cli/index.js` mtime is checked against source mtime; rebuild confirmed fresh.
- Live smoke from the UniHub-Frontend consumer repo (Step 17) runs the rebuilt binding via its actual launcher path; freshness stamp in the produced report verifies the binding's reported `binding_version` matches the bump from Step 12-13.
- Synthetic two-root smoke (Step 16) uses fresh tmpdirs per invocation — no env-var bleed-over between tools.

### 4.2 Checks

- [x] **Build:** `npm run build` succeeds in both `Bindings/Expo` and `Bindings/Postgres`. `dist/cli/index.js` produced in each; non-zero file size; has `#!/usr/bin/env node` shebang.
- [x] **Tests — Expo:** `npm test` → 51 (baseline) + new tests; net target ≥53; 0 fail. **Actual: 58/58 PASS.**
- [x] **Tests — Postgres:** `npm test` → 76 pass / 18 skip (baseline) + new tests; net target ≥78 pass / 18 skip; 0 fail. **Actual: 83 pass / 18 skip / 0 fail / 101 total.**
- [x] **Regression — SpringBoot:** `./mvnw clean test -U -B` → 104/104 PASS, no source touched. Failure here = REGRESSION (Phase 09.5 should be SpringBoot-neutral). **Actual: 104/104 PASS.**
- [x] **Regression — dispatcher:** `cd Tools/nissth-bridge && npm test` → 32/32 PASS; dispatcher untouched. **Actual: 32/32 PASS.**
- [x] **Synthetic two-root smoke (Expo):** binding spawns, schema validates, report writes to synthetic `consumerRepo/AgentReports/Bridge/`. Frontmatter `freshness.source` recorded. **Actual: exit 0; report at `<tmp>/nissth-step16-expo-6QcLuv/AgentReports/Bridge/route_lens_2026-05-23T103223Z.md` with `binding_version: 0.1.1`.**
- [x] **Synthetic two-root smoke (Postgres):** same, for at least one tool that doesn't require a live PG (e.g., `migration_status` will gracefully error on no `connection_string`; that's fine — schema lookup happens before the connection attempt and is what we're testing). **Actual: exit 2; BridgeError `{stage: "validate", error_code: "no_connection_string"}` — reached the binding's validate stage AFTER schema-load succeeded.**
- [x] **LIVE smoke (UniHub-Frontend):** `./nissth-bridge.ps1 expo_doctor_lens` from the consumer repo's cwd → exit 0; report at `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Bridge\`. **Actual: exit 0; `expo_doctor_lens_2026-05-23T104125Z.md` written.**

### 4.3 Pass criteria

ALL of:
- Build green for both JS bindings.
- Test counts match expectations: Expo ≥53 pass / 0 fail; Postgres ≥78 pass / 18 skip / 0 fail; SpringBoot 104/104; dispatcher 32/32.
- Synthetic two-root smoke succeeds for both Expo and Postgres.
- Live smoke from UniHub-Frontend succeeds — report file exists at the expected path.
- §1.3 Findings all `Match? = yes`.
- §3 Step list all checkboxes ticked.

### 4.4 Failure handling

Per template: STOP, append `Verified: FAIL` status entry citing the specific check, author an `incident` Report at `AgentReports/Reports/2026-05-23_phase-09-5-fail-<slug>.md` per §10.4(1). Roll back via Step 1 snapshots if needed. Do not retry silently — user decides.

---

## 5. Cleanup

- [x] Remove the Step 1 snapshot files once §4 PASS — they're rollback artifacts; not needed after the phase closes successfully. (Keep until the close status entry is appended.) **Actual: `AgentReports/Snapshots/before_phase09_5/` removed after Step 17 PASS and §6 status entry appended at 13:45.**
- [x] **Reports check (CLAUDE.md §10):** Phase 09.5 closes a non-trivial framework phase per §10.4(4) — author `AgentReports/Reports/2026-05-23_phase-09-5-binding-framework-root-snapshot.md` (kind: `snapshot`) summarizing the diff per binding, test counts before/after, and the live-smoke confirmation. Cross-link the companion `2026-05-23_phase-09-binding-frameworkroot-gap.md` incident Report (authored alongside this plan; documents the discovery). **Actual: snapshot Report authored 2026-05-23 at close.**
- [x] **Document Sync sweep (HR#11):**
  - Source files modified: `Bindings/Expo/src/core/{repoRoot,JsonCommandParser,ReportWriter}.ts`, `Bindings/Postgres/src/core/{repoRoot,JsonCommandParser,ReportWriter}.ts`, plus any tool files touched in Step 9, plus tests and `package.json` per binding.
  - Affected stable docs:
    - `Bindings/README.md` — if it describes the binding architecture, may want a one-line note about framework-root awareness. UPDATE inline if a relevant sentence exists; otherwise no action.
    - `Bindings/Expo/README.md`, `Bindings/Postgres/README.md` — same.
    - **`CLAUDE.md` §11.15** — currently describes framework-root resolution as dispatcher-only. May want a sentence: "Bindings honor the same three-tier resolution for framework-relative paths (schema loads); see each binding's `core/repoRoot.ts`." But CLAUDE.md is **NOT plan-exempt** under HR#12 — this would need its own follow-up Phase 09.6 (doc-only). For this phase: MARK STALE in CLAUDE.md §11.15's `last_regenerated`-equivalent if one exists, OR add a Phase 09.6 candidate to the §6 status entry's `Next:` field.
    - `Tools/nissth-bridge/README.md` — describes the dispatcher only; no change needed for Phase 09.5.
  - Log result in §6 `Doc sync:` line.
- [x] No orphan branches. The `nissth/phase-09-5-binding-framework-root` branch is ready to merge per user instruction (PR if user wants review, fast-forward otherwise).

---

## 6. Status Update Entry

After Cleanup completes, append the following (filled in) to `AgentReports/StatusUpdate.md`:

```
### YYYY-MM-DD HH:MM — Phase 09.5: Binding Framework-Root Awareness — CLOSED

**State:**
- Phase: 9.5 — JS bindings (Expo + Postgres) now honor NISSTH_FRAMEWORK_ROOT for schema/framework-relative paths; SpringBoot binding confirmed exempt (classpath schema loading).
- Build: CLEAN
- Tests: PASS — Expo <N> / Postgres <N pass + 18 skip> / SpringBoot 104 / dispatcher 32; new two-root cases pass.
- Active plan: ImplementationPlans/Phase_09_5_Binding_Framework_Root.md
- DBL refs: none — Nissth has no DBL.
- Bridge reports: synthetic two-root smoke reports per Step 16; live smoke report at C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Bridge\expo_doctor_lens_<ts>.md (Step 17).
- Blockers: none.

**Report:**
- §1.3 Findings: <condensed actual answers>.
- Confirmed SpringBoot exemption — its classpath schema loading is immune.
- Postgres tool callsites in §3.9 classified <N as keep, N as repoint>; <details>.

**Executed:**
- Steps 1-19 per plan §3.1, all checkboxes ticked.
- Modified: <list per binding>.
- Bumped <package.json + bridge.json> for both JS bindings.

**Verified:**
- All test suites green per §4.2.
- Synthetic two-root smoke + live smoke against UniHub-Frontend succeeded.
- Freshness: per §4.1.
- Doc sync: <result>; CLAUDE.md §11.15 Phase 09.6 doc-only candidate queued.
- Reports: AgentReports/Reports/2026-05-23_phase-09-binding-frameworkroot-gap.md (incident, discovery — authored at plan-author time), AgentReports/Reports/2026-05-23_phase-09-5-binding-framework-root-snapshot.md (snapshot, close — authored in Cleanup).

**Issues:**
- (or "none")

**Next:**
- Resume consumer-side Phase 00s.
- UniHub-Frontend agent boots, reads its latest status entry (appended in Step 19), sees "Phase 09.5 closed; resume Phase 00 §3 from Step 2", re-runs `expo_doctor_lens` as freshness smoke, proceeds through Phase 00 §3 Steps 3-19.
- UniHub-Backend agent similarly unblocked once JDK 21 download completes.
- Phase 09.6 doc-only candidate: add a sentence to CLAUDE.md §11.15 noting binding-side framework-root honoring. Plan required per HR#12.
```
