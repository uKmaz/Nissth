---
report_type: snapshot
title: Phase 09.5 — Binding Framework-Root Awareness — close snapshot
authored: 2026-05-23 by Claude (Opus 4.7)
last_updated: 2026-05-23 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-23 02:55 — Phase 09.5 plan + incident Report authored (pending approval)
  - 2026-05-23 13:05 — Phase 09.5 mid-execution save (Steps 1-15 + doc-sync done; Steps 16-19 + §5 Cleanup pending; plan mode active)
  - 2026-05-23 13:45 — Phase 09.5: Binding Framework-Root Awareness — CLOSED
related_plans:
  - Phase_09_Framework_Root_Resolution (closed 2026-05-18; introduced the dispatcher's three-tier resolution)
  - Phase_09_5_Binding_Framework_Root (this phase; extended the same three-tier model to the JS bindings)
covers:
  - end-of-phase architectural snapshot for Phase 09.5
  - per-binding diff summary (Expo + Postgres source, tests, manifests)
  - test counts before / after
  - synthetic + live smoke confirmations
  - cross-link to the discovery incident Report
supersedes:
  - none
---

> **Companion Report** — the `incident` Report at `AgentReports/Reports/2026-05-23_phase-09-binding-frameworkroot-gap.md` (Nissth-side framework perspective) and its consumer-side twin at `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Reports\2026-05-23_phase-09-binding-frameworkroot-gap.md` (frontend-session discovery record) document the bug class this phase fixed. This snapshot Report describes the closing state — what changed, what was confirmed, what remains queued.

## Summary

Phase 09.5 extended the three-tier framework-root resolution that Phase 09 added to the cross-binding dispatcher (`Tools/nissth-bridge/dispatcher.js:65-90`) into the two JS bindings (Expo + Postgres). The SpringBoot binding stayed untouched — its schema loads as a classpath resource (`Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/JsonCommandParser.java:22-47`, packaged into the JAR via `pom.xml:148`) and is structurally immune to the bug class.

Net result: a binding CLI spawned from inside a consumer repo (where `Bindings/` does not exist on disk) now correctly loads the contract schema from `$NISSTH_FRAMEWORK_ROOT/Bindings/_schemas/bridge-command.schema.json`, while still writing reports under `<consumerRepo>/AgentReports/Bridge/`. The two-root separation that Phase 09 introduced at the dispatcher level is now end-to-end coherent.

## Per-binding diff summary

### Expo (`Bindings/Expo/`)

| File | Change | LOC delta |
|:---|:---|---:|
| `src/core/repoRoot.ts` | Added `findFrameworkRoot(repoRoot: string)`; three-tier resolution mirroring `dispatcher.js:65-90` | +47 |
| `src/core/JsonCommandParser.ts` | `loadSchema()` now uses `findFrameworkRoot(findRepoRoot())` for the schema path; comment cites the Phase 09.5 motivation | +6 / -2 |
| `src/core/ReportWriter.ts` | `loadFrontmatterValidator()` same fix; **`write()` destination at `this.repoRoot` UNCHANGED** (lines 79-86 preserved per plan §3 Step 5 explicit rule) | +6 / -2 |
| `src/cli/index.ts` | Audit no-op — line 67 `findRepoRoot()` correctly feeds `ReportWriter`'s `repoRoot` constructor arg (report destination) | 0 |
| `tests/unit/repoRoot.test.ts` | **NEW** — 5 `findFrameworkRoot` cases (env var present + valid; env var present + missing `Bindings/`; env var unset + submodule present; env var unset + submodule absent → fallback; env var absent + repoRoot is the fallback) + 2 `ReportWriter` two-root integration cases (validator loads from frameworkRoot; write destination still at repoRoot) | +210 |
| `tests/unit/BindingManifest.test.ts` | Doc-sync ripple — version assertion `"0.1.0"` → `"0.1.1"` | +1 / -1 |
| `package.json` | Patch version bump `0.1.0` → `0.1.1` | +1 / -1 |
| `expo.bridge.json` | `binding_version` mirror bump | +1 / -1 |

### Postgres (`Bindings/Postgres/`)

| File | Change | LOC delta |
|:---|:---|---:|
| `src/core/repoRoot.ts` | Byte-equivalent to Expo's `repoRoot.ts` edit (Phase 09.5 §3.2 forbids DRY refactor across bindings — intentional duplication) | +47 |
| `src/core/JsonCommandParser.ts` | Same `findFrameworkRoot(findRepoRoot())` fix in `loadSchema()` | +6 / -2 |
| `src/core/ReportWriter.ts` | Same fix; `write()` destination preserved at `this.repoRoot` | +6 / -2 |
| `src/tools/{IndexAudit,LockAudit,MigrationStatus,QueryPlan,SchemaLens}.ts` | Audit no-op — each tool uses `findRepoRoot()` to construct `new ReportWriter({ repoRoot })`, which is the report-destination path and stays correct | 0 |
| `tests/unit/repoRoot.test.ts` | **NEW** — same shape as Expo's: 5 `findFrameworkRoot` cases + 2 `ReportWriter` two-root cases | +209 |
| `tests/unit/BindingManifest.test.ts` | Doc-sync ripple — version assertion update | +1 / -1 |
| `package.json` | Patch version bump | +1 / -1 |
| `postgres.bridge.json` | `binding_version` mirror bump | +1 / -1 |

### SpringBoot (`Bindings/SpringBoot/`)

**Zero changes.** Confirmed exempt at Pre-Flight §1.3: `JsonCommandParser.java:22-47` loads the contract schema via `getResourceAsStream("/bridge-command.schema.json")`. The classpath resource is packaged into the JAR (`pom.xml:148`) and never touches the filesystem `Bindings/` tree. Two-root divergence cannot reach this binding.

### Dispatcher (`Tools/nissth-bridge/`)

**Zero changes.** Phase 09's `findFrameworkRoot` model (`dispatcher.js:65-90`) is the canonical reference that Phase 09.5 mirrored into the JS bindings.

## Test counts — before / after

| Suite | Before Phase 09.5 | After Phase 09.5 | Net |
|:---|:---|:---|:---|
| Dispatcher (`Tools/nissth-bridge`) | 32 pass / 0 fail | 32 pass / 0 fail | unchanged |
| SpringBoot binding | 104 pass / 0 fail / 0 error | 104 pass / 0 fail / 0 error | unchanged (regression-protection) |
| Expo binding | 51 pass / 0 fail | **58 pass / 0 fail** | +7 (5 `findFrameworkRoot` + 2 `ReportWriter` two-root) |
| Postgres binding | 76 pass / 18 skip / 0 fail / 94 total | **83 pass / 18 skip / 0 fail / 101 total** | +7 (same shape; skips unchanged — Docker-gated IT) |

Total framework test count: 263 → 277 (+14 net new tests, +0 regressions).

## Verification — synthetic and live smoke

### Step 16 — synthetic two-root smoke

Harness at `C:\Users\admin\AppData\Local\Temp\nissth-step16-smoke.mjs` (out-of-tree; not committed). Two tmpdir consumers (`CLAUDE.md` only, no `Bindings/`); `NISSTH_FRAMEWORK_ROOT=C:\Users\admin\Desktop\Nissth`.

| Binding | Command | Result | Diagnostic |
|:---|:---|:---|:---|
| Expo | `{"tool":"route_lens"}` | Exit 0; report at `<consumer>/AgentReports/Bridge/route_lens_2026-05-23T103223Z.md` with `binding_version: 0.1.1` and `freshness.source` citing the consumer's `app/` path | Schema loaded from frameworkRoot; report wrote to repoRoot — full two-root separation |
| Postgres | `{"tool":"migration_status"}` (no connection string) | Exit 2; BridgeError `{stage: "validate", error_code: "no_connection_string"}` | Reached the binding's own validate stage — proves schema-load succeeded AFTER parser construction (would have ENOENT'd pre-fix) |

### Step 17 — live smoke against UniHub-Frontend

Invocation:
```powershell
Push-Location 'C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend'
& '.\nissth-bridge.ps1' expo_doctor_lens
```

Result:
- Exit code: 0
- Launcher printed: `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Bridge\expo_doctor_lens_2026-05-23T104125Z.md`
- Report frontmatter present: `binding: expo`, `binding_version: 0.1.1`, `freshness.source` cites `subprocess: npx --yes expo-doctor in C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend`, `source_state` cites subprocess exit 0 + stdout sha256 prefix.

**This is the end-to-end proof that closes the bug.** The exact failure mode in the discovery incident (`ENOENT` on `Bindings/_schemas/bridge-command.schema.json` resolved against the consumer repo root) no longer occurs.

## Divergences from plan

| Plan | Actual | Note |
|:---|:---|:---|
| Plan §3 forecast: each JS binding gains "+2 to +3" tests | Both gained +7 each (5 `findFrameworkRoot` + 2 `ReportWriter` integration) | More thorough coverage than the forecast; recorded in the 13:05 mid-execution status entry already |
| Plan §3 Step 13 forecast: bump `binding_version` "if duplicated" | Both manifests carry independent `binding_version` fields — both bumped to `0.1.1` | Findings row 5 at §1.3 confirmed YES |

No other divergences. The plan's `§3.2 Forbidden` list was honored — SpringBoot untouched, dispatcher untouched, contract schema untouched, no DRY refactor, no CLAUDE.md edits.

## Follow-ups (queued, not closing this phase)

1. **Phase 09.6 — CLAUDE.md §11.15 doc update (doc-only, HR#12 plan-required).** One sentence noting that bindings (not just the dispatcher) honor `NISSTH_FRAMEWORK_ROOT` for framework-relative paths. The companion `incident` Report and this snapshot Report together make the rationale unambiguous; the §11.15 sentence is a small forward-reference for the next agent who reads CLAUDE.md.
2. **Phase 09 framework-improvement candidate #1 — consumer-launcher local-checkout fallback** (carried over from the 2026-05-23 01:42 status entry). Unchanged by Phase 09.5; remains a separate candidate.
3. **DRY refactor across `Bindings/{Expo,Postgres}/src/core/repoRoot.ts`** — explicitly out-of-scope this phase. The two files are byte-equivalent; extracting a shared module would touch each binding's package shape. Defer to a future Phase that has a clearer reason to restructure both packages.

## Revision history

- 2026-05-23 — initial authoring at Phase 09.5 close, cross-linking the discovery `incident` Report and the §6 closing status entry.
