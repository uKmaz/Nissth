# Phase 25: DBL regeneration — a freshness check that can fail, and a worksheet that makes the rewrite cheap — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_25_DBL_Regeneration
- **Authored:** 2026-09-25 by Claude (Opus 5)
- **Approved:** 2026-09-25 by user ("continue" — this scope was proposed and accepted this session; framework phases carry standing authorisation)
- **Depends on:** Phase_16_DBL_Check, Phase_24_Plan_Lint
- **Estimated scope:** Two halves of the same leak. First a defect fix in `Tools/dbl-check`: its `covers-changed-since` check only fires when `source_state` is a *bare* hex ref, so a consumer writing `git c5e6a34 (Phase 06 …)` — an informative form §7.2 invites — has had that check silently skipped on **every** artifact. Then a new `Tools/dbl-regen/` that turns "this artifact is stale" into a worksheet the author can act on, and stamps the frontmatter once the body is rewritten. Field-verified against both live consumers, whose ledgers get the result.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Measure how much of the freshness contract is actually being checked today, and establish what a regeneration tool can do honestly without pretending to author an artifact's body.

### 1.1 Inputs to read

- **DBL:** none in this repo (`DBL/**` here is templates only). The consumers' artifacts are read as **evidence of the defect**, not as authority: `ExampleFinanceApp/DBL/**` (17 artifacts) and `ExampleDesktopApp/DBL/**` (35).
- **Bridge reports:** none — this is a framework tool, not live stack state.
- **Source:** `Tools/dbl-check/check.mjs:142` (`HEX_REF`), `:180-190` (the `design-only` and `covers-changed-since` branches), `:117-143` (`firstCoveredFile`, `gitChangedSince`), `CLAUDE.md` §7.2 (the `source_state` contract) and §7.3 (the freshness procedure this automates).
- **StatusUpdate.md:** latest entry 2026-09-25 00:30 (the Phase 24 consumer re-sync), whose `Next:` names this plan.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Count the `source_state` forms in the wild | `grep -h '^source_state:' DBL/*/*.md \| sort \| uniq -c` | both consumers, 52 artifacts | The contract says "commit hash"; what people write is the question |
| 2 | Test `HEX_REF` against each real form | run the regex over the collected strings | the four distinct shapes | Establishes whether the check runs at all |
| 3 | Measure the blast radius | for every artifact whose ref `HEX_REF` rejects, extract the ref by hand and run the same `git diff` the tool would | ExampleFinanceApp | Turns "the check may be blind" into a number |
| 4 | Establish what regeneration cannot do | read three real artifact bodies | consumers | A tool that claims to regenerate a body it cannot author would be worse than none |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| What `source_state` forms exist? | bare hex refs | four shapes: bare (`ec8d033`), **prefixed and annotated** (`git c5e6a34 (Phase 06 M5 feature commit — reports)`), `design-only — …`, and the unfilled template placeholder | no |
| Does `covers-changed-since` run on all of them? | yes | **no** — `HEX_REF` anchors `^…$`, so only the bare form is ever checked. All **17** ExampleFinanceApp artifacts use another form; not one has ever had its freshness checked | no |
| How many are stale right now? | unknown | **4 of 17**, while `dbl-check --strict` reports `0 error, 0 warn, 0 info`: `DependencyMaps/layers.md` (**35** covered files changed since `c5e6a34`), `Summaries/_layout.md` (**74**, since `ddd0e74`), `APIIndex/routes.md` (6), `Summaries/_state.md` (1) | no |
| Can a tool regenerate an artifact's body? | no | no — a Summary's "gotchas" and a DependencyMap's reasons are judgment. What a tool *can* do: name the changed files, list the current inventory under `covers`, point at the stack lens that answers the surface question, and stamp the frontmatter afterwards | yes |

**On the three `no` rows.** None invalidates a §2 or §3 premise — they *are* the premise, sharpened from "regeneration is expensive" to "the signal that should start a regeneration never fires". The plan was authored after these measurements, not before.

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `dbl-check` `HEX_REF` | match | `^[0-9a-f]{7,40}$` — the whole string, or nothing |
| `covers-changed-since` on an annotated ref | behaviour | silently skipped; no finding, no note |
| ExampleFinanceApp `dbl-check --strict` | verdict | `0 error, 0 warn, 0 info` while 4 artifacts are stale |
| `Tools/` | regeneration tooling | none — `CLAUDE.md`'s own banner still lists it unbuilt |
| a stale artifact | next step | the author re-derives the file list and the diff by hand |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `dbl-check` | ref extraction | takes the first hex token in `source_state`, whatever surrounds it; a string with no ref and no recognised form is **reported**, never silently skipped |
| ExampleFinanceApp `dbl-check` | verdict | the 4 stale artifacts are named |
| `Tools/dbl-regen/regen.mjs` | exists | lists stale artifacts; `--artifact` prints a worksheet (changed files, current inventory, the lens to run, a paste-ready frontmatter block, the artifact's own `stale_when` as a checklist) |
| `--stamp` | contract | rewrites only `last_regenerated` and `source_state`, one line each, byte-preserving; **refuses** when the artifact's body is unchanged in the working tree |
| `CLAUDE.md` | §7.2, §15 | the `source_state` form is stated with its consequence; the tool gets its own section |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** Fix the ref extraction. **File:** `Tools/dbl-check/check.mjs`. **Operation:** modify. **Acceptance:** `sourceRef()` returns the first `[0-9a-f]{7,40}` token in `source_state` (ignoring a `git ` prefix and any parenthetical), `null` for `design-only` and for the unfilled placeholder; `covers-changed-since` runs whenever a ref is found. A `source_state` that is neither a recognised form nor contains a ref gets a new `unrecognised-source-state` warn rather than silence.
- [x] **Step 2.** Regression-test the fix against the real corpus. **File:** `Tools/dbl-check/test.mjs`. **Operation:** modify. **Acceptance:** cases for all four real forms, and one asserting an annotated ref is *checked* rather than skipped.
- [x] **Step 3.** Write `Tools/dbl-regen/regen.mjs`: default lists every artifact with a reason to regenerate (stale ref, STALE flip, design-only with source present); `--artifact <path>` prints the worksheet; `--all` prints every worksheet; `--json`. **Operation:** add. **Acceptance:** run against a consumer it names the same artifacts the fixed `dbl-check` does, and the worksheet contains the changed-file list and the current inventory.
- [x] **Step 4.** Implement `--stamp <artifact>`: replace the `last_regenerated` and `source_state` lines in place, preserving every other byte (the Phase 19 lesson — no whole-block re-serialise). **Operation:** add. **Acceptance:** refuses with a named error when the artifact is unmodified in the working tree; refuses outside a git work tree; on success the file differs by exactly two lines.
- [x] **Step 5.** Tests for the new tool. **File:** `Tools/dbl-regen/test.mjs`. **Operation:** add. **Acceptance:** worksheet content, the stamp's precondition and its byte-preservation, and a case over this repo's (template-only) `DBL/` proving the tool is quiet when there is nothing to do.
- [x] **Step 6.** Documentation. **Files:** `Tools/dbl-regen/README.md`, `Tools/dbl-check/README.md` (the ref forms), `CLAUDE.md` §7.2 (state the form and its consequence), §7.3 (point at the tool), §5 tree, new §15. **Operation:** add/modify. **Acceptance:** `doc-claims` exit 0.
- [x] **Step 7.** Field run: the fixed `dbl-check` and `dbl-regen` against both consumers. **Operation:** verify. **Acceptance:** the findings are explainable; the worksheets name real changed files.
- [x] **Step 8.** Tell the consumers. **Files:** each consumer's `AgentReports/StatusUpdate.md`. **Operation:** modify. **Acceptance:** each ledger carries an entry naming its stale artifacts and the command that produces the worksheet. **Their `DBL/**` is not edited here** — regenerating an artifact's body is that project's session's work, and this phase will not pretend otherwise.

### 3.2 Forbidden in this phase

- **No edits to any consumer's `DBL/**`.** The whole argument of this plan is that a body is judgment; writing one from here would be the tool's first lie.
- No Bridge binding changes and no new Bridge tool — `dbl-regen` is a framework tool over artifacts, not a stack lens. It *points at* the lenses; it does not wrap them.
- No auto-stamping in the default path: `--stamp` is one artifact at a time, explicitly named, with its precondition.
- No `--fix` for anything else, no hook or CI wiring (still a separate decision, now four tools).
- No change to `_TEMPLATE.md`'s DBL shapes or to the `covers`/`stale_when` semantics.
- No retro-annotation of the consumers' closed plans (the standing `plan-lint` findings stay as they are — that was settled last phase and is the user's call).

### 3.3 Deviations recorded during execution

| # | Deviation | Why it was taken |
|:---|:---|:---|
| 1 | **A `dbl-check` test had to be rewritten, again, for depending on another repo's state.** Phase 21 already de-coupled it from the consumer's artifact *count*; the fix in Step 1 made it go red for the consumer's *cleanliness*, which is equally not this repository's business. | It now asserts only that a live consumer satisfies the **contract** — zero `error` findings. A `warn` is that project's regeneration backlog. An `error` would mean `dbl-check` or the contract is wrong, which is the only thing this suite can legitimately assert about someone else's repo. |
| 2 | **`unrecognised-source-state` was added**, which §3.1 Step 1 described only in passing. | Fixing the extractor without it would have left the same hole one shape further out: a `source_state` with no ref at all would still have been skipped in silence. The whole finding is that silence is not an acceptable outcome, so the tool now either checks or says why it did not. |
| 3 | **Both consumer `CLAUDE.md` copies were re-synced inside this phase**, where Phase 24 forbade it. | Declared in §5 up front rather than discovered: Step 8 already writes to those repos, and §5/§7/§15 all moved, so leaving the copies stale would have been the drift this session built a detector for. |

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Both tools read from disk per invocation and hold no cache; `node --test` spawns each CLI fresh into `mkdtemp` directories, several of them real `git init` repos so the git paths are exercised rather than mocked.
- The load-bearing evidence is a **live consumer**: the fixed `dbl-check` must name the four artifacts this plan's §1.3 measured by hand, and the count must match. A fix verified only against fixtures would be the third instance of the fixture problem this project has recorded.
- `--stamp`'s byte-preservation is asserted by diffing the file before and after, not by re-parsing it.

**Result, 2026-09-25 01:15:** six suites green — `dbl-check` **27/27** (baseline 24), `dbl-regen` **11/11** (new), `plan-lint` 14/14, `nissth-init` 32/32, `public-cut` 12/12, dispatcher 32/32. `doc-claims` exit 0; `plan-lint` over this repo 24 plans, 0 errors. **The count agreed:** the fixed `dbl-check` named exactly the four artifacts §1.3 measured by hand, with the same file counts (74, 35, 6, 1) — no more and no fewer. ExampleDesktopApp's clean verdict was cross-checked by hand rather than trusted (9 commits since `ec8d033`, none touching `src/**` or `Tests/**`). Worksheets printed for two real stale artifacts. Both consumers re-synced and `--check`-verified; neither consumer's `DBL/**` was modified.

### 4.2 Checks

- [x] **Build:** N/A — plain JS, no build step.
- [x] **Tests:** `node --test Tools/dbl-check/test.mjs` (above its 24 baseline) and `node --test Tools/dbl-regen/test.mjs` green; `plan-lint` 14/14, `nissth-init` 32/32, `public-cut` 12/12, dispatcher 32/32 unchanged; `doc-claims` exit 0; `plan-lint` over this repo still 0 errors.
- [x] **Runtime/integration:** fixed `dbl-check` over both consumers — expected: ExampleFinanceApp names exactly the 4 artifacts §1.3 found, ExampleDesktopApp's verdict explained either way. `dbl-regen --artifact` worksheets for two of them.
- [x] **Bridge re-query:** N/A.
- [x] **DBL freshness:** this repo's `DBL/` is templates only; `dbl-check` here must still report "templates only".

### 4.3 Pass criteria

ALL of the following must be true:
- Every §4.2 check passes.
- The fixed `dbl-check` finds the four stale artifacts, and its count agrees with the §1.3 hand measurement. Disagreement in either direction is a `Verified: FAIL`, including finding *more* than expected without an explanation.
- `--stamp` refuses on an unmodified artifact, and on success changes exactly two lines.
- Both consumer ledgers carry an entry naming their stale artifacts; neither consumer's `DBL/**` is modified.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry with `Verified: FAIL`, citing the check and the artifact.
3. Do not retry silently, and **do not narrow the ref extractor until the numbers agree** — a freshness check that was loosened to look clean is the exact defect this phase exists to remove.

---

## 5. Cleanup

- [x] Remove temp scripts/artifacts created during execution
- [x] Roll snapshots — N/A, HR#9 not triggered: additive changes, version-controlled in three repos
- [x] **Reports check (CLAUDE.md §10):** a check that reported clean while blind, across 17 artifacts of a live project, is an **incident**. Author `AgentReports/Reports/2026-09-25_dbl-freshness-blind-spot.md` (kind: `incident`) — the timeline, the one-anchor root cause, the blast radius, and the two mechanisms that now catch it. Link it from the closing status entry.
- [x] **Document Sync sweep (Hard Rule #11):** modified — `Tools/dbl-check/check.mjs`, `Tools/dbl-regen/**` (new), `CLAUDE.md` §5/§7.2/§7.3/§15, both tool READMEs. Consumer `CLAUDE.md` copies drift the moment §5 and §7 change: re-sync both at the end of this phase (unlike Phase 24, this is **in scope** — Step 8 already writes to those repos) and verify with `nissth-init --check`.
- [x] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

```
### 2026-09-25 HH:MM — Phase 25: DBL freshness that can fail, and a regeneration worksheet

**State:**
- Phase: 25 closed
- Build: CLEAN · Tests: PASS
- Active plan: none
- DBL refs: none in this repo · Bridge reports: none
- Blockers: none

**Report:**
- [condensed from §1 findings]

**Executed:**
- [condensed from §3, with checkboxes resolved]

**Verified:**
- [condensed from §4 results, including freshness statement]
- Doc sync: [updated: ...]
- Reports: AgentReports/Reports/2026-09-25_dbl-freshness-blind-spot.md (incident)

**Issues:**
- [or "none"]

**Next:**
- [the next phase or task]
```
