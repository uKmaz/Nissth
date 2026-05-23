---
report_type: incident
title: Phase 09 binding-side framework-root gap — schema lookup ignores NISSTH_FRAMEWORK_ROOT
authored: 2026-05-23 by Claude (Opus 4.7)
last_updated: 2026-05-23 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-23 01:42 — First real consumer install (UniHub) — Phase 09 recipe field test
  - 2026-05-23 ??:?? — Phase 09.5 plan authored (pending approval) — to be appended after this Report
related_plans:
  - Phase_09_Framework_Root_Resolution (closed 2026-05-18; introduced the dispatcher's three-tier resolution but did not extend it to bindings)
  - Phase_09_5_Binding_Framework_Root (authored 2026-05-23 alongside this Report; fixes the gap)
covers:
  - Phase 09 dispatcher vs. binding framework-root resolution asymmetry
  - the false-CLEAN failure mode that masked the bug at consumer-side bootstrap verification
  - SpringBoot's classpath-resource exemption from the bug class
supersedes:
  - none
---

> **Companion Report** — the consumer-side incident Report at `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\AgentReports\Reports\2026-05-23_phase-09-binding-frameworkroot-gap.md` (authored by the frontend-session agent at incident time) is the primary discovery record. This Nissth-side Report captures the framework-side perspective: what code is broken, what code is exempt, and what the Phase 09.5 fix covers. Both Reports are append-friendly; revisions update `last_updated` here.

## Summary

Phase 09 (`Phase_09_Framework_Root_Resolution`, closed 2026-05-18 21:00) added a three-tier `findFrameworkRoot` resolution to the cross-binding `nissth-bridge` dispatcher: env var → submodule convention → `repoRoot` fallback. The intent (per CLAUDE.md §11.15) was to let a consumer project's reports land in its own tree while the framework catalog comes from the framework checkout. The dispatcher correctly honors this split.

**The JS bindings' code did not get the same treatment.** Each binding's `core/repoRoot.ts` exports only `findRepoRoot()` — a single value used for BOTH the schema-load path AND the report-destination path. Where the dispatcher correctly distinguishes the two roots, the bindings collapse them back into one. In Nissth's own dogfooded environment (`repoRoot === frameworkRoot`) this collapse is invisible. In a consumer install where the two roots diverge — like UniHub-Frontend at `C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\` with `NISSTH_FRAMEWORK_ROOT` pointing at `C:\Users\admin\Desktop\Nissth\` — every binding-CLI invocation that touches the schema fails with `ENOENT`.

The bug was discovered when the UniHub-Frontend session executed Phase 00 §3 Step 2 (`./nissth-bridge.ps1 expo_doctor_lens`) on 2026-05-23 ~02:51. The session agent stopped per HR#2, diagnosed, and escalated.

**SpringBoot binding is exempt** because its `JsonCommandParser.java:22-47` loads the schema as a classpath resource (`/bridge-command.schema.json` copied into the JAR via `pom.xml:148`). The Maven build resolves the schema location at compile-time and bakes it into the artifact; runtime never touches the filesystem path that the JS bindings do.

## Timeline

| Time (local, 2026-05-23) | Event |
|:---|:---|
| 01:06 | UniHub-Frontend Bootstrap entry verified `./nissth-bridge.ps1 --list-bindings` and `--list-tools` as PASS. Honest — but neither command instantiates a binding's CLI, so the failure mode never triggered. False-CLEAN risk per CLAUDE.md §1/§11. |
| 01:35 | `Phase_00_DBL_Bootstrap.md` authored in UniHub-Frontend (pending approval). |
| ~02:5x | User approved Phase 00; frontend session began §3 execution. Step 2 (`expo_doctor_lens`) invoked. |
| ~02:5x | Step 2 failed with `ENOENT: no such file or directory, open 'C:\Users\admin\Desktop\UniHub\src\UniHub-Frontend\Bindings\_schemas\bridge-command.schema.json'`. |
| ~02:5x | Frontend agent invoked HR#2, stopped, diagnosed. Cited dispatcher.js:65-90 (correct), bindings' repoRoot.ts (single-root only), JsonCommandParser.ts:9-13 + ReportWriter.ts:10-12 (broken schema joins). |
| ~02:5x | User escalated to Nissth observation session with 4 resolution options; chose Option 1 (author `Phase_09_5_Binding_Framework_Root` in the framework repo). |
| 02:51 | Frontend session appended pause status entry and authored discovery Report. Branch `nissth/phase-00-dbl-bootstrap` left checked out with §0 approval flip durable. |
| Same day | Nissth observation session empirically confirmed the diagnosis (read dispatcher.js, both JS bindings' core files, SpringBoot's JsonCommandParser.java, grep across `Bindings/`). Authored `Phase_09_5_Binding_Framework_Root.md` + this Report. |

## Root cause

Phase 09's split between **`repoRoot`** (consumer; owns `CLAUDE.md` + `AgentReports/`) and **`frameworkRoot`** (Nissth; owns `Bindings/`) was implemented in the dispatcher only:

- `Tools/nissth-bridge/dispatcher.js:65-90` — `findFrameworkRoot(repoRoot)` resolves the framework root via three tiers. ✓ Correct.
- `Tools/nissth-bridge/dispatcher.js:~452-456` (per frontend agent's diagnosis) — spawns the binding's CLI with `env: process.env`. So `NISSTH_FRAMEWORK_ROOT` IS available in the binding's process. ✓ Correct plumbing.

But the bindings themselves never read the env var:

| File | Lines | Behavior | Status |
|:---|:---|:---|:---|
| `Bindings/Expo/src/core/repoRoot.ts` | 10-30 | Walks up from cwd to find `CLAUDE.md`. Returns ONE value. No `frameworkRoot` concept. | Broken (incomplete) |
| `Bindings/Expo/src/core/JsonCommandParser.ts` | 9-13 | `loadSchema()` calls `findRepoRoot()`, joins `Bindings/_schemas/bridge-command.schema.json`. Resolves under consumer repo where `Bindings/` doesn't exist. | Broken |
| `Bindings/Expo/src/core/ReportWriter.ts` | 10-12 | `loadFrontmatterValidator()` — same broken pattern. | Broken (schema path) |
| `Bindings/Expo/src/core/ReportWriter.ts` | 79-86 | `write()` uses constructor-injected `this.repoRoot` for `AgentReports/Bridge/` destination. | **Correct** (different mechanism — repoRoot is the right root for report writes) |
| `Bindings/Postgres/src/core/{repoRoot,JsonCommandParser,ReportWriter}.ts` | parallel lines | Byte-equivalent to Expo's. Same bug, same shape. | Broken / partial |
| `Bindings/Postgres/src/tools/{IndexAudit,LockAudit,MigrationStatus,QueryPlan,SchemaLens}.ts` | each ~60-100 | Each imports `findRepoRoot` for some local use. Pre-Flight of Phase 09.5 classifies whether each callsite is schema/framework or report/repo. | Audit pending |
| `Bindings/Expo/src/cli/index.ts` | 10, 67 | Imports `findRepoRoot`; usage at line 67 audited in Phase 09.5 Pre-Flight. | Audit pending |
| `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/JsonCommandParser.java` | 22-47 | Loads schema via classpath resource `/bridge-command.schema.json` (Maven jar-includes it per `pom.xml:148`). No filesystem path constructed. | **Exempt** — immune to the bug class |

Grep across `Bindings/` for `NISSTH_FRAMEWORK_ROOT` returned **zero matches in any binding's src/** before this Report. The env var was set and inherited correctly; just never consumed.

## Why this passed Phase 09's own test suite

Nissth's Phase 09 test suite (`Tools/nissth-bridge/test.mjs`, 32 cases) targets the dispatcher's `findFrameworkRoot` directly with synthetic fixtures + `withEnv()`-guarded env var manipulation. The dispatcher tests are CORRECT and continue to pass — but they exercise the dispatcher in isolation and never spawn a binding CLI process. The per-binding tests (Expo 51/51, Postgres 76 pass / 18 skip) use Vitest/Jest from within the framework checkout, where `repoRoot === frameworkRoot`, so the broken schema-join resolves correctly by coincidence.

In other words: **the test suites have a structural blind spot for the two-root divergence case** — single-root because they all run from the framework checkout. Phase 09.5 closes this by adding per-binding tests that mkdtempSync two distinct dirs (one with `CLAUDE.md`, one with `Bindings/_schemas/`) and set `NISSTH_FRAMEWORK_ROOT` to the latter.

## Why the consumer-side bootstrap PASS claim wasn't wrong

The 2026-05-23 01:06 UniHub-Frontend Bootstrap entry's verification block reports:
> - Launcher: PASS — `./nissth-bridge.ps1 --list-bindings` returns `expo`, `postgres`, `spring-boot` ✓
> - Tool catalog: PASS — `./nissth-bridge.ps1 --list-tools` returns 14 unique tool names ✓

Both `--list-bindings` and `--list-tools` are serviced entirely by the dispatcher (`dispatcher.js` lines ~360-392, per the frontend agent's reference). They glob the manifests, dedupe, and print. Neither command constructs a `JsonCommandParser` or `ReportWriter`. So the schema-load callsite never fires. The bootstrap's PASS was honest — it just didn't cover the failure mode that an actual tool invocation triggers.

This is the canonical CLAUDE.md §11.7 "false CLEAN" trap, applied to the framework's own self-verification: verification of code paths A and B says nothing about untested code path C. The fix that prevents recurrence is the new two-root test fixture in Phase 09.5 — it exercises C.

## Remediation

`Phase_09_5_Binding_Framework_Root.md` (authored 2026-05-23, pending user approval). Brief summary; full step list in the plan:

1. **Extend each JS binding's `core/repoRoot.ts`** with `findFrameworkRoot(repoRoot)` mirroring `dispatcher.js:65-90` exactly (env var → submodule convention → `repoRoot` fallback).
2. **Update `JsonCommandParser.ts` + `ReportWriter.ts`** in each JS binding to load the schema via `findFrameworkRoot(findRepoRoot())`. Keep `repoRoot` (constructor-injected) for the `AgentReports/Bridge/` destination — that part was always correct.
3. **Audit `cli/index.ts` (Expo) + `tools/*.ts` (Postgres)** for additional `findRepoRoot` callsites; classify each as schema/framework-relative (repoint) or report-destination (keep).
4. **Add two-root test fixtures** to each JS binding's `tests/unit/` (and `tests/integration/` for Expo where `_support.ts` bakes in single-root assumptions). The tests must demonstrate: schema validates from `frameworkRoot`, report writes to `repoRoot`, env-var-set path takes precedence.
5. **Bump each JS binding's `package.json` + `<stack>.bridge.json` `binding_version`** (patch increment).
6. **Rebuild `dist/`** for both JS bindings.
7. **Live smoke** from the UniHub-Frontend consumer repo: `./nissth-bridge.ps1 expo_doctor_lens` must succeed and the report must land at `<UniHub-Frontend>/AgentReports/Bridge/`.
8. **Cross-repo handoff:** append a "Phase 09.5 closed; resume Phase 00 §3" status entry to each affected consumer repo's `AgentReports/StatusUpdate.md`.

**SpringBoot is intentionally untouched** — its classpath-resource schema-loading architecture is the correct long-term pattern, and is immune to the two-root divergence by construction.

## Follow-ups

- **Phase 09.6 doc-only candidate.** CLAUDE.md §11.15 currently describes framework-root resolution as a dispatcher-only concept. A one-sentence addition noting that bindings honor the same resolution for schema/framework-relative paths would close the documentation gap. CLAUDE.md edits are NOT plan-exempt per HR#12, so this requires its own (small) plan.
- **JS bindings DRY refactor (future).** The two `repoRoot.ts` files are byte-equivalent before Phase 09.5 and will remain byte-equivalent after. Extracting a shared `core/roots.ts` module is appealing but cross-package; defer to a future phase. Phase 09.5 §3.2 forbids the refactor explicitly to keep blast radius tight.
- **JS bindings schema-bundling.** SpringBoot's classpath-resource pattern is architecturally cleaner than runtime env-var resolution — the schema travels WITH the binding rather than being separately resolvable. A future phase could investigate `tsup`/`esbuild` configurations to bake `bridge-command.schema.json` into each JS binding's `dist/`. This would eliminate the framework-root dependency entirely for schema lookup (only `AgentReports/Bridge/` destination would still need `repoRoot`). Out of scope for Phase 09.5; track as a longer-term hardening candidate.
- **Consumer-side test suite for bindings.** The structural test blind spot identified above (single-root test fixtures everywhere) is itself a Phase 09.5 byproduct: the new two-root tests close the immediate gap. A broader "consumer-shaped" test framework (run each binding against a synthetic consumer fixture in CI) is a hardening candidate.

## Revision history

- 2026-05-23 by Claude (Opus 4.7) — initial Nissth-side companion to the consumer-side discovery Report. Authored alongside `ImplementationPlans/Phase_09_5_Binding_Framework_Root.md`.
