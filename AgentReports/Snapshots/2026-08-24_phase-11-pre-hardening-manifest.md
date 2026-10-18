# Phase 11 — Pre-Hardening Rollback Manifest

> Hard Rule #9 artifact. Written at Step 1 of `ImplementationPlans/Phase_11_Fresh_Clone_Hardening.md` §3.1,
> before any change. Nothing in Phase 11 is committed until §4 passes, so `master`'s tip is the
> single restore point for the whole phase.

- **Authored:** 2026-08-24 by Claude (Opus 5)
- **Plan:** Phase_11_Fresh_Clone_Hardening
- **Branch:** master

## Baseline state

| Item | Value |
|:---|:---|
| `master` tip SHA | `184e3033a5e358e590b732e092e10cbb815cb2d3` |
| `git config core.autocrlf` | `true` |
| `Bindings/Postgres/tsconfig.tsbuildinfo` | **tracked**, blob `317ff668b9db9402d830d39ccc3dfd9d349336b1` |
| CRLF files in index (excl. `Axiom/`) | 0 |
| Repo-root `.gitattributes` | absent |
| `nissth/public` | `c0240f9` — out of scope, must not move |

## Measured suite baselines (development directory, 2026-08-24)

| Suite | Result |
|:---|:---|
| Dispatcher (`node --test`) | 32/32 pass |
| Expo (`npm test`) | 58/58 pass, 13 suites |
| Postgres (`npm test`) | 107 pass / 18 skip / 125 total, 11 of 16 suites |
| Spring Boot (`./mvnw clean test`) | 104/104, BUILD SUCCESS |

These are the numbers §4.3 requires the fresh worktree to reproduce exactly.

## Restore recipes

**Whole phase (preferred — nothing is committed until §4 passes):**
```
git checkout -- CLAUDE.md Bindings/Expo/.gitignore Bindings/Postgres/.gitignore Bindings/Expo/tests/
rm -f .gitattributes
git reset HEAD Bindings/Postgres/tsconfig.tsbuildinfo   # restores tracking (undoes rm --cached)
git status --short                                       # expect: only plan/manifest/StatusUpdate
```

**Per step:**

| Step | What it changed | Restore |
|:---|:---|:---|
| 2 | `git rm --cached Bindings/Postgres/tsconfig.tsbuildinfo` | `git reset HEAD Bindings/Postgres/tsconfig.tsbuildinfo` — file was never deleted from disk |
| 3 | `.tsbuildinfo` → `*.tsbuildinfo` in 2 `.gitignore` files | `git checkout -- Bindings/Expo/.gitignore Bindings/Postgres/.gitignore` |
| 4 | new `.gitattributes` | `rm .gitattributes` — untracked new file, nothing to restore |
| 5 | 7 regex sites in 4 Expo test files | `git checkout -- Bindings/Expo/tests/` |
| 6 | `CLAUDE.md` §8.1.6 / §8.2.6 / §8.3.6 | `git checkout -- CLAUDE.md` |

**Verification worktree:** `git worktree remove --force <path>` (resolve via `git worktree list`).
Do NOT remove the `nissth/public` worktree — Phase 10 owns it.

**If a checkout after Step 4 renormalized line endings unexpectedly:** `rm .gitattributes && git checkout -- .`
restores working-tree endings to whatever `core.autocrlf` dictates. §1.3 row 5 measured 0 CRLF files in the
index, so this should not arise.
