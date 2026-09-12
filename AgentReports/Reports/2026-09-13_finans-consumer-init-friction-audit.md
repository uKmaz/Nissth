---
report_type: audit
title: FinansYönetimApp consumer init — framework friction audit
authored: 2026-09-13 by Claude (Opus 5)
last_updated: 2026-09-13 by Claude (Opus 5)
related_status_entries:
  - 2026-09-13 01:20 — FinansYönetimApp consumer init: SRS + SDD authored, awaiting approval
  - 2026-09-13 01:45 — Consumer init closed; friction audit + Phase 15–17 plans authored
related_plans:
  - Phase_15_Consumer_Init_Tooling
  - Phase_16_DBL_Check
  - Phase_17_Greenfield_And_Expo_Local_Rules
covers:
  - consumer bootstrap (CLAUDE.md §9.1), consumer launchers, DBL first-population (§7.6, §8.2.9), Expo binding rules (§8.2)
supersedes:
  - none (extends the two candidates in 2026-05-23_example-consumer-install-decisions.md)
---

> **Audit Report** per CLAUDE.md §10.1. The user's instruction for this project was "improve Nissth using this app": the FinansYönetimApp init (2026-09-13, `C:\Users\admin\Desktop\FinansYönetimApp`, commits `f9750f9` + `620572c`) is Nissth's second real consumer install and first **greenfield** one. Every place where the agent had to improvise instead of operate is a framework defect by the framework's own philosophy (§2). This Report lists them, ranks them, and maps each to the plan that fixes it.

## Summary

The init succeeded — SRS/SDD, bootstrap, Phase 00 all closed green in one session — but it required nine manual improvisations. Two are repeats of candidates logged on 2026-05-23 and never fixed. None caused a `Verified: FAIL`; all are the kind of paper cut that a *third* consumer would hit identically, which is the threshold at which §2 ("agents must never explore — they must operate") says the step must become a mechanism.

## Findings

| # | Finding | Evidence | Severity | Fix |
|:---|:---|:---|:---|:---|
| F1 | **Consumer launcher templates have no `NISSTH_FRAMEWORK_ROOT` fallback.** Both `Tools/nissth-bridge/consumer-launcher/*` hard-code `Tools/Nissth/…/dispatcher.js`; the comment promises the env var but the code never checks it. Every local-checkout consumer (Example ×2, Finans) hand-edited the launcher. | Example Report candidate #1 (2026-05-23), still open; Finans launchers written from scratch. | High — repeat offender | Phase 15 |
| F2 | **§9.1 step 2 is a ten-item manual copy list with no script.** Deterministic, mechanical, and done by hand every time. | consumer-launcher README steps 3–4; Finans bootstrap = 12 shell commands. | High | Phase 15 (`Tools/nissth-init`) |
| F3 | **No consumer `CLAUDE.md` variant.** The consumer gets a 1 070-line copy whose title, banner and status describe *Nissth*; the agent rewrites the head by hand. `Ultimate_Guide.md`/README say "customise the banner" without providing the text. | Finans `CLAUDE.md` head replaced via `tail -n +6`. | Medium | Phase 15 (banner template) |
| F4 | **`StatusUpdate.md` preamble has no template file.** It was scraped from Nissth's own ledger with `awk`. | Finans bootstrap step. | Medium | Phase 15 (`templates/StatusUpdate.preamble.md`) |
| F5 | **Nissth's working copy carried CRLF on 219 tracked-as-LF files** (pre-Phase-11 checkout never re-normalised), so a plain `cp` of `_TEMPLATE.md`s shipped CRLF into the consumer. | `git ls-files --eol` 2026-09-13; consumer needed `sed`/renormalize. | Low (fixed in working copy this session) | Phase 15 (init normalises on copy — belt and braces) |
| F6 | **No DBL frontmatter validator.** §7.2 says "no exceptions" and §7.3 mandates a freshness check, but nothing enforces either; Phase 00 wrote a throwaway `node -e`. The same "rule followed, defect survives" pattern that motivated `doc-claims` (§12.1). | Finans `Phase_00` §4.2. | High | Phase 16 (`Tools/dbl-check`) |
| F7 | **No greenfield Phase 00 mode.** §7.6 and §8.2.9 assume source exists to scan; a new project has only an SDD. `source_state: design-only` and the "Phase 01 regenerates" `stale_when` clause were invented in the plan's §0. | Finans `Phase_00` §0, all 11 artifacts. | Medium | Phase 17 (§7.6 text) + Phase 16 (`design-only-but-source-exists` check makes it enforceable) |
| F8 | **§8.2 has no SchemaIndex row or ripple rule for in-app SQLite** (expo-sqlite + Drizzle); the table says "No SchemaIndex by default" and points at §8.1.4 "on a per-project basis". Finans borrowed §8.1.9 by hand. | Finans SDD §12, `DBL/SchemaIndex/sqlite.md`. | Medium | Phase 17 |
| F9 | **§8.2 assumes Expo Go + `npx expo start`.** Dev-client-only projects (native modules, widgets, App Intents, EAS cloud builds from a Windows host) have no command row, no verification clause, no forbidden-pattern guidance. | Finans SDD §2 D7, `_config.md`. | Low–Medium | Phase 17 |

## What went right (keep)

- HR#13 permission gate + §9 SRS/SDD stop produced exactly two user round-trips before any file was written; the user's "They are approved" / "approve Phase 00" were unambiguous.
- The Example decision Report was reusable as-is: wiring decision took one paragraph, not a re-debate (§10's purpose, demonstrated).
- `_TEMPLATE.md` fit a DBL-only phase without modification; §3.2 Forbidden kept Phase 00 from drifting into `npx create-expo-app`.
- The dispatcher's framework-root resolution (§11.15) worked first time from the consumer once the launcher pointed at it.

## Ranking and plan mapping

| Plan | Fixes | Kind | Plan-required? |
|:---|:---|:---|:---|
| **Phase 15 — Consumer init tooling** | F1, F2, F3, F4, F5 | `Tools/nissth-init` + launcher template fix | yes (`Tools/`) |
| **Phase 16 — `dbl-check`** | F6, F7 (enforcement half) | `Tools/dbl-check` | yes (`Tools/`) |
| **Phase 17 — Greenfield + Expo-local rules** | F7 (text half), F8, F9 | `CLAUDE.md` §7.6, §8.2, §9.1 | yes (`CLAUDE.md` edits are not plan-exempt per HR#12) |

Ordering rationale: 15 before 17 because §9.1 step 2's new wording must name a tool that exists; 16 independent of both.

## Follow-ups (not planned)

- The consumer's `Phase_01_M0_Skeleton` will exercise `route_scaffold`, `dependency_audit`, `expo_doctor_lens` against a real SDK 56 project — the first time the Expo binding meets an SDK it was not built against (Phase 06 targeted SDK 50–54). Expect a Phase 18 candidate.
- `Ultimate_Guide.md` consumer section should point at `nissth-init` once Phase 15 closes (doc-claims will not catch this; HR#11 sweep must).

## Revision history

- 2026-09-13 by Claude (Opus 5) — initial audit after the FinansYönetimApp init closed Phase 00.
- 2026-09-13 by Claude (Opus 5) — F1–F5 closed by Phase 15 (`48beaa7`); F6 + enforcement half of F7 closed by Phase 16 (`Tools/dbl-check`).
- 2026-09-13 by Claude (Opus 5) — F7 (text), F8, F9 delivered by Phase 17 (CLAUDE.md §7.6 greenfield, §8.2.1/3/4/5/6/9/10, §9.1; Ultimate_Guide §4.1/§6.6/§7.3). All nine findings closed.
