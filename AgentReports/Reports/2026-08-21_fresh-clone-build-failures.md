---
report_type: incident
title: Two pre-existing defects that only surface on a fresh clone — committed tsbuildinfo and CRLF-sensitive frontmatter parser
authored: 2026-08-21 by Claude (Opus 5)
last_updated: 2026-08-24 by Claude (Opus 5)
related_status_entries:
  - 2026-08-21 — Phase 10 authored + approved; pre-commit record
  - 2026-08-21 — Phase 10 VERIFY FAIL — fresh-clone build defects
  - 2026-08-24 04:16 — Readiness inspection for co-worker handoff; Phase 11 remediation scope recorded
related_plans:
  - Phase_10_Public_Preview_Branch
covers:
  - Bindings/Postgres — build reproducibility
  - Bindings/Expo — test-fixture line-ending sensitivity
  - repo-wide — git line-ending policy
supersedes:
  - none
---

# Fresh-clone build failures — Phase 10 verification incident

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
| 1a | `git rm --cached Bindings/Postgres/tsconfig.tsbuildinfo` | 1 file untracked | none — it is generated output |
| 1b | Correct the ignore pattern to `*.tsbuildinfo` in both `Bindings/Postgres/.gitignore` and `Bindings/Expo/.gitignore` | 2 files | none |
| 2a | Add a repo-root `.gitattributes` pinning `* text=auto eol=lf` (at minimum for `*.md` fixtures) | 1 new file | low — normalizes the working tree on next checkout |
| 2b | Make the **test-support** frontmatter regexes CRLF-tolerant | 4 files: `tests/integration/_support.ts:67,80`, `tests/contract/SchemaValidation.test.ts:48`, `tests/unit/StaleFlipper.test.ts:51,78,122`, `tests/unit/ReportWriter.test.ts:51,52` — scope corrected 2026-08-24; production parsers already tolerant | low |

Recommendation: do **both** 2a and 2b. `.gitattributes` fixes the checkout; the tolerant regexes fix the tests for consumers who set their own git config. **Revised 2026-08-24:** fix 1a is the load-bearing one overall — it is a broken build, not a red test. 2b was originally called load-bearing on the assumption that production parsers shared the LF anchor; they do not.

## Follow-ups

- Phase 10 is blocked at §4.4 until the user chooses. The branch `nissth/public` exists at `c0240f9` with a clean tree (scrub verified, 0 residual references) but **would fail its own test suite for anyone who clones it**. Nothing has been pushed.
- A follow-on plan (`Phase_11_Fresh_Clone_Hardening`) should carry the fixes on `master` first, then the public branch gets re-cut from the fixed `master`. Fixing only the public branch would leave `master` — and every consumer project installing Nissth as a submodule — carrying both defects.
- The verification protocols in `CLAUDE.md` §8.1.6 / §8.2.6 should gain a clause: **a binding's suite must be validated at least once from a fresh clone or worktree**, not only from the development directory. Neither protocol currently requires this, which is why both defects survived nine phases.
- ~~Consider whether `AgentReports/Bridge/` reports and `DBL/**` artifacts parsed by production code share the LF assumption.~~ **RESOLVED 2026-08-24 — they do not.** A grep sweep of every frontmatter regex under `Bindings/` found the production parsers already CRLF-tolerant: `Bindings/Expo/src/core/StaleFlipper.ts:16` and `Bindings/Postgres/src/core/StaleFlipper.ts:16` use `/^---?
([sS]*?)?
---?
([sS]*)$/`; `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/StaleFlipper.java:30` uses `Pattern.compile("^---\s*\R", MULTILINE)`. Only test-support code is LF-anchored — `Bindings/Expo/tests/integration/_support.ts:67,80`, `tests/contract/SchemaValidation.test.ts:48`, `tests/unit/StaleFlipper.test.ts:51,78,122`, `tests/unit/ReportWriter.test.ts:51,52`. Defect #2 therefore has **no** production blast radius; remediation 2b narrows to those four test files, and 2a (`.gitattributes`) remains worth doing on its own merits.

## Revision history

- 2026-08-21 — authored during Phase 10 §4.2 verification (Claude, Opus 5).
- 2026-08-24 — Follow-up #4 resolved: production frontmatter parsers confirmed CRLF-tolerant, so defect #2 is scoped to the Expo test tree. Corrected the over-broad blast-radius claim closing the defect-#2 root-cause section, narrowed remediation row 2b to its four actual files, and revised the recommendation (1a is load-bearing, not 2b). Defect #1 root cause and remediation unchanged. `related_status_entries` extended (Claude, Opus 5).
