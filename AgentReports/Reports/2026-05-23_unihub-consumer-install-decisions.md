---
report_type: decision
title: UniHub consumer install — Option A (per-repo) + local-checkout wiring
authored: 2026-05-23 by Claude (Opus 4.7) with user (Emre Uçmaz)
last_updated: 2026-05-23 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-23 01:42 — First real consumer install (UniHub) — Phase 09 recipe field test
related_plans:
  - none — pre-Phase-00 install decisions for the UniHub consumer adoption
covers:
  - the two architecture decisions that shaped Nissth's first real consumer install
  - trade-off rationale for future multi-repo or first-time consumer adopters
supersedes:
  - none
---

> **Decision Report** per CLAUDE.md §10.4(2). Documents two architecture decisions made jointly with the user during the UniHub consumer install on 2026-05-23, so future consumer installs (Süprüz at Phase 10, or any later project) can re-use the rationale instead of re-litigating.

## Context

UniHub is a brownfield product (~15k LOC) split across **two independent git repositories**: `unihub-backend` (Spring Boot 3.5.3 / Java 21 / PostgreSQL 16) and `UniHub-Frontend` (Expo SDK 54 / React Native 0.81 / React Navigation 6). The backend has its own GitHub Actions CI/CD pipeline deploying to a VPS at `api.unihub.tr`; the frontend is its own independent repo. They share a parent folder `C:\Users\admin\Desktop\UniHub\src\` on disk but **no umbrella git repo**.

Nissth's Phase 09 consumer-launcher recipe (delivered 2026-05-18) assumes a **single repo root** with `Tools/Nissth/` as a git submodule. Neither assumption held for UniHub. Two decisions had to be made before bootstrap could proceed.

## Options considered

### Decision 1 — Install topology

| Option | Description | Pros | Cons |
|:---|:---|:---|:---|
| **A — Two independent Nissth installs** *(chosen)* | Each repo becomes its own Nissth project: backend gets `CLAUDE.md` / `StatusUpdate.md` / `ImplementationPlans/` / `DBL/`; frontend the same. Backend uses Spring Boot + Postgres bindings; frontend uses Expo. | Respects existing repo + CI/CD boundaries. Per-binding 1:1 mapping. Matches independent deployment lifecycles. Each per-repo session's boot protocol (§1) works cleanly — auto-loaded `CLAUDE.md` is the right one for that repo. | Two ledgers, two `ImplementationPlans/` trees. Cross-repo API-contract consistency requires manual coordination. |
| B — One Nissth project in `UniHub/` wrapper | Single `CLAUDE.md` / `StatusUpdate.md` at `UniHub/` covering both. | One unified ledger; cross-stack work in one ledger. | Requires `UniHub/` itself become a git repo (so Nissth state is versioned); nested code repos inside → messy git semantics; fights existing repo structure. |

### Decision 2 — Framework wiring

| Option | Description | Pros | Cons |
|:---|:---|:---|:---|
| **Local-checkout via `NISSTH_FRAMEWORK_ROOT`** *(chosen)* | Point env var at the existing local Nissth checkout (`C:\Users\admin\Desktop\Nissth`). Tier-1 resolution per §11.15. Nothing added to consumer repos except control files. | Offline. Uses the exact code on the user's disk. No GitHub-remote dependency for first install. Lower-risk for a first-time consumer adopter. | Not version-pinnable. Framework root must stay at the configured path (if `Desktop\Nissth` moves, both consumer launchers break — env var must be updated). |
| Git submodule (`Tools/Nissth/`) | Canonical Phase 09 recipe: `git submodule add https://github.com/uKmaz/Nissth Tools/Nissth` in each repo. | Version-pinnable. Self-contained. Works for fresh-clone teammates. | Depends on the GitHub remote being current and accessible. Adds a submodule to each deploy repo (CI must opt in via `submodules: true` only if it needs Nissth — Nissth files are inert markdown, so CI typically doesn't need it). |

## Decision

- **Topology: Option A** — two installs, one per repo.
- **Wiring: Local-checkout** via `NISSTH_FRAMEWORK_ROOT`.

Made jointly with user via `AskUserQuestion` on 2026-05-23; both questions answered with the "Recommended" option as proposed by the agent.

## Consequences

**For UniHub:**

- Two independent `StatusUpdate.md` ledgers. Each evolves with its own repo's work. Cross-repo coordination (backend `DBL/APIIndex/*-api.md` ↔ frontend `DBL/APIIndex/backend-api-consumers.md` cross-check) is manual and lives as a step in each Phase 00's §3 list.
- Each repo's `nissth-bridge.ps1` is a **custom-adapted launcher** — the submodule path `Tools/Nissth/Tools/nissth-bridge/dispatcher.js` is replaced with `$NISSTH_FRAMEWORK_ROOT/Tools/nissth-bridge/dispatcher.js`. Both launchers default `NISSTH_FRAMEWORK_ROOT` to `C:\Users\admin\Desktop\Nissth` if unset.
- User has been warned in the per-repo `StatusUpdate.md` Bootstrap entries: moving `Desktop\Nissth` breaks the launchers unless the env var is updated.

**For Nissth (framework-improvement candidates surfaced by this install):**

- **Candidate #1 — Consumer-launcher local-checkout fallback.** The shipped `Tools/nissth-bridge/consumer-launcher/{nissth-bridge,nissth-bridge.ps1}` hardcode `$DIR/Tools/Nissth/Tools/nissth-bridge/dispatcher.js` and error out if the submodule path doesn't exist. They mention `NISSTH_FRAMEWORK_ROOT` in a comment ("Override resolution (advanced)") but the shell logic doesn't actually check the env var to locate `dispatcher.js` — only the dispatcher's *internal* `findFrameworkRoot` uses it. Result: every local-checkout install has to edit the launcher (which this session did for both UniHub launchers). **Fix:** add a fallback — if `$DIR/Tools/Nissth/` is absent, check `$NISSTH_FRAMEWORK_ROOT/Tools/nissth-bridge/dispatcher.js` before failing. Low-effort, dispatcher.js untouched. *Candidate for Phase 11+.*
- **Candidate #2 — Multi-repo consumer install pattern.** `CLAUDE.md` §9.1 and `Tools/nissth-bridge/consumer-launcher/README.md` describe single-repo installs only. Option A is a real-world pattern that deserves a §9.2 sub-section documenting the Option A/B trade-off above. CLAUDE.md edits are NOT plan-exempt per HR#12, so this would require an authored plan. *Candidate for Phase 11+.*

Both are paper cuts; UniHub install succeeded despite them. No `Verified: FAIL`, no framework regression.

## Revision history

- 2026-05-23 by Claude (Opus 4.7) — initial decision capture during the first real consumer install.
