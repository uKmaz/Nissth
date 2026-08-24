# Phase 14: Publish Doc-Claims — README Tree Row + Public Re-Cut — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_14_Publish_Doc_Claims
- **Authored:** 2026-08-24 by Claude (Opus 5)
- **Approved:** 2026-08-24 by user ("do it" — in response to the offer to re-cut with the README row folded in)
- **Depends on:** Phase_13_Doc_Claim_Validator (closed), Phase_12_Doc_Status_Sync (closed), Phase_10_Public_Preview_Branch (the re-cut procedure this phase reuses)
- **Estimated scope:** One `README.md` edit (the `Tools/` structure-tree row, marked-not-fixed at Phase 13 close because §3.2 forbade it there), then a re-cut of `nissth/public` from `dev` and a push, so `CLAUDE.md` §12 and `Tools/doc-claims/` reach `origin/master`. One document modified; no code touched.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none — Nissth's own `DBL/**` holds `_TEMPLATE.md` skeletons only.
- **Bridge reports:** none — no live stack state queried.
- **Source:** `README.md:208-214` (structure tree, `Tools/` row); `CLAUDE.md:99-104` (structure tree tail — the shape the re-cut's `Axiom/` strip must match)
- **StatusUpdate.md:** latest entry as of authoring — `2026-08-24 12:30 — Phase 13: Doc-Claim Validator — VERIFIED PASS`. Its `Issues` block names the two items this phase closes.

### 1.2 Diagnostic actions

> Prefer Bridge tools (`nissth-bridge <tool> ...`) and DBL reads over raw source greps when either covers the question (Hard Rule #4). Use the table's `Tool/command` column to record the exact invocation, including any `--scope.*` flags.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm `README.md`'s `Tools/` row is still childless | read `README.md:212` | 1 line | Step 2 edits it; catches drift |
| 2 | Confirm `CLAUDE.md`'s tree tail shape | read `CLAUDE.md:99-104` | 6 lines | **Load-bearing.** Phase 13 added children under `Tools/`, breaking the two-line `Tools/`+`Axiom/` pattern the re-cut used to strip the `Axiom/` row. Step 5 must match the new shape or it will silently leave `Axiom/` in the public tree |
| 3 | Run the doc-claim validator | `node Tools/doc-claims/validate.mjs` | repo | Baseline before the edit; must stay 0 after |
| 4 | Confirm `dev` is clean and pushed | `git status`, `git rev-parse dev origin/dev` | repo | The re-cut populates from `dev`; uncommitted work would be silently omitted |
| 5 | Confirm the LICENSE is on `dev` | `git ls-tree dev -- LICENSE` | 1 path | Phase 12 found the re-cut silently dropped it. `68250c4` put it on `dev`; this verifies the fix holds |
| 6 | Record the current public tip | `git rev-parse nissth/public` | 1 ref | §4.4 rollback target — the branch is deleted and recreated |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| `README.md:212` `Tools/` row childless? | yes — `├── Tools/   Framework tooling — Phase 5+ hardening` with no child rows | yes — childless, exactly as predicted | yes |
| `CLAUDE.md` tree tail shape? | `Tools/` now has two child rows between it and `└── Axiom/`; the old two-line strip pattern no longer matches | confirmed — `Tools/` now carries `nissth-bridge/` and `doc-claims/` child rows between it and `└── Axiom/`; the Phase 10 two-line pattern would no longer match | yes |
| Validator baseline? | exit 0, no findings | exit 0, no findings | yes |
| `dev` clean and synced with `origin/dev`? | yes — working tree clean, `dev` == `origin/dev` at `8c0e2c8` | yes — `dev` == `origin/dev` == `8c0e2c8`; the single dirty entry is this plan file, untracked | yes |
| LICENSE on `dev`? | yes — added `68250c4` | yes — present, added `68250c4` | yes |
| Current `nissth/public` tip? | `90dd9dc` | `90dd9dc` | yes |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

Row 2 is the one to read carefully. If the strip pattern is assumed rather than matched against the file, the failure is silent: the public branch ships an `Axiom/` row pointing at a directory that is not there.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `README.md:212` | `Tools/` tree row | `├── Tools/    Framework tooling — Phase 5+ hardening`, no children — does not show `nissth-bridge/` or `doc-claims/` |
| `origin/master` | served content | `90dd9dc` — pre-Phase-13: no `Tools/doc-claims/`, no `CLAUDE.md` §12 |
| `nissth/public` | tip | `90dd9dc` |
| `dev` | tip | `8c0e2c8`, clean, synced |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `README.md` | `Tools/` tree row | shows `nissth-bridge/` and `doc-claims/` children, matching `CLAUDE.md`'s tree |
| Validator | exit code on `dev` and on the cut | 0 |
| `nissth/public` | tip | a new single orphan commit containing `Tools/doc-claims/`, `CLAUDE.md` §12, LICENSE, and **no** `Axiom/` row in either tree |
| `origin/master` | served content | the new cut |
| `origin/dev` | content | `dev` including this phase's commits; `Axiom/` and both PDFs intact |
| `Bindings/**`, `Tools/**` | content | **unchanged** — no code touched |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [ ] **Step 1.** Run every §1.2 action; fill §1.3. **Operation:** modify (this plan). **Acceptance:** six rows answered; STOP on any `no`.

- [ ] **Step 2.** Fix the `README.md` structure-tree row so it matches `CLAUDE.md`'s. **File:** `README.md`. **Lines:** ~212. **Operation:** modify — `Tools/` gains `nissth-bridge/` and `doc-claims/` child rows; box-drawing connectors stay consistent and `└── Axiom/` remains the last row. **Acceptance:** the two child rows render aligned; `node Tools/doc-claims/validate.mjs` still exits 0.

- [ ] **Step 3.** Commit Step 2 on `dev` and push. **Operation:** modify. **Acceptance:** `dev` clean; `origin/dev` updated.

- [ ] **Step 4.** Re-cut. Delete `nissth/public` and its worktree if present, then follow `Phase_10_Public_Preview_Branch.md` §3.1 Steps 4–11 against the current `dev` tip: orphan worktree, five omitted paths, the two self-referential omissions (the Phase 10 plan and its manifest), `.gitignore` entry, status-log reset to one seed entry, §3.0 scrub map, single commit. **Acceptance:** one commit; 0 forbidden paths; 0 consumer references.

- [ ] **Step 5.** Strip the `Axiom/` rows from **both** structure trees, matching the **current** shapes rather than the Phase 10 patterns. `CLAUDE.md`'s `Tools/` row now has two children between it and `└── Axiom/`; `README.md`'s will too after Step 2. In both, remove the `└── Axiom/` line and promote the preceding line's connector to `└──`. **Operation:** remove. **Acceptance:** `grep -c "Axiom/"` returns 0 in both files **on the cut**, and the last tree row in each uses `└──`. A failed match must raise, not pass silently.

- [ ] **Step 6.** Verify the cut from its own worktree per Phase 10 §4.2 — four suites, dist file count, checkout line endings, bridge discovery — plus `node Tools/doc-claims/validate.mjs` and `node --test Tools/doc-claims/test.mjs` **run inside the cut**, since the tool is what this phase publishes. **Acceptance:** Phase 10 §4.3 criteria all true, `Axiom/` integrity gate included; validator exits 0; doc-claims suite green.

- [ ] **Step 7.** Push: `git push origin dev`, then `git push --force origin nissth/public:master`. **Operation:** modify (remote). **Acceptance:** both branches read back after `git fetch`, not inferred from push output.

- [ ] **Step 8.** Remove the worktree; append the §6 status entry.

### 3.2 Forbidden in this phase

- **No content changes to `README.md` beyond the `Tools/` tree row.** The README is written for its author rather than a newcomer — a real gap, and still not this phase's business.
- **No code changes.** Nothing under `Bindings/`, `Tools/`, or `Tests/`. `Tools/doc-claims/` ships exactly as Phase 13 closed it, allowlist included — pruning the 27 unused entries is a separate, deliberate audit.
- **No hook or CI wiring** for the validator. Still Option D in the decision Report, still deferred.
- **No touching `Axiom/`**, the two root PDFs, or `.claude/settings.local.json` in the primary working directory. Phase 10 §3.2's protections carry in full, including the ban on in-place orphan checkout.
- **No force-push to `origin/dev`.** Only `origin/master` is force-pushed, and only from `nissth/public`.
- **No re-litigating settled decisions** — the author name on `Approved:` lines, the `umutbrkt/Axiom` attribution, the public-repo exposure. All settled 2026-08-24 and recorded.
- **No new Hard Rule, no `CLAUDE.md` edit at all.** §12 already landed in Phase 13; this phase only publishes it.
- **No editing prose to make the validator pass.** If it reports a finding, narrow the tool or fix a genuinely wrong claim — never reword to dodge a true positive.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- The public cut is built in a **fresh `git worktree`** and verified from inside it — `npm ci` (not `npm install`), `./mvnw clean test`, `node --test` — per the §8.x.6 fresh-clone clause Phase 11 added. Re-resolve the scratchpad path via `git worktree list`; a prior session's path will not match.
- The validator and its suite are run **inside the cut**, not only on `dev`. Publishing a tool without checking it works in the tree being published is exactly the class of miss Phase 11 was built to prevent.
- Binding suite counts are re-measured in the cut rather than carried forward, because the scrub touches `JsonCommandParserTest.java`. No count is asserted that was not measured this phase.
- Remote state is confirmed by `git fetch` plus read-back of `origin/master` and `origin/dev`, never by trusting push output.
- The Spring Boot integration suite is **not** run: it needs a Docker daemon, absent on this host. This is stated rather than quietly omitted, and no `111/111` claim is made.

### 4.2 Checks

- [ ] **Tree parity:** `README.md` and `CLAUDE.md` structure trees both show `nissth-bridge/` and `doc-claims/` under `Tools/` — on `dev`.
- [ ] **Validator on `dev`:** exit 0.
- [ ] **Validator inside the cut:** exit 0.
- [ ] **doc-claims suite inside the cut:** `node --test Tools/doc-claims/test.mjs` — expected all green. Note its `the real repository passes` case runs against the cut, which is the point.
- [ ] **Four binding suites inside the cut:** Dispatcher 32/32; SpringBoot 104/104 unit; Expo 58/58; Postgres 107 pass/18 skip, 16 `.js` emitted.
- [ ] **Axiom rows gone on the cut:** `grep -c "Axiom/"` = 0 in both `CLAUDE.md` and `README.md`; last tree row in each uses `└──`.
- [ ] **Forbidden paths on the cut:** 0 `Axiom/`, 0 PDFs, 0 `settings.local.json`; 0 consumer references.
- [ ] **LICENSE on the cut:** present.
- [ ] **`Axiom/` integrity in the primary working directory:** 148 on disk / 148 tracked / 0 dirty, checked after worktree creation, after the deletion step, and before the push.
- [ ] **Remote:** `origin/master` serves `Tools/doc-claims/validate.mjs` and `CLAUDE.md` §12; `origin/dev` retains `Axiom/` and both PDFs.
- [ ] **Bridge re-query:** N/A — no binding tool surface changes.
- [ ] **DBL freshness:** N/A — skeletons only; no `covers` overlap.

### 4.3 Pass criteria

ALL of the following must be true:
- Validator exits 0 on `dev` **and** inside the cut; doc-claims suite green inside the cut.
- Four binding suites in the cut match their baselines.
- Both structure trees on the cut are `Axiom/`-free with correct connectors.
- LICENSE present on the cut; 0 forbidden paths; 0 consumer references.
- `Axiom/` integrity gate holds in the primary working directory at every checkpoint.
- `origin/master` serves the new cut; `origin/dev` keeps the full history.
- No file under `Bindings/`, `Tools/`, or `Tests/` appears in this phase's diff.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry with `Verified: FAIL`, citing the check and the artifact.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

Rollback: before Step 7 nothing is published — `git checkout -- README.md`, `git worktree remove --force <path>`, `git branch -D nissth/public`. After Step 7, re-push the prior public tip recorded in §1.3 row 6 to `master`.

---

## 5. Cleanup

- [ ] Remove the orphan worktree (`git worktree remove`); re-resolve via `git worktree list`. Leave no other worktree behind.
- [ ] Remove temp scripts/artifacts created during execution
- [ ] Roll snapshots if no longer needed (`AgentReports/Snapshots/`) — none created; §4.4's rollback is a checkout plus a branch delete
- [ ] **Reports check (CLAUDE.md §10):**
  - No mandatory Report expected on success: no named-alternative decision, no spec ingested, no cross-phase pivot, and a one-row doc fix plus a re-cut is not a non-trivial phase close (§10.4 trigger #4). On failure, trigger #1 makes an incident Report mandatory.
  - If the Step 5 strip needs a third distinct pattern, that is evidence the re-cut is accumulating hidden coupling to document layout — worth a `decision` Report proposing the strip be scripted and committed rather than re-derived each time.
  - List authored Reports here so they appear in the §6 status entry's `Reports:` line.
- [ ] **Document Sync sweep (Hard Rule #11):**
  - Source files modified in §3: `README.md` only.
  - Affected stable documents: `CLAUDE.md` §5 tree (already correct as of Phase 13 — confirm parity, do not edit); `Tools/doc-claims/README.md` and `Tools/nissth-bridge/README.md` (confirm neither describes the repo tree in a way this row change contradicts).
  - For each affected document, either **UPDATE** now or **MARK STALE** with a regeneration step queued in §6 `Next`.
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [updated: X, Y; marked stale: Z]`
- [ ] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 14: Publish Doc-Claims

**State:**
- Phase: 14 CLOSED.
- Build: CLEAN | HAS_ERRORS
- Tests: PASS | FAIL — in the cut: Dispatcher [n]/32; SpringBoot [n]/104; Expo [n]/58; Postgres [n] pass/[n] skip; doc-claims [n]/[n].
- Active plan: ImplementationPlans/Phase_14_Publish_Doc_Claims.md
- DBL refs: none — Nissth's own DBL holds `_TEMPLATE.md` skeletons only
- Bridge reports: none — no live stack state queried
- Blockers: [or "none"]

**Report:**
- [condensed §1.3 findings — especially row 2, the strip-pattern shape]

**Executed:**
- [Steps 1–8 with checkboxes resolved]

**Verified:**
- [§4 results. Freshness: fresh worktree, npm ci, mvn clean test, node --test; validator and its suite run INSIDE the cut; remote read back after fetch. Spring Boot ITs not run — Docker absent — and no 111/111 claimed.]
- Doc sync: [updated: ...; marked stale: ...]
- Reports: [... | none — no §10.4 trigger fired]

**Issues:**
- [or "none"; if Verified: FAIL, cite the matching incident Report filename here]

**Next:**
- [the next phase or task]
```
