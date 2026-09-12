---
report_type: snapshot
title: Phase 16 — DBL Check (`Tools/dbl-check`) — closing snapshot
authored: 2026-09-13 by Claude (Opus 5)
last_updated: 2026-09-13 by Claude (Opus 5)
related_status_entries:
  - 2026-09-13 02:50 — Phase 16: DBL Check
related_plans:
  - Phase_16_DBL_Check
covers:
  - Tools/dbl-check/** · CLAUDE.md §7.3, §13 · README.md tree + DBL paragraph
supersedes:
  - none (closes audit F6 and the enforcement half of F7 in 2026-09-13_finans-consumer-init-friction-audit.md)
---

> **Snapshot Report** per CLAUDE.md §10.4(4). Phase 16 gave §7.2 ("no exceptions") and §7.3 ("MANDATORY freshness check") a mechanism: a zero-dependency validator with eleven checks, one of which turns the greenfield Phase 00 → Phase 01 hand-off into a failing exit code.

## What shipped

| Component | Path | Size |
|:---|:---|:---|
| Validator + library (`check()`, `checkArtifact()`, `parseFrontmatter()`, `globToRegExp()`, `firstCoveredFile()`) | `Tools/dbl-check/check.mjs` | ≈ 260 lines, zero deps |
| Tests | `Tools/dbl-check/test.mjs` | 21 cases; static fixtures `clean/`, `stale/`, `design-only/`; broken + git-ref cases built in temp dirs |
| Docs | `Tools/dbl-check/README.md`, `CLAUDE.md` §13 (+ one sentence in §7.3, tree row in §5), `README.md` tree row + DBL paragraph | — |

## Check table and exit semantics

| Check | Severity | Purpose |
|:---|:---|:---|
| `missing-frontmatter`, `bad-frontmatter`, `missing-key`, `type-dir-mismatch`, `bad-regenerated-format`, `crlf` | error | §7.2 contract |
| `unknown-dir`, `over-budget` | warn | §7.1 directories, §7.4 budget |
| `stale-marked` | info | surfaces §11.4 stale-flips so the agent sees what must be regenerated |
| `design-only-source-exists` | error | greenfield Phase 00 artifacts overtaken by real source (§7.6) |
| `covers-changed-since` | warn | §7.3 step 2 when `source_state` is a git ref; skipped with a note when git/ref unavailable |

Exit **0** clean (info/warn only) · **1** any error (any finding with `--strict`) · **2** no `DBL/` or bad flags. `--json` returns `{ root, scanned, artifacts, findings, notes, summary, words }`.

## The load-bearing check

`design-only-source-exists` is why this phase and Phase 00's greenfield mode fit together. A greenfield consumer's Phase 00 writes artifacts from the SDD with `source_state: design-only …`. The moment Phase 01 creates a file under any `covers` glob, this check fails with exit 1 — so the "Phase 01 §5 must regenerate the design-only artifacts" rule is no longer something the agent has to remember. Verified live: the FinansYönetimApp checkout scans 11 artifacts clean today and will fail 11× the moment its `app/` or `src/` appears.

## Design decisions

| Decision | Alternative rejected | Why |
|:---|:---|:---|
| YAML **subset** parser (`key: scalar`, `key:` + `- item`, quotes, free text after the first colon) | `js-yaml` dependency | Every artifact the templates and the three bindings' `StaleFlipper`s write fits the subset; anything richer is a `bad-frontmatter` finding by design. Zero deps, like the other two tools. |
| `stale-marked` is **info**, not error | error | A STALE marker is the framework working correctly; failing on it would train agents to clear markers instead of regenerating. `--strict` exists for CI-style use. |
| `covers-changed-since` skips, never fails, without git | fail | A consumer tree without git (or with a shallow clone missing the ref) must still get the other ten checks. |
| Git pathspecs use `:(glob)` magic | filter `git diff` output in JS | Lets git do the matching with the same `**` semantics the artifact author intended. |
| The design-only walk excludes `DBL/` and build dirs | walk everything | An artifact can never "cover" the DBL tree; `node_modules` walks are slow and never intended. |
| Not wired into hooks/CI | hook | Same deferral as `doc-claims` §12.4; a separate decision with its own failure modes. |

## Verification

21/21 (dbl-check) · 20/20 (init) · 32/32 (dispatcher) · 23/23 (doc-claims) · doc-claims exit 0 · Step 9: Nissth root → 0 scanned exit 0; FinansYönetimApp → 11 scanned, 0 findings, exit 0. Fresh-worktree confirmation in the follow-up status entry.

## Revision history

- 2026-09-13 by Claude (Opus 5) — initial snapshot at Phase 16 close.
