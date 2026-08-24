# Phase 12: Documentation Status Sync — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_12_Doc_Status_Sync
- **Authored:** 2026-08-24 by Claude (Opus 5)
- **Approved:** `pending`
- **Depends on:** Phase_10_Public_Preview_Branch (closed), Phase_11_Fresh_Clone_Hardening (closed)
- **Estimated scope:** Correct the stale status claims in `CLAUDE.md` (2 sites) and `README.md` (6 sites). These documents still describe a framework where the PostgreSQL binding does not exist, the Expo binding is "in flight", and Spring Boot is 111/111 — none of which has been true since 2026-05-18. Two files modified, no source code touched. The public branch is then re-cut so the corrected text reaches `origin/master`, which currently serves the stale version to every visitor.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none — Nissth's own `DBL/**` holds `_TEMPLATE.md` skeletons only.
- **Bridge reports:** none — no live stack state is queried. The question is "what do our own docs claim", answerable from the docs and the manifests.
- **Source:**
  - `CLAUDE.md:5` (Status header), `CLAUDE.md:234` (§8 shipped/in-flight/queued claim)
  - `README.md:5` (Status line), `:210` (structure tree row), `:355` (Postgres tool list), `:516-518` (stack table rows), `:561` (expected test count)
  - `Bindings/*/*.bridge.json` — the tool manifests, ground truth for tool names and counts
- **StatusUpdate.md:** latest entry as of authoring — `2026-08-24 09:45 — Push result: restructure complete`.

### 1.2 Diagnostic actions

> Prefer Bridge tools (`nissth-bridge <tool> ...`) and DBL reads over raw source greps when either covers the question (Hard Rule #4). Use the table's `Tool/command` column to record the exact invocation, including any `--scope.*` flags.

No Bridge tool reports on documentation accuracy — the bindings lens over code and databases, not over prose claims about themselves. Grep plus the manifests is the correct instrument; recorded here per HR#5.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Enumerate stale claims | `grep -n` for `Phase 4 of 4`, `111/111`, `51/51`, `In flight`, `Queued`, `not yet authored`, `not on disk` | `CLAUDE.md`, `README.md`, `AGENTS.md`, `Ultimate_Guide.md` | Establishes the exact edit set; catches drift since authoring |
| 2 | Confirm `Ultimate_Guide.md` is clean | same grep | `Ultimate_Guide.md` | If it is not clean, scope must widen before execution, not during |
| 3 | Read ground-truth tool names per binding | `python -c` over `Bindings/*/*.bridge.json` | 3 manifests | `README.md:355` names `index_drift`, which is not a real tool. The manifests decide the correct list |
| 4 | Confirm current suite counts | latest StatusUpdate entry (measured 2026-08-24) | `AgentReports/StatusUpdate.md` | The replacement numbers must be measured, not guessed |
| 5 | Confirm which phases actually closed | `grep '^### 2026' AgentReports/StatusUpdate.md` + plan list | repo | The new Status line enumerates them; it must not overclaim |
| 6 | Confirm `origin/master` serves the stale text | `git show origin/master:README.md` head | remote | Establishes that the re-cut in Step 5 is necessary, not cosmetic |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Stale claims in `CLAUDE.md`? | 2 sites — line 5 header (`Phase 4 of 4`), line 234 (`111/111`, Expo "in flight", Postgres "no §8.3 yet authored") | _to be filled_ | _to be filled_ |
| Stale claims in `README.md`? | 6 sites — `:5`, `:210`, `:355`, `:516`, `:518`, `:561` | _to be filled_ | _to be filled_ |
| `AGENTS.md` stale? | no — its only Phase reference is to HR#12, which is current | _to be filled_ | _to be filled_ |
| `Ultimate_Guide.md` stale? | no — grep returns zero hits | _to be filled_ | _to be filled_ |
| Postgres tool names per manifest? | `schema_lens`, `query_plan`, `index_audit`, `lock_audit`, `migration_status` — note `index_drift` in `README.md:355` is not among them | _to be filled_ | _to be filled_ |
| Tools per binding? | 5 each, all three bindings | _to be filled_ | _to be filled_ |
| Current measured suite counts? | Dispatcher 32/32; SpringBoot 104/104; Expo 58/58; Postgres 107 pass/18 skip/125 total | _to be filled_ | _to be filled_ |
| Does `origin/master` serve the stale README? | yes | _to be filled_ | _to be filled_ |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

Row-specific note: if rows 3 or 7 differ from expected, the *replacement text* is wrong, not just the plan — re-derive the numbers from the manifests and the latest measured entry before editing.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `CLAUDE.md:5` | Status header | "Phase 4 of 4 — Spring Boot stack bound" |
| `CLAUDE.md:234` | §8 binding roster | Spring Boot shipped at "111/111"; Expo "In flight … under active development"; PostgreSQL "Queued … no §8.3 yet authored" |
| `README.md:5` | Status line | "Phase 6/6+ complete"; SpringBoot "111/111"; Expo "51/51"; "PostgreSQL binding queued (Phase 07 candidate)" |
| `README.md:210` | structure tree | `└── Postgres/   (Phase 07 queued; not yet authored)` |
| `README.md:355` | Postgres tool list | "will ship `query_plan`, `index_drift`, `lock_audit`, plus an action tool TBD" |
| `README.md:516` | stack table, Spring Boot row | "Shipped (Phase 05 closed 2026-05-17, 111/111 green)" |
| `README.md:518` | stack table, PostgreSQL row | "`Bindings/Postgres/` (not on disk yet)"; "**Queued** — plan not yet authored"; "TBD (Go or Python)"; tools "TBD" |
| `README.md:561` | expected test count | "Expected: 104 unit + 7 IT = 111/111 PASS" |
| `origin/master` | served docs | the stale text above — visible to every visitor |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `CLAUDE.md:5` | Status header | Framework operational; four bindings shipped (Spring Boot, Expo, PostgreSQL) plus the unified dispatcher; names the hard rules and contracts added and when; keeps the "Phase 5+ hardening" note only where it is still true (`.claude/` hooks, `Tools/` DBL regeneration are genuinely still unbuilt) |
| `CLAUDE.md:234` | §8 binding roster | All three sections shipped, each with its closing phase and current measured count; no "in flight", no "queued" |
| `README.md:5` | Status line | Current phase state, three bindings shipped with measured counts, dispatcher shipped, honest note on what remains unbuilt |
| `README.md:210` | structure tree | `Postgres/` described as shipped, matching the `Expo/` and `SpringBoot/` rows |
| `README.md:355` | Postgres tool list | The five real tools from the manifest, described as shipped and diagnostic-only |
| `README.md:516` | stack table, Spring Boot row | "Shipped (Phase 05 closed 2026-05-17, 104/104 green)" |
| `README.md:518` | stack table, PostgreSQL row | Shipped; path exists; TypeScript/npm; 5 tools |
| `README.md:561` | expected test count | The measured Maven count, 104/104 |
| `origin/master` | served docs | the corrected text, via a re-cut of `nissth/public` |
| `Bindings/**`, `Tools/**`, any `src/` | content | **unchanged** — no code is touched in this phase |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [ ] **Step 1.** Run every §1.2 action and fill §1.3. **File:** this plan, §1.3. **Operation:** modify. **Acceptance:** all eight rows have an `Actual answer`; STOP if any reads `no`.

- [ ] **Step 2.** Correct the two `CLAUDE.md` sites. **File:** `CLAUDE.md`. **Lines:** `5`, `234`. **Operation:** modify. Replacement numbers come from §1.3 rows 3 and 7, not from memory. Retain the "Phase 5+ hardening" language **only** for `.claude/` hook enforcement and `Tools/` DBL auto-regeneration, which remain genuinely unbuilt — do not declare them done. **Acceptance:** neither line contains `Phase 4 of 4`, `111/111`, `In flight`, or `no §8.3 yet authored`; the §8 roster names all three bindings as shipped.

- [ ] **Step 3.** Correct the six `README.md` sites. **File:** `README.md`. **Lines:** `5`, `210`, `355`, `516`, `518`, `561`. **Operation:** modify. `:355`'s tool list must be the manifest's five names — `index_drift` does not exist and must not survive the edit. **Acceptance:** the §1.2 action-1 grep returns 0 hits across `README.md`; the PostgreSQL row no longer says "not on disk yet".

- [ ] **Step 4.** Re-run the §1.2 action-1 grep across all four documents as a self-check before any commit. **Operation:** verify. **Acceptance:** 0 stale-claim hits repo-wide, `Axiom/` excluded.

- [ ] **Step 5.** Re-cut the public branch so the corrections reach `origin/master`. Follow `Phase_10_Public_Preview_Branch.md` §3.1 Steps 4–12 verbatim against the current `dev` tip — orphan worktree, five omitted paths, status-log reset, §3.0 scrub map, `Axiom/` row removal, single commit, verification from the worktree, worktree removal. Carry forward the LICENSE and the two self-referential omissions (the Phase 10 plan and its manifest); add `Phase_12_Doc_Status_Sync.md` to the shipped plan set. **Acceptance:** Phase 10 §4.3 pass criteria all true against the new cut, including the `Axiom/` integrity gate.

- [ ] **Step 6.** Push. **Commands:** `git push origin dev`, then `git push --force origin nissth/public:master`. **Operation:** modify (remote). **Acceptance:** `origin/master` serves the corrected `README.md`; `origin/dev` carries the full history; both read back after `git fetch`, not inferred from push output.

- [ ] **Step 7.** Append the §6 status entry.

### 3.2 Forbidden in this phase

- **No code changes.** Nothing under `Bindings/*/src/`, `Bindings/*/tests/`, or `Tools/` is touched. This phase edits prose only. If a doc claim turns out to be wrong *about the code* rather than stale, record it and stop — fixing the code is a different plan.
- **No rewriting either document for a public audience.** The README is written for its author and reads that way; that is a real gap but a separate piece of work. This phase corrects claims that are **false**, not claims that are merely unpolished.
- **No LICENSE, CONTRIBUTING, or CODE_OF_CONDUCT.** LICENSE already landed on the public branch; the rest is out of scope.
- **No re-litigating the author-name or exposure decisions.** Both were settled by the user on 2026-08-24 and are recorded in the 09:35 entry.
- **No touching `Axiom/`**, the two root PDFs, or `.claude/settings.local.json` in the primary working directory — the Phase 10 §3.2 protections carry over in full, including the ban on in-place orphan checkout.
- **No deleting or force-pushing `origin/dev`.** Only `origin/master` is force-pushed, and only from `nissth/public`.
- **No version bumps, no changelog, no `*.bridge.json` edits.** The manifests are read as ground truth, never written.
- **No DBL population.** `DBL/**` staying as skeletons is correct for the framework's own repo; filling it is not a documentation fix.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- The claim-accuracy check is a grep over the working tree **after** the edits are saved; the files are read from disk, not from an editor buffer.
- Tool names are verified against `Bindings/*/*.bridge.json` parsed as JSON, not against prose elsewhere in the docs — otherwise one stale doc would validate another.
- Suite counts are **not** re-measured in this phase and none are claimed as fresh: the numbers written into the docs are the ones measured on 2026-08-24 and recorded in the 08:40 and 09:05 status entries. This is stated explicitly rather than implying a new run. If any doc number would assert something never measured, STOP.
- The public-branch verification in Step 5 inherits Phase 10 §4.1 in full — fresh `npm ci`, `mvn clean test`, `node --test`, run from inside the new orphan worktree per the fresh-clone clause Phase 11 added.
- Remote state in Step 6 is confirmed by `git fetch` + read-back of `origin/master` and `origin/dev`, never by trusting push output.

### 4.2 Checks

- [ ] **Claim sweep:** the §1.2 action-1 grep over `CLAUDE.md`, `README.md`, `AGENTS.md`, `Ultimate_Guide.md` — expected: 0 hits.
- [ ] **Tool-name accuracy:** every tool named in `README.md:355` appears in `Bindings/Postgres/postgres.bridge.json` — expected: 5/5 match, `index_drift` absent.
- [ ] **No code drift:** `git diff --stat` touches only `CLAUDE.md`, `README.md`, this plan, and `StatusUpdate.md` — expected: no file under `Bindings/`, `Tools/`, or `Tests/`.
- [ ] **Suites unchanged:** the four suites on `dev` — expected: 32/32, 104/104, 58/58, 107 pass/18 skip. A prose-only change must not move them; this is a guard against an accidental code edit, not a fresh measurement of the docs' claims.
- [ ] **Public branch:** Phase 10 §4.2 checks against the new cut — 0 forbidden paths, 0 consumer references, 4 suites green from the worktree, bridge discovery resolves 3 bindings.
- [ ] **Remote:** `origin/master` README contains the corrected PostgreSQL row; `origin/dev` unchanged in content except this phase's commits.
- [ ] **Bridge re-query:** N/A — no Bridge tool covers documentation claims and no tool surface changes.
- [ ] **DBL freshness:** N/A — `DBL/**` holds `_TEMPLATE.md` skeletons only; no `covers` glob overlaps `CLAUDE.md` or `README.md`.

### 4.3 Pass criteria

ALL of the following must be true:
- 0 stale-claim grep hits across the four documents.
- `README.md` names exactly the five real Postgres tools; `index_drift` appears nowhere.
- No file under `Bindings/`, `Tools/`, or `Tests/` appears in the diff.
- The four suites on `dev` match their recorded baselines.
- The new public cut passes Phase 10 §4.3 in full, `Axiom/` integrity gate included (148 on disk / 148 tracked / 0 dirty in the primary working directory).
- `origin/master` serves the corrected docs; `origin/dev` retains the full history and its 148 `Axiom/` files.
- Both root PDFs still present and tracked on `dev`.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

Rollback: `git checkout -- CLAUDE.md README.md` before any commit. After a commit but before the push, `git reset --hard HEAD~1` on `dev`. After the push, re-push the prior `nissth/public` tip to `master` — the previous public commit stays reachable locally on the branch until it is re-cut, so record its SHA in the Step 1 findings before Step 5 replaces it.

---

## 5. Cleanup

- [ ] Remove the orphan worktree created in Step 5 (`git worktree remove`); re-resolve its path via `git worktree list`.
- [ ] Remove temp scripts/artifacts created during execution
- [ ] Roll snapshots if no longer needed (`AgentReports/Snapshots/`)
- [ ] **Reports check (CLAUDE.md §10):**
  - No mandatory Report is triggered on success: no named-alternative decision, no external spec ingested, no cross-phase pivot, and a prose-accuracy fix is not a "non-trivial phase close" under §10.4 trigger #4. On failure, §10.4 trigger #1 makes an incident Report mandatory.
  - Consider whether the *cause* deserves a Report: these claims went stale across seven phases despite HR#11 requiring a Doc Sync sweep at every close. If the executing agent concludes the mandate has a structural gap for repo-root documents (as opposed to `DBL/` artifacts, which have `covers` globs to key off), that is a `decision` Report worth authoring — the fix would be a mechanism, not another rule.
  - List authored Reports here so they appear in the §6 status entry's `Reports:` line.
- [ ] **Document Sync sweep (Hard Rule #11):**
  - Source files modified in §3: `CLAUDE.md`, `README.md`. No code.
  - Affected stable documents: `AGENTS.md` (redirect only — re-read to confirm it makes no binding-roster claim of its own); `Ultimate_Guide.md` (confirmed clean at §1.3 row 4, re-confirm post-edit); `Bindings/README.md` and the three per-binding READMEs (check whether any repeats a "queued"/"not yet authored" claim about a sibling binding).
  - For each affected document, either **UPDATE** now or **MARK STALE** with a regeneration step queued in §6 `Next`.
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [updated: X, Y; marked stale: Z]`
- [ ] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 12: Documentation Status Sync

**State:**
- Phase: 12 CLOSED.
- Build: CLEAN | HAS_ERRORS
- Tests: PASS | FAIL — dev: Dispatcher [n]/32; SpringBoot [n]/104; Expo [n]/58; Postgres [n] pass/[n] skip. Public cut: [same four].
- Active plan: ImplementationPlans/Phase_12_Doc_Status_Sync.md
- DBL refs: none — Nissth's own DBL holds `_TEMPLATE.md` skeletons only
- Bridge reports: none — no live stack state queried; documentation phase
- Blockers: [or "none"]

**Report:**
- [condensed §1.3 findings — especially the tool-name row and the measured-count row, the two that gate replacement text]

**Executed:**
- [Steps 1–7 with checkboxes resolved; state plainly which claims were false and what they became]

**Verified:**
- [§4 results. Freshness: claim sweep read from disk post-edit; tool names parsed from the manifests; suite counts carried forward from 2026-08-24 measurements and NOT re-measured for the doc text; public cut verified from its own worktree; remote read back after fetch.]
- Doc sync: [updated: ...; marked stale: ...]
- Reports: [... | none — no §10.4 trigger fired]

**Issues:**
- [or "none"; if Verified: FAIL, cite the matching incident Report filename here]

**Next:**
- [the next phase or task]
```
