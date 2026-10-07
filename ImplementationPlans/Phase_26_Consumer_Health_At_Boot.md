# Phase 26: Consumer health at boot — a Bridge that cannot be silently dead, and drift a consumer can see — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_26_Consumer_Health_At_Boot
- **Authored:** 2026-10-07 by Claude (Opus 5.5)
- **Approved:** 2026-10-07 by user ("Phase 26 approved")
- **Depends on:** Phase_15_Consumer_Init_Tooling, Phase_22_Consumer_Sync_And_Policy
- **Estimated scope:** Lessons 1–2 of `AgentReports/Reports/2026-10-06_sibling-consumers-harvest.md`. Two consumers ran five weeks with an unreachable Bridge and a framework body 1 192 lines behind, and nothing on their side said so. This phase adds a `--health` command to the dispatcher (framework root and how it was resolved, per-binding *built and runnable*, framework-body drift); makes `nissth-init --check` report stale or unreachable consumer launchers; puts the health probe into the §1 boot protocol as a Blocker source; and rolls the result out to both live sibling consumers. Touches `Tools/nissth-bridge/`, `Tools/nissth-init/`, `CLAUDE.md` §1 / HR#4 / §11.5 / §9.1, three READMEs, and — in the consumers — only framework-owned files (`CLAUDE.md` body, launchers, skeleton dirs, ledger).

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm that the Bridge's failure in the consumers was invisible by construction, and that the existing discovery commands cannot detect it.

### 1.1 Inputs to read

- **DBL:** none in this repo (`DBL/**` holds templates only).
- **Bridge reports:** none — the subject is the Bridge's own reachability.
- **Source:** `Tools/nissth-bridge/dispatcher.js:157-226` (`parseArgv`), `:253-282` (`resolveCliEntry`, `buildSpawnSpec`), `:321-358` (root resolution, `--list-bindings`); `Tools/nissth-bridge/consumer-launcher/nissth-bridge{,.ps1}` (whole, 45 lines each); `Tools/nissth-init/init.mjs:186-197` (launcher templating), `:257-305` (`checkConsumer`), `:358-376` (`missingSkeleton`), `:418-480` (`main --check`); `CLAUDE.md` §1 (lines 9-21), HR#4 (line 61), §11.5 (963-988).
- **Reports:** `AgentReports/Reports/2026-10-06_sibling-consumers-harvest.md` §A rows 1–2.
- **StatusUpdate.md:** latest entry 2026-10-06 23:14 +03:00, whose `Next:` names this plan.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Run each consumer's own launcher | `powershell -File ./nissth-bridge.ps1 --list-bindings` | both sibling consumers | Is the launcher the current template, and does it reach a dispatcher? |
| 2 | Inspect the launchers' baked root | `grep NISSTH_FRAMEWORK_ROOT nissth-bridge.ps1` | both consumers | Confirms the failure is a stale machine path, not a missing env var |
| 3 | Check binding builds in this checkout | `ls Bindings/*/dist/cli/index.js Bindings/SpringBoot/target/*.jar` | framework | A fresh clone carries manifests but no build output |
| 4 | Run `--list-bindings` here | `./nissth-bridge --list-bindings` | framework | Does the existing discovery command notice unbuilt bindings? |
| 5 | `init --check` both consumers | `node Tools/nissth-init/init.mjs --check <dir>` | both consumers | What drift is already reported, and whether launchers are part of it |
| 6 | Baseline suites | `node --test Tools/nissth-bridge/test.mjs` · `node --test Tools/nissth-init/test.mjs` | framework | Pass counts before change |

### 1.3 Findings (filled during execution)

> Rows 1–5 were already observed while authoring (2026-10-06/07); execution re-runs them and records the result here.

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| 1. Consumer launchers reach a dispatcher? | no — both are the May-era launcher, which errors | _to be filled_ | _to be filled_ |
| 2. Baked root? | `C:\Users\admin\Desktop\Nissth` (previous machine) in both | _to be filled_ | _to be filled_ |
| 3. Bindings built here? | none of the three (`dist/`, `target/` untracked) | _to be filled_ | _to be filled_ |
| 4. `--list-bindings` notices? | no — prints three bindings, exit 0 | _to be filled_ | _to be filled_ |
| 5. `init --check` mentions launchers? | no — body drift (1 192 lines) + missing `Archive`, `Tests/README.md` only | _to be filled_ | _to be filled_ |
| 6. Baseline | dispatcher 32/32 · nissth-init 32/32 | _to be filled_ | _to be filled_ |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Tools/nissth-bridge/dispatcher.js` | health command | none; `--list-bindings` reads manifests only and exits 0 with nothing built |
| `Tools/nissth-init/init.mjs` `checkConsumer` | launcher check | none — a launcher from May with a dead path is invisible |
| `CLAUDE.md` §1 | Bridge probe at boot | none |
| `CLAUDE.md` HR#4 | unreachable Bridge | unstated — "Bridge reports: none" was written for five weeks without being a Blocker |
| both sibling consumers | body / launchers / skeleton | 1 192 lines behind / May launcher, dead path / `Archive`, `Tests/README.md` missing |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `dispatcher.js` | `--health [--json]` | prints repo root, framework root + resolving tier, per binding `ok` / `not-built` / `build-older-than-source` / `runtime-missing` with the fix command, and framework-body drift when repo ≠ framework; exit 0 healthy, 1 any problem |
| consumer launchers | not-found message | names itself a boot Blocker (§1) |
| `init.mjs --check` | launchers | reports `launcher-drift` (differs from template beyond `DEFAULT_ROOT`) and `launcher-root-unreachable`; both count as drift, exit 1 |
| `CLAUDE.md` §1 | new step | run `nissth-bridge --health`; non-zero ⇒ Blocker, settled before other work |
| `CLAUDE.md` HR#4, §11.5, §9.1 | text | unreachable Bridge is a defect to fix or escalate; `--health` and exit 1 documented; init handoff runs `--health` |
| suites | counts | dispatcher ≥ 32 + new · nissth-init ≥ 32 + new, all green |
| both sibling consumers | state | body in sync, current launchers wired to this checkout, skeleton complete, `--health` exit 0, committed **unpushed** |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [ ] **Step 1.** Add `--health` to `parseArgv` and a `healthReport(repoRoot)` function. **File:** `Tools/nissth-bridge/dispatcher.js`. **Lines:** `157-226`, new function after `:282`, branch in `runDispatcher` after framework-root resolution. **Operation:** modify. Reports: framework root and which tier resolved it; per manifest, `cli_entry` exists (`not-built` + build hint per runtime: `npm ci && npm run build` / `./mvnw -q -B package -DskipTests`), `cli_entry` older than the newest file under the binding's `src/` (`build-older-than-source`), `java` on PATH for `java-jar` (`runtime-missing`); when repo root ≠ framework root, the framework-body drift from `checkConsumer` (imported from `<frameworkRoot>/Tools/nissth-init/init.mjs`). Framework-root resolution failure is itself reported, not thrown. **Acceptance:** Step 4 tests.
- [ ] **Step 2.** Not-found message names the boot Blocker. **Files:** `Tools/nissth-bridge/consumer-launcher/nissth-bridge`, `…/nissth-bridge.ps1`. **Lines:** the two `echo` / `WriteLine` lines at the end. **Operation:** modify. **Acceptance:** `DEFAULT_ROOT` placeholders unchanged (`init.mjs:189` still accepts the templates).
- [ ] **Step 3.** `checkConsumer` compares each consumer launcher to the template with the `DEFAULT_ROOT` line normalised, and checks that a non-empty `DEFAULT_ROOT` holds `Tools/nissth-bridge/dispatcher.js`. **File:** `Tools/nissth-init/init.mjs`. **Lines:** `257-305`, `418-480`. **Operation:** modify. New result fields `launchers: [{file, problem}]`; `inSync` false when non-empty; printed under the existing drift block. **Acceptance:** Step 4 tests.
- [ ] **Step 4.** Tests. **Files:** `Tools/nissth-bridge/test.mjs` (+ fixture under `_fixtures/` for a built and an unbuilt binding), `Tools/nissth-init/test.mjs`. **Operation:** add. Cases: health all-ok; `not-built`; `build-older-than-source`; invalid framework root reported with exit 1; drift reported when repo ≠ framework; `--json` shape; `--check` flags a May-shape launcher, an unreachable `DEFAULT_ROOT`, and passes a freshly templated launcher. **Acceptance:** both suites green, new cases counted.
- [ ] **Step 5.** `CLAUDE.md`: §1 new step 2 (renumber 2→6): run `./nissth-bridge --health` (`.\nissth-bridge.ps1 --health` on Windows); non-zero ⇒ record under `Blockers:` and settle with the user before other work. HR#4 one sentence: an unreachable Bridge is a defect to fix or escalate, never a licence to fall back to raw tools. §11.5: `--health` in the discovery block, exit `1` = health problems found. §9.1 step 2 handoff: first command in the new project is `--health`. **Operation:** modify. **Acceptance:** `doc-claims` exit 0.
- [ ] **Step 6.** READMEs. **Files:** `Tools/nissth-bridge/README.md`, `Tools/nissth-bridge/consumer-launcher/README.md`, `Tools/nissth-init/README.md`; the init handoff text at `init.mjs:~470`. **Operation:** modify. **Acceptance:** each documents the new command / check in one short section.
- [ ] **Step 7.** Field run in this checkout: `--health` before building (expect exit 1, three `not-built`), then build the three bindings per their READMEs, then `--health` (expect exit 0). **Operation:** run. **Acceptance:** both outputs recorded in §1.3 / §4.
- [ ] **Step 8.** Roll out to the two sibling consumers, one at a time. For each: snapshot = current `HEAD` ref (recorded, HR#9); replace the `CLAUDE.md` framework body with this checkout's, keeping the consumer banner; replace both launchers with the templates, `DEFAULT_ROOT` = this checkout (local wiring, as at install); create `AgentReports/Archive/README.md` and `Tests/README.md` from `Tools/nissth-init/templates/`; run `--check` (expect exit 0 apart from the consumer's own DBL / plan findings, which are not this phase's) and `--health` from the consumer (expect exit 0); append a consumer status entry; **commit, do not push** — on the backend a push to `main` is a production deploy. **Acceptance:** `--check` reports body + skeleton + launchers in sync; `--health` exit 0; `git log -1` in each consumer shows the commit; `git status -sb` shows ahead-by-1.

### 3.2 Forbidden in this phase

- Lessons 3–18 of the harvest Report (runtime/deploy fields, micro plans, cross-repo ledger, binding slices) — they are Phases 27–31.
- Rotating any ledger, this one included, though all three are over §5.1's threshold — a separate, deliberate act.
- Fixing the consumers' DBL (CRLF, stale artifacts) or their plan-lint errors — consumer work, under consumer plans.
- Pushing either consumer. Touching any consumer file outside `CLAUDE.md`, the two launchers, `AgentReports/Archive/`, `Tests/README.md`, `AgentReports/StatusUpdate.md`.
- Tagging the app consumer's dangling SDK-55 commit — the user's call, raised separately.
- A `--sync` mode for `nissth-init`, or any auto-fix in `--health` — both report only.
- Hook or CI wiring for `--health`.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Suites run with `node --test` straight from the edited files on disk; no build step sits between source and test.
- Field runs (Step 7, Step 8) execute the launchers as a session would, from each repo's root, after the edits are saved; bindings built in Step 7 are rebuilt from the current tree, and `--health`'s own `build-older-than-source` check is the freshness guard for them.
- Run performed in the development directory; the dispatcher and init tool have no build inputs, so §8.x.6 fresh-clone validation does not apply — the bindings' build inputs are unchanged by this phase.

### 4.2 Checks

- [ ] **Build:** bindings built per READMEs (Step 7) — expected: three `cli_entry` paths exist.
- [ ] **Tests:** `node --test Tools/nissth-bridge/test.mjs` and `node --test Tools/nissth-init/test.mjs` — expected: all pass, counts above 32 each.
- [ ] **Runtime/integration:** `./nissth-bridge --health` here: exit 1 before build, exit 0 after; in each consumer after Step 8: exit 0.
- [ ] **Bridge re-query:** N/A — no binding source changed.
- [ ] **DBL freshness:** N/A — no DBL here; consumer DBL untouched (§3.2).
- [ ] **Validators:** `doc-claims` exit 0; `plan-lint --plan ImplementationPlans/Phase_26_Consumer_Health_At_Boot.md` no errors.

### 4.3 Pass criteria

ALL of the following must be true:
- `--health` fails on each of: unbuilt binding, stale build, unreachable framework root, body drift — and passes on a healthy tree. Proven by tests, not by reading the code.
- `init --check` flags a May-shape launcher and an unreachable `DEFAULT_ROOT`, and passes a fresh template.
- Both consumers report in sync and healthy from their own root, with an unpushed commit.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [ ] Remove temp scripts/artifacts created during execution
- [ ] Snapshots: refs only (consumer `HEAD` before Step 8, recorded in the status entry); nothing under `AgentReports/Snapshots/`
- [ ] **Reports check (CLAUDE.md §10):** non-trivial phase close → `snapshot` Report only if §3 grows past its plan; otherwise the status entry suffices. List any here.
- [ ] **Document Sync sweep (Hard Rule #11):** `dispatcher.js`, the launchers and `init.mjs` are cited by `CLAUDE.md` §11.15 / §9.1 and three READMEs (updated in Steps 5–6); run `doc-claims`, and `init --check` against both consumers once more after the final `CLAUDE.md` edit.
- [ ] No orphan branches, no leftover debug code

---

## 6. Status Update Entry

```
### YYYY-MM-DD HH:MM±hh:mm — Phase 26: Consumer health at boot

**State:**
- Phase: 26 closed
- Build: CLEAN
- Tests: PASS — dispatcher N/N · nissth-init N/N · doc-claims exit 0
- Active plan: ImplementationPlans/Phase_26_Consumer_Health_At_Boot.md
- DBL refs: none
- Bridge reports: none
- Blockers: none — both consumers committed, not pushed (user's step; the backend push deploys)

**Report:**
- [condensed from §1 findings]

**Executed:**
- [condensed from §3]

**Verified:**
- [§4 results + freshness statement]
- Doc sync: updated: CLAUDE.md §1/HR#4/§11.5/§9.1, Tools/nissth-bridge/README.md, consumer-launcher/README.md, Tools/nissth-init/README.md; consumers re-synced
- Reports: none | [snapshot]

**Issues:**
- [or "none"]

**Next:**
- Author Phase_27 (ledger and plan contract — harvest Report rows 3–7, 15).
```
