# Phase 15: Consumer Init Tooling (`Tools/nissth-init`) — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_15_Consumer_Init_Tooling
- **Authored:** 2026-09-13 by Claude (Opus 5)
- **Approved:** `pending`
- **Depends on:** none (Phase_14 closed; audit Report `AgentReports/Reports/2026-09-13_finans-consumer-init-friction-audit.md` F1–F5 is the motivation)
- **Estimated scope:** New `Tools/nissth-init/` (one `init.mjs` ≈ 250 lines, `templates/` ×7, `test.mjs` ≈ 15 cases, `package.json`, `README.md`); rewrite of the two consumer launcher templates under `Tools/nissth-bridge/consumer-launcher/` to add the env-var fallback chain; consumer-launcher `README.md` recipe collapsed to "run init"; `CLAUDE.md` §9.1 step 2 and §5 tree + `README.md` tree row updated to name the tool. Zero runtime dependencies, Node 20+, same shape as `doc-claims`.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none — Nissth's own DBL is templates only.
- **Bridge reports:** none.
- **Source:** `Tools/nissth-bridge/consumer-launcher/nissth-bridge` (whole file, 36 lines), `nissth-bridge.ps1` (34 lines), `README.md` (lines 1–60 recipe); `Tools/doc-claims/{package.json,test.mjs:1-30}` for conventions; `Tools/nissth-bridge/dispatcher.js` — only the `findFrameworkRoot` function (grep `NISSTH_FRAMEWORK_ROOT`) to keep the launcher chain identical to the dispatcher's; `AgentReports/StatusUpdate.md:1-40` (preamble to templatise); `CLAUDE.md:1-6` (banner shape), `CLAUDE.md` §9.1 step 2 (lines ~626–640).
- **StatusUpdate.md:** latest entry `2026-09-13 01:45 — Consumer init closed; friction audit + Phase 15–17 plans authored`.
- **Reference consumer:** `C:\Users\admin\Desktop\FinansYönetimApp` at `620572c` — the hand-made output this tool must reproduce.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Launcher templates still lack the fallback | `Grep 'NISSTH_FRAMEWORK_ROOT' Tools/nissth-bridge/consumer-launcher/` | 2 files | Must be only in comments → F1 confirmed |
| 2 | Dispatcher's resolution order | `Grep -n 'NISSTH_FRAMEWORK_ROOT\|Tools/Nissth' Tools/nissth-bridge/dispatcher.js` | 1 file | Launcher chain must mirror it (env → submodule → …) |
| 3 | Existing tools green | `npm --prefix Tools/nissth-bridge test`; `npm --prefix Tools/doc-claims test` | Tools/ | Baseline before adding a third tool |
| 4 | Working copy LF | `git ls-files --eol \| grep -c w/crlf` → 2 (the `mvnw.cmd`s) | repo | Templates copied by init must be LF at the source too |
| 5 | Tree clean | `git status --short` → empty | repo | HR#9 baseline is HEAD |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| env var referenced only in comments? | yes | _to be filled_ | _to be filled_ |
| dispatcher order | env → `Tools/Nissth` → repoRoot | _to be filled_ | _to be filled_ |
| bridge / doc-claims tests | 32/32, 23/23 | _to be filled_ | _to be filled_ |
| CRLF count | 2 | _to be filled_ | _to be filled_ |
| `git status` | empty | _to be filled_ | _to be filled_ |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/` | children | `doc-claims/`, `nissth-bridge/` |
| `consumer-launcher/nissth-bridge:26` | dispatcher path | hard-coded `$DIR/Tools/Nissth/…` |
| `consumer-launcher/nissth-bridge.ps1:22` | dispatcher path | hard-coded `Tools\Nissth\…` |
| `CLAUDE.md` §9.1 step 2 | text | "Copy framework files into the project root: …" (manual list) |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/nissth-init/init.mjs` | CLI | `node Tools/nissth-init/init.mjs --target <dir> --name <ProjectName> --stack <expo\|spring-boot\|postgres\|none> [--wiring local\|submodule] [--framework-root <abs>] [--dry-run] [--json]` |
| `init.mjs` | refusal | exit 2 if `<dir>/AgentReports/StatusUpdate.md` already exists (`error_code: already_initialized`); exit 2 if `--stack` unknown; exit 2 if `--framework-root` lacks `Bindings/` |
| `init.mjs` | output | creates: `CLAUDE.md` (framework body from this checkout + banner from template with `{{NAME}}`, `{{DATE}}`, `{{STACK}}`, `{{FRAMEWORK_ROOT}}`), `AGENTS.md` (name substituted), `ImplementationPlans/_TEMPLATE.md`, `DBL/*/_TEMPLATE.md`, `AgentReports/StatusUpdate.md` (preamble + a filled "Bootstrap" entry listing the created files), `AgentReports/{Reports,Bridge,Snapshots}/.gitkeep`, `Tests/.gitkeep`, `Tools/.gitkeep`, `.claude/settings.json`, `.gitignore` (stack-specific), `.gitattributes`, `nissth-bridge`, `nissth-bridge.ps1` |
| `init.mjs` | invariants | every written text file is LF (input CRLF stripped); never overwrites an existing file (exit 2 `file_exists` listing them, nothing written — checks all targets first); `--dry-run` prints the file list and writes nothing; prints the HR#13 reminder line on stdout |
| `init.mjs` | does NOT | run `git init`, `npm`, or any subprocess; author SRS/SDD or Phase 00 |
| `Tools/nissth-init/templates/` | files | `CLAUDE.banner.md`, `StatusUpdate.preamble.md`, `Bootstrap.entry.md`, `gitattributes`, `gitignore.expo`, `gitignore.spring-boot`, `gitignore.postgres`, `gitignore.none`, `settings.json` |
| `consumer-launcher/nissth-bridge` | resolution | `$NISSTH_FRAMEWORK_ROOT` → `$DIR/Tools/Nissth` → `{{FRAMEWORK_ROOT}}` placeholder line (init substitutes; template ships with the line commented) → exit 3 listing all tried |
| `consumer-launcher/nissth-bridge.ps1` | resolution | same chain |
| both launchers | env export | export `NISSTH_FRAMEWORK_ROOT` to the resolved root when it was not set, so the dispatcher's tier-1 resolution agrees with the launcher's choice |
| `Tools/nissth-init/test.mjs` | cases | ≥ 15 (see §4.2) |
| `CLAUDE.md` §9.1 step 2 | text | names `node Tools/nissth-init/init.mjs …` as the mechanical bootstrap; manual list retained as "what it creates" |
| `CLAUDE.md` §5 tree, `README.md` tree | rows | `Tools/nissth-init/` row added |
| `Tools/nissth-bridge/consumer-launcher/README.md` | recipe | steps 2–4 replaced by the init command; submodule and local-checkout wiring both documented |
| `node Tools/doc-claims/validate.mjs` | exit | 0 |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [ ] **Step 1.** Create `Tools/nissth-init/package.json` (`@nissth/init`, private, `type: module`, `engines.node >= 20`, scripts `test: node --test test.mjs`). **Operation:** add. **Acceptance:** `npm --prefix Tools/nissth-init test` runs (0 tests yet).
- [ ] **Step 2.** Write `templates/CLAUDE.banner.md` — the FinansYönetimApp head (title line + two blockquote paragraphs) generalised with `{{NAME}}`, `{{DATE}}`, `{{STACK_SENTENCE}}`, `{{FRAMEWORK_ROOT}}`. **Acceptance:** rendering with Finans values reproduces `FinansYönetimApp/CLAUDE.md:1-5` modulo the project-specific status sentence.
- [ ] **Step 3.** Write `templates/StatusUpdate.preamble.md` (Nissth ledger lines 1–40 with `{{NAME}}` in the title) and `templates/Bootstrap.entry.md` (schema-conformant entry with `{{DATE_TIME}}`, `{{FILE_LIST}}`, `{{WIRING}}`, `{{FRAMEWORK_ROOT}}`; `Next:` = "Author SRS/SDD if absent (§9), then `Phase_00_DBL_Bootstrap.md`"). **Acceptance:** rendered entry has every schema field incl. `Doc sync: none — no source files` and `Bridge reports: none`.
- [ ] **Step 4.** Write `templates/gitattributes` (repo `.gitattributes` minus the Maven-wrapper comment) and `templates/gitignore.{expo,spring-boot,postgres,none}` (Finans `.gitignore` for expo; `target/`, `build/`, `.gradle/` for spring-boot; node basics for postgres; Bridge dir + editor/OS + Claude transients for none — the `none` block is included in all four). **Acceptance:** each contains `AgentReports/Bridge/`.
- [ ] **Step 5.** Write `templates/settings.json` — the Finans allow-list generalised per stack (expo: npm/tsc/expo-doctor; spring-boot: `./mvnw`/`./gradlew` test+verify; postgres: none extra; all: launchers). **Acceptance:** valid JSON; contains no `bypassPermissions` or `defaultMode` key (user feedback: narrow allow-lists only).
- [ ] **Step 6.** Rewrite `Tools/nissth-bridge/consumer-launcher/nissth-bridge` with the resolution chain (env → submodule → `{{FRAMEWORK_ROOT}}` line → error listing all tried) and the env export. **Lines:** whole file. **Operation:** modify. **Acceptance:** with `NISSTH_FRAMEWORK_ROOT` set to this checkout and no submodule, `--list-bindings` from a temp dir prints 3 bindings; unset + no submodule + placeholder unsubstituted → exit 3.
- [ ] **Step 7.** Same for `nissth-bridge.ps1`. **Acceptance:** identical behaviour via `pwsh`/`powershell` (test skips with a note if neither is on PATH — this host has both).
- [ ] **Step 8.** Write `Tools/nissth-init/init.mjs`: arg parsing (`--target`, `--name`, `--stack`, `--wiring` default `local`, `--framework-root` default = this checkout's root resolved from `import.meta.url`, `--dry-run`, `--json`); plan phase (compute every target path + content, fail fast on collisions/unknown stack/invalid framework root); write phase (mkdir -p, LF-normalise, `chmod 755` the POSIX launcher on non-Windows); summary to stdout (`created N files`, HR#13 reminder, next steps: `git init`, SRS/SDD, Phase 00). Exports `plan()` and `apply()` for tests. **Acceptance:** `--dry-run --json` against a temp dir lists exactly the §2 After file set; a real run creates them and a second run exits 2 `already_initialized`.
- [ ] **Step 9.** Write `Tools/nissth-init/test.mjs` (node:test, temp dirs under `os.tmpdir()`, cleaned in `after`): cases enumerated in §4.2. **Acceptance:** all pass.
- [ ] **Step 10.** Write `Tools/nissth-init/README.md` (what/why/run/flags/what-it-creates/what-it-refuses; ≤ 120 lines) and collapse `consumer-launcher/README.md` steps 2–4 to the init command, keeping the resolution-order and update sections. **Acceptance:** doc-claims exit 0.
- [ ] **Step 11.** `CLAUDE.md`: §9.1 step 2 rewritten to name the tool and its refusals (keep the file list as "creates:"); §5 tree gains `Tools/nissth-init/`; §11.15 last paragraph's "consumer-side launcher template … ships the recipe" sentence updated to point at init. **Lines:** the three sites only. **Acceptance:** `Grep 'nissth-init' CLAUDE.md` → 3 hits; doc-claims exit 0.
- [ ] **Step 12.** `README.md`: tree row `Tools/nissth-init/` + one sentence in the consumer-install section. **Acceptance:** doc-claims exit 0 (tool-count rows untouched).
- [ ] **Step 13.** Field test: run init against a fresh temp dir with `--stack expo --name FieldTest`, then `./nissth-bridge.ps1 --list-tools --binding expo` from it → 5 tools. Diff the generated `CLAUDE.md` body (lines 7–end) against this checkout's `CLAUDE.md` (lines 7–end) → identical. **Acceptance:** both true; temp dir deleted afterwards.

### 3.2 Forbidden in this phase

- Changing `Tools/nissth-bridge/dispatcher.js` — the launcher chain mirrors it; if they disagree, the plan is wrong, not the dispatcher.
- Making init run `git init`, `npm install`, `npx create-expo-app`, or author SRS/SDD/Phase 00 — init is §9.1 step 2 only, and only after the HR#13 gate the *agent* owns.
- Adding a "force/overwrite" flag. Refuse-and-list is the contract (§11.7 spirit: no warn-and-proceed).
- Touching `Bindings/**`, `Ultimate_Guide.md` (deferred to Phase 17's doc sweep), or the FinansYönetimApp repo.
- Re-cutting the public branch.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- All tools are plain Node with no build step and no cache; tests spawn `process.execPath` on the files on disk. Freshness = "files saved before `npm test`", guaranteed by running the suite as the last step of §3 in the same session.
- Fresh-clone clause (§8.x.6 by analogy): `git worktree add ../nissth-p15-verify HEAD` after the commit; run `npm --prefix Tools/nissth-init test` and the Step 13 field test from the worktree; cite its path.

### 4.2 Checks

- [ ] **Build:** N/A — no compile step.
- [ ] **Tests:** `npm --prefix Tools/nissth-init test` — expected ≥ 15 pass, 0 fail, covering: (1) plan lists the full file set for `expo`; (2) `spring-boot` gitignore differs from `expo`; (3) `none` stack works; (4) unknown stack → exit 2; (5) already-initialised target → exit 2, nothing written; (6) a single pre-existing file (e.g. `CLAUDE.md`) → exit 2 `file_exists`, nothing written; (7) `--dry-run` writes nothing; (8) every written file is LF even when the template source is CRLF (test injects a CRLF fixture); (9) banner substitution `{{NAME}}`/`{{DATE}}`; (10) `CLAUDE.md` body identical to source checkout from line 7; (11) `StatusUpdate.md` has the schema comment and one entry with all fields; (12) `--framework-root` without `Bindings/` → exit 2; (13) `--wiring submodule` leaves the placeholder line commented and the Bootstrap entry says submodule; (14) POSIX launcher resolves via env var (spawn `sh` if available, else skip with note); (15) PowerShell launcher resolves via env var (skip if no pwsh/powershell); (16) `--json` output is parseable and has `created[]`.
- [ ] **Existing tools still green:** `npm --prefix Tools/nissth-bridge test` 32/32; `npm --prefix Tools/doc-claims test` 23/23.
- [ ] **Doc-claims:** `node Tools/doc-claims/validate.mjs` → exit 0.
- [ ] **Runtime/integration:** Step 13 field test from the fresh worktree.
- [ ] **Bridge re-query:** N/A.
- [ ] **DBL freshness:** N/A — Nissth DBL is templates.

### 4.3 Pass criteria

ALL of the following must be true:
- init suite ≥ 15/15 green in the development directory **and** in the fresh worktree.
- Field-test consumer's `nissth-bridge.ps1 --list-tools --binding expo` → 5 tools from the worktree-generated project.
- doc-claims exit 0; dispatcher and doc-claims suites unchanged and green.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [ ] Remove the field-test temp dir and the verification worktree (`git worktree remove`).
- [ ] No snapshots taken (baseline = HEAD).
- [ ] **Reports check (CLAUDE.md §10):** §10.4 #4 fires (new tool = more than incremental change) → author `AgentReports/Reports/2026-MM-DD_phase-15-consumer-init-tooling-snapshot.md` (kind `snapshot`): CLI surface, refusal table, template list, what changed in the launchers, what remains manual (git init, SRS/SDD, Phase 00).
- [ ] **Document Sync sweep (Hard Rule #11):** modified: `consumer-launcher/*` (3), `CLAUDE.md` (3 sites), `README.md` (2 sites). Affected docs: `Ultimate_Guide.md` §4.1 adoption sequence still describes the manual copy — **mark for Phase 17** (its doc sweep) rather than edit here (§3.2). `AgentReports/Reports/2026-05-23_unihub-consumer-install-decisions.md` candidate #1 → add a one-line `## Revision history` entry "fixed by Phase 15". Log: `Doc sync: [updated: CLAUDE.md §5/§9.1/§11.15, README.md tree + consumer section, consumer-launcher/README.md, unihub decision Report revision line; deferred: Ultimate_Guide.md §4.1 → Phase 17]`.
- [ ] Commit `feat(tools): add nissth-init consumer bootstrap; launcher env-var fallback` after the status entry.

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 15: Consumer Init Tooling

**State:**
- Phase: 15 closed
- Build: CLEAN (no compile step; suites green)
- Tests: PASS — init N/N, dispatcher 32/32, doc-claims 23/23; fresh worktree at <path>
- Active plan: none
- DBL refs: none
- Bridge reports: none
- Blockers: none

**Report:**
- Pre-flight §1.3: [rows]

**Executed:**
- Tools/nissth-init/{init.mjs,test.mjs,package.json,README.md,templates/×9}; consumer-launcher/{nissth-bridge,nissth-bridge.ps1,README.md}; CLAUDE.md §5/§9.1/§11.15; README.md ×2

**Verified:**
- [suite counts + field test + doc-claims, freshness statement with worktree path]
- Doc sync: [as §5]
- Reports: AgentReports/Reports/<date>_phase-15-consumer-init-tooling-snapshot.md (snapshot)

**Issues:**
- [or none]

**Next:**
- Execute Phase_16_DBL_Check (approved?) or Phase_17; re-run init against FinansYönetimApp in --dry-run to confirm it would have produced the hand-made tree.
```
