# Phase 19: Expo binding — stop the false STALE-flips and stop rewriting DBL frontmatter — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_19_Bridge_Flip_And_Yaml
- **Authored:** 2026-09-23 by Claude (Opus 5)
- **Approved:** 2026-09-23 by user (Emre Uçmaz) — standing authorisation, verbatim: *"Şu andan itibaren onaylıyorum plan ve actionlarını nissth ve finans yönetim arasındaki bağlamda. Bana sormadan çalış ve bitir."* (Framework plans were already pre-approved per the 2026-09-13 08:30 entry.)
- **Depends on:** Phase_06_Bridge_Expo_FirstSlice (the two lenses and `StaleFlipper`); Phase_16_DBL_Check (`Tools/dbl-check`, which is what *reports* the damage)
- **Estimated scope:** Three defects in `Bindings/Expo`, all of them paid for by the consumer on **every phase close since its Phase 01** — nine phases of `git checkout -- DBL/` after every Bridge run. Digest items **B1**, **B2**, **C2** (`FinansYönetimApp/AgentReports/Reports/2026-09-13_nissth-feedback-digest.md`), re-confirmed on 2026-09-21 with fresh evidence. (1) `component_lens` compares the live component names of the **whole scanned tree** against **each** artifact's body, so every Summary that documents no component drifts by construction. (2) `route_lens` compares route URLs literally, so the Expo file notation `/account/[id]` "drifts" from the documented `/account/:id` — the same route in the other spelling. (3) `StaleFlipper.writeArtifact` re-serialises the whole frontmatter through `yaml.stringify`, re-wrapping long `name:` / `stale_when:` lines at 80 columns, which `dbl-check` then reports as `bad-frontmatter` — the Bridge breaking the artifact it flipped.

### 0.1 Design choices fixed at authoring

| # | Choice | Why |
|:---|:---|:---|
| C1 | **Flip needs local evidence.** A lens may flip an artifact only when the scan found something **under that artifact's own `covers`**. `DriftCheck` already receives the artifact's frontmatter, so no signature change: `ComponentLens` filters its component list by `covers` and returns `false` when nothing of its kind lives there. Alternative — a global "does this artifact mention every component in the tree" test — is what ships today and is wrong in the general case, not just here. | digest B1; evidence 2026-09-21 (8 Summaries flipped, 0 real drift) |
| C2 | **Compare routes canonically.** Both sides are canonicalised before the set comparison: `[id]` → `:param`, `[...rest]` → `:splat`, `:id` → `:param`, trailing `/` dropped. A DBL table written in either notation matches. Alternative — teach the DBL convention one spelling — was rejected: `CLAUDE.md` §8.2.4 says "URL path", every hand-authored table so far used `:id`, and the lens must not dictate prose style. | evidence 2026-09-21: the only three "drifting" routes were `[id]` vs `:id` |
| C3 | **Never re-serialise frontmatter.** The flip becomes a surgical line replacement on the raw text: find the top-level `last_regenerated:` line (plus its folded continuation lines, if any), replace exactly those lines, leave every other byte — including the line endings — untouched. `writeArtifact` (whole-file re-serialise) is deleted; nothing outside `StaleFlipper` used it. | digest B2/C2 |
| C4 | **Regression coverage is the deliverable, not the fix.** Each defect gets a unit test that fails against the current code: a Summary with no components under its `covers` (B1), a routes table in `:id` notation (C2), and a byte-comparison of everything except the flipped line (C3). | `CLAUDE.md` §3 VERIFY |
| C5 | **Out of scope:** `expo_doctor_lens` parsing (digest B3), `dependency_audit` heuristics (B4), the `--scope.root-path` flag spelling (A3), and every documentation item (A4, A5, B5, B6) — they are Phase 20/21 per the 2026-09-13 08:30 ledger entry. This phase touches two tools and one core file. | scope guard |

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- `Bindings/Expo/src/core/StaleFlipper.ts`, `src/tools/ComponentLens.ts`, `src/tools/RouteLens.ts`
- `Bindings/Expo/tests/unit/StaleFlipper.test.ts`, `tests/integration/{ComponentLens,RouteLens}.it.test.ts`
- Consumer evidence (read-only): `FinansYönetimApp/AgentReports/Bridge/route_lens_2026-09-21T213212Z.md`, `component_lens_2026-09-21T213226Z.md`, `DBL/APIIndex/routes.md`
- `CLAUDE.md` §11.4 (the stale-flip contract this must keep), §13 (what `dbl-check` reports)

### 1.2 Diagnostic actions

| # | Action | Expected |
|:---|:---|:---|
| 1 | Read `ComponentLens.detectDrift` | compares `documented` (PascalCase identifiers in the body) against the **whole scan's** live names |
| 2 | Read `RouteLens.detectDrift` | symmetric set difference of literal URL strings |
| 3 | Read `StaleFlipper.writeArtifact` | `yamlStringify(frontmatter)` — whole-block re-serialise |
| 4 | Diff the consumer's live route set against its `routes.md` | the only differences are `[id]` vs `:id` on three routes |
| 5 | `grep -rn "writeArtifact" Bindings/Expo` | no caller outside `StaleFlipper.ts` |
| 6 | `npm test` in `Bindings/Expo` before any edit | the existing suite is green (baseline) |

### 1.3 Findings (filled during execution)

| # | Expected | Actual | Match? |
|:---|:---|:---|:---|
| 1 | global comparison per artifact | confirmed — `detectDrift(body, liveComponentNames)` with `liveComponentNames` built from the whole scan | yes |
| 2 | literal URL comparison | confirmed | yes |
| 3 | whole-block re-serialise | confirmed | yes |
| 4 | only `[id]` vs `:id` | confirmed — live `/account/[id] /person/[id] /transaction/[id]`, DBL `:id`; the other 9 routes identical | yes |
| 5 | no external caller | confirmed | yes |
| 6 | green baseline | 58/58 across 13 suites | yes |

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Area | State |
|:---|:---|
| `component_lens` | flips every Summary whose `covers` overlap the scan root and whose body does not name every component in the tree |
| `route_lens` | flips `APIIndex` artifacts whose table uses `:id` notation |
| `StaleFlipper` | rewrites the whole frontmatter; long lines re-wrap; `dbl-check` → `bad-frontmatter` |
| Consumer cost | `git checkout -- DBL/` after every Bridge run, nine phases running |

### After (post-execution target)

| Area | State |
|:---|:---|
| `component_lens` | flips only artifacts that actually cover at least one scanned component file, and only on a real name difference there |
| `route_lens` | flips only on a real route-set difference, notation-independent |
| `StaleFlipper` | flips by replacing one line; every other byte identical |
| Suite | previous tests still green + 5 new regression tests |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [x] **Step 1.** `StaleFlipper`: add `static coversPath(pattern, filePath): boolean` (exact · directory prefix · glob via the existing `globMatch`). **Acceptance:** unit cases incl. `src/ui/**` vs `src/ui/MoneyText.tsx` true, vs `src/domain/x.ts` false.
- [x] **Step 2.** `StaleFlipper`: replace `writeArtifact` with `static setLastRegenerated(path, value): boolean` — surgical single-line replacement on the raw text (handles a folded value spanning indented continuation lines); `flipIfStale` calls it. Delete `writeArtifact`. **Acceptance:** the C3 byte-comparison test.
- [x] **Step 3.** `ComponentLens`: pass the repo-relative component paths (`<subPath>/<rel>`) into the drift check; filter by the artifact's `covers`; return `false` when no component file is covered. **Acceptance:** the B1 regression test.
- [x] **Step 4.** `RouteLens`: canonicalise both sides (`canonicalRoute`) before comparing. **Acceptance:** the C2 regression test.
- [x] **Step 5.** Tests: 5 new cases across `tests/unit/StaleFlipper.test.ts` (coversPath, byte-preservation), `tests/unit/` new `LensDrift.test.ts` (ComponentLens per-covers filter, RouteLens notation). **Acceptance:** each fails on the pre-change code (checked by reverting the source hunk once), passes after.
- [x] **Step 6.** `Bindings/Expo/README.md` — one line per lens under the stale-flip description stating the new flip condition. **Acceptance:** `node Tools/doc-claims/validate.mjs` exit 0.
- [x] **Step 7.** Commit; fresh-worktree run of the binding suite (§4.1).

### 3.3 Deviations from the step list

| # | Step | Deviation | Why |
|:---|:---|:---|:---|
| 1 | 3 | C1 as authored ("filter the live components by the artifact's `covers`") was **not sufficient**. The field check against the consumer went 9 flips → 2, and both survivors were still false: `_layout.md` (`covers: src/**`, the repository layout map — it names component *files* in its directory tree) and `features-hooks.md` (`covers: src/features/**`, a hooks artifact that happens to contain a provider component). Two guards were added: **(a)** cover patterns whose literal prefix is at or above the scan root are catch-alls and are ignored, **(b)** an artifact that names **none** of the components it covers is not a component inventory and is never flipped. | the defect's acceptance test is "the consumer's `DBL/` is untouched"; 2 of 9 is not a fix |
| 2 | 5 | Guard (b) means a **rename** (the artifact lists `Spacer`, the tree has `MoneyText`, nothing overlaps) no longer flips. Accepted and documented: a Summary is prose, its PascalCase words are not a component list, and `dbl-check`'s `covers-changed-since` still reports the source change. The regression test was re-cast to the case that matters — a *new* component next to a documented one. | false positives every run cost more than a missed rename once |
| 3 | 5 | "Each new test fails on the pre-change code" could not be shown by reverting the source: the suite calls `setLastRegenerated` / `canonicalRoute`, which do not exist there, so it fails to compile rather than to assert. The defect is instead pinned by an explicit test that calls `detectDrift` the way the old caller did and asserts it reports drift for an artifact with no components. | honest evidence beats a green-to-red screenshot |

### 3.2 Forbidden in this phase

- No change to the §11.2 command contract, the report frontmatter contract (§11.3), or `Bindings/_schemas/`.
- No change to `expo_doctor_lens`, `dependency_audit`, `route_scaffold`, or the dispatcher — Phase 20/21 items (C5).
- No change to `CLAUDE.md` §8.2 / §11 text: this phase fixes implementations to match the documented contract, it does not move the contract.
- No new dependency. No change to `Tools/dbl-check`.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- `npm run clean` → `npm ci` → `npx tsc -p .` → `npm test` in `Bindings/Expo`, once in place and once in a **fresh worktree** (§8.2.6 item 6), citing the worktree path.
- Field check against the real consumer: run `component_lens` and `route_lens` from the FinansYönetimApp checkout and confirm `git status --short DBL/` is **empty** afterwards — the defect's own acceptance test.

**Result (2026-09-23, `86ad0a3`).**
> "Baseline before any edit: 13 suites / 58 tests green. After: `npx tsc -p .` 0 and `npx jest` **14 suites / 67 tests** in place; **fresh worktree `C:\Temp\nissth-p19` at `86ad0a3`**, 08:59:07–08:59:52: `npm ci` 0 → `tsc -p .` 0 → **14/67** green, `git status` empty in that tree; worktree removed and pruned. `node Tools/doc-claims/validate.mjs` exit 0. **Field check** in `C:\Users\Ucmaz pc\Git\FinansY-netimApp` at its `0848763`: `route_lens --mode with_params`, `component_lens --scope.package src` and `component_lens` (default scope) run back to back → `git status --short DBL/` **empty**, `dbl-check` **17 artifacts — 0 error / 0 warn / 0 info**. The same three commands before the fix flipped 9 artifacts and left 2 of them with re-wrapped, invalid YAML."


### 4.2 Checks

- [x] **Build:** `tsc -p .` 0.
- [x] **Tests:** previous 58 green + **9 new** (13/58 → 14/67); the fail-first check is §3.3 row 3.
- [x] **Field:** **three** lens runs in FinansYönetimApp leave `DBL/` untouched and `dbl-check` at 17 — 0/0/0.
- [x] **Doc:** `node Tools/doc-claims/validate.mjs` exit 0.

### 4.3 Pass criteria

ALL of: build clean; suite green in place **and** in a fresh worktree; the field check leaves `DBL/` byte-identical; no file outside `Bindings/Expo/{src,tests,README.md}` modified.

### 4.4 Failure handling

On any failure: STOP, append a `Verified: FAIL` entry with the artifact, author the incident Report, do not retry silently. Known risk: a consumer artifact that is **genuinely** stale would now stop being flipped if its `covers` name no component — that is the intended behaviour (the artifact documents no components), and `dbl-check`'s `covers-changed-since` remains the mechanism that catches real source drift.

---

## 5. Cleanup

- [x] Remove the verification worktree.
- [x] **Reports check (§10):** none required — a defect fix with its own regression tests; the narrative lives in this plan and the status entry. The consumer's digest report gets its status column updated (B1/B2/C2 → applied) in the consumer's own session.
- [x] **Document Sync sweep (HR#11):** `Bindings/Expo/README.md` (flip condition); `CLAUDE.md` §11.13 tool table — check whether its one-line descriptions still hold (they do if the flip condition is described as "on drift"); no DBL artifact in this repo covers `Bindings/Expo/src`.
- [x] No orphan branches, no leftover debug code.

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`.

```
### 2026-09-23 09:05 — Phase 19: Expo binding — flip only on local evidence; frontmatter written surgically — CLOSED
```
