---
report_type: snapshot
title: Phase 05 close-out — Spring Boot Bridge binding architectural snapshot
authored: 2026-05-17 by Claude (Opus 4.7)
last_updated: 2026-05-17 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-17 — Phase 05 Steps 17 + 20 Complete (Failsafe IT green; Phase 05 closed)
related_plans:
  - Phase_05_Bridge_SpringBoot_FirstSlice
covers:
  - Bindings/SpringBoot — first reference binding for Nissth Diagnostic Bridge (§11)
supersedes:
  - none
---

## Context

Phase 05 delivered the first concrete implementation of the Diagnostic Bridge contract (`CLAUDE.md` §11). The Spring Boot binding under `Bindings/SpringBoot/` is the reference for every future binding (Expo, Postgres, Vue, Django, …). This snapshot freezes what landed, what diverged from the original plan, what's load-bearing for future bindings, and what's deferred.

## Final state

| Surface | Count / Value |
|:---|:---|
| Production source files | 19 Java (`src/main/java/com/nissth/bridge/{cli,core,tools,manifest}/`) |
| Tools shipped | 5 (`compile_verify`, `endpoint_lens`, `entity_lens`, `migration_status`, `entity_field_add`) |
| Test source files | 14 Java (12 unit + 2 contract + 7 IT under `it/`) |
| Tests passing | **104 unit + 7 IT = 111/111** via `./mvnw clean verify -U -B` |
| Build artifact | `target/nissth-bridge-0.1.0.jar` — 5.83 MB shaded, Main-Class `com.nissth.bridge.cli.NissthBridgeCli` |
| Launchers | POSIX (`scripts/nissth-bridge`), PowerShell (`scripts/nissth-bridge.ps1`) |
| MCP shim | `mcp/index.js` (266 LOC, Node 20+, `@modelcontextprotocol/sdk` v1.x) |
| Fixture project | `tests/fixture/` — minimal Spring Boot 3.2 + JPA + Flyway, used by all 7 ITs |
| Contract schema | `Bindings/_schemas/bridge-command.schema.json` — round-trip validated by `SchemaValidationTest` |
| Manifest | `spring-boot.bridge.json` — registers the five tools + their modes + enforcement contracts |

### Tool catalog (final)

| Tool | Kind | Hard-enforce contract | IT coverage |
|:---|:---|:---|:---|
| `compile_verify` | diagnostic | Refuses CLEAN if precondition skipped (Gradle daemon stop / Maven `-U`) | `CompileVerifyIT` |
| `endpoint_lens` | diagnostic | — | `EndpointLensIT` |
| `entity_lens` | diagnostic | Column-level drift detection → STALE-flips `DBL/SchemaIndex/*.md` | `EntityLensIT` |
| `migration_status` | diagnostic | Errors with `missing_flyway_plugin` snippet if Flyway not configured | `MigrationStatusIT` ×2 (PENDING + APPLIED) |
| `entity_field_add` | action | Atomic `.java` + Flyway migration write; rollback on any failure → exit 5 | `EntityFieldAddIT` + `EntityFieldAddContractTest` (real-FS rollback) |

## Divergences from the original plan

| Plan said | Reality | Why |
|:---|:---|:---|
| Gradle Kotlin DSL preferred | Maven chosen, Maven Wrapper added | `feedback_blanket_consent.md` + `2026-05-15_phase-05-maven-pivot.md` — Maven Wrapper gives zero-install onboarding on Windows hosts where Gradle daemon caching had already burned the team |
| `EntityLens` drift on table-name set | Drift on `Map<table, Set<column>>` (column-level) | `EntityLensIT` could not realistically trigger a flip with table-only drift; hardening landed mid-Phase 05 (see status entry 2026-05-17 hardening) |
| `MigrationStatus.infoCommand` hardcodes `mvn` | Routes through `CompileVerify.mavenCommand` (prefers `./mvnw` when target has wrapper) | Same hardening turn — `MigrationStatusIT` would otherwise need `mvn` on PATH inside Failsafe-forked JVM |
| Bridge reports versioned only as `1` in Markdown | Same — IT assertion was over-specific, fixed to match production output | Production correctly renders Flyway's version column (`1`) without the `V` prefix; `MigrationStatusIT.contains("V1")` was tightened to `contains("\| 1 \| init \|")` |
| Step 17 acceptance assumed first run green | Required 3 rounds: (1) wrapper missing from fixture, (2) `mvnw.cmd` upstream bug on paths-with-spaces, (3) over-specific IT assertion | All three are now fixed at source; new bindings get a clean slate |

## Load-bearing patterns future bindings MUST inherit

1. **Stack-agnostic contract is owned by `Bindings/_schemas/bridge-command.schema.json`.** A binding implements; it does not modify. Adding `scope.extra` keys is fine; widening top-level `scope` is a contract change (requires user approval per HR#12).
2. **Every Bridge tool writes a `<tool>_<ISO8601>.md` report under `AgentReports/Bridge/`** with the §11.3 frontmatter (`tool`, `mode`, `binding`, `binding_version`, `generated_at`, `scope`, `freshness.{source,source_state,guarantee}`, `contract_version: 1`). `SchemaValidationTest` is the reusable validator — copy + reparameterize per binding.
3. **Action tools refuse to proceed if their enforcement contract is unsatisfiable.** `entity_field_add` is the reference: atomic write of both halves, rollback on either failure, exit 5 with `error_code` naming the specific contract that broke. Future action tools (e.g., Expo's `route_scaffold`, Postgres's `index_add`) follow the same shape.
4. **`StaleFlipper` is the runtime mechanism for HR#11 Document Sync.** Any diagnostic tool whose result contradicts a DBL artifact's `covers` overlap flips the artifact's frontmatter to `last_regenerated: STALE — superseded by AgentReports/Bridge/<report>`. Agents reading a STALE-flagged artifact treat it as unreadable until regenerated.
5. **CLI surface is one binary: `nissth-bridge` dispatches to the correct binding by tool name** (§11.5). Each binding registers its tools via a `<stack>.bridge.json` manifest. Cross-binding tool name collisions are a build-time error (TBD: enforced when a second binding lands).
6. **MCP wrapper is a thin Node forwarder** (`mcp/index.js`) — no in-process JVM. It spawns the binding's CLI per request. Future bindings ship with their own MCP shim or share a single multiplexing shim — decision deferred to Phase 06.
7. **Maven Wrapper must be vendored INTO the fixture too**, not only at the binding root. `mvnw.cmd` line 43's `%__MVNW_CMD__%` invocation needs the quoting fix (`"%__MVNW_CMD__%" %*`) when the host path contains spaces (Windows-only, but framework targets Windows-first dev environments). The patched copy lives in `Bindings/SpringBoot/mvnw.cmd` and `Bindings/SpringBoot/tests/fixture/mvnw.cmd`.

## Implications for downstream bindings

### Expo binding (Phase 06 candidate)

- The diagnostic/action split holds: `route_lens`, `component_lens`, `asset_audit` are diagnostics; `route_scaffold`, `screen_scaffold` are actions.
- Replace Maven Wrapper with `npx expo` invocations. The "wrapper preference" logic in `CompileVerify.mavenCommand` becomes a generalizable pattern — extract a `WrapperResolver` abstraction in `core/` if a second stack actually needs it (don't pre-extract).
- Schema-validation harness is reusable as-is; the binding just plugs its own `<binding>.bridge.json` manifest.
- Testcontainers does not apply (no DB); use the Expo dev server + Detox or a CLI-level smoke harness for IT tests.

### Postgres binding (later)

- `migration_status` already proves the "spawn build-tool plugin and parse output" pattern; Postgres binding's tools (`query_plan`, `index_drift`, `lock_audit`) need a JDBC pool instead.
- The contract permits this: tools open their own connections; `scope.extra.{jdbc_url, user, password}` are valid binding-defined keys.

### Cross-cutting

- **Snapshot Report cadence:** every binding's Phase_NN close-out gets its own snapshot under this naming convention. Future Phase 06 / Phase 07 reports cite this one in `supersedes:` only when they wholesale replace a pattern documented here.

## Deferred / out of scope

- **MCP shim runtime smoke** — Node 20+ now confirmed present on the dev host (v22.18.0); a follow-up turn can run `npm install && node mcp/index.js` to confirm the four tools register. Not a Phase 05 blocker.
- **`compile_verify` Gradle path IT coverage** — fixture is Maven-only; Gradle IT requires a second fixture, deferred to whichever Phase first introduces a Gradle-based binding.
- **Cross-binding tool-name collision detection** — single-binding repo today, will need a build-time check when a second binding lands.
- **Engineer-facing top-level `README.md`** — deferred per `feedback_docs_last.md`; now unblocked since Phase 05 is closed.

## Doc sync sweep (Phase 05 close)

| Document | State at close | Action |
|:---|:---|:---|
| `ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md` | Steps 17 + 20 ticked, 20/20 done | Updated this turn |
| `CLAUDE.md` §11.12 | Lists the five-tool first slice — matches what landed | No change needed |
| `Bindings/SpringBoot/README.md` | Tool catalog + install + MCP integration pointer all current | No change needed |
| `Bindings/SpringBoot/mvnw.cmd` | Patched to quote `%__MVNW_CMD__%` (Windows path-with-space bug) | Updated this turn |
| `Bindings/SpringBoot/tests/fixture/mvnw.cmd` | Added + patched | Created + updated this turn |
| `Bindings/SpringBoot/tests/fixture/.mvn/wrapper/` | Copied from binding root | Created this turn |
| DBL artifacts | Nissth core has no DBL; fixture's `items.md` is intentionally stale by design | No change |

## Revision history

- 2026-05-17 — initial authoring at Phase 05 close.
