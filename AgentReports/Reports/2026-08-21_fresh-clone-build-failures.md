---
report_type: incident
title: Two pre-existing defects that only surface on a fresh clone — committed tsbuildinfo and CRLF-sensitive frontmatter parser
authored: 2026-08-21 by Claude (Opus 5)
last_updated: 2026-08-24 by Claude (Opus 5)
related_status_entries:
  - 2026-08-21 — Phase 10 authored + approved; pre-commit record
  - 2026-08-21 — Phase 10 VERIFY FAIL — fresh-clone build defects
  - 2026-08-24 04:16 — Readiness inspection for co-worker handoff; Phase 11 remediation scope recorded
  - 2026-08-24 07:43 — Phase 11 authored (Approved: pending)
  - 2026-08-24 08:05 — Phase 11 §3 complete; pre-commit record (verification branch)
related_plans:
  - Phase_10_Public_Preview_Branch
  - Phase_11_Fresh_Clone_Hardening
covers:
  - Bindings/Postgres — build reproducibility
  - Bindings/Expo — test-fixture line-ending sensitivity
  - repo-wide — git line-ending policy
supersedes:
  - none
---

# Fresh-clone build failures — Phase 10 verification incident

> **STATUS: RESOLVED 2026-08-24.** All four remediations landed on `master` as `85f79ae` via
> `Phase_11_Fresh_Clone_Hardening`. Both defects were re-verified fixed from a fresh `git worktree`:
> Postgres 107 pass / 18 skip (was 106 / 1 fail) and Expo 58/58 (was 57 / 1 fail), both matching the
> development-directory baseline exactly. Phase 10 is unblocked and `nissth/public` can be re-cut.

## Summary

Phase 10's §4.2 verification ran the four binding suites against a **fresh git worktree** of the new `nissth/public` branch — the first time in this repo's life that the suites have run against a tree checked out from scratch rather than the author's long-lived working directory. Two suites failed. **Neither failure was caused by the Phase 10 scrub.** Both are pre-existing defects that the primary working directory has been masking, and both would hit any person who clones the repository — which is precisely the audience the public branch exists to serve.

| Suite | Baseline (primary dir) | Fresh worktree | Verdict |
|:---|:---|:---|:---|
| Dispatcher | 32/32 | 32/32 | PASS |
| Spring Boot | 104/104 | 104/104 | PASS — includes the scrubbed `com.example.reservation` assertion |
| PostgreSQL | 107 pass / 18 skip | **106 pass / 1 fail** | FAIL — defect #1 |
| Expo | 58/58 | **57 pass / 1 fail** | FAIL — defect #2 |

This is the framework's own false-CLEAN trap (Hard Rule #10) firing against the framework itself. The verification protocol worked: §4.1's insistence on a fresh `npm ci` in an isolated worktree — rather than reusing the installed tree — is what exposed both defects.

## Timeline

| When | What |
|:---|:---|
| 2026-08-21, pre-execution | §1.2 action 6 measured the baseline in the **primary** working directory: all four suites green (32 / 104 / 58 / 107+18). |
| 2026-08-21, §3 Steps 4–10 | Orphan branch built in a scratchpad worktree; 227 files committed as `c0240f9`. |
| §4.2, check 2 | Postgres `npm ci` → `npm test`: `tests/contract/SecretRedaction.test.ts` fails on its 7th assertion. Six redaction assertions pass — **no password ever leaked**; the failure is a spawn error. |
| §4.2, diagnosis | `dist/` held 13 `.js` files; `dist/core/JsonCommandParser.js` absent, so the spawned CLI died with `MODULE_NOT_FOUND`. `npm run build` exited 0 without emitting it. |
| §4.2, causation proof | Deleted `tsconfig.tsbuildinfo` + `dist/`, rebuilt → 16 `.js` files, suite returns **107 pass / 18 skip**, matching baseline exactly. |
| §4.2, check 2 (cont.) | Expo `npm ci` → `npm test`: `tests/integration/RouteLens.it.test.ts` fails. Expo sources were **not touched by the scrub** — byte-identical to `master`. |
| §4.2, diagnosis | `existsSync(dblPath)` passes but `readDBLFrontmatter()` returns `null`. Fixture in the fresh worktree has CRLF endings; the same file in the primary directory has LF. |
| §4.4 | STOP invoked. Cleanup (§5) and Step 12 not executed. Worktree left in place for fix-forward. |

## Root cause

### Defect #1 — `Bindings/Postgres/tsconfig.tsbuildinfo` is committed to git

`Bindings/Postgres/tsconfig.json` sets `"incremental": true`, so `tsc` writes `tsconfig.tsbuildinfo` recording which outputs it has already emitted. That file is **tracked in git** (the only tracked build artifact in the repo). On a fresh clone or worktree, `dist/` is absent (correctly gitignored) but the build-info file arrives from the index claiming the outputs already exist. `tsc` therefore skips emitting most of them, exits 0, and leaves a partially-built `dist/`.

The ignore rule that should have prevented this is wrong:

```
Bindings/Postgres/.gitignore:3      .tsbuildinfo
actual filename on disk             tsconfig.tsbuildinfo
```

`.tsbuildinfo` matches a file *named* `.tsbuildinfo`, not one *ending in* it. The pattern never matched, so the artifact was never ignored and got committed.

`Bindings/Expo/.gitignore` carries the identical wrong pattern. No `tsbuildinfo` is currently committed there, so Expo is clean today — but the gap is latent and will bite the moment one gets staged.

**Why the primary directory hides it:** it holds a complete `dist/` from months of successful incremental builds, so the stale build-info is harmless there.

### Defect #2 — LF-only frontmatter parser meets a CRLF checkout

`Bindings/Expo/tests/integration/_support.ts:80`:

```ts
const m = text.match(/^---\n([\s\S]*?)\n---\n/);
if (!m) return null;
```

The regex requires LF. The repo has `core.autocrlf=true` and **no `.gitattributes`**, so every text file — including `tests/fixture/DBL/APIIndex/routes.md` — is materialized with CRLF on a fresh Windows checkout. The regex fails, `readDBLFrontmatter` returns `null`, and the test dereferences it.

**Why the primary directory hides it:** those fixture files were originally written by an agent with LF endings and have never been re-checked-out. Git has been warning about this for months on every `git add` (`LF will be replaced by CRLF the next time Git touches it`) — the warning was accurate and unheeded.

**Scope, corrected 2026-08-24.** This paragraph originally claimed the defect was not confined to tests, and that any component parsing DBL frontmatter (§7.2) or Bridge reports (§11.3) was exposed. A grep sweep of every frontmatter regex under `Bindings/` disproved it — the production parsers are already CRLF-tolerant (see Follow-ups). The LF assumption lives **only** in Expo test-support code. The failing test is the defect, not a symptom of a wider one.

## Remediation

None applied. Both fixes are outside Phase 10's approved §3 step list, and §3.2 forbids binding source changes beyond the two named scrubs. Per §4.4 the decision is the user's.

| # | Fix | Files | Risk |
|:---|:---|:---|:---|
| 1a | **APPLIED** `85f79ae` — `git rm --cached Bindings/Postgres/tsconfig.tsbuildinfo` (index only; file kept on disk) | 1 file untracked | none — it is generated output |
| 1b | **APPLIED** `85f79ae` — pattern corrected to `*.tsbuildinfo` at line 3 of both `Bindings/Postgres/.gitignore` and `Bindings/Expo/.gitignore`; `git check-ignore -v` confirms the new rule matches | 2 files | none |
| 2a | **APPLIED** `85f79ae` — repo-root `.gitattributes` added: `* text=auto eol=lf` baseline, plus `*.cmd`/`*.bat text eol=crlf` and `binary` for pdf/png/jpg/jar | 1 new file | low — and lower than estimated: the index was measured uniformly LF (0 CRLF files outside `Axiom/`) before the file was added, so nothing renormalized |
| 2b | **APPLIED** `85f79ae` — all LF-anchored **read** regexes in the four Expo test-support files made `?
`-tolerant. Count correction: the sites number **8**, not 7 — the enumeration was always right (`_support.ts:67,80`; `SchemaValidation.test.ts:48`; `ReportWriter.test.ts:51,52`; `StaleFlipper.test.ts:51,78,122` = 8 lines), only the summed label was wrong. The write-side literal at `StaleFlipper.test.ts:23` is deliberately left emitting LF | 4 files | low |

Recommendation: do **both** 2a and 2b. `.gitattributes` fixes the checkout; the tolerant regexes fix the tests for consumers who set their own git config. **Revised 2026-08-24:** fix 1a is the load-bearing one overall — it is a broken build, not a red test. 2b was originally called load-bearing on the assumption that production parsers shared the LF anchor; they do not.

## Follow-ups

- ~~Phase 10 is blocked at §4.4 until the user chooses.~~ **RESOLVED 2026-08-24** — user chose fix-forward; Phase 11 landed all four remediations on `master` as `85f79ae`. Phase 10 is unblocked and `nissth/public` is to be re-cut from the fixed `master`. Original text follows for the record: The branch `nissth/public` exists at `c0240f9` with a clean tree (scrub verified, 0 residual references) but **would fail its own test suite for anyone who clones it**. Nothing has been pushed.
- ~~A follow-on plan (`Phase_11_Fresh_Clone_Hardening`) should carry the fixes on `master` first~~ **DONE 2026-08-24** — authored, approved, executed, verified from a fresh worktree. Original text: a follow-on plan should carry the fixes on `master` first, then the public branch gets re-cut from the fixed `master`. Fixing only the public branch would leave `master` — and every consumer project installing Nissth as a submodule — carrying both defects.
- ~~The verification protocols in `CLAUDE.md` §8.1.6 / §8.2.6 should gain a clause~~ **DONE 2026-08-24** — the clause landed in all three protocols (§8.1.6 item 5, §8.2.6 item 6, §8.3.6 item 6), each tailored to its stack's failure mode, and all three Freshness statement templates gained a directory-naming slot. Original text: the protocols should gain a clause: **a binding's suite must be validated at least once from a fresh clone or worktree**, not only from the development directory. Neither protocol currently requires this, which is why both defects survived nine phases.
- ~~Consider whether `AgentReports/Bridge/` reports and `DBL/**` artifacts parsed by production code share the LF assumption.~~ **RESOLVED 2026-08-24 — they do not.** A grep sweep of every frontmatter regex under `Bindings/` found the production parsers already CRLF-tolerant: `Bindings/Expo/src/core/StaleFlipper.ts:16` and `Bindings/Postgres/src/core/StaleFlipper.ts:16` use the CRLF-tolerant form `/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/` (quoted correctly here; an earlier revision of this line embedded literal CR bytes instead of the escape text and split the sentence across four lines — repaired 2026-08-24); `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/StaleFlipper.java:30` uses `Pattern.compile("^---\s*\R", MULTILINE)`. Only test-support code is LF-anchored — `Bindings/Expo/tests/integration/_support.ts:67,80`, `tests/contract/SchemaValidation.test.ts:48`, `tests/unit/StaleFlipper.test.ts:51,78,122`, `tests/unit/ReportWriter.test.ts:51,52`. Defect #2 therefore has **no** production blast radius; remediation 2b narrows to those four test files, and 2a (`.gitattributes`) remains worth doing on its own merits.

## Revision history

- 2026-08-21 — authored during Phase 10 §4.2 verification (Claude, Opus 5).
- 2026-08-24 — Follow-up #4 resolved: production frontmatter parsers confirmed CRLF-tolerant, so defect #2 is scoped to the Expo test tree. Corrected the over-broad blast-radius claim closing the defect-#2 root-cause section, narrowed remediation row 2b to its four actual files, and revised the recommendation (1a is load-bearing, not 2b). Defect #1 root cause and remediation unchanged. `related_status_entries` extended (Claude, Opus 5).
- 2026-08-24 — **Incident resolved.** All four remediations applied on `master` as `85f79ae` via `Phase_11_Fresh_Clone_Hardening`. Re-verified from a fresh `git worktree`: Postgres 107 pass / 18 skip (was 106 / 1 fail), Expo 58/58 (was 57 / 1 fail), Dispatcher 32/32, Spring Boot 104/104 — all four equal to the development-directory baseline, and `Bindings/Postgres/dist/` emitted 16 `.js` files on the fresh tree (the stale `tsbuildinfo` had capped it at 13). Status banner, remediation table, and Follow-ups #1-#3 updated. Two housekeeping fixes to this file itself: the Follow-up #4 sentence had literal CR bytes embedded where `\r?\n` was meant, splitting it across four lines — repaired; and the whole file was normalized CRLF -> LF to match the new `.gitattributes` policy (Claude, Opus 5).
