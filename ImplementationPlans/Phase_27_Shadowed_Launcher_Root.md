# Phase 27: Shadowed launcher root — a two-machine consumer that is healthy on both machines — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_27_Shadowed_Launcher_Root
- **Authored:** 2026-10-18 by Claude (Opus 5.5)
- **Approved:** 2026-10-18 by user (standing pre-approval for framework-side phases, 2026-09-13; this phase was requested by the user, who relayed the ExampleCommunityApp backend session's F1 finding to a Nissth session)
- **Depends on:** Phase_26_Consumer_Health_At_Boot
- **Estimated scope:** Closes F1 of the ExampleCommunityApp backend's feedback digest (`AgentReports/Reports/2026-10-11_nissth-feedback-digest.md` in that repo). A consumer worked from two machines has no state in which `--health` passes on both: each launcher bakes one machine's `DEFAULT_ROOT`, and `init --check` counts the other machine's path as drift even when `NISSTH_FRAMEWORK_ROOT` resolves first and the launcher therefore never reaches `DEFAULT_ROOT`. This phase reports such a shadowed `DEFAULT_ROOT` as a note, not drift; makes both framework test suites independent of a user-wide `NISSTH_FRAMEWORK_ROOT`, which is the recommended per-machine fix and currently breaks two dispatcher tests; and documents the two-machine setup. Touches `Tools/nissth-init/init.mjs`, `Tools/nissth-bridge/dispatcher.js`, both suites, `CLAUDE.md` §9.1 (one sentence), two READMEs, and the harvest Report's phase numbering. **No consumer file.**

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm that the drift is reported for a `DEFAULT_ROOT` the launcher cannot reach, and that nothing else in the consumers is out of sync.

### 1.1 Inputs to read

- **DBL:** none in this repo (`DBL/**` holds templates only).
- **Bridge reports:** none — the subject is the Bridge's own health check.
- **Source:** `Tools/nissth-init/init.mjs:257-312` (`checkConsumer`), `:360-401` (`launcherProblems`), `:476-523` (`main --check`); `Tools/nissth-bridge/dispatcher.js:403-428` (`healthReport`, consumer section); `Tools/nissth-bridge/consumer-launcher/nissth-bridge:8-35` (resolution order); `Tools/nissth-bridge/test.mjs:274-335`, `Tools/nissth-init/test.mjs:476-532`.
- **Reports:** the ExampleCommunityApp backend feedback digest, row F1; `AgentReports/Reports/2026-10-06_sibling-consumers-harvest.md` §"Proposed phase grouping".
- **StatusUpdate.md:** latest entry 2026-10-07 20:20 +03:00, whose `Next:` names the harvest's Phase 27. That proposal and the four after it shift by one (§3 Step 6).

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | `--health` from each ExampleCommunityApp consumer | `./nissth-bridge --health` | backend, frontend | What fails, and whether bindings are reachable |
| 2 | Baked roots | `grep DEFAULT_ROOT nissth-bridge nissth-bridge.ps1` | both consumers | Confirms the other machine's path |
| 3 | Launcher resolution order | read the template | framework | Is `DEFAULT_ROOT` reached when the env var resolves? |
| 4 | Baseline suites, env var as on this machine | `node --test` both suites | framework | Pass counts before change |
| 5 | Baseline suites, env var unset | `env -u NISSTH_FRAMEWORK_ROOT node --test …` | framework | Is any failure caused by the env var alone? |

### 1.3 Findings

> Observed 2026-10-18 14:00–14:10 +03:00, while authoring.

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| 1. `--health` in the consumers | exit 1, only finding: both launchers' `DEFAULT_ROOT` holds no dispatcher; all bindings `ok` | both consumers: exactly that, via `NISSTH_FRAMEWORK_ROOT env var`; body otherwise in sync | yes |
| 2. Baked roots | `<path>/Nissth` | that path, in both launchers of both consumers | yes |
| 3. `DEFAULT_ROOT` reached when env var resolves? | no — env var is tier 1, `DEFAULT_ROOT` tier 3 | no — first candidate holding `dispatcher.js` wins | yes |
| 4. Baseline, env var set | dispatcher 40/40 · nissth-init 36/36 | dispatcher **38/40** (two synthetic `runDispatcher` tests read the user's env var) · nissth-init 36/36 | no — see note |
| 5. Baseline, env var unset | — | dispatcher 40/40 · nissth-init 36/36 | — |

**Row 4 note.** The mismatch is not stale state: it is the same defect class as F1, appearing in the framework's own suite the moment a machine adopts the per-machine fix. It is folded into Step 3 rather than treated as a stop.

**Stop condition:** If any row's `Match? = no` for a reason other than the one this plan addresses, STOP — append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `init.mjs` `launcherProblems` | unreachable `DEFAULT_ROOT` | always drift, even when a higher tier resolves |
| `checkConsumer` | result | no place for a launcher note |
| `dispatcher.js --health` | consumer launcher notes | none |
| suites | env var set user-wide | dispatcher 38/40 |
| `CLAUDE.md` §9.1 | `--check` sentence | "a `DEFAULT_ROOT` that resolves on this machine" |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `launcherProblems` | unreachable `DEFAULT_ROOT` | drift only when no higher tier holds a dispatcher; otherwise a note naming the tier that shadows it |
| `checkConsumer(target, fwRoot, env = process.env)` | result | adds `launcherNotes: [{file, note}]`; `inSync` ignores notes |
| `init --check` CLI | output | notes printed in both branches, exit code unchanged by them |
| `--health` | consumer launcher notes | appended to `notes`; never change the exit code |
| suites | env var set or unset | identical counts, all green |
| `CLAUDE.md` §9.1 | `--check` sentence | launchers must match the template and reach a dispatcher on this machine; a shadowed `DEFAULT_ROOT` is a note |
| both ExampleCommunityApp consumers | `--health` on `admin` | launcher drift gone; remaining drift is the one §9.1 sentence until each consumer re-syncs its body (their session's step) |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** `launcherProblems(targetAbs, fwRoot, env)` returns `{ problems, notes }`. A non-empty `DEFAULT_ROOT` without `dispatcher.js` is a note when `env.NISSTH_FRAMEWORK_ROOT` or `<target>/Tools/Nissth` holds `Tools/nissth-bridge/dispatcher.js` (the launcher's own test), else a problem as today. `checkConsumer` gains `env = process.env`, returns `launcherNotes`. **File:** `Tools/nissth-init/init.mjs`. **Lines:** `257-312`, `360-401`. **Operation:** modify. **Acceptance:** Step 3 tests.
- [x] **Step 2.** Surface notes. `init --check` prints `launcher <file>: note — …` in both branches; `--health` pushes `<file>: <note>` into `report.notes`. **Files:** `init.mjs:476-523`, `dispatcher.js:403-428`. **Operation:** modify. **Acceptance:** Step 3 tests.
- [x] **Step 3.** Tests. **Files:** `Tools/nissth-init/test.mjs`, `Tools/nissth-bridge/test.mjs`. **Operation:** modify + add. Both suites clear `NISSTH_FRAMEWORK_ROOT` at load, so a user-wide setting cannot change a result; tests that need it already set it explicitly. New cases: unreachable `DEFAULT_ROOT` shadowed by the env var → note, in sync; shadowed by a `Tools/Nissth` submodule → note; env var set but holding no dispatcher → still drift; CLI `--check` exits 0 and prints the note; `--health` from such a consumer → exit 0 with the note. The existing drift case passes `env: {}`. **Acceptance:** both suites green with the env var set and unset, same counts.
- [x] **Step 4.** `CLAUDE.md` §9.1 `--check` sentence (line 742). **Operation:** modify. **Acceptance:** `doc-claims` exit 0.
- [x] **Step 5.** READMEs: `Tools/nissth-init/README.md` "Launchers" paragraph; `Tools/nissth-bridge/consumer-launcher/README.md` "When it stops resolving" — a consumer worked from two machines sets `NISSTH_FRAMEWORK_ROOT` per machine and leaves the launchers alone; re-running `nissth-init` only moves the drift to the other machine. **Operation:** modify.
- [x] **Step 6.** Harvest Report: proposed phases 27–31 become 28–32; `last_updated` + revision-history line. **File:** `AgentReports/Reports/2026-10-06_sibling-consumers-harvest.md`. **Operation:** modify (plan-exempt surface).
- [x] **Step 7.** Field run on this machine: `--health` and `init --check` against both ExampleCommunityApp consumers. **Operation:** run, read-only on the consumers. **Acceptance:** no launcher drift; launcher notes printed.

### 3.2 Forbidden in this phase

- Editing any consumer file — launchers, `CLAUDE.md` body, ledger, feedback digest. The ExampleCommunityApp backend session is live in that repo; the re-sync and the F1 status change are its step.
- Changing the launcher templates or their resolution order, or adding an untracked per-machine root file (F1's second proposal) — the env var already is that mechanism.
- ExampleCommunityApp backend F2–F4, Phase 26's four `--check` gaps, or any harvest lesson — Phase 28 onward.
- Rotating this ledger (497 KB, past §5.1's threshold) — a separate, deliberate act.
- Pushing.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Suites run with `node --test` straight from the edited files on disk, once with the user-wide env var and once with it unset; no build step sits between source and test.
- Field runs execute each consumer's own launcher from its root after the edits are saved.
- Run in the development directory; neither tool has build inputs and no binding changed, so §8.x.6 fresh-clone validation does not apply.

### 4.2 Checks

- [x] **Build:** N/A — no binding source changed; `--health` here stays exit 0 (`build-older-than-source` guards it).
- [x] **Tests:** both suites, env var set and unset — all pass, counts above 40 and 36.
- [x] **Runtime/integration:** Step 7.
- [x] **Bridge re-query:** N/A.
- [x] **DBL freshness:** N/A — no DBL here.
- [x] **Validators:** `doc-claims` exit 0; `plan-lint --plan ImplementationPlans/Phase_27_Shadowed_Launcher_Root.md` no errors.

### 4.3 Pass criteria

- A shadowed `DEFAULT_ROOT` is a note and an unshadowed one is still drift — proven by tests, including the env-var-set-but-invalid case.
- The framework suites give the same result whether or not the machine sets `NISSTH_FRAMEWORK_ROOT`.
- Both ExampleCommunityApp consumers show no launcher drift from this machine.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [x] Remove temp scripts/artifacts created during execution
- [x] Snapshots: ref only (`d44b252`, `HEAD` before this phase); nothing under `AgentReports/Snapshots/`
- [x] **Reports check (CLAUDE.md §10):** single-defect phase — no snapshot Report.
- [x] **Document Sync sweep (Hard Rule #11):** `init.mjs` and `dispatcher.js` are cited by `CLAUDE.md` §9.1 / §11.5 / §11.15 and the two READMEs (Steps 4–5); run `doc-claims` and `plan-lint`.
- [x] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

```
### YYYY-MM-DD HH:MM±hh:mm — Phase 27: Shadowed launcher root — CLOSED

**State:**
- Phase: 27 closed; no phase open
- Build: CLEAN · Tests: PASS — dispatcher N/N · nissth-init N/N (env var set and unset) · doc-claims exit 0 · plan-lint clean
- Active plan: ImplementationPlans/Phase_27_Shadowed_Launcher_Root.md (closed)
- DBL refs: none · Bridge reports: none
- Blockers: none

**Report:** [condensed from §1]
**Executed:** [condensed from §3]
**Verified:** [§4 + freshness] · Doc sync: … · Reports: none
**Issues:** [or "none"]
**Next:** Author Phase_28 (ledger and plan contract — harvest rows 3–7, 15; formerly proposed as 27).
```
