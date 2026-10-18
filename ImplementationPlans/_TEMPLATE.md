# Phase [NN]: [Feature/Task Name] — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_[NN]_[Slug]
- **Authored:** YYYY-MM-DD by [agent or user name]
- **Approved:** YYYY-MM-DD by [user] | `pending`
- **Depends on:** [list of prior plan IDs, or `none`]
- **Estimated scope:** [files touched, components affected — high level, one paragraph max]

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** [specific files in `DBL/` — or `none yet — Phase ≤3`]
- **Bridge reports:** [specific files in `AgentReports/Bridge/` produced for this plan — cite `<file>` + `freshness.generated_at`; or `none — no live state queried`]
- **Source:** [specific files + line ranges; explicit, no full-tree dumps]
- **StatusUpdate.md:** latest entry as of plan authoring (cite timestamp)

### 1.2 Diagnostic actions

> Prefer Bridge tools (`nissth-bridge <tool> ...`) and DBL reads over raw source greps when either covers the question (Hard Rule #4). Use the table's `Tool/command` column to record the exact invocation, including any `--scope.*` flags.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | [what to check] | [`nissth-bridge <tool> --mode <m>` / DBL read / Grep / build / test command] | [scope.* flags or path + filter] | [what assumption it validates] |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| [restate from 1.2] | [predicted] | _to be filled_ | _to be filled_ |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| [file:line or component path] | [property/field] | [value] |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| [file:line or component path] | [property/field] | [value] |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [ ] **Step 1.** [Action]. **File:** `[path]`. **Lines:** `[n-m]` or `new file`. **Operation:** add | modify | remove. **Acceptance:** [what proves this specific step is done].
- [ ] **Step 2.** ...
- [ ] **Step 3.** ...

### 3.2 Forbidden in this phase

> Explicitly list what is OUT OF SCOPE. This is the anti-scope-creep guard.

- [thing not to touch — even if tempting]
- [refactor not to do]

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

> How do you know the verifier sees the latest changes? (Addresses the "false CLEAN" failure mode — HR#10.)

- [e.g., "Build is run from a clean state with `--no-cache`"; or "Test runner reads from disk, files were saved before invocation"; or "`nissth-bridge compile_verify` ran — its freshness stamp confirms the daemon was stopped and a clean rebuild happened (§11.7)"]
- If a Bridge action tool's hard-enforce contract is the freshness guarantee, cite the tool's exit-code-5 behavior explicitly.

### 4.2 Checks

- [ ] **Build:** `[command]` — expected: [exit code / log line / artifact path]
- [ ] **Tests:** `[command + filter]` — expected: [pass count / specific test names]
- [ ] **Runtime/integration:** [if applicable — describe + expected]
- [ ] **Bridge re-query:** [if a Bridge tool covers the touched surface, re-run it post-execution and cite the new report file. Confirms no DBL artifacts got flipped to STALE unexpectedly (§11.4).]
- [ ] **DBL freshness:** [if structure changed, regenerate affected DBL artifacts; list them. Any DBL artifact STALE-flipped by a Bridge re-query MUST be regenerated here or queued in §6 Next.]

### 4.3 Pass criteria

ALL of the following must be true:
- [criterion 1]
- [criterion 2]
- [criterion 3]

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [ ] Remove temp scripts/artifacts created during execution
- [ ] Roll snapshots if no longer needed (`AgentReports/Snapshots/`)
- [ ] **Reports check (CLAUDE.md §10):**
  - Did this phase trigger a mandatory Report (Verified: FAIL → incident; named-alternative decision → decision; long external spec ingested → spec_digest; non-trivial phase close → snapshot; cross-phase pivot → decision)? If yes, author it under `AgentReports/Reports/YYYY-MM-DD_<slug>.md` with the §10.3 frontmatter.
  - If a status block (`Issues`, `Report`, `Verified`) is approaching ~10 lines, spin the detail off into a Report and link it.
  - List authored Reports here so they appear in the §6 status entry's `Reports:` line.
- [ ] **Document Sync sweep (Hard Rule #11):**
  - List source files modified in §3
  - For each, identify affected stable documents:
    - `DBL/**` artifacts whose `covers` overlap (most common)
    - Other plans in `ImplementationPlans/` that cross-reference the modified files/symbols
    - `CLAUDE.md` examples that cite the modified code
  - For each affected document, either:
    - **UPDATE** it now in this same task (preferred for small surface), OR
    - **MARK STALE** in the document's frontmatter (DBL: `last_regenerated: STALE — [reason]`) AND add a regeneration step to the next plan (`§6 Next`)
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [updated: X, Y; marked stale: Z]`
- [ ] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase [NN]: [Task Name]

**State:**
- Phase: [n/N]
- Build: CLEAN | HAS_ERRORS
- Tests: PASS | FAIL
- Active plan: ImplementationPlans/Phase_[NN]_[Slug].md
- DBL refs: [files read or updated during this phase]
- Bridge reports: [files under `AgentReports/Bridge/` produced or read this phase; or "none"]
- Blockers: [or "none"]

**Report:**
- [condensed from §1 findings]

**Executed:**
- [condensed from §3, with checkboxes resolved]

**Verified:**
- [condensed from §4 results, including freshness statement]
- Doc sync: [updated: ...; marked stale: ...] | none — no source files modified
- Reports: [AgentReports/Reports/<filename> (kind), ...] | none — no Report-worthy artifacts produced

**Issues:**
- [or "none"; if Verified: FAIL, cite the matching incident Report filename here]

**Next:**
- [the next phase or task]
```
