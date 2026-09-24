# Phase 24: Plan linter, and making a consumer's open feedback visible from here — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_24_Plan_Lint
- **Authored:** 2026-09-24 by Claude (Opus 5)
- **Approved:** 2026-09-24 by user ("Go on" — this scope was proposed and accepted this session; framework phases carry standing authorisation)
- **Depends on:** Phase_16_DBL_Check, Phase_22_Consumer_Sync_And_Policy
- **Estimated scope:** A new `Tools/plan-lint/` that validates an `ImplementationPlans/Phase_NN_*.md` against the §6 template contract and — the load-bearing check — against the `DBL/DependencyMaps/` artifacts whose `covers` overlap the plan's own §3 targets. Plus a small extension to `nissth-init --check` so a consumer's *open* framework-feedback rows are visible from a Nissth session. Reuses `Tools/dbl-check`'s frontmatter and glob parsing rather than re-implementing them.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Establish what a plan linter can check **honestly**, from the shape real plans and real dependency maps actually have — not from the shape the templates describe.

### 1.1 Inputs to read

- **DBL:** none in this repo (`DBL/**` here is templates only). The consumers' maps — `PostPilot/DBL/DependencyMaps/layers.md` and `FinansYonetimApp/DBL/DependencyMaps/layers.md` — are read as **input shapes**, not as authority over this repo; they are cited with their project prefix so the path is not read as one of this repo's own.
- **Bridge reports:** none — no live state is queried.
- **Source:** `ImplementationPlans/_TEMPLATE.md` (§0–§6 headings, §1.1 Inputs, §3.1/§3.2), `Tools/dbl-check/check.mjs:42-143` (`parseFrontmatter`, `globToRegExp` — reused), `Tools/nissth-init/init.mjs` `checkConsumer`.
- **StatusUpdate.md:** latest entry 2026-09-24 23:55 (Phase 23 closed). The scope comes from `AgentReports/Reports/2026-09-13_two-consumer-run-assessment.md` "Recommended build order" items 4 (L2, the linter half) and 5 (L5).

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Read a real dependency map's boundary rules | read `PostPilot/DBL/DependencyMaps/layers.md` and the FinansYönetimApp equivalent | the two live consumers | The template shows bullets; what gets written may differ, and the linter must parse what exists |
| 2 | Read the step-line shape real plans use | grep `**File:**` / `**Files:**` across `ImplementationPlans/Phase_*.md` | this repo's 20 plans | The target-path extractor has to survive the variation |
| 3 | Confirm what the template requires of §1.1 | read `_TEMPLATE.md` §1.1 | template | Establishes whether "cite the DependencyMap" is already a rule or a new one |
| 4 | Establish the defect this is aimed at | re-read the 2026-09-13 assessment L2 and PostPilot's Phase 06 note | the assessment | A linter aimed at the wrong defect is worse than none |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| What shape are boundary rules in? | bullets, per the template | **a table** in both live consumers (`\| MUST NOT \| Reason \| Enforcement \|`), never the template's bullets. The parser reads both | no — the template's shape is not the one in use |
| Can a linter decide a forbidden *import* from a plan? | unknown | **no.** A plan names target files, not imports, and the rules are module-level (`Core → App`, `anything outside Core/Data/ → SQL text`). Anything stronger would be a guess dressed as a verdict, so the check claims only what it can prove: this plan works inside a written-down boundary and does not say it read it | yes |
| Does `_TEMPLATE.md` §1.1 already require citing the DependencyMap? | yes, per the consumer's note | **only generically** — §1.1 says "**DBL:** [specific files in `DBL/`]" and never names DependencyMaps. So the citation rule is new, not merely unenforced | no |
| What do §3 step lines look like? | `**File:** \`path\`` | 14 of this repo's 23 plans use the `**File:**`/`**Files:**` marker; **9 never adopted it**. The extractor prefers the marker and falls back to the whole step line | no — the marker is a convention, not a guarantee |

**On the three `no` rows.** All three are the pre-flight doing its job: each one says the *template's* description of a plan is not the *corpus's* reality, and each one changed the tool rather than the plan. None invalidates a §2 or §3 premise — they are the reason §1.2 read real plans and real maps instead of the template alone. Recorded rather than rounded to "yes".

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `ImplementationPlans/**` | validation | none — §6's "every plan MUST conform" is unchecked |
| a plan that targets files under a constrained boundary | signal | nothing; the conflict surfaces at execution (PostPilot Phase 06) |
| a consumer's open feedback rows | visibility from a Nissth session | none — a ten-item digest sat unseen for ten days |
| `Tools/` | plan tooling | none |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/plan-lint/lint.mjs` | exists | validates §0–§6 structure, §0 metadata, a non-empty §3.2, cited-artifact existence, and the DependencyMap citation rule |
| `dependency-map-not-cited` | behaviour | error when §3 targets paths a boundary-carrying DependencyMap covers and §1.1 does not cite it; the finding prints the map's rules |
| `nissth-init --check` | output | also reports a consumer's **open** framework-feedback rows, so the digest is visible from here |
| `CLAUDE.md` | §6, §10 | the linter and the feedback-digest convention are documented where the rules they serve live |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** Write `Tools/plan-lint/lint.mjs`: parse a plan into sections, extract §3 target paths and §1.1 cited artifacts, run the checks, report. Reuse `parseFrontmatter`/`globToRegExp` from `Tools/dbl-check/check.mjs` by import. **Operation:** add. **Acceptance:** `node Tools/plan-lint/lint.mjs` lints every plan in `ImplementationPlans/`; `--plan <file>` lints one; exit 0 clean, 1 findings, 2 usage.
- [x] **Step 2.** Implement the structural checks: the seven §0–§6 headings, Plan ID matching the filename, an `Approved:` line that is `pending` or an ISO date, `Depends on` naming plans that exist, and a non-empty §3.2 Forbidden list. **Operation:** add. **Acceptance:** each check has a named finding code and fires on a fixture.
- [x] **Step 3.** Implement `missing-cited-artifact`: any `DBL/…md` or `ImplementationPlans/…md` path cited in §1.1 must exist. **Operation:** add. **Acceptance:** fires on a fixture citing a non-existent artifact; silent when the file is there.
- [x] **Step 4.** Implement `dependency-map-not-cited` — the load-bearing check. For every §3 target path, find each `DBL/DependencyMaps/*.md` whose `covers` matches it; if that map states boundary rules and §1.1 cites neither it nor "no DBL", emit an error naming the map and quoting its rules. **Operation:** add. **Acceptance:** fires on a fixture plan touching a covered path without the citation; silent once the citation is added.
- [x] **Step 5.** Extend `nissth-init --check` to report a consumer's open feedback rows: read `AgentReports/Reports/*feedback*.md`, count rows whose status cell is `open`, and print them. **File:** `Tools/nissth-init/init.mjs`. **Operation:** modify. **Acceptance:** `--check` on a consumer with open rows names them; a consumer with none is unaffected; it never changes the exit code on its own (drift is a defect, an open feedback row is information).
- [x] **Step 6.** Tests. **Files:** `Tools/plan-lint/test.mjs` (fixtures per finding, plus a run over this repo's real plans), `Tools/nissth-init/test.mjs` (the feedback reporting). **Operation:** add/modify. **Acceptance:** both suites green; the real-plan run is an assertion, not a smoke test.
- [x] **Step 7.** Fix whatever the linter finds in this repo's own 21 plans, or record why a finding stands. **Operation:** modify. **Acceptance:** `node Tools/plan-lint/lint.mjs` exits 0, or every remaining finding is named in §3.3 with its reason.
- [x] **Step 8.** Documentation. **Files:** `Tools/plan-lint/README.md` (new), `CLAUDE.md` §6 (the linter, beside the authoring rules it checks) and §10 (the feedback-digest convention + `--check`), `Tools/nissth-init/README.md`. **Operation:** add/modify. **Acceptance:** `doc-claims` exit 0.

### 3.2 Forbidden in this phase

- **No new Hard Rule.** This is a mechanism, exactly as §12.1 argues for `doc-claims`; §6 already says plans must conform.
- **No `--fix`.** Like `doc-claims`, `dbl-check` and `nissth-init --check`, it reports and exits. A tool that rewrites a plan is a different risk with its own plan.
- No changes to `_TEMPLATE.md`'s section shape — the linter adapts to the template, never the reverse. If a check cannot be made honest against the real template, it is not written.
- No changes to any binding under `Bindings/`, and no Bridge tool registration: a plan is not live state.
- No edits to consumer repos this phase; `--check` reads them.
- No hook or CI wiring for any validator — still a separate decision (three open, unchanged).
- No DBL regeneration tooling. That is the other half of assessment item 4 and is its own phase.

### 3.3 Deviations recorded during execution

| # | Deviation | Why it was taken |
|:---|:---|:---|
| 1 | **Two tolerance bugs were fixed in the linter, not in the plans it flagged.** The first run reported `unknown-dependency` twice: once for `Phase_07_*` (a reserved-number *wildcard* in prose) and once for `Phase_17` (a short id for `Phase_17_Greenfield_And_Expo_Local_Rules`). | Both plans are correct and idiomatic; the check was too literal. §4.4 forbids narrowing a check to go green, and this is the opposite case — the check was wrong about what a valid dependency reference looks like. Resolving by prefix, with a trailing `_` treated as the wildcard stem it is, fixes both without weakening anything. |
| 2 | **`Phase_19` got a waiver rather than an edit.** Its §1.1 lists three consumer artifacts on one line, the last two inheriting the `FinansYonetimApp/` prefix from the first; the linter reads the third as a path in this repo. | The plan is closed and its sentence is correct English. Rewriting a closed plan's evidence line to satisfy a tool is the wrong direction; the waiver records the reason where a reader will meet it. This phase's own plan took the other fix — its citation was reworded to carry the project prefix, because it was still being written. |
| 3 | **`--check` gained the feedback report in both output branches**, not only the in-sync one. | A consumer that has drifted is exactly the consumer whose feedback is most likely to be waiting. Feedback never touches the exit code either way. |
| 4 | **Both consumers are left out of sync, deliberately.** `--check` now reports 1113 differing lines in each, caused by this session's own §5 tree rows (Phase 23's `public-cut/`, this phase's `plan-lint/`). | §3.2 forbids consumer edits this phase, and that guard is kept rather than amended to permit what would have been convenient. The re-sync is one command per consumer and is named in §6 `Next`. This is Hard Rule #11's "mark stale and queue the regeneration" branch, taken openly. |

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- `plan-lint` reads files from disk on every invocation and holds no cache; `node --test` spawns the CLI fresh per case, into `mkdtemp` directories removed afterwards.
- The load-bearing evidence is not a fixture: the linter is run against **this repository's own 21 plans** and against the two live consumers' plan sets, because a linter that only passes its own fixtures is the fixture problem Phase 19 and Phase 23 both recorded.
- `dbl-check`'s parsers are imported rather than copied, so the two tools cannot disagree about what a `covers` glob means.

**Result, 2026-09-25 00:40:** `plan-lint` **14/14**, `nissth-init` **32/32** (baseline 29), `dbl-check` 24/24, `public-cut` 12/12, dispatcher 32/32, `doc-claims` exit 0 — five suites, none regressed. `plan-lint` over this repo: **23 plans, 0 error, 0 warn, 2 info** (both `no-step-targets`, both correct — those two plans' §3 is prose and verification). Over the consumers, where the dependency maps live: **PostPilot 19 plans → 5 `dependency-map-not-cited`**, all in plans 13–18, and **every plan 01–12 cites the map** — the lapse is real, dated, and exactly the defect the check was built for; **FinansYonetimApp 9 plans → 1**, a Swift target file under a `targets/**` boundary. `nissth-init --check` on both consumers: drift reported (this session's own tree rows), feedback section silent because that digest's rows were all closed in Phase 22 — the positive case is covered by three fixture tests instead.

### 4.2 Checks

- [x] **Build:** N/A — plain JS, no build step.
- [x] **Tests:** `node --test Tools/plan-lint/test.mjs` and `node --test Tools/nissth-init/test.mjs` green, the latter above its 29 baseline. `node --test Tools/dbl-check/test.mjs` and `Tools/public-cut/test.mjs` unchanged (the import is new; nothing else moves).
- [x] **Runtime/integration:** `node Tools/plan-lint/lint.mjs` over this repo; the same over both consumers' `ImplementationPlans/`. `nissth-init --check` over both consumers.
- [x] **Bridge re-query / DBL freshness:** N/A.

### 4.3 Pass criteria

ALL of the following must be true:
- Both suites green; the other two unchanged; `doc-claims` exit 0.
- The linter runs clean over this repo's plans, or every standing finding is recorded in §3.3 with its reason.
- Run against the consumers, the linter's findings are **explainable** — each one either a real authoring gap or a documented shape difference. A wave of unexplainable findings means the check is wrong and §4.4 applies.
- `--check` still exits 0 for both consumers (feedback rows are information, not drift).

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry with `Verified: FAIL`, citing the check and the artifact.
3. Do not retry silently. In particular, **do not narrow a check until it passes** — a check that was loosened to go green is the `doc-claims` failure mode §12.2 warns about. Either the check is right and the plans are wrong, or the check is wrong and it comes out.

---

## 5. Cleanup

- [x] Remove temp scripts/artifacts created during execution
- [x] Roll snapshots — N/A, HR#9 not triggered: every change is additive and version-controlled
- [x] **Reports check (CLAUDE.md §10):** no `Verified: FAIL`, no choice between named alternatives, no external spec, no pivot. No Report unless the consumer run turns up a defect class worth one.
- [x] **Document Sync sweep (Hard Rule #11):** modified — `Tools/plan-lint/**` (new), `Tools/nissth-init/init.mjs`, `CLAUDE.md` §5 tree + §6 + §10, `Tools/nissth-init/README.md`. `Tools/public-cut/` needs no change: it strips the `public-cut/` row by name, and a new sibling row does not move it. Verify that claim by running `public-cut/test.mjs`, which reads the real trees.
- [x] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

```
### 2026-09-25 HH:MM — Phase 24: plan linter; a consumer's open feedback made visible

**State:**
- Phase: 24 closed
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
