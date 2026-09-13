# Phase 18: `Tests/` Self-Describing at Init — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_18_Tests_Dir_Self_Describing
- **Authored:** 2026-09-13 by Claude (Opus 5)
- **Approved:** 2026-09-13 by user (Emre Uçmaz) — blanket framework approval (memory `feedback_framework_plan_autoapprove`, 2026-09-13) + "fix the Tests … We should be able to execute the nissth init from here as I did in the other project"
- **Depends on:** Phase_15_Consumer_Init_Tooling (nissth-init exists), Phase_17 (closed; `CLAUDE.md` §9.1 wording is current)
- **Estimated scope:** `Tools/nissth-init/` — one new template, one line in `init.mjs`, one line in `test.mjs`, one line in `README.md`; `CLAUDE.md` §5 tree line + §9.1 step 2 file list; `README.md` tree line. ≈ 30 changed lines. Consumer-facing defect: PostPilot's Phase 00 drafted a `tests/` sibling and then asked the user where tests go, although §5 already said `Tests/ ← Verification artifacts and test sources`. The rule existed as one tree comment; nothing init writes into the consumer repeats it. Fix: init creates `Tests/README.md` (replacing `Tests/.gitkeep`) that states the rule where the agent drafting a layout will read it.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none — Nissth's own `DBL/` holds templates only.
- **Bridge reports:** none.
- **Source:** `Tools/nissth-init/init.mjs:163` (keepers loop), `test.mjs:39-58` (`EXPECTED`), `README.md:40-52`, `CLAUDE.md:99` (§5 tree), `CLAUDE.md:655` (§9.1 step 2), `README.md:211`.
- **StatusUpdate.md:** latest entry `2026-09-13 03:40 — Author-identity correction`, `Next: no pending Nissth work`.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Tree clean on `dev` at `a6877ac` | `git status --short` → empty | repo | baseline; HR#9 satisfied by git |
| 2 | init suite green before change | `npm --prefix Tools/nissth-init test` → 20 pass | tool | know the starting count |
| 3 | `Tests/.gitkeep` is created by the keepers loop and expected by the test | `grep -n 'Tests/.gitkeep' Tools/nissth-init/init.mjs Tools/nissth-init/test.mjs Tools/nissth-init/README.md` → 3 hits | tool | exact edit sites |
| 4 | `CLAUDE.md` §5 line reads `Tests/ ← Verification artifacts and test sources.` | `grep -n '├── Tests/' CLAUDE.md` | doc | confirm the rule text to sharpen |
| 5 | doc-claims clean | `node Tools/doc-claims/validate.mjs` → exit 0 | docs | baseline for §4 |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| `git status --short` | empty | empty except this plan (untracked) | yes |
| init tests | 20 pass / 0 fail | 20 / 0 | yes |
| `Tests/.gitkeep` hits | 3 (init.mjs, test.mjs, README.md) | 3 (one each) | yes |
| §5 line | as quoted | `CLAUDE.md:99` exactly as quoted | yes |
| doc-claims | exit 0 | exit 0 | yes |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/nissth-init/init.mjs` keepers | list | includes `"Tests/.gitkeep"` |
| `Tools/nissth-init/templates/` | files | 13, no `Tests.README.md` |
| `Tools/nissth-init/test.mjs` `EXPECTED` | entry | `"Tests/.gitkeep"` |
| `CLAUDE.md` §5 | `Tests/` line | `← Verification artifacts and test sources.` |
| consumer output | file count | 18 |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/nissth-init/templates/Tests.README.md` | new | states: test sources + verification artifacts live here; never create `tests/` or `test/` at the root (case-insensitive FS merges them); stack conventions that mandate another path (Expo `__tests__/` mirroring) are recorded in `DBL/Summaries/_layout.md` |
| `init.mjs` | keepers | `Tests/.gitkeep` removed; `add("Tests/README.md", readTemplate("Tests.README.md"), "test-sources root; states the Tests/ rule")` |
| `test.mjs` | `EXPECTED` | `"Tests/README.md"` replaces `"Tests/.gitkeep"`; new case asserts the README contains `never create` and `tests/` |
| `Tools/nissth-init/README.md` | file list | `Tests/README.md` (the rule) instead of `Tests/.gitkeep` |
| `CLAUDE.md` §5 | `Tests/` line | `← Test sources and verification artifacts. Consumer test projects live here; never a tests/ or test/ sibling (case-insensitive filesystems merge them).` |
| `CLAUDE.md` §9.1 step 2 | list | `` `Tests/README.md` `` instead of `` `Tests/` `` |
| `README.md` | tree line | `Tests/  Test sources + verification artifacts` |
| consumer output | file count | 18 (unchanged) |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** Add `Tools/nissth-init/templates/Tests.README.md`. **Operation:** add. **Acceptance:** ≤ 15 lines; contains the phrases `test sources`, `verification artifacts`, `never create`, `tests/`, `case-insensitive`; LF.
- [x] **Step 2.** Edit `Tools/nissth-init/init.mjs:163`. **Operation:** modify. **Acceptance:** keepers loop lists 4 `.gitkeep`s; a following `add("Tests/README.md", readTemplate("Tests.README.md"), "test-sources root; states the Tests/ rule")` line; total created files still 18.
- [x] **Step 3.** Edit `Tools/nissth-init/test.mjs`. **Operation:** modify. **Acceptance:** `EXPECTED` swaps the entry; a new `test("Tests/README.md states the rule")` asserts both phrases → 21 cases.
- [x] **Step 4.** Edit `Tools/nissth-init/README.md:47`. **Operation:** modify. **Acceptance:** file list names `Tests/README.md` with a three-word gloss.
- [x] **Step 5.** Edit `CLAUDE.md` §5 tree line (`:99`) and §9.1 step 2 list (`:655`). **Operation:** modify. **Acceptance:** wording per §2 After; no other `CLAUDE.md` line changes.
- [x] **Step 6.** Edit `README.md:211`. **Operation:** modify. **Acceptance:** tree gloss `Test sources + verification artifacts`.

### 3.2 Forbidden in this phase

- Changing the 18-file count, the refuse-and-list contract, or any other template.
- Touching `.gitignore` presets (the .NET-preset idea from PostPilot's ledger is a separate, unauthored phase).
- Editing consumer repos from here.
- Adding a Hard Rule — this is a mechanism (a file the agent reads), not a stricter restatement (§12.1 lesson).

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Plain Node, no build step, no daemon. `node --test` reads the files from disk at run time. Fresh-worktree clause: `Tools/nissth-init` templates are build inputs of the consumer skeleton → run the suite once from a `git worktree` after the commit and cite its path.
- Freshness statement: "`npm --prefix Tools/nissth-init test` run at YYYY-MM-DD HH:MM after the last §3 write in the development directory, and again in fresh worktree `<path>` at HEAD after commit."

### 4.2 Checks

- [x] **Build:** N/A — plain Node.
- [x] **Tests:** RESULT 21 pass / 0 fail (2026-09-13 04:20, development directory, after last write). Expected 21 pass / 0 fail.
- [ ] **Field run:** `node Tools/nissth-init/init.mjs --target %TEMP%\ni-p18 --name P18 --stack none --dry-run --json` → 18 files, includes `Tests/README.md`, excludes `Tests/.gitkeep`.
- [x] **doc-claims:** RESULT exit 0.
- [x] **dbl-check (self):** RESULT templates only, exit 0.
- [x] **Other suites untouched:** RESULT dispatcher 32/32, doc-claims 23/23, dbl-check 21/21.
- [ ] **Fresh worktree:** init suite 21/21 from `git worktree add ../nissth-p18-verify HEAD` — RESULT recorded in the follow-up status entry (runs after commit).

### 4.3 Pass criteria

All checks above pass; `git status` after commit is clean; worktree removed.

### 4.4 Failure handling

If any check fails: STOP; append `Verified: FAIL` + incident Report (§10.4 #1); do not retry silently.

---

## 5. Cleanup

- [ ] Remove `%TEMP%\ni-p18` and the verification worktree.
- [x] Snapshots: none taken (git baseline `a6877ac`).
- [x] **Reports check (§10):** no trigger — single-mechanism change; PostPilot's ledger already holds the incident narrative.
- [x] **Document Sync sweep (HR#11):** modified: `init.mjs`, `test.mjs`, template, `Tools/nissth-init/README.md`, `CLAUDE.md` §5/§9.1, `README.md`. DBL: none covers these. Plans: Phase 15 describes "Tests/.gitkeep" in its §2 — historical, left as is. `Ultimate_Guide.md`: grep for `Tests/.gitkeep` → no hit; nothing to update. Logged `Doc sync: [updated: CLAUDE.md §5/§9.1, README.md tree, Tools/nissth-init/README.md; Phase 15 §2 left historical]`.
- [x] Status entry (pre-commit per memory rule), then commit `feat(nissth-init): Tests/README.md states the test-sources rule; CLAUDE.md §5 sharpened`.

---

## 6. Status Update Entry

```
### YYYY-MM-DD HH:MM — Phase 18: Tests/ self-describing at init

**State:**
- Phase: 18 closed
- Build: CLEAN — plain Node
- Tests: PASS — nissth-init 21/21 · dispatcher 32 · doc-claims 23 · dbl-check 21
- Active plan: none
- DBL refs: none · Bridge reports: none · Blockers: none

**Report:** pre-flight rows; the PostPilot trigger.
**Executed:** file list.
**Verified:** suites + field run + fresh worktree path; Doc sync line; Reports: none.
**Issues:** none / follow-ups (gitignore preset idea).
**Next:** one step.
```
