# Phase 16: DBL Frontmatter + Freshness Validator (`Tools/dbl-check`) — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_16_DBL_Check
- **Authored:** 2026-09-13 by Claude (Opus 5)
- **Approved:** `pending`
- **Depends on:** none (independent of Phase 15; audit Report F6/F7)
- **Estimated scope:** New `Tools/dbl-check/` (`check.mjs` ≈ 300 lines, `test.mjs` ≈ 20 cases, `_fixtures/` ×5 mini-repos, `package.json`, `README.md`); `CLAUDE.md` gains §13 (≈ 40 lines) and one sentence in §7.3; `README.md` tree row. Zero runtime deps, Node 20+, same shape as `doc-claims`. Read-only: reports and exits, never edits a DBL artifact.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** `DBL/*/_TEMPLATE.md` (4 files, frontmatter only) — the key set and `artifact_type` values.
- **Bridge reports:** none.
- **Source:** `Tools/doc-claims/validate.mjs` (whole — reuse its arg parsing, finding shape, `--json`, exit-code convention), `Tools/doc-claims/test.mjs:1-30`; `CLAUDE.md` §7.2–§7.4 (frontmatter contract, freshness check, token budget), §11.4 (STALE marker format `last_regenerated: STALE — …`); `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/StaleFlipper.java` — only the regex that writes the STALE line (so the checker recognises exactly what bindings write).
- **Reference consumer:** `C:\Users\admin\Desktop\FinansYönetimApp\DBL/**` at `620572c` — 11 real artifacts, all `design-only`; used as an external fixture in one test (skipped if the path is absent).
- **StatusUpdate.md:** latest entry `2026-09-13 01:45`.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Key set in templates | `Grep '^[a-z_]*:' DBL/*/_TEMPLATE.md` | 4 files | The six keys the checker requires |
| 2 | STALE line format across bindings | `Grep -rn 'STALE' Bindings/*/src --include=*.java --include=*.ts` | 3 bindings | Recognise every writer's exact format |
| 3 | doc-claims conventions | read `validate.mjs` | 1 file | Match finding/exit shape |
| 4 | Tree clean | `git status --short` → empty | repo | Baseline |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| six keys | artifact_type, name, last_regenerated, source_state, covers, stale_when | _to be filled_ | _to be filled_ |
| STALE format | `STALE — superseded by AgentReports/Bridge/<report>` (em dash) | _to be filled_ | _to be filled_ |
| doc-claims exit codes | 0 clean / 1 findings / 2 usage | _to be filled_ | _to be filled_ |
| `git status` | empty | _to be filled_ | _to be filled_ |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/` | children | `doc-claims/`, `nissth-bridge/` (+ `nissth-init/` if Phase 15 ran first) |
| `CLAUDE.md` | last section | §12 Doc-Claim Validator |
| DBL frontmatter enforcement | mechanism | none (discipline only, §7.3) |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/dbl-check/check.mjs` | CLI | `node Tools/dbl-check/check.mjs [--root <dir>] [--json] [--strict]` — scans `<root>/DBL/**/*.md` excluding `_TEMPLATE.md` |
| check `missing-frontmatter` | fires when | file does not start with `---\n…\n---` |
| check `missing-key` | fires when | any of the six §7.2 keys absent (one finding per key) |
| check `type-dir-mismatch` | fires when | `artifact_type` ≠ the value implied by the directory (`Summaries→summary`, `DependencyMaps→dependency_map`, `APIIndex→api_index`, `SchemaIndex→schema_index`) |
| check `stale-marked` | fires when | `last_regenerated` starts with `STALE` — severity `info` (it is the mechanism working, not a defect), listed so the agent sees what must be regenerated |
| check `bad-regenerated-format` | fires when | `last_regenerated` is neither `YYYY-MM-DD by <text>` nor `STALE — <text>` |
| check `design-only-source-exists` | fires when | `source_state` starts with `design-only` **and** at least one file exists under any `covers` glob (relative to `--root`) — the greenfield Phase 00 → Phase 01 regeneration trigger, now mechanical |
| check `covers-changed-since` | fires when | `source_state` is a 7–40 hex git ref, `--root` is a git work tree, and `git diff --name-only <ref> -- <covers…>` is non-empty; severity `warn`; skipped (not failed) when git or the ref is unavailable, with a note |
| check `over-budget` | fires when | word count > 1 100 (≈ 1 500 tokens, §7.4) — severity `warn` |
| check `crlf` | fires when | file contains `\r` |
| exit codes | | 0 clean (info/warn only unless `--strict`) · 1 findings of severity `error` (or any finding with `--strict`) · 2 usage/config (no `DBL/` dir, bad args) |
| `--json` | shape | `{ root, scanned, findings: [{check, severity, file, line?, message}], summary }` |
| `CLAUDE.md` §13 | content | why (F6: rule followed, defect survives — same as §12.1), what it checks (table), when to run (Phase 00 §4, every plan's §5 DBL sweep, before citing a DBL artifact in §1), what it is not (no edits, no Bridge report) |
| `CLAUDE.md` §7.3 | addition | one sentence: "`node Tools/dbl-check/check.mjs` performs steps 1–2 mechanically (§13)." |
| `README.md` tree | row | `Tools/dbl-check/` |
| `node Tools/dbl-check/check.mjs` on Nissth | result | exit 0, `scanned: 0` (templates excluded), message "no artifacts — templates only" |
| `node Tools/dbl-check/check.mjs --root C:\Users\admin\Desktop\FinansYönetimApp` | result | exit 0 today (11 design-only, no source); becomes exit 1 with 11× `design-only-source-exists` the moment Phase 01 creates `app/` |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [ ] **Step 1.** `Tools/dbl-check/package.json` (`@nissth/dbl-check`, private, ESM, node ≥ 20, `test`, `check: node check.mjs --root ../..`). **Acceptance:** `npm --prefix Tools/dbl-check test` runs.
- [ ] **Step 2.** Fixtures under `Tools/dbl-check/_fixtures/`: `clean/` (4 valid artifacts, one per type), `broken/` (missing frontmatter; missing two keys; wrong type for dir; bad date; CRLF; > 1 100 words), `stale/` (one STALE-marked artifact in the binding's exact format), `design-only/` (2 design-only artifacts, `covers: src/**`, **with** `src/x.ts` present for one and absent for the other), `git-ref/` (built at test time: temp git repo, commit, `source_state: <sha>`, then modify a covered file). **Acceptance:** each fixture directory has a `README` line stating the expected finding set.
- [ ] **Step 3.** `check.mjs` core: frontmatter parser (minimal YAML subset: scalars, `- ` lists, `STALE —` free text), directory→type map, the nine checks, glob matcher for `covers` (reuse the tiny matcher pattern from `dispatcher.js` or implement `**`/`*` only — no deps), git call via `execFileSync` with graceful skip. Exports `check(root, opts)`. **Acceptance:** fixtures produce exactly the expected finding sets.
- [ ] **Step 4.** CLI wrapper: args, `--json`, text reporter (grouped by file, severity-tagged), exit codes. **Acceptance:** `--json` parses; exit codes per §2.
- [ ] **Step 5.** `test.mjs` — cases in §4.2. **Acceptance:** all pass.
- [ ] **Step 6.** `Tools/dbl-check/README.md` (≤ 100 lines) mirroring doc-claims' README shape.
- [ ] **Step 7.** `CLAUDE.md`: append §13 (after §12); add the one sentence to §7.3 step 2. **Lines:** end of file + §7.3. **Acceptance:** `Grep 'dbl-check' CLAUDE.md` → ≥ 3 hits; doc-claims exit 0 (the new section names no tools in enumeration form; if `fictional-tool` fires on check names, add a `<!-- doc-claims:allow fictional-tool - check names, not bridge tools -->` waiver rather than editing the allowlist).
- [ ] **Step 8.** `README.md` tree row + one sentence in the DBL section. **Acceptance:** doc-claims exit 0.
- [ ] **Step 9.** Run the checker on Nissth (`exit 0, scanned 0`) and on `C:\Users\admin\Desktop\FinansYönetimApp` (`exit 0, scanned 11, 0 findings`); paste both outputs into the status entry. **Acceptance:** as stated.

### 3.2 Forbidden in this phase

- Auto-fixing anything (no `--fix`); the tool never writes to `DBL/`.
- Adding a YAML dependency — the subset parser is deliberate; if an artifact uses YAML the subset cannot read, that is a `bad-frontmatter` finding, not a reason to add `js-yaml`.
- Wiring into hooks or CI (same deferral as doc-claims §12.4).
- Editing any DBL artifact in the FinansYönetimApp repo, even if the checker finds something there — report it in `Issues:` for that repo's session.
- Touching `Bindings/**` StaleFlipper code even if its format looks improvable.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- No build, no cache; tests spawn `process.execPath` on the on-disk files after the last write. Fresh-worktree run per the Phase 15 pattern (`git worktree add ../nissth-p16-verify HEAD`), path cited in the status entry.

### 4.2 Checks

- [ ] **Build:** N/A.
- [ ] **Tests:** `npm --prefix Tools/dbl-check test` — expected ≥ 20 pass: clean fixture → 0 findings; each broken case → exactly its finding; STALE → `info` only, exit 0 without `--strict`, exit 1 with; design-only with source → `error`, without → clean; git-ref changed → `warn`; git-ref unchanged → clean; no git → skip note, exit 0; missing `DBL/` → exit 2; `--json` shape; CRLF; over-budget; templates excluded; unknown subdir under `DBL/` (e.g. `DBL/Notes/`) → `unknown-dir` `warn`.
- [ ] **Existing suites:** dispatcher 32/32, doc-claims 23/23 (and init if Phase 15 closed).
- [ ] **Doc-claims:** exit 0.
- [ ] **Runtime/integration:** Step 9 on both repos.
- [ ] **Bridge re-query:** N/A.
- [ ] **DBL freshness:** N/A.

### 4.3 Pass criteria

ALL of the following must be true:
- dbl-check suite green in dev dir and fresh worktree.
- Step 9 outputs match §2 After.
- doc-claims exit 0; other suites unchanged.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [ ] Remove worktree and any temp git repos created by tests (tests own their cleanup; verify `os.tmpdir()` has no `nissth-dbl-check-*` leftovers).
- [ ] **Reports check:** §10.4 #4 fires → `AgentReports/Reports/<date>_phase-16-dbl-check-snapshot.md` (snapshot): check table, exit semantics, the design-only → Phase 01 trigger explained, deliberate non-features.
- [ ] **Document Sync sweep:** modified `CLAUDE.md` (§7.3, §13), `README.md`. `Ultimate_Guide.md` §7.3 (freshness/stale-flip) should mention the checker — defer to Phase 17's sweep. Log: `Doc sync: [updated: CLAUDE.md §7.3 + §13, README.md; deferred: Ultimate_Guide.md §7.3 → Phase 17]`.
- [ ] Commit `feat(tools): add dbl-check DBL frontmatter/freshness validator; CLAUDE.md §13`.

---

## 6. Status Update Entry

```
### YYYY-MM-DD HH:MM — Phase 16: DBL Check

**State:**
- Phase: 16 closed
- Build: CLEAN
- Tests: PASS — dbl-check N/N, dispatcher 32/32, doc-claims 23/23; fresh worktree at <path>
- Active plan: none
- DBL refs: none
- Bridge reports: none
- Blockers: none

**Report:**
- Pre-flight §1.3: [rows]

**Executed:**
- Tools/dbl-check/{check.mjs,test.mjs,package.json,README.md,_fixtures/×5}; CLAUDE.md §7.3 + §13; README.md

**Verified:**
- [counts; Step 9 outputs on Nissth (0 scanned) and FinansYönetimApp (11 scanned, 0 findings); freshness statement]
- Doc sync: [as §5]
- Reports: AgentReports/Reports/<date>_phase-16-dbl-check-snapshot.md (snapshot)

**Issues:**
- [or none]

**Next:**
- Phase_17 (docs) if approved; tell the FinansYönetimApp session to add `node <framework>/Tools/dbl-check/check.mjs` to its Phase 01 §4.2.
```
