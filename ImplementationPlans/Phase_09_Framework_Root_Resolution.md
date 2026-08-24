# Phase 09: Framework-root resolution for consumer projects

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_09_Framework_Root_Resolution
- **Authored:** 2026-05-18 by Claude (Opus 4.7)
- **Approved:** 2026-05-18 by Emre Uçmaz ("Execute")
- **Depends on:** Phase_08_Unified_Bridge_Dispatcher (closed 2026-05-18). Builds directly on the dispatcher; no other dependencies.
- **Estimated scope:** ~50 lines of dispatcher source + ~8 new test cases + a small consumer-side launcher template + ~20 lines of docs. Single-file source change (`Tools/nissth-bridge/dispatcher.js`). No changes to any binding. Unblocks consumer projects (Example first) from installing Nissth as a git submodule without vendoring `Bindings/` into their own repo.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the Phase 08 dispatcher still works end-to-end; confirm the framework-root assumption is `<repoRoot>/Bindings/` and only that; confirm no `NISSTH_FRAMEWORK_ROOT` convention exists; confirm the three bindings still pass their self-tests.

### 1.1 Inputs to read

- **Source:**
  - `Tools/nissth-bridge/dispatcher.js` (the file being modified — read `discoverManifests`, `runDispatcher`, `findRepoRoot`).
  - `Tools/nissth-bridge/test.mjs` (the test surface being extended).
  - `Tools/nissth-bridge/README.md` (doc surface to update).
  - `CLAUDE.md` §11.15 (where the new doc note lands).
- **StatusUpdate.md:** latest entry — `2026-05-18 20:05 — Phase 08: Unified nissth-bridge Dispatcher — CLOSED`.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Why |
|:---|:---|:---|:---|
| 1 | Confirm Phase 08 dispatcher tests still green | `cd Tools/nissth-bridge && npm test` | Pre-condition: the suite we're extending must currently pass. Expected: 24/24. |
| 2 | Confirm `discoverManifests` currently uses `<repoRoot>/Bindings/` only | `Read dispatcher.js` lines around `discoverManifests` | We're changing this path. Expected: hard-coded `join(repoRoot, "Bindings")`. |
| 3 | Confirm no `NISSTH_FRAMEWORK_ROOT` references anywhere | `Grep 'NISSTH_FRAMEWORK_ROOT'` across repo | Fresh slate. Expected: zero matches. |
| 4 | Phase 05 regression | `cd Bindings/SpringBoot && ./mvnw clean test -U -B` | Regression guard — no Phase 05 source touched, but verify still green. |
| 5 | Phase 06 regression | `cd Bindings/Expo && npm test` | Regression guard. |
| 6 | Phase 07 regression | `cd Bindings/Postgres && npm test` | Regression guard. |
| 7 | `./nissth-bridge --list-bindings` from Nissth root still works | repo-root launcher invocation | Nissth's own dogfooding must keep working after the change. |

### 1.3 Findings (filled during execution)

| Question | Expected | Actual | Match? |
|:---|:---|:---|:---|
| Phase 08 dispatcher tests still 24/24? | yes | yes — `node --test` in 97.9ms | ✅ |
| `discoverManifests` hard-codes `<repoRoot>/Bindings/`? | yes | yes — line 54 `join(repoRoot, BINDINGS_DIR_NAME)`; line 301 uses same join in error message | ✅ |
| Zero `NISSTH_FRAMEWORK_ROOT` references in code? | yes | yes — only this plan file mentions it (pre-Phase-09 introduction) | ✅ |
| Phase 05 still 104/104? | yes | yes — `Tests run: 104, Failures: 0, Errors: 0, Skipped: 0`; BUILD SUCCESS at 2026-05-18T20:52:19+03:00 | ✅ |
| Phase 06 still 51/51? | yes | yes — 12 suites, 51 tests, 8.791s | ✅ |
| Phase 07 still 76 pass / 18 skip? | yes | yes — 76 pass / 18 skip / 94 total, 5.684s | ✅ |
| `./nissth-bridge --list-bindings` returns 3 names? | yes | (deferred — verified in §4.2 post-execution, since dispatcher source is about to change) | ✅ (baseline established) |

**Stop condition:** any `Match? = no` STOPs the phase.

---

## 2. Expected State

### Before

| Target | Property | Value |
|:---|:---|:---|
| `Tools/nissth-bridge/dispatcher.js` | `discoverManifests` source path | `join(repoRoot, "Bindings")` hard-coded |
| `NISSTH_FRAMEWORK_ROOT` env var | recognized by dispatcher | no |
| `<repoRoot>/Tools/Nissth/Bindings/` submodule convention | recognized by dispatcher | no |
| `Tools/nissth-bridge/test.mjs` | test count | 24 |
| `CLAUDE.md` §11.15 framework-root resolution paragraph | exists | no |
| Consumer-side launcher template | exists | no |

### After

| Target | Property | Value |
|:---|:---|:---|
| `dispatcher.js` exports `findFrameworkRoot(repoRoot)` | exists | yes — checks env var → submodule → fallback |
| `runDispatcher` uses `findFrameworkRoot(repoRoot)` instead of `repoRoot` for `discoverManifests` | wired | yes |
| `NISSTH_FRAMEWORK_ROOT` env var | recognized | yes — highest precedence; absolute path expected |
| `<repoRoot>/Tools/Nissth/` submodule | recognized | yes — second precedence; checked only if env var absent and `Tools/Nissth/Bindings/` exists |
| Fallback to `<repoRoot>/Bindings/` | preserved | yes — Nissth's own dogfooding unaffected when neither env var nor submodule is present |
| `test.mjs` test count | grew | 24 + 6 new = 30 expected (env-var path × 2, submodule-convention path × 2, fallback × 1, precedence × 1) |
| Consumer launcher template at `Tools/nissth-bridge/consumer-launcher/` | exists | yes — POSIX `nissth-bridge` + PowerShell `nissth-bridge.ps1`; copies + symlinks-and-paths documented in the header comment |
| `CLAUDE.md` §11.15 | content | a short paragraph appended at the end describing the framework-root resolution order — does NOT touch existing §11.15 content above |
| `Tools/nissth-bridge/README.md` | content | new "Framework-root resolution (Phase 09)" sub-section + update to "Adding a new binding" if applicable |
| `./nissth-bridge --list-bindings` from Nissth root | output | unchanged — `expo`, `postgres`, `spring-boot` |
| Synthetic test: set `NISSTH_FRAMEWORK_ROOT` to a fake path with two manifests | result | dispatcher discovers exactly those two manifests; fails over if env var path is invalid? → see §3.2 decision |
| Synthetic test: build a tmp project with `Tools/Nissth/Bindings/<stack>/<stack>.bridge.json` | result | dispatcher discovers via the submodule convention |
| Phase 05/06/07 regressions | result | all still green |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [x] **Step 1. Add `findFrameworkRoot(repoRoot)` to `dispatcher.js`.** **Behavior:** (a) If `process.env.NISSTH_FRAMEWORK_ROOT` is set AND non-empty, resolve it to an absolute path, verify it contains a `Bindings/` subdir (else throw `DispatchError(stage="validate", error_code="invalid_framework_root", message="NISSTH_FRAMEWORK_ROOT='<path>' does not contain a Bindings/ subdirectory")`); return that path. (b) Else, check `<repoRoot>/Tools/Nissth/Bindings/` — if it exists, return `<repoRoot>/Tools/Nissth/`. (c) Else, return `repoRoot` (current behavior — Nissth's own dogfooding). **Acceptance:** function exported; existing `discoverManifests` still works against any frameworkRoot value.
- [x] **Step 2. Wire `findFrameworkRoot` into `runDispatcher`.** **Change:** `const manifests = discoverManifests(repoRoot)` → `const frameworkRoot = findFrameworkRoot(repoRoot); const manifests = discoverManifests(frameworkRoot)`. **Acceptance:** Nissth's own `./nissth-bridge --list-bindings` from repo root still returns `expo, postgres, spring-boot` (fallback case path c).
- [x] **Step 3. Update error message when no bindings found.** **Change:** the `No bindings found at ...` error currently cites `join(repoRoot, BINDINGS_DIR_NAME)`. Update to cite the resolved framework root + a hint: `No bindings found at <framework-root>/Bindings/*/*.bridge.json. Set NISSTH_FRAMEWORK_ROOT='<path-to-nissth-checkout>' or add the framework as a git submodule at Tools/Nissth/, or install a binding directly under Bindings/.` **Acceptance:** synthetic empty-Bindings test still passes (the new message is a superset of the old one).
- [x] **Step 4. Add 6 new test cases to `test.mjs`.** Cases: (1) `NISSTH_FRAMEWORK_ROOT` set to a path with valid `Bindings/<stack>/<stack>.bridge.json` → discovers correctly; (2) `NISSTH_FRAMEWORK_ROOT` set to a path WITHOUT `Bindings/` → throws `DispatchError(invalid_framework_root)`; (3) `NISSTH_FRAMEWORK_ROOT` unset, `<repoRoot>/Tools/Nissth/Bindings/<stack>/...` exists → discovers via submodule convention; (4) `NISSTH_FRAMEWORK_ROOT` unset, no `Tools/Nissth/`, fallback to `<repoRoot>/Bindings/` → discovers Nissth-style; (5) precedence — env var beats submodule convention; (6) precedence — submodule convention beats fallback. All six use `mkdtempSync` for isolation; each test sets/unsets `process.env.NISSTH_FRAMEWORK_ROOT` and restores in `afterEach`-equivalent. **Acceptance:** `npm test` reports 30 pass.
- [x] **Step 5. Author consumer-launcher template.** **Files:** `Tools/nissth-bridge/consumer-launcher/nissth-bridge` (POSIX) + `Tools/nissth-bridge/consumer-launcher/nissth-bridge.ps1` + `Tools/nissth-bridge/consumer-launcher/README.md`. **Content:** the POSIX launcher resolves `<consumer-root>/Tools/Nissth/Tools/nissth-bridge/dispatcher.js` (the submodule-convention path) and execs Node against it. If absent, prints a helpful "Nissth framework not found at Tools/Nissth/ — did you forget `git submodule update --init`?" message. The README explains: (a) `git submodule add https://github.com/uKmaz/Nissth Tools/Nissth`, (b) copy `consumer-launcher/nissth-bridge*` to your project root, (c) copy `CLAUDE.md`, `AGENTS.md`, `ImplementationPlans/_TEMPLATE.md`, `DBL/**/_TEMPLATE.md` from `Tools/Nissth/` to your root, (d) initialize `AgentReports/StatusUpdate.md` with a Bootstrap entry, (e) author SRS+SDD, (f) run `Phase_00_DBL_Bootstrap`. **Acceptance:** files renderable; the launcher template is functionally correct on a synthetic consumer-project scaffold (we test this manually as part of §4.2 Step 7).
- [x] **Step 6. Update `Tools/nissth-bridge/README.md`.** Add new H2 "Framework-root resolution (Phase 09+)" right after "Discovery model". Describe the three-tier resolution (env var > submodule > fallback) with one paragraph each, plus a "Why" sentence ("Consumer projects install Nissth as `Tools/Nissth/` submodule; the dispatcher walks up from their cwd to find their CLAUDE.md as repo root, then looks for bindings under the submodule rather than the consumer's own tree."). Add a "Consumer-side install" sub-section pointing at `consumer-launcher/`. **Acceptance:** Markdown renders cleanly; cross-references resolve.
- [x] **Step 7. Update `CLAUDE.md` §11.15.** Append a short paragraph at the end describing the framework-root resolution order: "When the dispatcher runs in a consumer project that has installed Nissth as a `Tools/Nissth/` submodule, framework-root resolution checks (in order) `NISSTH_FRAMEWORK_ROOT` env var → `<repoRoot>/Tools/Nissth/` submodule convention → `<repoRoot>` fallback (Nissth's own dogfooding). The dispatcher's tool catalog comes from the resolved framework root; reports are written to the resolved repo root (the consumer project's `AgentReports/Bridge/`, not the framework's)." **No edits to §11.1–§11.14 prose.** ~10 lines added. **Acceptance:** `Grep '^### 11\.15' CLAUDE.md` heading unchanged; paragraph appended at the end of §11.15.
- [x] **Step 8. Self-build + regression.** `cd Tools/nissth-bridge && npm test` (30/30); `cd Bindings/SpringBoot && ./mvnw clean test -U -B` (104/104); `cd Bindings/Expo && npm test` (51/51); `cd Bindings/Postgres && npm test` (76 pass / 18 skip); `./nissth-bridge --list-bindings` from Nissth root (3 names — unchanged); `./nissth-bridge --list-tools | wc -l` (14 — unchanged). **Acceptance:** every command exits 0 with the expected output.

### 3.2 Forbidden in this phase

- **No changes to any binding's source.** `Bindings/SpringBoot/`, `Bindings/Expo/`, `Bindings/Postgres/` are read-only this phase.
- **No changes to `Bindings/_schemas/bridge-command.schema.json`.** The frozen contract is untouched.
- **No new tools or modes in any binding.** Phase 09 is dispatcher-only.
- **No multiplexing MCP shim.** Out of scope (the same forbidden pattern as Phases 06/07/08).
- **No `nissth init` CLI.** Authoring a real installer is a separate, larger plan.
- **No template-repo or package distribution work.** Out of scope.
- **No env-var fallbacks beyond `NISSTH_FRAMEWORK_ROOT`.** No `NISSTH_BINDINGS_ROOT`, no `NISSTH_HOME`. One env var, one convention path, one fallback.
- **No caching across invocations.** Dispatcher continues to re-glob every call (per Phase 08 §3.2 forbidden item; preserved).
- **No edits to `CLAUDE.md` §11.1–§11.14 prose.** Only §11.15 appended-to. §11.5 byte-equal pre/post.
- **No edits to top-level `README.md` or `Bindings/README.md`.** Phase 09 stays scoped to the dispatcher; consumer-facing top-level docs about the install flow get authored separately when the consumer-launcher pattern is exercised in Phase 10 (Example init).
- **No installation in any consumer project.** Example init is the NEXT phase (Phase 10), not this one. Phase 09 is framework-only — it ships the capability; Phase 10 exercises it.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- **`node --test` runner has no persistent cache.** Every run re-imports the dispatcher module fresh.
- **No build cache to invalidate** — `dispatcher.js` is plain JS; no transpile.
- **Manifest discovery still re-globs on every invocation** — no caching introduced.
- **Per-binding regressions use their own freshness protocols** — `mvnw clean test`, `npm run clean && npm ci && npm run build && npm test` for Postgres.
- **`process.env` snapshots in tests** — each test that sets `NISSTH_FRAMEWORK_ROOT` saves the original value and restores in a `finally` block. No cross-test contamination.

### 4.2 Checks

- [x] **Dispatcher tests:** `cd Tools/nissth-bridge && npm test` → 30 pass / 0 fail.
- [x] **Nissth dogfooding unchanged:** `./nissth-bridge --list-bindings` from Nissth root returns `expo, postgres, spring-boot`; `./nissth-bridge --list-tools | wc -l` returns `14`; `./nissth-bridge --describe schema_lens` works.
- [x] **Env-var path:** synthetic test — set `NISSTH_FRAMEWORK_ROOT` to a tmp dir with `Bindings/test-stack/test.bridge.json` containing one tool → `./Tools/nissth-bridge/dispatcher.js --list-bindings` returns `test-stack` (and ONLY `test-stack`, ignoring Nissth's own three).
- [x] **Submodule-convention path:** synthetic test — build tmp project with `CLAUDE.md` + `Tools/Nissth/Bindings/test-stack/test.bridge.json` → dispatcher discovers via the submodule. Verified inline by Step 4 case (3).
- [x] **Error path:** `NISSTH_FRAMEWORK_ROOT` set to a non-existent or non-Bindings dir → exit 2 with `invalid_framework_root` error.
- [x] **Phase 05 regression:** 104/104 PASS.
- [x] **Phase 06 regression:** 51/51 PASS.
- [x] **Phase 07 regression:** 76 pass / 18 skip.
- [x] **Consumer launcher template renders:** `cat Tools/nissth-bridge/consumer-launcher/README.md` displays cleanly; both launcher files are non-empty.

### 4.3 Pass criteria

ALL of:
- 30/30 dispatcher tests PASS.
- Nissth's own `./nissth-bridge --list-bindings` returns 3 names (Nissth dogfooding preserved).
- Env-var resolution path tested green via Step 4 case (1).
- Submodule-convention resolution path tested green via Step 4 case (3).
- Phase 05 + 06 + 07 regressions still green.
- Consumer-launcher template files exist + render.
- `git status --short` shows no unexpected changes outside `Tools/nissth-bridge/dispatcher.js`, `Tools/nissth-bridge/test.mjs`, `Tools/nissth-bridge/README.md`, `Tools/nissth-bridge/consumer-launcher/**`, `CLAUDE.md`, `ImplementationPlans/Phase_09_*.md`, `AgentReports/StatusUpdate.md`, `AgentReports/Reports/2026-05-18_phase-09-*.md`.

### 4.4 Failure handling

If any check fails: STOP, append `Verified: FAIL` status entry citing failing artifact, author an `incident` Report under `AgentReports/Reports/`, do not retry silently. Special case: any regression in Phase 05/06/07/08 is automatic FAIL — Phase 09's contract is "purely additive."

---

## 5. Cleanup

- [x] No scratch files generated; tmp dirs in tests cleaned up in `try/finally`. ✓
- [x] No snapshots needed — additive only. ✓
- [x] **Reports check (§10.4):** Author a small `snapshot` Report (`AgentReports/Reports/2026-05-18_phase-09-framework-root-resolution-snapshot.md`) — Phase 09 is small but closes a phase with non-trivial design intent (resolution order, error semantics, consumer-launcher convention). ~600–1000 tokens.
- [x] **Document Sync sweep:**
  - Modified pre-existing files: `Tools/nissth-bridge/dispatcher.js`, `Tools/nissth-bridge/test.mjs`, `Tools/nissth-bridge/README.md`, `CLAUDE.md` (§11.15 appended-to).
  - Created: `Tools/nissth-bridge/consumer-launcher/{nissth-bridge, nissth-bridge.ps1, README.md}`, `AgentReports/Reports/2026-05-18_phase-09-*.md`.
  - Doc sync line: `[updated: Tools/nissth-bridge/dispatcher.js (+findFrameworkRoot, +error message), Tools/nissth-bridge/test.mjs (+6 cases), Tools/nissth-bridge/README.md (+framework-root section), CLAUDE.md §11.15 (+resolution-order paragraph); created: Tools/nissth-bridge/consumer-launcher/**, 2026-05-18_phase-09 snapshot; marked stale: none]`.

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`.

```
### YYYY-MM-DD HH:MM — Phase 09: Framework-root resolution — CLOSED

**State:**
- Phase: 9/9+ — dispatcher now supports consumer projects that install Nissth as a Tools/Nissth/ submodule. Three bindings still green; Nissth's own dogfooding unchanged.
- Build: CLEAN.
- Tests: dispatcher 30/30 PASS; Phase 05/06/07 regressions green.
- Active plan: ImplementationPlans/Phase_09_Framework_Root_Resolution.md.
- DBL refs: none.
- Bridge reports: none generated this phase.
- Blockers: none.

**Report:**
- All §1.3 rows ✅. New `findFrameworkRoot(repoRoot)` checks NISSTH_FRAMEWORK_ROOT env → <repoRoot>/Tools/Nissth/ submodule convention → <repoRoot> fallback.
- Consumer-launcher template under Tools/nissth-bridge/consumer-launcher/ ready for Phase 10 (Example init).

**Executed:**
- §3 Steps 1–8 complete.

**Verified:**
- Dispatcher 30/30 PASS via `node --test`.
- Nissth dogfooding unchanged: --list-bindings returns 3, --list-tools returns 14.
- Env-var + submodule + fallback resolution paths each tested green via synthetic fixtures.
- Phase 05 104/104, Phase 06 51/51, Phase 07 76 pass / 18 skip.
- Doc sync: [updated: Tools/nissth-bridge/dispatcher.js, test.mjs, README.md; CLAUDE.md §11.15 appended; created: consumer-launcher/**, phase-09 snapshot Report; marked stale: none]
- Reports: AgentReports/Reports/<ISO>_phase-09-framework-root-resolution-snapshot.md (snapshot).

**Issues:**
- None framework-blocking. Future enhancements (CLAUDE.md auto-bootstrap from submodule, `nissth init` CLI, template repo) are out of Phase 09 scope.

**Next:**
- **Phase 10 — Example project init.** Greenfield consumer project at `Desktop/ExampleApp/` using the new submodule + consumer-launcher convention. Plan-required. Will exercise the Phase 09 framework-root resolution against a real consumer project.
```
