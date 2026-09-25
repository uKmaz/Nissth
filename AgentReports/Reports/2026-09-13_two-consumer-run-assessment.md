---
report_type: audit
title: Nissth under two concurrent consumers (ExampleDesktopApp + ExampleFinanceApp) — what held, what leaked, what to build next
authored: 2026-09-13 by Claude (Opus 5)
last_updated: 2026-09-13 by Claude (Opus 5)
related_status_entries:
  - 2026-09-13 08:22 — Session close: ExampleDesktopApp consumer run (Phases 00–06) and what it fed back
  - 2026-09-13 08:30 — Two-consumer audit: ExampleFinanceApp digest linked; Next re-ordered
  - 2026-09-13 08:35 — Assessment Report saved
related_plans:
  - none
covers:
  - framework process (Loop, HR#11/#12, plan template, DBL, Reports)
  - Tools/nissth-init, Tools/dbl-check, Bindings/Expo
  - multi-session / multi-consumer operation
supersedes:
  - none
---

# Nissth under two concurrent consumers — assessment

## Context
On 2026-09-13 the framework ran three repos at once: **Nissth** itself (Phases 15–18), **ExampleDesktopApp** (C#/.NET 10/WPF/SQLite, `--stack none`, Phases 00–06 closed, 165 tests, driven from the Nissth checkout's session), and **ExampleFinanceApp** (Expo SDK 57 + expo-sqlite/Drizzle, Expo binding, Phases 00–02 closed, Phase 03 at 18/19 steps, 175 tests, driven from its own session). Roughly thirteen phases in one day. This is the first time the framework carried real load. Companion document: `ExampleFinanceApp/AgentReports/Reports/2026-09-13_nissth-feedback-digest.md` (ten open items, adopted into `Next` at 08:30).

## What held

| Area | Evidence |
|:---|:---|
| The Loop under load | Every phase close in all three repos cites a produced artifact (TRX / Jest report / init suite) **and** a fresh `git worktree` run; zero `Verified: FAIL` entries; the per-phase user approval gate (HR#12) was never bypassed |
| Resumability | Both consumer sessions were saved and resumed cold from their append-only ledgers alone (`**Next:**` was sufficient every time); no chat history was needed |
| Feedback loop | Three consumer findings (greenfield DBL mode, no DBL validator, `Tests/` collision) became framework Phases 15–18 within hours of being hit |
| Structural guards | `dbl-check` caught real drift the day it shipped (WAVs under `app-theme.md`'s `covers`, ExampleDesktopApp Phase 05); `LayerBoundaryTests` forced a Phase 06 relocation instead of letting a layering violation ship; §7.4 word budget triggered three sensible splits |
| `--stack none` | A consumer with no binding still got full value from process + DBL + `dbl-check`; its SDD §11 verification protocol (dotnet clean build → TRX → worktree → PowerShell UI Automation drills) substituted for a binding without framework changes |

## What leaked

| # | Leak | Cost observed | Root cause |
|:---|:---|:---|:---|
| L1 | **Ceremony per phase** — plan (2–3k words), pre-flight, hand-written DBL regeneration scripts, budget splits, snapshot Report, two status entries, pin commit, worktree | ≈ one third of each phase is bookkeeping; six ExampleDesktopApp phases ≈ 8 h wall clock | §7.4 "hand-maintained for now": DBL auto-regeneration under `Tools/` is still unbuilt — the single largest token leak |
| L2 | **Plans are not mechanically checked against DBL** | ExampleDesktopApp Phase 06 placed classes where `layers.md` forbids tests from reaching; caught at execution, not at approval; relocation recorded as a deviation | `_TEMPLATE.md` §1.1 relies on authoring discipline; no check reads a plan's file list against DependencyMap rules |
| L3 | **Two sessions, one framework checkout** | A `filter-branch` rewrite at 02:14 (author identity) ran while another session had the same checkout open; commits happened to be serialised, so nothing diverged | No lock, no "another session is active" signal; §9.1 never told the ExampleDesktopApp session to leave the framework folder (init handoff line missing) |
| L4 | **Bridge first-contact bugs** (digest B1–B4) | A restore step at every ExampleFinanceApp phase close (false STALE-flips, YAML re-wrap breaking `dbl-check`) | Bindings were verified against their own fixtures, never against a live consumer before 2026-09-13 |
| L5 | **Consumer feedback not visible from the framework ledger** | The Finans digest with ten open items was unreferenced in Nissth's `StatusUpdate.md` until 08:30 | The banner says "report back to the Nissth repo" but no step in a consumer's phase close writes a pointer into Nissth |
| L6 | **Windows agent-environment friction** | Bash heredocs mangling `\a`/`\u`/apostrophes (both sessions), no TTY for drizzle-kit, UIA quirks (owned popups, overflow flyout) | Not Nissth's, but Nissth agents run there; the workarounds (Write-tool scripts, fake-TTY driver) are only recorded in consumer gotchas |

## Verdict
The **discipline half** of Nissth is proven: the Loop, append-only ledgers, plan gates, Reports and DBL freshness survived three repos and two operators' worth of pace with no lost state. The **tooling half** is where the remaining cost sits: DBL regeneration, plan-vs-DBL checking, and Bridge hardening. `--stack none` showed the process alone is worth adopting; the bindings still have to earn their place on a second consumer each.

## Recommended build order (feeds `Next`)
1. **Phase 19 — Bridge flip + YAML** (digest B1, B2/C2): stop false STALE-flips; write frontmatter verbatim; `dbl-check` regression test. Removes a restore step from every Expo phase close.
2. **Phase 20 — init handoff + doc fixes** (L3, digest A3/A5/B5/B6): `nissth-init` ends with "open Claude Code in `<target>`"; `README`/§9.1 say it; §11.5 flag spelling and `--json-stdin`; Metro bundle check in §8.2.6.
3. **Phase 21 — parsers + policy** (digest B3/B4/A4).
4. **Then the leak that matters most — DBL regeneration tooling (L1)**: even a per-stack "list public surface + diff against `covers`" generator that proposes the frontmatter and file lists would halve the close-out cost. Pair it with a **plan linter (L2)**: read §3's target paths against `DependencyMaps/*.md` forbidden rows before `Approved:` is stamped.
5. **Consumer → framework pointer (L5)**: a one-line convention — a consumer's phase-close status entry that records framework friction also appends a `### … — consumer feedback: <project>` stub to Nissth's ledger, or `nissth-bridge` grows a `feedback` note tool. Cheap; prevents the silent-digest failure.
6. **Session hygiene (L3)**: one Nissth session at a time; consumers treat the framework root as read-only; any history rewrite is announced in the ledger *before* it runs.

## Revision history
- 2026-09-13 — authored at the user's request after the ExampleDesktopApp session close and the two-consumer audit.
