# Phase 23: Script the public re-cut, refresh the stale suite counts, publish — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_23_Public_Recut
- **Authored:** 2026-09-24 by Claude (Opus 5)
- **Approved:** 2026-09-24 by user ("update the public version" — explicit instruction this session; framework phases carry standing authorisation)
- **Depends on:** Phase_10_Public_Preview_Branch (the procedure), Phase_21_Lens_Parsers_And_Budget, Phase_22_Consumer_Sync_And_Policy (the content being published)
- **Estimated scope:** A new `Tools/public-cut/` that performs the cut this ledger has re-derived by hand four times, its tests, and the cut itself: `nissth/public` rebuilt from the current `dev` tip and force-pushed to `origin/master`. Also fixes three stale test-count claims in `README.md` and `CLAUDE.md` §8 before they are published.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Establish what the hand procedure actually does, what has changed under it since the last cut, and which public-facing claims are false today.

### 1.1 Inputs to read

- **DBL:** none.
- **Bridge reports:** none.
- **Source:** `ImplementationPlans/Phase_10_Public_Preview_Branch.md` §3.0 (scrub map), §3.1 Steps 4–12, §3.2 (the `Axiom/` protection), §4.2–4.3 (verification). `CLAUDE.md` §5 tree, `README.md` tree + status block.
- **StatusUpdate.md:** latest entry 2026-09-24 23:20 (Phase 22 closed), plus the standing open item repeated since 2026-08-24: *"script the public re-cut so its `Axiom/` strip stops being re-derived by hand."*

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Establish the gap between the published cut and `dev` | `git log --oneline nissth/public..dev` | whole repo | What this cut publishes |
| 2 | Inventory the scrub surface as it stands now | `git grep -I -i -l` for each identifier in the §3.0 map plus the two newer consumers | committed tree | The map was written when there was one consumer; there are now three |
| 3 | Check every public-facing count claim | grep `58/58`, `104/104`, `107 pass` in `README.md` + `CLAUDE.md` | both files | §12.4 — stale claims are worst where strangers read them |
| 4 | Confirm the `Axiom/` tree-row shape | read the last rows of both project trees | `CLAUDE.md` §5, `README.md` | The strip pattern has needed re-deriving for three cuts running |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| What does this cut publish? | phases 11–22 | _to be filled_ | _to be filled_ |
| Is the §3.0 scrub map complete? | no — written for one consumer | _to be filled_ | _to be filled_ |
| Are the published suite counts true? | unknown | _to be filled_ | _to be filled_ |
| Is the `Axiom/` row still the last row under a `├── Tools/` block in both trees? | yes | _to be filled_ | _to be filled_ |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `origin/master` | content | `728e218`, cut from `dev` before Phase 15 |
| the cut procedure | form | prose in a closed plan, re-derived by hand each time |
| `README.md` / `CLAUDE.md` §8 | Expo suite count | `58/58 across 13 suites` |
| `Tools/` | public-cut tooling | none |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/public-cut/` | exists | `cut.mjs` + `scrub-map.json` + tests; performs the whole cut and refuses on any unmet precondition |
| `README.md` / `CLAUDE.md` §8 | Expo suite count | the number a fresh worktree actually produces, with its measurement date |
| `nissth/public` | content | rebuilt from the current `dev` tip: no `Axiom/`, no PDFs, no `settings.local.json`, zero consumer identifiers, one commit |
| `origin/master` | content | that cut |
| `C:\…\Nissth\Axiom\` | on-disk files | **148, untouched** — the hard gate of Phase 10 §3.2, carried forward verbatim |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [ ] **Step 1.** Measure the Expo suite in a **fresh worktree** of `dev` (the §8.2.6 item 6 shape) and record the number. **Operation:** verify. **Acceptance:** `npm ci` → `tsc -p .` → `jest` all green in a tree that is not the development directory.
- [ ] **Step 2.** Correct the three stale count claims to what Step 1 measured, with its date. **Files:** `README.md` (status block + stack table), `CLAUDE.md` §8 preamble. **Operation:** modify. **Acceptance:** no `58/58` remains; `doc-claims` exit 0.
- [ ] **Step 3.** Write `Tools/public-cut/scrub-map.json` — the §3.0 map plus every identifier §1.3 row 2 finds. **Operation:** add. **Acceptance:** each entry carries `find`, `replace`, and a one-line `reason`.
- [ ] **Step 4.** Write `Tools/public-cut/cut.mjs`: preconditions → orphan worktree → populate → delete the non-shareable paths → scrub → strip the `Axiom/` tree rows → reset the ledger to the seed entry → commit → verify → report. **Operation:** add. **Acceptance:** `--dry-run` reports every planned action and writes nothing; the `Axiom/` strip raises rather than passing silently when the tree shape moves.
- [ ] **Step 5.** Write `Tools/public-cut/test.mjs` — the strip on both real trees, the scrub on fixtures, precondition refusals. **Operation:** add. **Acceptance:** green; the strip case asserts the connector is repaired, not just that the row is gone.
- [ ] **Step 6.** Write `Tools/public-cut/README.md`. **Operation:** add. **Acceptance:** states the `Axiom/` hard gate and that the push is a separate, human-confirmed step.
- [ ] **Step 7.** Run the real cut with the script. **Operation:** add (branch). **Acceptance:** §4.2 verification passes inside the worktree; `Axiom/` in the primary directory still holds 148 tracked files at every point.
- [ ] **Step 8.** Force-push `nissth/public:master`, **from the branch ref, never checking the orphan out in the primary working directory** (Phase 10 §3.2). **Operation:** push. **Acceptance:** `origin/master` is the new cut; `git rev-parse --abbrev-ref HEAD` in the primary directory still reports `dev`.

### 3.2 Forbidden in this phase

- **NEVER delete, move, rename, or modify `C:\Users\admin\Desktop\Nissth\Axiom\`.** Carried verbatim from Phase 10 §3.2 — it is the user's live reference material. Every removal happens inside the scratchpad worktree. No `git checkout --orphan` in the primary working directory, no `git clean`, at any point.
- No content change to `dev` beyond Step 2's count correction, this plan, the new tool, and the closing status entry.
- No binding source changes, no dependency bumps.
- No edit to past `StatusUpdate.md` entries (HR#3) — the ledger reset happens only on the orphan branch, where the file is a new blob with no history.
- No `git push` of `dev` without the user saying so; only `master` is in this phase's mandate ("update the public version").
- No retraction of anything already published. A force-push does not unpublish, and forks and caches keep their copies — settled 2026-08-24.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Git is the verifier for every tree and history claim, and is inherently fresh: `git ls-tree -r`, `git rev-list --count` and `git grep` read the committed object graph produced by the cut's own commit, not a cache. All assertions run **after** that commit.
- The suite counts published in Step 2 come from a **fresh worktree**, not the development directory (§8.2.6 item 6) — the whole point of the number.
- The `Axiom/` integrity gate is checked in the **primary** working directory immediately after the deletion step and again at the end, by file count and by `git status`.

### 4.2 Checks

- [ ] **Build:** the cut worktree's `Bindings/Expo` type-checks and its suite runs green from a clean `npm ci`.
- [ ] **Tests:** `node --test Tools/public-cut/test.mjs` green; `node Tools/doc-claims/validate.mjs` exit 0 on `dev` **and** inside the cut.
- [ ] **Runtime/integration:** inside the cut — `./nissth-bridge --list-bindings` finds all three bindings (proves the deletions did not disturb manifest discovery).
- [ ] **Content gates inside the cut:** zero hits for every `find` in the scrub map; no `Axiom/` path; no `*.pdf`; no `.claude/settings.local.json`; `git rev-list --count HEAD` = 1; `LICENSE` present.
- [ ] **`Axiom/` integrity (hard gate):** in the primary working directory — `git status --porcelain --ignored Axiom/` empty AND `git ls-files Axiom | wc -l` = 148.
- [ ] **Bridge re-query / DBL freshness:** N/A.

### 4.3 Pass criteria

ALL of the following must be true:
- Every §4.2 check passes.
- `origin/master` points at the new cut and the primary working directory is still on `dev`, clean, with `Axiom/` intact.
- The published README and CLAUDE.md state suite counts that a fresh tree actually produced today.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup, and do not push.
2. Append a status entry with `Verified: FAIL` citing the check and the artifact.
3. Rollback recipe: `git worktree remove --force <scratchpad>/nissth-public && git branch -D nissth/public`. Nothing is pushed until Step 8, the primary working directory is never modified, and `dev` holds every omitted file — so the operation is reversible up to that point.

---

## 5. Cleanup

- [ ] Remove the scratchpad worktree; confirm `git worktree list` shows only the primary working directory and that `nissth/public` still exists as a branch
- [ ] Roll snapshots — N/A: the cut is a branch, `dev` is untouched, and the rollback recipe is one line
- [ ] **Reports check (CLAUDE.md §10):** a publish is not a code change; no snapshot Report. If a stale claim is found *after* publishing, that is an incident and gets one.
- [ ] **Document Sync sweep (Hard Rule #11):** modified — `README.md`, `CLAUDE.md` §8, `Tools/public-cut/**` (new). Affected: `CLAUDE.md` §5 tree gains a `public-cut/` row; `AgentReports/Reports/2026-08-24_phase-10-public-preview-snapshot.md` describes the hand procedure and is superseded in method, not in content — noted in its own file.
- [ ] No orphan branches beyond `nissth/public`, which is the deliverable

---

## 6. Status Update Entry

```
### 2026-09-24 HH:MM — Phase 23: public re-cut, scripted

**State:**
- Phase: 23 closed
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
