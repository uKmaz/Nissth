# Phase 11: Fresh-Clone Hardening — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_11_Fresh_Clone_Hardening
- **Authored:** 2026-08-24 by Claude (Opus 5)
- **Approved:** 2026-08-24 by user ("lets just do the plan")
- **Depends on:** Phase_10_Public_Preview_Branch (halted at §4.4 — this plan unblocks it), Phase_09_5_Binding_Framework_Root
- **Estimated scope:** Repo-hygiene fixes on `master` for the two defects that made Phase 10's fresh-worktree verification fail. Untracks one generated build artifact, corrects two `.gitignore` patterns, adds a repo-root `.gitattributes` line-ending policy, makes 7 LF-anchored regex sites across 4 Expo **test-support** files CRLF-tolerant, and adds a fresh-clone clause to the three `CLAUDE.md` verification protocols (§8.1.6 / §8.2.6 / §8.3.6). Eight files modified, one created, one untracked. No production source under any `Bindings/*/src/` is touched. Phase 10's re-cut of `nissth/public` is explicitly NOT in this phase.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none — Nissth's own `DBL/**` holds `_TEMPLATE.md` skeletons only; no `covers` glob overlaps anything this phase modifies.
- **Bridge reports:** none — no live stack state is queried. The defects are git-configuration and test-support defects, not runtime state.
- **Source:**
  - `Bindings/Expo/.gitignore:1-10` and `Bindings/Postgres/.gitignore:1-10`
  - `Bindings/Expo/tests/integration/_support.ts:63-86`
  - `Bindings/Expo/tests/contract/SchemaValidation.test.ts:44-52`
  - `Bindings/Expo/tests/unit/StaleFlipper.test.ts:45-55, 74-82, 118-126`
  - `Bindings/Expo/tests/unit/ReportWriter.test.ts:47-56`
  - `CLAUDE.md:312-330` (§8.1.6), `CLAUDE.md:450-470` (§8.2.6), `CLAUDE.md:568-584` (§8.3.6)
- **Reports:** `AgentReports/Reports/2026-08-21_fresh-clone-build-failures.md` (incident — root cause + remediation table for both defects, as revised 2026-08-24)
- **StatusUpdate.md:** latest entry as of plan authoring — `2026-08-24 04:31 — Doc-sync correction to the 04:16 entry`. The remediation scope this plan implements was recorded in the `2026-08-24 04:16` entry, items 1–5.

### 1.2 Diagnostic actions

> Prefer Bridge tools (`nissth-bridge <tool> ...`) and DBL reads over raw source greps when either covers the question (Hard Rule #4). Use the table's `Tool/command` column to record the exact invocation, including any `--scope.*` flags.

No Bridge tool covers git-index or line-ending questions — no binding registers a repo-hygiene lens. Raw `git` plumbing is the correct instrument here; this sentence is the recorded reason required by HR#5.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm the generated build artifact is still tracked | `git ls-files` filtered for `tsbuildinfo` | repo | Defect #1 still present; Step 2 has something to untrack |
| 2 | Confirm both ignore patterns are still wrong | `grep -n tsbuildinfo Bindings/*/.gitignore` | 2 files | Step 3 targets the right line in each file |
| 3 | Confirm no repo-root `.gitattributes` exists | `ls -la .gitattributes` | repo root | Step 4 creates rather than modifies; no existing policy is overwritten |
| 4 | Confirm the checkout filter that materializes CRLF | `git config core.autocrlf` | local git config | Establishes that a fresh clone on this host converts LF→CRLF, which is defect #2's mechanism |
| 5 | Confirm the index is uniformly LF outside `Axiom/` | `git grep -I -l` for a carriage return over `HEAD -- . ':!Axiom'` | HEAD tree | **Load-bearing for Step 4's safety.** If the index already holds CRLF files, `eol=lf` would renormalize them and produce a diff far outside this plan's declared scope |
| 6 | Enumerate tracked `*.cmd` / `*.bat` files | `git ls-files` filtered, excluding `^Axiom/` | repo | Batch files are the one class that must stay CRLF; determines whether Step 4 needs the override stanza |
| 7 | Confirm the 7 LF-anchored test-support regex sites | `grep -rnF -- '---\n' Bindings/Expo/tests/` | `Bindings/Expo/tests/` | Step 5 edits exactly these lines; catches any drift since the 2026-08-24 04:16 survey |
| 8 | Confirm production parsers are already CRLF-tolerant | read `Bindings/Expo/src/core/StaleFlipper.ts:16`, `Bindings/Postgres/src/core/StaleFlipper.ts:16`, `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/StaleFlipper.java:30` | 3 files | Re-validates the 2026-08-24 finding that scoped defect #2 to the test tree. If false, this plan is under-scoped and must STOP |
| 9 | Measure the four suite baselines in the development directory | `node --test Tools/nissth-bridge/test.mjs`; `npm test` in `Bindings/Expo`; `npm test` in `Bindings/Postgres`; `mvn clean test` in `Bindings/SpringBoot` | 4 suites | §4.2 compares fresh-clone counts against these numbers; the phase is judged by making them equal |
| 10 | Locate the stale Phase 10 worktree | `git worktree list` | repo | Confirms it is still checked out and must NOT be disturbed by this phase (§3.2) |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Is `Bindings/Postgres/tsconfig.tsbuildinfo` tracked? | yes — exactly one match | yes — exactly 1 match: `Bindings/Postgres/tsconfig.tsbuildinfo` | yes |
| Ignore pattern at `Bindings/{Expo,Postgres}/.gitignore:3`? | bare `.tsbuildinfo` (wrong) in both | bare `.tsbuildinfo` at line 3 of both files | yes |
| Does a repo-root `.gitattributes` exist? | no | no — `ls` reports No such file or directory | yes |
| `core.autocrlf` value? | `true` | `true` | yes |
| CRLF files in the index outside `Axiom/`? | zero | **zero** — `git grep -I -l` over `HEAD -- . `:!Axiom`` exits 1 with no output | yes |
| Tracked `*.cmd` / `*.bat` outside `Axiom/`? | 2 — `Bindings/SpringBoot/mvnw.cmd`, `Bindings/SpringBoot/tests/fixture/mvnw.cmd` | 2 — exactly the two predicted `mvnw.cmd` files | yes |
| LF-anchored regex sites under `Bindings/Expo/tests/`? | 7 read sites across 4 files: `_support.ts:67,80`; `SchemaValidation.test.ts:48`; `ReportWriter.test.ts:51,52`; `StaleFlipper.test.ts:51,78,122` — plus write-side literals (`StaleFlipper.test.ts:23`) that are NOT in scope | 9 hits total = the 7 predicted read sites at the exact predicted lines, plus 2 write-side literals (`StaleFlipper.test.ts:23` has both) which stay out of scope | yes |
| Are the three production `StaleFlipper` frontmatter parsers CRLF-tolerant already? | yes — all three | yes — Expo and Postgres `src/core/StaleFlipper.ts:16` are both anchored `^---` + CR-optional-LF at all three delimiter positions; SpringBoot `StaleFlipper.java:30` uses a MULTILINE pattern whose boundary is `^---` + whitespace + `\R` (any Unicode line break). Confirmed by direct read of all three lines. | yes |
| Dev-directory suite baselines? | Dispatcher 32/32; Expo 58/58; Postgres 107 pass/18 skip; SpringBoot 104/104 | Dispatcher 32/32; Expo 58/58 (13 suites); Postgres 107 pass/18 skip/125 total (11 of 16 suites); SpringBoot 104/104 BUILD SUCCESS | yes |
| Stale Phase 10 worktree still present? | yes — a scratchpad worktree at `c0240f9 [nissth/public]` | yes — `…/95cc69f3-…/scratchpad/nissth-public` at `c0240f9 [nissth/public]` | yes |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

Row-specific escalation: if row 5 (**CRLF files in the index**) is non-zero, STOP regardless of the other rows. Step 4 would then renormalize those files and produce a diff far larger than this plan's declared scope, which is a §3.2 violation.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Postgres/tsconfig.tsbuildinfo` | git tracking status | tracked — the only tracked build artifact in the repo |
| `Bindings/Postgres/.gitignore:3` | pattern | `.tsbuildinfo` — never matches the real filename `tsconfig.tsbuildinfo` |
| `Bindings/Expo/.gitignore:3` | pattern | `.tsbuildinfo` — same wrong pattern, latently exposed |
| `<repo root>/.gitattributes` | existence | absent — no line-ending policy; checkout behavior is whatever the cloner's `core.autocrlf` says |
| `Bindings/Expo/tests/integration/_support.ts:67,80` | frontmatter regex | LF-anchored `^---\n…` — returns `null` on a CRLF checkout |
| `Bindings/Expo/tests/contract/SchemaValidation.test.ts:48` | frontmatter regex | LF-anchored |
| `Bindings/Expo/tests/unit/ReportWriter.test.ts:51,52` | frontmatter regex | LF-anchored |
| `Bindings/Expo/tests/unit/StaleFlipper.test.ts:51,78,122` | frontmatter regex | LF-anchored |
| `CLAUDE.md` §8.1.6 / §8.2.6 / §8.3.6 | fresh-clone requirement | absent — all three validate from the development directory only |
| Fresh clone of `master` — Postgres suite | result | 106 pass / 1 fail (`SecretRedaction.test.ts` CLI spawn dies `MODULE_NOT_FOUND`) |
| Fresh clone of `master` — Expo suite | result | 57 pass / 1 fail (`RouteLens.it.test.ts`) |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Postgres/tsconfig.tsbuildinfo` | git tracking status | untracked and ignored; file remains on disk in the development directory |
| `Bindings/Postgres/.gitignore:3` | pattern | `*.tsbuildinfo` |
| `Bindings/Expo/.gitignore:3` | pattern | `*.tsbuildinfo` |
| `<repo root>/.gitattributes` | existence | present — `* text=auto eol=lf` baseline, `*.cmd`/`*.bat` pinned to `crlf`, binaries marked `binary` |
| `Bindings/SpringBoot/mvnw.cmd`, `Bindings/SpringBoot/tests/fixture/mvnw.cmd` | working-tree line endings | CRLF — preserved by the override stanza |
| All 7 Expo test-support regex sites | frontmatter regex | `\r?\n`-tolerant at every delimiter position |
| `Bindings/{Expo,Postgres,SpringBoot}/src/**` | content | **unchanged** — production parsers were already tolerant; nothing to fix |
| `CLAUDE.md` §8.1.6 / §8.2.6 / §8.3.6 | fresh-clone requirement | each carries a clause requiring at least one validation run from a fresh clone or worktree, and each freshness statement names the directory the run happened in |
| Fresh clone of `master` — Postgres suite | result | 107 pass / 18 skip — equal to the dev-directory baseline |
| Fresh clone of `master` — Expo suite | result | 58/58 — equal to the dev-directory baseline |
| Fresh clone of `master` — Dispatcher, SpringBoot | result | 32/32 and 104/104 — unchanged |
| `nissth/public` @ `c0240f9` and its worktree | state | **untouched** — Phase 10's resume owns them |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [x] **Step 1.** Write the pre-change rollback artifact (HR#9). Record: `master` tip SHA, the §1.3 measured baselines, `git config core.autocrlf`, the tracked status and blob SHA of `Bindings/Postgres/tsconfig.tsbuildinfo`, and an explicit restore recipe for each of Steps 2–6. **File:** `AgentReports/Snapshots/2026-08-24_phase-11-pre-hardening-manifest.md`. **Lines:** new file. **Operation:** add. **Acceptance:** file exists and names a concrete `git` restore command for every subsequent step.

- [x] **Step 2.** Untrack the generated TypeScript build-info artifact. **File:** `Bindings/Postgres/tsconfig.tsbuildinfo`. **Lines:** whole file. **Operation:** remove **from the index only** — `git rm --cached`, never `git rm`. **Acceptance:** `git ls-files` returns no `tsbuildinfo` match AND the file still exists on disk in the development directory.

- [x] **Step 3.** Correct the ignore pattern in both bindings so it matches the real filename. **Files:** `Bindings/Postgres/.gitignore`, `Bindings/Expo/.gitignore`. **Lines:** `3` in each. **Operation:** modify — `.tsbuildinfo` becomes `*.tsbuildinfo`. **Acceptance:** `git check-ignore -v Bindings/Postgres/tsconfig.tsbuildinfo` names the corrected rule as the reason, and `git status --short` shows the file as neither modified nor untracked.

- [x] **Step 4.** Create the repo-root line-ending policy. **File:** `.gitattributes`. **Lines:** new file. **Operation:** add. Content, in this order:
  - `* text=auto eol=lf` — baseline. Safe because §1.3 row 5 proves the index is already uniformly LF, so this normalizes nothing in the index and only pins the **working-tree** side.
  - `*.cmd text eol=crlf` and `*.bat text eol=crlf` — the two tracked `mvnw.cmd` files must keep CRLF; a batch file with LF-only endings can misparse labels and `goto` on Windows. Without this stanza the baseline line would be a Windows regression, not a fix.
  - `*.pdf binary`, `*.png binary`, `*.jpg binary`, `*.jar binary` — never text-filter binaries.
  - Note deliberately NOT added: `*.ps1` needs no override (PowerShell reads LF correctly), and the extensionless POSIX launchers (`nissth-bridge`, `mvnw`) are correctly served by the LF baseline.
  **Acceptance:** `git check-attr -a -- Bindings/Expo/tests/fixture/DBL/APIIndex/routes.md` reports `text: auto` and `eol: lf`; `git check-attr -a -- Bindings/SpringBoot/mvnw.cmd` reports `eol: crlf`.

- [x] **Step 5.** Make the LF-anchored **test-support** frontmatter regexes CRLF-tolerant — each `\n` that is part of a frontmatter delimiter match becomes `\r?\n`. Do NOT touch write-side literals (`StaleFlipper.test.ts:23` and any `writeFileSync` template that authors `---\n`); those emit LF deliberately and are unaffected by checkout. **Files and lines:**
  - `Bindings/Expo/tests/integration/_support.ts:67, 80`
  - `Bindings/Expo/tests/contract/SchemaValidation.test.ts:48`
  - `Bindings/Expo/tests/unit/ReportWriter.test.ts:51, 52`
  - `Bindings/Expo/tests/unit/StaleFlipper.test.ts:51, 78, 122`
  **Operation:** modify. **Acceptance:** `grep -rnF -- '---\n' Bindings/Expo/tests/` returns only write-side literals — all 7 read sites gone; and a hand-written CRLF fixture parses to a non-`null` frontmatter object.

- [x] **Step 6.** Add the fresh-clone clause to the three stack verification protocols. **File:** `CLAUDE.md`. **Lines:** §8.1.6 (~312–330), §8.2.6 (~450–470), §8.3.6 (~568–584). **Operation:** modify. Each section gains (a) a numbered clause stating that a binding's suite MUST be validated at least once from a **fresh clone or worktree** — not only from the development directory — before a phase that changes that binding's build inputs may close, and (b) a sentence in that section's **Freshness statement** template naming the directory the run happened in. State the rationale inline: nine phases of green verification missed both Phase 10 defects precisely because no protocol required this. **Acceptance:** all three sections carry the clause and all three freshness statements name a directory.

- [x] **Step 7.** Append the filled-in §6 entry to `AgentReports/StatusUpdate.md`, after §4 and §5 complete.

### 3.2 Forbidden in this phase

> Explicitly list what is OUT OF SCOPE. This is the anti-scope-creep guard.

- **Do NOT touch `nissth/public` @ `c0240f9` or its worktree.** Removing the stale worktree and re-cutting the branch is Phase 10's resume (Phase 10 §3.1 Steps 4–11), not this phase. This phase fixes `master` only.
- **Do NOT modify any production source under `Bindings/*/src/`.** §1.2 action 8 exists to confirm the production frontmatter parsers are already CRLF-tolerant. If they are, there is nothing to change; if they are not, STOP per §1.3 rather than widening scope here.
- **Do NOT run `git add --renormalize`.** §1.3 row 5 establishes the index is already LF; renormalizing is a no-op at best and a scope explosion at worst.
- **Do NOT delete `Bindings/Postgres/tsconfig.tsbuildinfo` from disk.** `git rm --cached` only. Deleting it forces an unnecessary full rebuild in the development directory and is not what defect #1 requires.
- **Do NOT `git push`.** No remote operation in this phase.
- **Do NOT redact `Emre Uçmaz` from any plan's Approved line, and do not settle the public-branch commit identity.** That is the open user decision carried from Phase 10 §6 `Next` item b — a distribution question, not a fresh-clone defect.
- **Do NOT "fix" line-ending-adjacent things noticed in passing** — the `.gitkeep` files, the `.ps1` launchers, or the `Axiom/` tree (reference material; stays exactly as-is).
- **Do NOT bump any binding version, changelog, or `*.bridge.json` manifest.** No tool behavior changes in this phase.
- **Do NOT add CI configuration.** A fresh-clone check in CI is a reasonable follow-on but is a new capability and belongs in its own plan.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

> How do you know the verifier sees the latest changes? (Addresses the "false CLEAN" failure mode — HR#10.)

This phase's verification is **defined by** freshness — the defects exist only because every prior phase verified from the development directory.

- The **authoritative** run happens in a **brand-new git worktree** of the fixed `master`, created under the current session's scratchpad via `git worktree add`. The development directory's `node_modules/`, `dist/`, `target/`, and stale `tsconfig.tsbuildinfo` are therefore structurally unreachable by the verifier.
- **Re-resolve the scratchpad path via `git worktree list` before use.** The path recorded in the 2026-08-24 04:16 status entry belongs to a prior session id and will not match this session's.
- Expo + Postgres, per §8.2.6: `npm ci` (lockfile-driven, not `npm install`), then `npx tsc --noEmit`, then `npm test`. `ts-jest` transforms on every run with no persistent test cache.
- Spring Boot, per §8.1.6: `mvn clean test` — the `clean` target is the freshness guarantee for the Maven-based binding.
- Dispatcher: `node --test Tools/nissth-bridge/test.mjs` — no cache.
- The Postgres defect specifically requires `tsc` to emit **all 16** files rather than the 13 a stale `tsconfig.tsbuildinfo` produces. The file count of `Bindings/Postgres/dist/` in the fresh worktree is read directly as the artifact, not inferred from a green suite.
- The Expo defect requires that `Bindings/Expo/tests/fixture/DBL/APIIndex/routes.md` materialize with the endings `.gitattributes` dictates. Its actual bytes in the fresh worktree are inspected, not assumed.
- No Bridge action tool is involved, so no exit-code-5 hard-enforce contract is being leaned on here.

### 4.2 Checks

- [x] **Build (Expo, fresh worktree):** `npm ci && npx tsc --noEmit` in `Bindings/Expo` — expected: exit 0, no diagnostics.
- [x] **Build (Postgres, fresh worktree):** `npm ci && npm run build` in `Bindings/Postgres` — expected: exit 0, and `Bindings/Postgres/dist/` contains **16** emitted files. A count of 13 means defect #1 is unfixed.
- [x] **Build (Spring Boot, fresh worktree):** `mvn clean test` in `Bindings/SpringBoot` — expected: `BUILD SUCCESS`.
- [x] **Tests (fresh worktree, all four):** expected exactly — Dispatcher **32/32**; Spring Boot **104/104**; Postgres **107 pass / 18 skip / 125 total**; Expo **58/58** across 13 suites. Named regressions that must pass: `Bindings/Postgres/tests/contract/SecretRedaction.test.ts` (all 7 assertions, including the CLI spawn) and `Bindings/Expo/tests/integration/RouteLens.it.test.ts`.
- [x] **Tests (development directory, regression guard):** the same four suites re-run in the primary working directory — expected: identical counts. Proves Step 5's regex change did not break the LF path that already worked.
- [x] **Line-ending policy:** in the fresh worktree, `Bindings/Expo/tests/fixture/DBL/APIIndex/routes.md` shows no CRLF; `Bindings/SpringBoot/mvnw.cmd` shows CRLF line terminators.
- [x] **Index hygiene:** `git ls-files` returns no `tsbuildinfo` match; `git status --short` in the development directory shows no modifications beyond this plan's declared file set.
- [x] **Runtime/integration:** `nissth-bridge --list-bindings` executed **from the fresh worktree** resolves all three bindings (expo, postgres, spring-boot). End-to-end proof that a cloner gets a working dispatcher.
- [x] **Bridge re-query:** N/A — no Bridge tool covers repo hygiene or line-ending policy, and no binding's tool surface changes in this phase.
- [x] **DBL freshness:** N/A — Nissth's own `DBL/**` holds only `_TEMPLATE.md` skeletons; no `covers` glob overlaps any file in §3. To be restated in the §6 `Doc sync:` line rather than silently omitted.

### 4.3 Pass criteria

ALL of the following must be true:
- All four suites in the **fresh worktree** match the development-directory baselines exactly: 32/32, 104/104, 107 pass/18 skip, 58/58.
- `Bindings/Postgres/dist/` holds 16 files in the fresh worktree, and `SecretRedaction.test.ts`'s CLI-spawn assertion passes.
- `RouteLens.it.test.ts` passes in the fresh worktree.
- Both the fixture's LF and `mvnw.cmd`'s CRLF are confirmed by direct byte inspection in the fresh worktree.
- `git ls-files` shows zero tracked `*.tsbuildinfo`.
- The development-directory suites still match their baselines — no regression from Step 5.
- `nissth/public` is unchanged: `git rev-parse nissth/public` still resolves to `c0240f9`, and its worktree is undisturbed.
- `git diff --stat` on `master` touches only: 2 × `.gitignore`, 1 × `.gitattributes` (new), 4 × Expo test files, `CLAUDE.md`, the deleted index entry for `tsconfig.tsbuildinfo`, plus this plan, the Step 1 manifest, and `StatusUpdate.md`.
- `Axiom/` is untouched: 148 files on disk, 148 tracked, 0 dirty.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

Rollback recipe (detailed per-step in the Step 1 manifest): `git checkout --` the two `.gitignore` files, `CLAUDE.md`, and `Bindings/Expo/tests/`; delete the new `.gitattributes`; `git add Bindings/Postgres/tsconfig.tsbuildinfo` to restore tracking; `git worktree remove --force <fresh-verify-worktree>`. `master`'s tip SHA is unchanged throughout, because nothing is committed until after §4 passes.

Escalation note: a fresh-worktree failure that **also** reproduces in the development directory is a genuine regression from §3 and must be fixed forward or rolled back. A failure that appears **only** in the fresh worktree is a third pre-existing defect of the same family as #1 and #2 — record it in the incident Report and treat it as a scope question for the user, not as a silent addition to §3.

---

## 5. Cleanup

- [x] Remove the fresh verification worktree: `git worktree remove <path>` (re-resolve via `git worktree list`). Leave the Phase 10 `nissth/public` worktree in place — §3.2.
- [x] Remove temp scripts/artifacts created during execution
- [x] Roll snapshots if no longer needed (`AgentReports/Snapshots/`) — keep the Step 1 manifest until Phase 10 resumes and closes.
- [x] **Reports check (CLAUDE.md §10):**
  - Update `AgentReports/Reports/2026-08-21_fresh-clone-build-failures.md` — mark remediations 1a / 1b / 2a / 2b applied, close Follow-ups #1–#3, bump `last_updated`, extend `related_status_entries` and `related_plans`, add a revision-history line. The Report is append-friendly per §10.2, so this is an update, not a new file.
  - No new mandatory Report is triggered on success: this phase makes no named-alternative architecture decision, ingests no external spec, and is a bounded hygiene fix rather than a non-trivial phase close (§10.4 trigger #4). If §4 fails, §4.4 plus §10.4 trigger #1 make an incident Report mandatory.
  - List authored Reports here so they appear in the §6 status entry's `Reports:` line.
- [x] **Document Sync sweep (Hard Rule #11):**
  - Source files modified in §3: `Bindings/Expo/tests/integration/_support.ts`, `Bindings/Expo/tests/contract/SchemaValidation.test.ts`, `Bindings/Expo/tests/unit/ReportWriter.test.ts`, `Bindings/Expo/tests/unit/StaleFlipper.test.ts`, `Bindings/Expo/.gitignore`, `Bindings/Postgres/.gitignore`, `.gitattributes` (new), `CLAUDE.md`.
  - Affected stable documents:
    - `DBL/**` — none. Skeletons only; no `covers` overlap.
    - `ImplementationPlans/Phase_10_Public_Preview_Branch.md` — cross-references the failing suites; its §4.4 halt is resolved by this phase. Note the linkage in the §6 entry; do NOT edit Phase 10's plan body (it is an approved contract).
    - `CLAUDE.md` — modified by Step 6, so it is both a target and a sync obligation. Confirm §8.2.6's freshness-statement template and §11.13's Expo tool descriptions still read correctly after the edit.
    - `Bindings/Expo/README.md`, `Bindings/Postgres/README.md` — check whether either documents a build/verify sequence the new fresh-clone clause contradicts. Update if so.
  - For each affected document, either **UPDATE** now or **MARK STALE** with a regeneration step queued in §6 `Next`.
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [updated: X, Y; marked stale: Z]`
- [x] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 11: Fresh-Clone Hardening

**State:**
- Phase: 11 — Phase 10 unblocked, awaiting its own resume
- Build: CLEAN | HAS_ERRORS
- Tests: PASS | FAIL — fresh worktree: Dispatcher [n]/32; SpringBoot [n]/104; Postgres [n] pass/[n] skip; Expo [n]/58. Dev directory: [same four].
- Active plan: ImplementationPlans/Phase_11_Fresh_Clone_Hardening.md
- DBL refs: none — Nissth's own DBL holds `_TEMPLATE.md` skeletons only
- Bridge reports: none — no live stack state queried; repo-hygiene phase
- Blockers: [or "none"]

**Report:**
- [condensed §1.3 findings — especially row 5 (index CRLF count) and row 8 (production parsers already tolerant), the two rows that gate scope]

**Executed:**
- [Steps 1–7 with checkboxes resolved; note the `git rm --cached` vs `git rm` distinction and the `*.cmd` CRLF override explicitly]

**Verified:**
- [§4 results. Freshness: fresh `git worktree` of fixed `master` under this session's scratchpad; `npm ci` (not `npm install`); `mvn clean test`; `node --test`. Postgres `dist/` file count read directly. Fixture and `mvnw.cmd` bytes inspected.]
- Doc sync: [updated: ...; marked stale: ...]
- Reports: AgentReports/Reports/2026-08-21_fresh-clone-build-failures.md (incident — updated: remediations applied, follow-ups closed)

**Issues:**
- [or "none"; if Verified: FAIL, cite the matching incident Report filename here]

**Next:**
- Resume Phase 10: remove the stale `nissth/public` worktree and branch (`c0240f9`), re-cut `nissth/public` from the fixed `master` per Phase 10 §3.1 Steps 4–11 in a fresh scratchpad worktree, re-run Phase 10 §4.2 **from that worktree**, then execute Phase 10 §5 plus Step 12. Open user decision still pending: whether to redact the author name from the 8 scrubbed phase plans, and which identity authors the public commit.
```
