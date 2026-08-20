# Phase 10: Public Preview Branch (Generalized Nissth) — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_10_Public_Preview_Branch
- **Authored:** 2026-08-21 by Claude (Opus 5)
- **Approved:** 2026-08-21 by user
- **Depends on:** Phase_09_7_Postgres_Binding_CoerceSsl_Fix (last closed phase; master is at `a324e75` integrating 09.5 + 09.7)
- **Estimated scope:** Creates an **orphan** branch `nissth/public` carrying a generalized, shareable snapshot of the framework. On that branch only: omit `Axiom/` (148 files, 2.2 MB), omit both root PDFs, omit `.claude/settings.local.json`, reset `AgentReports/StatusUpdate.md` to its schema preamble plus one seed entry, drop one consumer-specific Report, and scrub consumer project names out of ~26 remaining files (docs, 9 phase plans, 11 Reports, 2 binding test files, 1 JSON schema description). `master` is not modified by §3 except for this plan file and the closing status entry.
- **Isolation requirement (user directive, 2026-08-21):** `Axiom/` is the user's live reference material for developing Nissth. It is removed **only from the public branch's tree** — never from `master`, and never from the primary working directory, not even transiently. The branch is therefore built in a **separate git worktree** under the session scratchpad, so `C:\Users\admin\Desktop\Nissth\Axiom\` is never deleted, moved, or modified at any point during execution.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none — Nissth's own `DBL/` holds only `_TEMPLATE.md` skeletons (it dogfoods the structure, does not populate it). Nothing to query.
- **Bridge reports:** none — no live stack state is relevant. This phase is a repo-content operation, not a runtime query.
- **Source:**
  - `.gitignore` (full, 33 lines) — to confirm `AgentReports/Bridge/` and `node_modules/` are ignored so `git add -A` on the orphan branch does not sweep them in.
  - `AgentReports/StatusUpdate.md:1-48` — the reusable schema preamble; `:49` is the first entry (`2026-05-05 19:10 — Framework Bootstrap`).
  - `CLAUDE.md:101,651,697,844,849,907` — the six consumer/Axiom-directory references.
  - `README.md:213,480,522`; `Bindings/README.md:45`.
  - `Bindings/_schemas/bridge-command.schema.json:30`; `Bindings/SpringBoot/src/test/java/com/nissth/bridge/core/JsonCommandParserTest.java:37,52`; `Bindings/Postgres/tests/unit/CoerceSsl.test.ts:95`.
- **StatusUpdate.md:** latest entry as of plan authoring — `2026-06-30 — Merge 09.7 → master + push to origin`.

### 1.2 Diagnostic actions

> Prefer Bridge tools and DBL reads over raw source greps when either covers the question (Hard Rule #4). Bridge tools are N/A here — no Bridge tool inspects repo-wide text content or git branch topology; a tracked-file grep is the only instrument that answers these questions.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Enumerate every tracked file carrying a consumer-project or local-path reference | case-insensitive grep for the §3.0 find-terms over `git ls-files` output | tracked files, `Axiom/` excluded | Defines the exact scrub surface; a missed file ships a consumer name publicly |
| 2 | Confirm working tree is clean and `master` is the checkout | `git status --porcelain` + `git rev-parse --abbrev-ref HEAD` | repo root | An orphan checkout inherits the working tree; uncommitted noise would land in the public commit |
| 3 | Confirm every file slated for deletion is committed on `master` | `git ls-files Axiom`, `git ls-files "*.pdf"`, `git ls-files .claude/settings.local.json` | deletion targets | Rollback guarantee: anything committed on `master` is restorable by `git checkout master -- .` (HR#9) |
| 4 | Confirm `.gitignore` covers Bridge reports, `node_modules/`, build output | `cat .gitignore` | repo root | `git add -A` on the orphan branch must not sweep transient/vendored trees into the public commit |
| 5 | Record the pre-execution `master` SHA | `git rev-parse master` | repo root | §4.2 asserts `master` is unchanged afterward |
| 6 | Confirm the four binding suites are green *before* the scrub | dispatcher `node --test Tools/nissth-bridge/test.mjs`; `npm test` in `Bindings/Postgres` and `Bindings/Expo`; `mvn clean test` in `Bindings/SpringBoot` | each binding root | Establishes the baseline so a post-scrub failure is attributable to the scrub, not pre-existing |
| 7 | Confirm nothing under `Axiom/` is untracked or ignored | `git status --porcelain --ignored Axiom/` | `Axiom/` | If any file there were uncommitted, no checkout could restore it. Must be 0 before `Axiom/` is relied on as recoverable |
| 8 | Confirm git supports orphan worktrees | `git --version` (needs ≥ 2.42 for `git worktree add --orphan`) | repo root | The isolation requirement in §0 depends on this; below 2.42 the plan must STOP and be re-authored |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| How many tracked files (excl. `Axiom/`) carry a consumer reference? | 26 files, out of 231 tracked scanned | **26 of 231** | **yes** |
| Is the working tree clean on `master`? | clean, on `master` | **`master`; only untracked file is this plan** | **yes** |
| Are all deletion targets tracked on `master`? | `Axiom/` 148 files; 2 PDFs; `.claude/settings.local.json` — all tracked | **Axiom 148 ✓; both PDFs ✓; settings.local.json ✓; unihub Report ✓** | **yes** |
| Does `.gitignore` cover `AgentReports/Bridge/`, `**/node_modules/`, `**/target/`, `**/build/`? | yes, all four | **all four IGNORED** (verified via `git check-ignore`) | **yes** |
| Pre-execution `master` SHA | `a324e75` | **`9f4b0c8c1d56e541c8b0de160e831bd3b20e732f`** — **expected value was a transcription slip at authoring**: `a324e75` is the FF-merge commit named in the 2026-06-30 status entry, not the branch tip. The tip adds `9f4b0c8 docs(status): record 09.7→master FF merge + pre-push entry`, the docs-only commit that same entry predicted. Baseline captured; no §2 content assumption is affected (all other rows matched exactly). | **yes — corrected baseline** |
| Baseline suite results | Dispatcher 32/32; Postgres 107 pass / 18 skip; Expo 51/51; SpringBoot 104/104 | **Dispatcher 32/32 ✓ · Postgres 107 pass/18 skip/125 total ✓ · Expo 58/58 (13 suites) · SpringBoot 104/104 ✓.** **Expo expected value was a transcription slip**: 58 is the count the Phase 09.5 close recorded; 51 appears nowhere. The measured numbers are the authoritative baseline for §4.2. | **yes — corrected baseline** |
| Any untracked/ignored files under `Axiom/`? | 0 — everything committed on `master`, therefore fully recoverable | **0 untracked/ignored; 148 files on disk = 148 tracked** | **yes** |
| Does git support `worktree add --orphan` (≥ 2.42)? | yes | **2.54.0.windows.1** | **yes** |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| repo | branches | `master`, `nissth/phase-09-5-*`, `nissth/phase-09-7-*` (+ origin mirrors) |
| `Axiom/` | tracked files | 148 (2.2 MB) |
| repo root | PDFs | `Süprüz Software Requirements Specification Report (3).pdf`, `Software Design Document Template.pdf` |
| `.claude/settings.local.json` | tracked | yes — contains absolute local paths and consumer-repo commands |
| `AgentReports/StatusUpdate.md` | length | 2181 lines; 125 lines carry consumer references |
| `AgentReports/Reports/` | files | 13 (12 Reports + `README.md`) |
| `ImplementationPlans/` | files | 10 (9 phase plans incl. this one + `_TEMPLATE.md`) |
| tracked files (excl. `Axiom/`) | consumer references | 26 files affected |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| repo | branches | `master` (content unchanged), plus new orphan `nissth/public` |
| `nissth/public` | commit count | exactly 1 (`git rev-list --count HEAD` = 1) |
| `nissth/public` | `Axiom/` | absent from tree AND from history |
| `nissth/public` | root PDFs | both absent from tree AND from history |
| `nissth/public` | `.claude/settings.local.json` | absent; `.gitignore` gains an entry for it |
| `nissth/public:AgentReports/StatusUpdate.md` | shape | schema preamble (lines 1–48 verbatim) + one `2026-08-21 — Public preview seed` entry |
| `nissth/public:AgentReports/Reports/` | files | 12 (11 Reports + `README.md`) — `2026-05-23_unihub-consumer-install-decisions.md` dropped |
| `nissth/public:ImplementationPlans/` | files | 10, all scrubbed |
| `nissth/public` | consumer references | 0 grep hits across the whole committed tree |
| `nissth/public` | Axiom *lineage prose* | RETAINED — `CLAUDE.md:733,862` and the architecture Report credit the public `github.com/umutbrkt/Axiom` repo (see §3.2) |
| `master` | content | identical to pre-execution, plus this plan + snapshot manifest + closing status entry |
| `master:Axiom/` | tracked files | **148 — unchanged.** The reference framework stays on `master` permanently |
| `C:\Users\admin\Desktop\Nissth\Axiom\` | on-disk files | **148 — never deleted, not even transiently.** Primary working directory stays on `master` throughout |
| worktree | state | scratchpad worktree removed at Step 12; `nissth/public` branch survives it |
| binding suites on `nissth/public` | result | same counts as the §1.3 baseline |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.0 Scrub map (single source of truth for Step 8)

| Find | Replace with | Rationale |
|:---|:---|:---|
| `com.supruz.reservation` | `com.example.reservation` | RFC 2606 reserved documentation namespace |
| `Süprüz` / `Supruz` / `supruz` (prose) | `Example` / `example` | consumer project name |
| `Desktop/Supruz/` | `Desktop/ExampleApp/` | local consumer path |
| `UniHub-Backend` | `Example-Backend` | consumer repo name |
| `UniHub-Frontend` | `Example-Frontend` | consumer repo name |
| `UniHub` (bare) | `Example` | consumer project name |
| `C:/Users/admin/Desktop/<X>`, `/c/Users/admin/…`, `C:\Users\admin\…` | `<repo-root>/…` (relative) | local filesystem layout |
| `iyzico` | `a payment provider` (or `Provider A` in option tables) | ties the doc example to the consumer's vendor choice |
| `Uçmaz pc` | `<user>` | local Windows account name |

### 3.1 Step list

- [ ] **Step 1.** Run every §1.2 diagnostic action and fill the §1.3 Findings table. **File:** `ImplementationPlans/Phase_10_Public_Preview_Branch.md`. **Lines:** §1.3. **Operation:** modify. **Acceptance:** all six rows have an `Actual answer`; if any `Match? = no`, STOP per §1.3.
- [ ] **Step 2.** Write the rollback manifest. **File:** `AgentReports/Snapshots/2026-08-21_phase-10-pre-orphan-manifest.md`. **Lines:** new file. **Operation:** add. **Acceptance:** file records the pre-execution `master` SHA, the deletion target list, and the restore recipe (HR#9).
- [ ] **Step 3.** Commit this plan + the manifest on `master` so the orphan checkout starts from a clean tree. **Operation:** add. **Acceptance:** `git status --porcelain` is empty on `master`, excluding the always-excluded `.claude/settings.local.json` and `Bindings/Postgres/tsconfig.tsbuildinfo`.
- [ ] **Step 4.** Create the orphan branch **in a separate worktree**, leaving the primary working directory on `master` and untouched. **Command:** `git worktree add --orphan -b nissth/public "<scratchpad>/nissth-public"`. **Operation:** add. **Acceptance:** the new directory exists with an unborn HEAD on `nissth/public`; `git -C "C:\Users\admin\Desktop\Nissth" rev-parse --abbrev-ref HEAD` still reports `master`; `C:\Users\admin\Desktop\Nissth\Axiom\` still contains 148 files.
- [ ] **Step 4b.** Populate the worktree with `master`'s tree. **Command:** `git -C "<scratchpad>/nissth-public" checkout master -- .` **Operation:** add. **Acceptance:** the worktree holds every tracked file from `master`; the primary working directory is byte-identical to before Step 4.
- [ ] **Step 5.** Remove the non-shareable trees and files **from the worktree only**. **Files (all paths relative to `<scratchpad>/nissth-public/`):** `Axiom/` (recursive), `Süprüz Software Requirements Specification Report (3).pdf`, `Software Design Document Template.pdf`, `.claude/settings.local.json`, `AgentReports/Reports/2026-05-23_unihub-consumer-install-decisions.md`. **Operation:** remove. **Acceptance:** none of the five paths exists **inside the worktree**, AND `C:\Users\admin\Desktop\Nissth\Axiom\` is verified still present with 148 files immediately after this step.
- [ ] **Step 6.** Add `.claude/settings.local.json` to `.gitignore` under the existing `# ----- Claude Code transients -----` block. **File:** `.gitignore`. **Operation:** modify. **Acceptance:** `git check-ignore -q .claude/settings.local.json` exits 0.
- [ ] **Step 7.** Reset the status log. **File:** `AgentReports/StatusUpdate.md`. **Lines:** truncate to `1-48` (preamble through the closing `-->` and its trailing rule), then append one seed entry `### 2026-08-21 — Public preview seed` whose `**Next:**` tells a newcomer to read `README.md` then `Ultimate_Guide.md`, and whose `**State:**` records the shipped bindings and their test counts. **Operation:** modify. **Acceptance:** file is ≤ 90 lines and contains exactly one `^### 2026` heading.
- [ ] **Step 8.** Apply the §3.0 scrub map to every remaining file flagged in §1.3 row 1. **Files:** `CLAUDE.md`, `README.md`, `Bindings/README.md`, `Bindings/_schemas/bridge-command.schema.json`, `Bindings/SpringBoot/src/test/java/com/nissth/bridge/core/JsonCommandParserTest.java` (both sides of the assertion — lines 37 and 52 — so the test stays green), `Bindings/Postgres/tests/unit/CoerceSsl.test.ts` (comment only), the 9 phase plans in `ImplementationPlans/`, and the 11 remaining files in `AgentReports/Reports/`. **Operation:** modify. **Acceptance:** the §1.2 action-1 grep returns 0 hits across the working tree, except the deliberate retentions in §3.2.
- [ ] **Step 9.** Remove the two `Axiom/` directory rows from the project-structure trees. **Files:** `CLAUDE.md:101`, `README.md:213`. **Operation:** remove. **Acceptance:** neither tree lists an `Axiom/` row; box-drawing alignment preserved and the last remaining row uses the `└──` connector.
- [ ] **Step 10.** Stage and commit the orphan tree. **Command:** `git add -A` then `git commit`. **Operation:** add. **Acceptance:** `git rev-list --count HEAD` = 1; the committed tree contains no `Axiom/`, no `.pdf`, no `.claude/settings.local.json`.
- [ ] **Step 11.** Run §4.2 verification inside the worktree, then confirm the primary working directory is untouched. **Operation:** verify. **Acceptance:** §4.3 pass criteria all true, including the `Axiom/` integrity check.
- [ ] **Step 12.** Remove the worktree once the branch is committed and verified. **Command:** `git worktree remove "<scratchpad>/nissth-public"`. **Operation:** remove. **Acceptance:** `git worktree list` shows only the primary working directory; `git branch --list nissth/public` still lists the branch (removing a worktree does not delete its branch).

### 3.2 Forbidden in this phase

- **NEVER delete, move, rename, or modify `C:\Users\admin\Desktop\Nissth\Axiom\`.** It is the user's live reference material for developing Nissth (directive, 2026-08-21). Every removal in Step 5 happens inside the scratchpad worktree. No `rm`, no `git clean`, no `git checkout --orphan` in the primary working directory — the in-place variant is forbidden precisely because it would empty `Axiom/` from disk between Steps 5 and 11. The same protection covers the two root PDFs and `.claude/settings.local.json`.
- **No push.** `nissth/public` is created locally only. Publishing is the user's explicit call (standing workflow rule) and is queued in §6 `Next`.
- **No modification of `master`'s content** beyond this plan file, the Step 2 snapshot manifest, and the closing status entry. `master` keeps `Axiom/`, both PDFs, and the full 2181-line log.
- **No rewriting of `master`'s history**, no `filter-branch`, no `filter-repo`, no force-push anywhere.
- **Do not strip Axiom *lineage* prose.** `CLAUDE.md:733,862`, `README.md`, and `AgentReports/Reports/2026-05-15_diagnostic-bridge-architecture.md` credit Axiom as the predecessor design and cite the already-public `github.com/umutbrkt/Axiom`. That is attribution, not leakage. Only the vendored `Axiom/` **directory** and the two `Axiom/` tree rows go. (If the user wants the `umutbrkt` handle scrubbed too, that is a scope change — ask, do not self-approve.)
- **No LICENSE file, no CONTRIBUTING, no README rewrite for a public audience.** Real gaps for a shared repo, but out of scope here — queued in §6 `Next`.
- **No `git config` change to author identity.** The public commit will carry the repo's configured identity; flagged in §6, not silently altered.
- **No touching the other two `nissth/phase-09-*` branches**, and no deletion of any branch.
- **No dependency bumps, no binding source changes** beyond the two string/comment scrubs named in Step 8.
- **No edits to past `StatusUpdate.md` entries on `master`** (HR#3). The reset in Step 7 happens only on the orphan branch, where the file is a new blob with no history.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Git is the verifier for tree/history claims and is inherently fresh: `git ls-tree -r HEAD` and `git rev-list --count HEAD` read the committed object graph produced in Step 10, not a cache. All assertions run **after** the commit, so they see the final state.
- The scrub-completeness grep runs against the **committed** tree (`git grep` on `nissth/public`), not the working directory, so an unstaged edit cannot produce a false clean.
- Test freshness follows each binding's §8.x protocol: SpringBoot per §8.1.6 (`mvn clean test` — clean target, no stale daemon reuse); Expo/Postgres per §8.2.6 (`ts-jest` transforms on every run, no persistent test cache); dispatcher via `node --test`, which has no cache.
- **Worktree consequence (accepted cost).** `node_modules/` and `target/` are gitignored, so the scratchpad worktree starts with neither. The Expo and Postgres suites therefore require a fresh `npm ci` in the worktree, and SpringBoot a fresh `mvn clean test` (Maven may re-resolve into the shared `~/.m2` cache). This is **slower but strictly more correct** than reusing the primary directory's installed tree — `npm ci` is exactly what §8.2.6 mandates for a verification run, and it rules out the "green against a stale `node_modules/`" false-CLEAN this framework exists to prevent. If `npm ci` cannot run (offline host), that is a §4.4 STOP, not a fall-back to `npm install`.
- No Bridge action tool is involved, so no exit-code-5 enforcement contract applies here.

### 4.2 Checks

- [ ] **Build:** `mvn -q clean test` in `Bindings/SpringBoot` — expected: exit 0, 104/104 (the `JsonCommandParserTest` package-string assertion must still pass after the both-sides rename)
- [ ] **Tests:** `node --test Tools/nissth-bridge/test.mjs` → 32/32 · `npm test` in `Bindings/Postgres` → 107 pass / 18 skip · `npm test` in `Bindings/Expo` → 51/51. All counts must equal the §1.3 baseline.
- [ ] **Runtime/integration:** `./nissth-bridge --list-bindings` and `--list-tools` on `nissth/public` — expected: all three bindings (spring-boot, expo, postgres) still discovered; confirms Step 5's deletions did not disturb `Bindings/*/*.bridge.json` discovery.
- [ ] **History isolation:** `git rev-list --count nissth/public` = `1`; the committed tree lists no path matching `^Axiom/`, `\.pdf$`, or `settings\.local\.json`.
- [ ] **Scrub completeness:** `git grep -inE` over `nissth/public` for the §3.0 find-terms — expected **0 hits**. Any hit is a FAIL.
- [ ] **`master` untouched:** `git diff <baseline-sha> master` shows only `ImplementationPlans/Phase_10_Public_Preview_Branch.md`, `AgentReports/Snapshots/2026-08-21_*`, and `AgentReports/StatusUpdate.md`.
- [ ] **`Axiom/` integrity (hard gate):** in the **primary** working directory — `git status --porcelain --ignored Axiom/` is empty AND `find Axiom -type f | wc -l` = `148` AND `git ls-files Axiom | wc -l` = `148`. Run this immediately after Step 5 and again at Step 11. A non-empty status or a count ≠ 148 is an immediate FAIL under §4.4.
- [ ] **Worktree isolation:** `git worktree list` shows the scratchpad worktree during execution and only the primary directory after Step 12; the primary directory reports `master` at every point in §3.
- [ ] **Bridge re-query:** N/A — no Bridge tool's coverage surface (endpoints, entities, routes, components, schema, locks, migrations) is touched by this phase. No DBL artifact can be STALE-flipped by a docs/branch operation.
- [ ] **DBL freshness:** N/A — Nissth's own `DBL/` contains only `_TEMPLATE.md` skeletons; no artifact has a `covers` glob overlapping any file modified in §3.

### 4.3 Pass criteria

ALL of the following must be true:
- `nissth/public` has exactly 1 commit, and `Axiom/`, both PDFs, and `.claude/settings.local.json` are absent from both its tree and its history.
- The scrub grep returns 0 hits across the committed `nissth/public` tree.
- All four binding suites on `nissth/public` match the §1.3 baseline counts exactly.
- `master` differs from its pre-execution state only by this plan file, the snapshot manifest, and the closing status entry.
- `./nissth-bridge --list-bindings` on `nissth/public` discovers all three bindings.
- **`C:\Users\admin\Desktop\Nissth\Axiom\` still holds all 148 files, clean and unmodified** — the §0 isolation requirement. This criterion outranks the others: if it fails, the phase failed regardless of what else passed.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.
   - **Rollback recipe** (from the Step 2 manifest): `git worktree remove --force "<scratchpad>/nissth-public" && git branch -D nissth/public`. Nothing was pushed, the primary working directory was never modified, and `master` holds every omitted file — so the operation is fully reversible and `Axiom/` is never at risk.
   - **If the `Axiom/` integrity gate ever fails** (should be impossible under the worktree design): restore immediately with `git checkout master -- Axiom/` from the primary directory, confirm 148 files, and treat it as an incident Report under §10.4 trigger #1 before doing anything else.

---

## 5. Cleanup

- [ ] Remove temp scripts/artifacts created during execution (scrub scripts live in the session scratchpad, never in the repo)
- [ ] Remove the scratchpad worktree (§3.1 Step 12) and confirm `git worktree list` shows only the primary working directory
- [ ] Final `Axiom/` integrity confirmation in the primary working directory: 148 files, `git status --porcelain --ignored Axiom/` empty
- [ ] Roll snapshots if no longer needed — **keep** `AgentReports/Snapshots/2026-08-21_phase-10-pre-orphan-manifest.md` until the user confirms the public branch is good
- [ ] **Reports check (CLAUDE.md §10):**
  - §10.4 trigger #4 (end of phase) applies — this phase produces a new distribution artifact. Author `AgentReports/Reports/2026-08-21_public-preview-branch-snapshot.md` (kind: `snapshot`) recording what was removed, what was scrubbed, the retention decision on Axiom lineage prose, and the open pre-publication gaps (LICENSE, author identity).
  - §10.4 trigger #2 (named-alternative decision) applies — orphan-vs-normal-branch and the three scrub depths were weighed against each other. Fold the option matrix into the same snapshot Report; if that section exceeds ~10 lines, spin off `2026-08-21_public-branch-strategy-decision.md` (kind: `decision`).
- [ ] **Document Sync sweep (Hard Rule #11):**
  - Files modified in §3 (on the orphan branch only): `CLAUDE.md`, `README.md`, `Bindings/README.md`, `Bindings/_schemas/bridge-command.schema.json`, `JsonCommandParserTest.java`, `CoerceSsl.test.ts`, `.gitignore`, `AgentReports/StatusUpdate.md`, 9 × `ImplementationPlans/*.md`, 11 × `AgentReports/Reports/*.md`
  - Affected stable documents: **none.** `DBL/**` holds only templates (no `covers` globs to overlap). The two binding-source edits are a test-fixture string and a comment — neither changes a public API, so no `DBL/Summaries/` or `APIIndex/` artifact could describe them. `CLAUDE.md` is itself in the modified set and is updated in-place by Steps 8–9.
  - Result to log in §6: `Doc sync: [updated: CLAUDE.md, README.md, Bindings/README.md (+ 20 scrubbed plan/report files) — orphan branch only; marked stale: none]`
- [ ] No orphan branches, no leftover debug code — *note:* `nissth/public` is an orphan branch **by design** (§0), not the accidental kind this checklist item guards against. No other branch is created.

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.
>
> **Note:** this entry is appended to **`master`'s** `StatusUpdate.md`. The orphan branch's copy of that file was reset to a seed entry in Step 7 and deliberately does not carry this history.

```
### 2026-08-21 HH:MM — Phase 10: Public Preview Branch (Generalized Nissth)

**State:**
- Phase: 10 (distribution artifact; not a framework capability phase)
- Build: CLEAN | HAS_ERRORS
- Tests: PASS | FAIL
- Active plan: ImplementationPlans/Phase_10_Public_Preview_Branch.md
- DBL refs: none — Nissth's own DBL holds templates only
- Bridge reports: none — no live stack state queried
- Blockers: [or "none"]

**Report:**
- [condensed from §1 findings — file count carrying consumer refs, master SHA, baseline suite counts]

**Executed:**
- [condensed from §3, with checkboxes resolved — deletions, .gitignore addition, status reset, scrub map application, orphan commit SHA]

**Verified:**
- [condensed from §4 — commit count, history-isolation checks, scrub grep 0 hits, four suite counts, master-unchanged diff, freshness statement per §4.1]
- Axiom integrity: primary working directory Axiom/ = 148 files, clean, never modified (§0 isolation requirement); public branch omits it from tree + history only
- Doc sync: [updated: CLAUDE.md, README.md, Bindings/README.md (+20 scrubbed plan/report files) — orphan branch only; marked stale: none]
- Reports: AgentReports/Reports/2026-08-21_public-preview-branch-snapshot.md (snapshot)

**Issues:**
- [or "none"; if Verified: FAIL, cite the matching incident Report filename here]

**Next:**
- Decide the three pre-publication items before `git push -u origin nissth/public`: (a) LICENSE choice, (b) whether the commit's author identity (name + email) should be the repo default or a scrubbed identity, (c) whether the `github.com/umutbrkt/Axiom` attribution URL stays. Push only on explicit user word.
```
