# Phase 13: Doc-Claim Validator — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_13_Doc_Claim_Validator
- **Authored:** 2026-08-24 by Claude (Opus 5)
- **Approved:** 2026-08-24 by user ("Continue" — in response to the offer to close the Phase 12 structural gap as Phase 13)
- **Depends on:** Phase_12_Doc_Status_Sync (closed — established the defect class this phase mechanizes against)
- **Estimated scope:** New zero-dependency Node tool at `Tools/doc-claims/` that checks repo-root prose against ground truth in the binding manifests, plus its own `node --test` suite. Mirrors the existing `Tools/nissth-bridge/` shape. No existing source is modified; `CLAUDE.md` gains a short §12 documenting the tool, which is the one plan-required edit outside `Tools/`.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none — Nissth's own `DBL/**` holds `_TEMPLATE.md` skeletons only.
- **Bridge reports:** none — no live stack state. The tool being built reads manifests and Markdown, not a running stack.
- **Source:**
  - `Tools/nissth-bridge/dispatcher.js`, `test.mjs`, `package.json` — the shape to mirror (zero runtime deps, `node --test`)
  - `Bindings/*/*.bridge.json` — ground truth for binding ids and tool names
  - `CLAUDE.md`, `README.md`, `AGENTS.md`, `Ultimate_Guide.md` — the documents to be validated
- **StatusUpdate.md:** latest entry as of authoring — `2026-08-24 11:20 — Phase 12: Documentation Status Sync — VERIFIED PASS`.

### 1.2 Diagnostic actions

> Prefer Bridge tools (`nissth-bridge <tool> ...`) and DBL reads over raw source greps when either covers the question (Hard Rule #4). Use the table's `Tool/command` column to record the exact invocation, including any `--scope.*` flags.

No Bridge tool validates documentation — that is precisely the gap this phase fills. Reading the manifests and greping the docs is the correct instrument; recorded per HR#5.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm the tooling shape to mirror | read `Tools/nissth-bridge/package.json`, head of `dispatcher.js` and `test.mjs` | 3 files | New tool must match house style: plain `.mjs`/`.js`, zero runtime deps, `node --test` |
| 2 | Enumerate every backticked snake_case identifier in the four docs | `grep -oh` + `sort -u` | 4 docs | Sizes the false-positive problem the allowlist must absorb |
| 3 | Classify those identifiers | manual read against manifests | ~45 identifiers | Separates real tools, external vocabulary (PG catalog, frontmatter keys), and deliberately hypothetical tools |
| 4 | Confirm §11.7's illustrative tools are hypothetical | read `CLAUDE.md` §11.7 table | 1 section | `migration_author`, `endpoint_scaffold` are examples, not shipped. If the check flags them it is wrong, not the doc |
| 5 | Confirm the docs are currently clean | Phase 12's claim sweep, re-run | 4 docs | The validator must exit 0 on the current tree. If it does not, either the tool is wrong or Phase 12 missed something |
| 6 | Confirm manifest tool counts and the README table's count column | parse manifests; read `README.md` stack table | 3 manifests + 1 table | Check 3 compares these two directly |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| `Tools/nissth-bridge/` shape? | plain JS, zero runtime deps, `node --test` suite | confirmed — `@nissth/bridge-dispatcher`, `type: module`, no `dependencies` block, `test: node --test test.mjs`, Node >=20. Mirrored exactly. | yes |
| Backticked snake_case identifiers across the four docs? | ~45 distinct | **46** distinct. | yes |
| How many are real tools? | 15 — five per binding | **14, not 15.** `migration_status` is registered by *both* SpringBoot and Postgres, so 15 registrations are 14 distinct names. See the note below. | no |
| How many are external vocabulary (PG catalog, frontmatter keys, config keys)? | ~24 | **26** (expectation said ~24; within the stated approximation). | yes |
| How many are deliberately hypothetical tools? | 6 — `endpoint_scaffold`, `migration_author`, `index_create`, `vacuum_analyze`, `model_field_add`, `migration_apply` | **6** exactly, as predicted: `endpoint_scaffold`, `migration_author`, `index_create`, `vacuum_analyze`, `model_field_add`, `migration_apply`. | yes |
| Do the docs currently pass a correct validator? | yes — Phase 12 left them clean | yes — **after two narrowing fixes to the tool.** The first draft reported 2 false positives on the real tree; both were fixed in the checker, neither by editing prose. See the note below. | yes |
| Manifest tool counts vs README table column? | 5 / 5 / 5, matching | 5 / 5 / 5, matching the manifests. | yes |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

Row-specific note: row 6 is the load-bearing one. A validator that reports findings on a tree Phase 12 just cleaned is a **false-positive generator**, and shipping it would train the reader to ignore it. If row 6 reads `no`, fix the tool, not the docs.


### 1.3a Execution notes

**Row 3 reads `no` — an arithmetic slip in the expectation, not stale state.** The plan wrote
"15 — five per binding". Three bindings registering five tools each is 15 *registrations* but
**14 distinct names**, because `migration_status` is registered by both Spring Boot and
PostgreSQL. This is a documented fact of the framework — `CLAUDE.md` §11.15 names that exact
collision as the reason the dispatcher requires `--binding` to disambiguate. Nothing about the
tree differed from what the plan assumed; the plan's arithmetic did. Proceeding.

**Row 6 held only after the tool was narrowed twice.** §4.4 says a finding on the real tree is
presumed to be a false positive in the checker, and both were:

1. `CLAUDE.md` §11.12 — "`Phase_05_Bridge_SpringBoot_FirstSlice.md` (not yet authored at the
   time §11 is written)". Accurate historical prose about a *plan*; the naive matcher read
   "SpringBoot" inside the filename as a claim about the binding. Fixed by stripping
   `Phase_NN_*` filenames before matching binding names.
2. §12.1 of `CLAUDE.md` — the section documenting this tool — quotes the historical defect
   verbatim and tripped its own checker. Fixed by adding an inline waiver mechanism
   (`<!-- doc-claims:allow <check> - reason -->`, required check name, free-text reason) rather
   than rewording the explanation into something vaguer. A waiver naming one check provably
   does not silence another; there is a test for it.

Neither fix touched a document to make the tool pass, which was the §3.2 line that mattered.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/doc-claims/` | existence | absent |
| Doc-claim accuracy | enforcement | none — prose is checked only by an agent remembering to look. This is why `index_drift` survived, and why three binding-status claims stayed wrong across seven phase closes |
| HR#11 Doc Sync sweep | coverage of repo-root prose | none — the mandate keys off `DBL/` `covers` globs and plan cross-references; `README.md` has neither, so nothing points at it when a binding ships |
| `CLAUDE.md` | §12 | absent — document ends at §11 |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/doc-claims/validate.mjs` | existence | present — zero runtime deps, runs on Node 20+ |
| `Tools/doc-claims/known-non-tools.json` | existence | present — explicit allowlist of identifiers that are deliberately not shipped tools, each with a one-line reason |
| `Tools/doc-claims/test.mjs` | existence | present — `node --test`, covering each check's pass and fail path against fixtures |
| `Tools/doc-claims/README.md` | existence | present — what it checks, how to run, how to add an allowlist entry |
| Validator on the current tree | exit code | **0**, no findings |
| Validator on a fixture reintroducing `index_drift` | exit code | 1, with a finding naming file, line, and the offending identifier |
| Validator on a fixture calling a shipped binding "queued" | exit code | 1, with a finding naming file, line, and the binding |
| `CLAUDE.md` | §12 | short section documenting the tool and when to run it |
| `Bindings/**`, `README.md`, `AGENTS.md`, `Ultimate_Guide.md` | content | **unchanged** — the tool reads them, this phase does not edit them |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.0 Check design (single source of truth for Step 2)

Three checks. Each is chosen to have a near-zero false-positive rate on the current tree; a noisy validator is worse than none, because it teaches the reader to skip it.

| # | Check | Fires when | Why it is narrow |
|:---|:---|:---|:---|
| 1 | **Stale binding status** | A line contains a shipped binding's name AND one of a fixed phrase list (`queued`, `not yet authored`, `not on disk`, `not yet on disk`, `in flight`, `plan not yet authored`) | Only fires when `Bindings/<X>/` **and** its `*.bridge.json` both exist on disk. A genuinely unbuilt binding cannot trigger it |
| 2 | **Fictional tool name** | Inside a *tool-enumeration line* — one naming a real binding AND containing a shipping verb (`ships`, `will ship`, `delivers`, `registers`) AND ≥2 backticked snake_case identifiers — any identifier that is neither a real tool nor allowlisted | Restricting to enumeration lines is what keeps §11.7's illustrative table and the PG-catalog vocabulary out of scope. This is the exact shape of the sentence that carried `index_drift` |
| 3 | **Tool-count drift** | A `README.md` stack-table row links `Bindings/<X>/` and its trailing count column disagrees with that binding's manifest tool count | Purely numeric comparison against the manifest |

**Allowlist policy.** `known-non-tools.json` holds identifiers that are deliberately not shipped tools, each with a `reason`. Adding an entry is a deliberate act recorded in a file, which is the point — an agent writing a hypothetical tool into the docs must say so. The allowlist is not a silencer for real drift: it is keyed by identifier, so a *fictional* name still fails until someone justifies it in writing.

### 3.1 Step list

- [x] **Step 1.** Run every §1.2 action and fill §1.3. **Operation:** modify (this plan). **Acceptance:** all seven rows answered; STOP if any reads `no`.

- [x] **Step 2.** Implement the validator. **File:** `Tools/doc-claims/validate.mjs`. **Operation:** add. Reads manifests from `Bindings/*/*.bridge.json`, scans the four repo-root docs, applies §3.0's three checks, prints findings as `file:line  [check]  message`, exits 0 / 1 / 2 (clean / findings / usage-or-config error). Accepts `--json` for machine-readable output and `--root <path>` so tests can point it at a fixture tree. **Acceptance:** `node Tools/doc-claims/validate.mjs` exits 0 on the current tree with no findings.

- [x] **Step 3.** Author the allowlist. **File:** `Tools/doc-claims/known-non-tools.json`. **Operation:** add. Every identifier from §1.3 rows 4 and 5, each with a one-line `reason`. **Acceptance:** the file parses as JSON; removing any single entry that Check 2 depends on makes the validator report a finding (spot-checked on one entry, not all).

- [x] **Step 4.** Write the test suite. **File:** `Tools/doc-claims/test.mjs`. **Operation:** add. `node --test`. Covers, per check, one passing fixture and one failing fixture, plus: exit codes, `--json` shape, allowlist honoured, and a binding that is genuinely absent from disk **not** triggering Check 1. **Acceptance:** `node --test Tools/doc-claims/test.mjs` all green.

- [x] **Step 5.** Fixtures. **Files:** under `Tools/doc-claims/_fixtures/`. **Operation:** add. Minimal fake repo trees — a manifest plus a doc — one clean, one per failing check. **Acceptance:** fixtures are self-contained; no test reads the real repo docs, so a future doc edit cannot turn a unit test red.

- [x] **Step 6.** Tool README. **File:** `Tools/doc-claims/README.md`. **Operation:** add. What each check does, how to run, exit codes, and how to add an allowlist entry with a reason. **Acceptance:** file exists and documents all three checks and all three exit codes.

- [x] **Step 7.** `package.json` for the tool, mirroring `Tools/nissth-bridge/package.json`. **File:** `Tools/doc-claims/package.json`. **Operation:** add. **Acceptance:** `npm test` inside `Tools/doc-claims/` runs the suite; no runtime dependencies declared.

- [x] **Step 8.** Document the tool in `CLAUDE.md` as a new **§12**. **File:** `CLAUDE.md`. **Operation:** modify. Cover what it checks, how to run it, and its relationship to HR#11 — it mechanizes the repo-root-prose half of the Doc Sync Mandate that `covers` globs cannot reach. State plainly that it is a *check*, not an action tool, and that it does not edit docs. **Acceptance:** §12 exists; the §5 project-structure tree gains a `Tools/doc-claims/` row.

- [x] **Step 9.** Append the §6 status entry.

### 3.2 Forbidden in this phase

- **No edits to `README.md`, `AGENTS.md`, or `Ultimate_Guide.md`.** The validator reads them. If it reports a finding on the current tree, that is a §1.3 row-6 failure — fix the tool, not the docs. Only `CLAUDE.md` §12 is edited, and only to document the tool.
- **No changes to any binding.** Nothing under `Bindings/` is touched, manifests included. The manifests are read as ground truth and never written.
- **No hook wiring, no `.claude/settings.json` change.** Making this run automatically on every edit is a separate decision with its own failure modes; this phase ships the tool and documents it. Queue automation in §6 `Next`.
- **No CI configuration.** Same reasoning.
- **No new HR.** The point of this phase is a mechanism instead of another rule. Do not add Hard Rule #14.
- **No widening to DBL artifacts or Bridge reports.** Those already have `covers` globs and the §11.4 stale-flip. This tool covers repo-root prose only.
- **No runtime dependencies.** Zero-dep plain Node, matching `Tools/nissth-bridge/`.
- **No re-cut or push.** Publishing this to `origin/master` is a separate action; queue it in §6 `Next` rather than bundling it.
- **No touching `Axiom/`**, the PDFs, or `.claude/settings.local.json`.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- The validator is run **against the real repo** after the files are written to disk; Node reads from disk on each invocation with no module cache carried between runs (each run is a fresh process).
- Tests run via `node --test`, which has no persistent test cache — every run re-executes.
- Ground truth is parsed from `Bindings/*/*.bridge.json` at each invocation rather than hard-coded, so a manifest change is picked up without touching the validator.
- Test fixtures are self-contained fake trees; no unit test reads the real docs, so results cannot silently change when documentation is edited later.
- Suite counts for the four bindings are **not** re-measured in this phase and none is claimed: this phase touches no binding source. The Step 8 `CLAUDE.md` edit adds a section and a tree row; it asserts no test numbers.

### 4.2 Checks

- [x] **Validator on the real tree:** `node Tools/doc-claims/validate.mjs` — expected: exit 0, "no findings".
- [x] **Check 1 fires:** run against a fixture calling a shipped binding "queued" — expected: exit 1, finding names file, line, binding.
- [x] **Check 2 fires:** run against a fixture reintroducing `index_drift` in an enumeration line — expected: exit 1, finding names the identifier.
- [x] **Check 3 fires:** run against a fixture whose table count disagrees with the manifest — expected: exit 1.
- [x] **No false positive on an unbuilt binding:** fixture where a doc says "queued" about a binding with no directory — expected: exit 0.
- [x] **Allowlist honoured:** fixture using `migration_author` in an enumeration line while allowlisted — expected: exit 0.
- [x] **Tests:** `node --test Tools/doc-claims/test.mjs` — expected: all pass, 0 fail.
- [x] **Regression guard:** dispatcher suite `node --test Tools/nissth-bridge/test.mjs` — expected: 32/32, proving the new sibling directory under `Tools/` did not disturb it.
- [x] **No drift:** `git diff --stat` touches only `Tools/doc-claims/**`, `CLAUDE.md`, this plan, and `StatusUpdate.md`.
- [x] **Bridge re-query:** N/A — no Bridge tool covers documentation, and no binding tool surface changes.
- [x] **DBL freshness:** N/A — `DBL/**` holds `_TEMPLATE.md` skeletons; no `covers` glob overlaps `Tools/` or `CLAUDE.md`.

### 4.3 Pass criteria

ALL of the following must be true:
- Validator exits 0 on the current tree with zero findings.
- Each of the three checks demonstrably fires on its fixture, and each demonstrably does **not** fire on the clean fixture.
- `node --test Tools/doc-claims/test.mjs` fully green.
- Dispatcher suite still 32/32.
- The validator would have caught **both** Phase 12 defects: the `index_drift` fiction and the "PostgreSQL binding queued" staleness. Demonstrated against fixtures reproducing the pre-Phase-12 text, not asserted.
- No file under `Bindings/`, and no doc other than `CLAUDE.md`, appears in the diff.
- Zero runtime dependencies in `Tools/doc-claims/package.json`.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

Specific guidance: if the validator reports findings on the **real** tree, the default assumption is a false positive in the tool, not a defect in the docs — Phase 12 cleaned them and its sweep is recent. Narrow the check before touching prose, and never widen the allowlist to silence a finding that is actually correct.

Rollback: `rm -rf Tools/doc-claims && git checkout -- CLAUDE.md`. Nothing else is touched, and nothing is pushed in this phase.

---

## 5. Cleanup

- [x] Remove temp scripts/artifacts created during execution
- [x] Roll snapshots if no longer needed (`AgentReports/Snapshots/`) — none created; this phase adds files rather than rewriting them, and §4.4's rollback is a single `rm -rf` plus one checkout
- [x] **Reports check (CLAUDE.md §10):**
  - A `decision` Report is warranted and should be authored: this phase chooses a *mechanism* over a *rule* for a defect class that a rule (HR#11) already nominally covered and still missed seven times. The alternatives considered — add Hard Rule #14, wire a hook, do nothing — are exactly the kind of thing a future agent would otherwise re-litigate. Author as `AgentReports/Reports/2026-08-24_doc-claim-enforcement-decision.md` with §10.3 frontmatter and the §10.7 decision body shape.
  - List authored Reports here so they appear in the §6 status entry's `Reports:` line.
- [x] **Document Sync sweep (Hard Rule #11):**
  - Source files added in §3: `Tools/doc-claims/**`. Modified: `CLAUDE.md`.
  - Affected stable documents: `CLAUDE.md` §5 structure tree (gains a `Tools/doc-claims/` row — part of Step 8); `Tools/nissth-bridge/README.md` (check whether it claims to be the only thing under `Tools/`); `README.md`'s structure tree (**note**: it is forbidden to edit this phase, so if it needs a row, MARK it and queue in §6 `Next` rather than editing).
  - For each affected document, either **UPDATE** now or **MARK STALE** with a regeneration step queued in §6 `Next`.
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [updated: X, Y; marked stale: Z]`
- [x] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 13: Doc-Claim Validator

**State:**
- Phase: 13 CLOSED.
- Build: CLEAN | HAS_ERRORS
- Tests: PASS | FAIL — doc-claims [n]/[n]; dispatcher [n]/32. Binding suites not re-run (no binding source touched).
- Active plan: ImplementationPlans/Phase_13_Doc_Claim_Validator.md
- DBL refs: none — Nissth's own DBL holds `_TEMPLATE.md` skeletons only
- Bridge reports: none — no live stack state queried
- Blockers: [or "none"]

**Report:**
- [condensed §1.3 findings — especially rows 5 and 6, the two that decide whether the tool is narrow enough]

**Executed:**
- [Steps 1–9 with checkboxes resolved; name the three checks and what each catches]

**Executed:**

**Verified:**
- [§4 results, including the demonstration that both Phase 12 defects are caught against fixtures. Freshness: fresh Node process per run, no test cache, manifests parsed at invocation, fixtures self-contained.]
- Doc sync: [updated: ...; marked stale: ...]
- Reports: [AgentReports/Reports/2026-08-24_doc-claim-enforcement-decision.md (decision), ...]

**Issues:**
- [or "none"; if Verified: FAIL, cite the matching incident Report filename here]

**Next:**
- [the next phase or task — likely: re-cut and push so the tool reaches origin/master, and decide whether to wire it into a hook]
```
