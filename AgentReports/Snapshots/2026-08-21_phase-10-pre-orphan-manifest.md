# Pre-Execution Rollback Manifest — Phase 10 (Public Preview Branch)

**Created:** 2026-08-21 by Claude (Opus 5)
**Plan:** `ImplementationPlans/Phase_10_Public_Preview_Branch.md`
**Purpose:** Hard Rule #9 rollback artifact for the orphan-branch build.

---

## Baseline

| Field | Value |
|:---|:---|
| `master` SHA at execution start | `9f4b0c8c1d56e541c8b0de160e831bd3b20e732f` |
| Branch checked out | `master` |
| Working tree | clean (only untracked file: the Phase 10 plan itself) |
| Tracked files total | 379 (231 excluding `Axiom/`) |
| git version | 2.54.0.windows.1 |

## Baseline test counts (§1.3 action 6 — measured, authoritative)

| Suite | Command | Result |
|:---|:---|:---|
| Dispatcher | `node --test Tools/nissth-bridge/test.mjs` | 32 pass / 0 fail |
| Postgres | `npm test` in `Bindings/Postgres` | 107 pass / 18 skip / 125 total |
| Expo | `npm test` in `Bindings/Expo` | 58 pass / 13 suites |
| Spring Boot | `mvn clean test` in `Bindings/SpringBoot` | 104 pass / 0 fail / BUILD SUCCESS |

## Paths omitted from the public branch (NOT deleted from `master`)

| Path | Tracked on `master` | Recoverable |
|:---|:---|:---|
| `Axiom/` | 148 files, 2.2 MB | yes — 0 untracked, 0 ignored; all committed |
| `Süprüz Software Requirements Specification Report (3).pdf` | yes | yes |
| `Software Design Document Template.pdf` | yes | yes |
| `.claude/settings.local.json` | yes | yes |
| `AgentReports/Reports/2026-05-23_unihub-consumer-install-decisions.md` | yes | yes |

**`Axiom/` is the user's live reference material for developing Nissth (directive, 2026-08-21).** It is omitted from the public branch's tree only. It is never deleted from `master` and never removed from the primary working directory — not even transiently. All removals happen inside a scratchpad git worktree.

## Isolation design

The orphan branch is built via `git worktree add --orphan -b nissth/public <scratchpad>/nissth-public`. The primary working directory at `C:\Users\admin\Desktop\Nissth` stays on `master` for the entire phase. The in-place `git checkout --orphan` variant is forbidden by §3.2 precisely because it would empty `Axiom/` from disk between Steps 5 and 11.

## Restore recipes

| Situation | Command |
|:---|:---|
| Abandon the phase entirely | `git worktree remove --force "<scratchpad>/nissth-public" && git branch -D nissth/public` |
| `Axiom/` integrity gate failed (should be structurally impossible) | `git checkout master -- Axiom/` from the primary directory, then confirm `find Axiom -type f \| wc -l` = 148 |
| Undo the Step 3 commit on `master` | `git reset --hard 9f4b0c8c1d56e541c8b0de160e831bd3b20e732f` (only safe while unpushed) |

Nothing in this phase is pushed. Nothing rewrites `master`'s history.

## Integrity gate

Checked immediately after §3.1 Step 5 and again at Step 11, in the **primary** working directory:

```
git status --porcelain --ignored Axiom/    # must be empty
find Axiom -type f | wc -l                 # must be 148
git ls-files Axiom | wc -l                 # must be 148
```

Any deviation is an immediate FAIL under §4.4 and outranks every other pass criterion.
