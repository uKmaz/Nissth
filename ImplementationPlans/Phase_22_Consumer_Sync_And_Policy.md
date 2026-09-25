# Phase 22: Consumer-copy drift check, ledger rotation, init handoff, clean-break migration policy — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_22_Consumer_Sync_And_Policy
- **Authored:** 2026-09-24 by Claude (Opus 5)
- **Approved:** 2026-09-24 by user (standing authorisation for framework-side phases, recorded 2026-09-13)
- **Depends on:** Phase_18_Tests_Dir_Self_Describing, Phase_20_Bridge_Cli_And_Docs, Phase_21_Lens_Parsers_And_Budget
- **Estimated scope:** The framework-side items both consumers are still carrying. `Tools/nissth-init/init.mjs` (+ one template, README, tests) gains the `AgentReports/Archive/` skeleton, the end-of-init session handoff, and a new `--check` mode that diffs a consumer's framework body against this checkout. `CLAUDE.md` gains the ledger-rotation procedure (§5), the clean-break migration paragraph (§8.2.10, digest A4), and the §9.1 pointers. Then both live consumers' `CLAUDE.md` copies are re-synced and each gets a status entry in its own ledger.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Establish that each item is genuinely still open, and measure the drift before proposing a mechanism for it.

### 1.1 Inputs to read

- **DBL:** none — no DBL artifact in this repo covers `Tools/` or `CLAUDE.md`.
- **Bridge reports:** none — no live state is queried; this phase is init tooling and prose.
- **Source:** `Tools/nissth-init/init.mjs:125-245` (plan builder, keepers, final message) and `:264-322` (CLI), `CLAUDE.md` §5 tree line 88, §8.2.10, §9.1 step 2.
- **Consumers:** `ExampleDesktopApp/AgentReports/StatusUpdate.md` (the repeated `AgentReports/Archive/` item, first raised 2026-09-19), the Nissth ledger entry 2026-09-13 08:22 item 2 (the init handoff ExampleDesktopApp asked for, never delivered — `Phase_19` became the Bridge flip fix instead), `ExampleFinanceApp/AgentReports/Reports/2026-09-13_nissth-feedback-digest.md` row A4.
- **StatusUpdate.md:** latest entry 2026-09-24 22:40 — "Phase 21 … CLOSED", whose `Next:` names this plan and this scope.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Measure the actual drift in both consumer copies | `diff` each consumer's `CLAUDE.md` framework body against this checkout's | from `## 1. Boot Protocol` onward | A drift check is worth building only if drift exists; the size tells us what it must catch |
| 2 | Confirm `nissth-init` never creates `Archive/` | read the keeper list | `init.mjs:161-163` | ExampleDesktopApp's claim, verified at the source |
| 3 | Confirm the init run prints no session handoff | read the success message | `init.mjs:311-317` | The 2026-09-13 item said it should; check whether some later phase quietly added it |
| 4 | Baseline the init suite | `node --test Tools/nissth-init/test.mjs` | whole suite | A changed skeleton changes the file count several tests assert |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Does ExampleDesktopApp's copy differ from the framework body? | yes, by the Phase 20 hunks | **worse — 1010 of 1164 body lines differ, first at line 7.** It is behind phases 17, 18, 20 and 21, so everything after the first insertion is offset. It was also missing `Tests/README.md`'s sibling rule and had never received §1's `git fetch` | no — worse than expected |
| Does ExampleFinanceApp's copy differ? | yes, by the Phase 21 hunks only (Phase 20 re-synced it) | yes — 37 lines, first at body line 1010 (the Phase 21 tool descriptions), plus two missing skeleton paths | yes |
| Does `nissth-init` create `AgentReports/Archive/`? | no | no — the keeper list has `Reports`, `Bridge`, `Snapshots`, `Tools`, and not `Archive` | yes |
| Does the init run tell the user to open a session in the target? | no | no — the success message ends at "SRS + SDD (§9), then Phase_00". The 2026-09-13 item proposed a `Phase_19_Init_Handoff`; Phase 19 became the Bridge flip fix and the handoff was never delivered | yes |
| Baseline init suite | green | green, 21/21 | yes |

**On the one `no`:** the drift is larger than predicted, in the direction that strengthens the plan rather than invalidating it — every §2 and §3 premise holds, and the measurement is the argument for Step 3 existing at all. Recorded, not silently rounded to "yes".

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `nissth-init` skeleton | `AgentReports/Archive/` | not created; §5 documents it |
| `nissth-init` success message | session handoff | absent — the agent that ran init keeps operating the consumer from the framework session |
| `nissth-init` CLI | modes | create only |
| `CLAUDE.md` §5 | rotation | one tree comment ("Rotated logs when StatusUpdate.md exceeds ~100KB"), no procedure |
| `CLAUDE.md` §8.2.10 | clean break | "never edit an already-applied migration", no guidance when the only applied copies are test devices |
| Consumer `CLAUDE.md` copies | drift | unmeasured, nothing mechanical points at them |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `nissth-init` skeleton | `AgentReports/Archive/README.md` | created, carrying the rotation procedure (the `Tests/README.md` precedent: a rule in a tree comment is a rule nobody reads) |
| `nissth-init` success message | session handoff | states that initialisation is done and the next session opens **in the target directory** |
| `nissth-init` CLI | modes | `--check <dir>` compares a consumer's framework body + skeleton against this checkout; exit 0 in sync, 1 drift, 2 usage |
| `CLAUDE.md` §5 | rotation | a named procedure, HR#3-compatible, with the file-name shape and the pointer line |
| `CLAUDE.md` §8.2.10 | clean break | a paragraph sanctioning baseline reset + legacy wipe when every applied copy is a test device, recorded in the SchemaIndex baseline |
| Both consumers | `CLAUDE.md` | framework body byte-identical to this checkout; each repo carries its own status entry saying so |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** Add `AgentReports/Archive/README.md` to the skeleton, carrying the rotation procedure. **Files:** `Tools/nissth-init/templates/Archive.README.md` (new), `Tools/nissth-init/init.mjs` (replace the `Archive` gap in the keeper list). **Operation:** add/modify. **Acceptance:** a dry run lists the file; `nissth-init --help` unchanged.
- [x] **Step 2.** Add the end-of-init session handoff to the success message. **File:** `Tools/nissth-init/init.mjs`. **Operation:** modify. **Acceptance:** stdout tells the user to open the next session in the target directory and states that this session's initialisation work is done.
- [x] **Step 3.** Add `--check <dir>`: reads the target's `CLAUDE.md`, compares the framework body (from the first `---` rule) against this checkout's, and reports the first differing lines plus any missing skeleton path. **File:** `Tools/nissth-init/init.mjs`. **Operation:** modify. **Acceptance:** exit 0 on an in-sync copy, 1 on a drifted one naming the first differing line, 2 when the path is not a Nissth project.
- [x] **Step 4.** `CLAUDE.md` §5: the ledger-rotation procedure. **Operation:** modify. **Acceptance:** states the threshold, the archive file-name shape, the pointer line left in the live ledger, and that rotation is not an edit of past entries (HR#3).
- [x] **Step 5.** `CLAUDE.md` §8.2.10: the clean-break paragraph (digest A4). **Operation:** modify. **Acceptance:** names the precondition (every applied copy is a test device and the spec authorises a wipe), the sanctioned path (regenerate baseline + explicit code-level legacy wipe), and where it is recorded.
- [x] **Step 6.** `CLAUDE.md` §9.1 step 2: mention the handoff and `--check`. **Operation:** modify. **Acceptance:** `doc-claims` exit 0.
- [x] **Step 7.** Tests + README for the above. **Files:** `Tools/nissth-init/test.mjs`, `Tools/nissth-init/README.md`. **Operation:** modify. **Acceptance:** suite green above baseline, covering the new skeleton file, the handoff string, and `--check` in all three exit states.
- [x] **Step 8.** Re-sync both consumers' `CLAUDE.md` framework bodies using `--check` as the verifier, and append a status entry to each consumer's own ledger. **Files:** `ExampleDesktopApp/CLAUDE.md`, `ExampleFinanceApp/CLAUDE.md` + each `AgentReports/StatusUpdate.md`. **Operation:** modify. **Acceptance:** `--check` exits 0 for both; each consumer's banner is untouched; each repo has a new entry naming what changed.

### 3.2 Forbidden in this phase

- No change to any binding under `Bindings/` — Phase 21 closed that surface.
- No change to the framework body's §1–§4 rules, and no new Hard Rule: this phase adds a mechanism and two policy paragraphs, exactly the argument §12.1 makes about `doc-claims`.
- No rotation of this repo's own ledger, and no rotation of any consumer's — §5 gains the procedure; running it is a separate decision for whoever owns the ledger.
- No commit or push in the consumer repos beyond their own `CLAUDE.md` + ledger entry; their product code is not this phase's business.
- No `--fix`/`--sync` flag on `--check`. It reports, like `doc-claims` and `dbl-check`; a tool that rewrites a consumer's `CLAUDE.md` unattended is a different risk with its own plan.
- No public re-cut — that is Phase 23.

### 3.3 Deviations recorded during execution

| # | Deviation | Why it was taken |
|:---|:---|:---|
| 1 | **`AgentReports/Bridge` was dropped from the skeleton the check requires.** It was in the first draft of `missingSkeleton`. | Every consumer gitignores `AgentReports/Bridge/` wholesale (§11.10 #6), so a fresh clone never carries it and the runtime recreates it. Requiring it would have made the check fail on every cloned consumer — the exact failure mode §12.2 warns about, built in on day one. Caught by running the check against the two real consumers rather than only against fixtures. |
| 2 | **The first-drift line is printed as a window around the first differing character**, not the first 96 characters. | Not in the step list; added after the first real run printed two visibly identical lines that differ at column 400. A drift report that cannot show the drift is decoration. |
| 3 | **Step 8 also filled the missing skeleton paths** (`Archive/README.md` in both consumers, `Tests/README.md` in ExampleFinanceApp), which the step described only as a `CLAUDE.md` re-sync. | Leaving `--check` at exit 1 in both consumers on the day it shipped would have taught its first two readers to ignore it. |

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- `nissth-init` runs no subprocess and holds no cache; `node --test` spawns the CLI fresh per case, and every write goes to a `mkdtemp` directory removed afterwards.
- `--check` is verified against the two **live consumer checkouts**, before and after the re-sync — the only evidence that matters for a drift detector is that it found real drift and then went quiet once the drift was gone.
- Consumer edits are verified by `git diff` in each consumer repo, not by this repo's tooling.

**Result, 2026-09-24 23:15:** `node --test Tools/nissth-init/test.mjs` **29 pass / 0 fail** (baseline 21); `node Tools/doc-claims/validate.mjs` exit 0; `node --test Tools/dbl-check/test.mjs` 24/24 and `node --test Tools/nissth-bridge/test.mjs` 32/32, both unchanged. `--check` before the re-sync: ExampleDesktopApp exit 1 (1010 lines), ExampleFinanceApp exit 1 (37 lines + 2 missing paths); after: **both exit 0, in sync**. A non-project directory exits 2. Each consumer has one new status entry, an unchanged banner, and a commit (ExampleDesktopApp `ca14140`, ExampleFinanceApp `a716d7c`).

### 4.2 Checks

- [x] **Build:** N/A — plain JS, no build step.
- [x] **Tests:** `node --test Tools/nissth-init/test.mjs` — expected green, count above baseline. `node Tools/doc-claims/validate.mjs` — expected exit 0. `node --test Tools/dbl-check/test.mjs` and `node --test Tools/nissth-bridge/test.mjs` — expected unchanged (regression guard: §5/§9.1 text is quoted by neither, but the init suite reads `CLAUDE.md`).
- [x] **Runtime/integration:** `--check` against both consumers before the re-sync (expect exit 1 with named drift) and after (expect exit 0). A fresh `--dry-run` init into a tmp dir lists `AgentReports/Archive/README.md`.
- [x] **Bridge re-query:** N/A — no Bridge tool covers `Tools/` or documentation.
- [x] **DBL freshness:** N/A — no DBL artifact covers the touched files.

### 4.3 Pass criteria

ALL of the following must be true:
- Init suite green above baseline; `doc-claims` exit 0; the other two suites unchanged.
- `--check` exits 1 naming real drift before the re-sync and 0 for both consumers after it.
- A dry-run init lists `AgentReports/Archive/README.md` and prints the session handoff.
- Each consumer repo has exactly one new status entry and an unchanged project banner.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [x] Remove temp scripts/artifacts created during execution (the tmp init targets are removed by the suite itself)
- [x] Roll snapshots if no longer needed (`AgentReports/Snapshots/`) — N/A, HR#9 not triggered: every edit is additive and version-controlled in three repos
- [x] **Reports check (CLAUDE.md §10):** no `Verified: FAIL`, no choice between named alternatives, no external spec ingested, no pivot. No Report; the consumers' own ledgers record their half.
- [x] **Document Sync sweep (Hard Rule #11):** modified — `Tools/nissth-init/init.mjs`, `+templates/Archive.README.md`, `CLAUDE.md` §5/§8.2.10/§9.1. Affected stable documents: `Tools/nissth-init/README.md` (updated in Step 7), both consumer `CLAUDE.md` copies (updated in Step 8 — and this phase is what makes that mechanical from now on). None marked stale.
- [x] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

```
### 2026-09-24 HH:MM — Phase 22: consumer-copy drift check, ledger rotation, init handoff, clean-break policy

**State:**
- Phase: 22 closed
- Build: CLEAN · Tests: PASS
- Active plan: none
- DBL refs: none · Bridge reports: none
- Blockers: none

**Report:**
- [condensed from §1 findings]

**Executed:**
- [condensed from §3, with checkboxes resolved]

**Verified:**
- [condensed from §4 results, including freshness statement]
- Doc sync: [updated: ...]
- Reports: none

**Issues:**
- [or "none"]

**Next:**
- [the next phase or task]
```
