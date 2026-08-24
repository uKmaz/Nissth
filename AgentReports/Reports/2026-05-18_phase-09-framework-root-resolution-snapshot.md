---
report_type: snapshot
title: Phase 09 — Framework-root resolution — close snapshot
authored: 2026-05-18 by Claude (Opus 4.7)
last_updated: 2026-05-18 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-18 — Phase 09: Framework-root resolution — CLOSED
related_plans:
  - Phase_09_Framework_Root_Resolution
covers:
  - Tools/nissth-bridge/dispatcher.js (findFrameworkRoot + wire-up)
  - Tools/nissth-bridge/test.mjs (+8 cases)
  - Tools/nissth-bridge/consumer-launcher/**
  - CLAUDE.md §11.15 (framework-root resolution paragraph appended)
supersedes:
  - none
---

# Phase 09 close snapshot — Framework-root resolution

Small additive phase. Makes the unified dispatcher consumer-project-aware: a project that installs Nissth as a `Tools/Nissth/` git submodule can now invoke the dispatcher and have it find `Bindings/` under the submodule rather than requiring vendoring.

The phase ships one new exported function in `dispatcher.js`, eight new tests, a consumer-launcher template directory, and one paragraph of `CLAUDE.md` §11.15 prose. No binding source touched.

## What shipped

| Artifact | Change | Lines |
|:---|:---|:---|
| `Tools/nissth-bridge/dispatcher.js` | New `findFrameworkRoot(repoRoot)` export; `DispatchError` extended with optional `errorCode`; `runDispatcher` rewired to resolve framework root before discovering manifests; no-bindings error message updated with three-tier resolution hint | +52 / −5 |
| `Tools/nissth-bridge/test.mjs` | 8 new test cases (6 from §3.1 Step 4 + 2 bonus end-to-end `runDispatcher` cases) | +180 / 0 |
| `Tools/nissth-bridge/consumer-launcher/nissth-bridge` | NEW — POSIX launcher template targeting `Tools/Nissth/Tools/nissth-bridge/dispatcher.js` | 32 |
| `Tools/nissth-bridge/consumer-launcher/nissth-bridge.ps1` | NEW — PowerShell equivalent | 27 |
| `Tools/nissth-bridge/consumer-launcher/README.md` | NEW — consumer install recipe (`git submodule add ... Tools/Nissth`, copy launchers + framework files, init StatusUpdate.md) | ~110 |
| `Tools/nissth-bridge/README.md` | New "Framework-root resolution (Phase 09+)" sub-section after "Discovery model" | +30 |
| `CLAUDE.md` §11.15 | Appended one paragraph describing the resolution order + the consumer-launcher pointer | +5 lines |

## Resolution order — the three tiers

```
1. NISSTH_FRAMEWORK_ROOT env var  (explicit; absolute path with Bindings/)
   ↓ (if unset)
2. <repoRoot>/Tools/Nissth/        (submodule convention)
   ↓ (if absent)
3. <repoRoot>                       (fallback — Nissth's own dogfooding)
```

The dispatcher distinguishes **repo root** (where `CLAUDE.md` lives; where Bridge reports get written) from **framework root** (where `Bindings/` lives; where the tool catalog comes from). For Nissth developing itself, the two are the same — fallback tier (3) wins. For consumer projects with a submodule install, they're different — tier (2) wins. For one-off setups (developing the framework against a checkout that isn't a submodule), tier (1) wins.

**Error semantics for tier 1 mismatches.** If `NISSTH_FRAMEWORK_ROOT` is set to a path that exists but doesn't contain `Bindings/`, the dispatcher exits 2 with `error_code: invalid_framework_root` and a message naming the missing subdirectory. The system does NOT silently fall through to tier 2 — typos and stale `.envrc` entries should surface, not be hidden by the next-tier resolver.

## Verification

| Check | Status | Detail |
|:---|:---|:---|
| Dispatcher tests | ✅ 32/32 | `node --test` in 127ms. 24 baseline + 8 new. |
| Phase 05 regression | ✅ 104/104 | BUILD SUCCESS at `2026-05-18T20:52:19+03:00`; 10.608s. |
| Phase 06 regression | ✅ 51/51 | 12 suites, 8.791s. |
| Phase 07 regression | ✅ 76 pass / 18 skip | 9 suites passed + 5 skipped; 5.684s. |
| Nissth dogfooding (`./nissth-bridge --list-bindings`) | ✅ unchanged | Returns `expo, postgres, spring-boot` (fallback tier wins because Nissth has no submodule of itself) |
| `./nissth-bridge --list-tools \| wc -l` | ✅ 14 | Unchanged from Phase 08 |

## The 8 new test cases

| # | Case | What it proves |
|:---|:---|:---|
| 1 | `findFrameworkRoot`: env-var path valid | Tier 1 happy path |
| 2 | `findFrameworkRoot`: env-var path lacks `Bindings/` | DispatchError(exit 2, `invalid_framework_root`) — explicit failure rather than silent fall-through |
| 3 | `findFrameworkRoot`: submodule convention `<repoRoot>/Tools/Nissth/` exists | Tier 2 happy path |
| 4 | `findFrameworkRoot`: fallback (no env, no submodule) | Tier 3 — Nissth's own dogfooding case |
| 5 | `findFrameworkRoot`: env var beats submodule (precedence) | Tier 1 wins even when tier 2 would resolve |
| 6 | `findFrameworkRoot`: submodule beats fallback | Tier 2 wins when tier 1 is unset and both tier 2 + tier 3 paths have `Bindings/` |
| 7 | `runDispatcher`: env-var routes `--list-bindings` to env-var target's tools (Nissth's own bindings invisible) | End-to-end through the run loop; tier 1 isolates a consumer project completely from the framework checkout |
| 8 | `runDispatcher`: invalid env var exits 2 with the expected error | End-to-end error path |

Cases 7 + 8 are the "bonus" cases beyond §3.1 Step 4's named six — they exercise the full `runDispatcher` integration rather than just `findFrameworkRoot` in isolation.

## Divergences from Phase 09 plan §2

| Plan §2 row | Plan said | Actually did | Why |
|:---|:---|:---|:---|
| New test cases | 6 → expected 30 total | 8 → 32 total | Two bonus end-to-end `runDispatcher` cases added during Step 4. Better coverage of the wire-up; not in the plan but consistent with §3.1 Step 4's spirit ("(d) Phase 09 tests"). |
| `DispatchError` API | (not specified) | Extended constructor signature with optional `errorCode` (3rd arg, backward-compatible) | Needed to expose the `invalid_framework_root` error code to tests + downstream consumers. Backward-compatible: existing two-arg `new DispatchError(2, msg)` calls still work; new code can pass a third arg. |

No semantic divergences. The dispatcher still re-globs on every invocation (no caching, per §3.2 forbidden), the per-binding launchers stay as escape hatches, the MCP layer is untouched.

## What unblocks

- **Phase 10 — Example project init.** Now ready. Example can `git submodule add https://github.com/uKmaz/Nissth Tools/Nissth`, copy the consumer launchers, and have a working `./nissth-bridge` from day one. The 76-pass-18-skip Postgres binding, 51/51 Expo binding, and 104/104 Spring Boot binding are all available without copying any binding source into Example.
- **Future consumer projects.** Any project that follows the recipe in `consumer-launcher/README.md` gets the framework without vendoring.

## Known limitations / follow-ups

1. **Submodule path is hardcoded as `Tools/Nissth/`.** A future enhancement could make this configurable (e.g., `.nissth-config.json` at repo root), but standardizing on one path means agents in different consumer projects share a mental model. Worth holding for now.
2. **No auto-bootstrap on submodule install.** A user who runs `git submodule add ... Tools/Nissth` still has to manually copy CLAUDE.md, AGENTS.md, templates, and the launchers. A future `nissth init` CLI (separate plan) would automate this.
3. **No upstream-bump diff helper.** When bumping the submodule, the user has to manually inspect changes to CLAUDE.md and copy them into their own root copy. Tooling for this is deferred.
4. **Framework-root resolution does NOT cache.** Every dispatcher invocation re-evaluates the three tiers (~3 syscalls extra). Acceptable for CLI overhead; if dispatch becomes hot-path, in-memory caching with mtime invalidation is straightforward.

## Pointers

- **Plan:** `ImplementationPlans/Phase_09_Framework_Root_Resolution.md`
- **Dispatcher source change:** `Tools/nissth-bridge/dispatcher.js` (lines around `findFrameworkRoot` + `runDispatcher`)
- **Tests:** `Tools/nissth-bridge/test.mjs` (tail — the Phase 09 section)
- **Consumer-launcher template:** `Tools/nissth-bridge/consumer-launcher/`
- **CLAUDE.md spec:** §11.15 (appended paragraph)

## Revision history

- 2026-05-18 — initial authoring on phase close.
