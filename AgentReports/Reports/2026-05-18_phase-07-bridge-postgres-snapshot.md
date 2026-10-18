---
report_type: snapshot
title: Phase 07 — Bridge — PostgreSQL First Slice (diagnostic-only) — close snapshot
authored: 2026-05-18 by Claude (Opus 4.7)
last_updated: 2026-05-18 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-18 — Phase 07: Bridge — PostgreSQL First Slice — CLOSED
related_plans:
  - Phase_07_Bridge_Postgres_FirstSlice
covers:
  - Bindings/Postgres/**
  - CLAUDE.md §8.3
  - CLAUDE.md §11.14
supersedes:
  - none
---

# Phase 07 close snapshot — PostgreSQL Diagnostic Bridge binding

The third Nissth binding ships: **general-purpose, diagnostic-only PostgreSQL binding**, five tools, written in TypeScript/Node, installable alongside whatever application-side binding owns the backend. First binding in the framework that is cross-cutting (not coupled to an application stack) and the first to add no action tools at all — by design, real-development writes flow through pgAdmin / psql / JPA / the project's migration runner; Nissth observes, never modifies.

## Tools shipped

| Tool | Modes | What it does |
|:---|:---|:---|
| `schema_lens` | tables · columns · relationships · full (default) | `information_schema` introspection; emits table/column/FK Markdown tables; STALE-flips `DBL/SchemaIndex/*.md` and `DBL/DependencyMaps/*.md` on drift |
| `query_plan` | explain (default) · analyze · buffers | `EXPLAIN (FORMAT JSON)` wrap; analyze/buffers refuse mutating statements via regex guard |
| `index_audit` | usage (default) · unused · duplicate · bloat | `pg_stat_user_indexes` + `pg_index` reads; bloat mode gracefully degrades when pgstattuple is absent |
| `lock_audit` | current (default) · waiting · long_running | `pg_locks` JOIN `pg_stat_activity`; waiting mode emits blocker→waiter graph via `pg_blocking_pids()`; falls back to own-session-only when role lacks `pg_read_all_stats` |
| `migration_status` | flyway · liquibase · auto (default) | `flyway_schema_history` / `databasechangelog` reads; reports applied + failed rows; auto-detects table |

Zero action tools (Q3 at plan authoring).

## Architecture as built

```
Bindings/Postgres/
├── postgres.bridge.json          ← 5-tool manifest; binding_version 0.1.0; contract_version 1
├── package.json + tsconfig.json  ← npm subproject; CommonJS module per Phase 06 Jest-interop choice
├── src/
│   ├── core/                     ← 9 files; all ports of Phase 06 + new ConnectionManager
│   │   ├── types.ts
│   │   ├── BridgeError.ts
│   │   ├── JsonCommandParser.ts
│   │   ├── ReportWriter.ts
│   │   ├── StaleFlipper.ts
│   │   ├── BindingManifest.ts
│   │   ├── ToolDispatcher.ts
│   │   ├── ConnectionManager.ts  ← NEW — env-var precedence + per-call override + password redaction
│   │   └── repoRoot.ts
│   ├── tools/                    ← 5 ToolHandler implementations
│   │   ├── SchemaLens.ts
│   │   ├── QueryPlan.ts
│   │   ├── IndexAudit.ts
│   │   ├── LockAudit.ts
│   │   └── MigrationStatus.ts
│   └── cli/index.ts              ← Node CLI; flag form + JSON stdin + discovery modes
├── scripts/nissth-bridge{,.ps1}  ← POSIX + PowerShell launchers
├── mcp/                          ← per-binding MCP shim mirroring Phase 06 shape
│   ├── package.json + index.js
│   ├── README.md
│   └── smoke-test.mjs            ← dual-mode: LIVE if NISSTH_PG_URL set, OFFLINE otherwise
└── tests/
    ├── unit/        (7 files, 63 tests)
    ├── integration/ (5 files, 18 tests — all skipped on strategy C)
    ├── contract/    (2 files, 13 tests including load-bearing SecretRedaction)
    └── fixture/
        ├── seed.sql                              ← users + orders + 2 indexes + 1 view + sample rows
        ├── flyway_history.sql                    ← synthetic Flyway table (2 PASS + 1 FAIL rows)
        ├── pg-bootstrap.ts                       ← Strategy A/B/C selector
        └── DBL/SchemaIndex/users.md              ← intentionally-stale DBL artifact for STALE-flip IT
```

### What we re-used from Phase 06

- **JsonCommandParser, ReportWriter, StaleFlipper, BindingManifest, ToolDispatcher, BridgeError, types, repoRoot** — copied near-verbatim; only `BindingManifest` differs (looks for `postgres.bridge.json` instead of `expo.bridge.json`).
- **CLI flag-form parser** — same dotted-flag → BridgeCommand grammar, plus a small enhancement: values matching `[...]` or `{...}` are JSON.parse'd (for `--scope.extra.params '[1, 2]'` etc.).
- **MCP shim plumbing** — `runBridge()`, `textResponse()`, `readReportSafely()`, `listBridgeReports()`, and the four MCP tool surfaces (`Nissth_Gateway` / `Nissth_Verify` / `Nissth_ReadReport` / `Nissth_Status`) copied from Phase 06. Only `VERIFY_OPS` is binding-specific.

### What's stack-specific

- **`ConnectionManager` (new core class).** No Phase 05/06 analog. Handles:
  - Resolution order: `scope.extra.connection_string` > `NISSTH_PG_URL` env > `BridgeError(no_connection_string)`.
  - Parse via `pg-connection-string` (canonical libpq URL parser).
  - **Password redaction** is always-on: `redactForLog()` returns a copy with `password: "***REDACTED***"`; `redactedUrl()` builds `postgresql://<user>@<host>:<port>/<db>` (no password); `scrubString()` removes the literal password from any string passed to it.
  - `withClient(cmd, fn)` opens a fresh `pg.Client` per invocation, runs the callback, closes in `finally`. **No connection pool** — one connection per tool call.
  - `fingerprint(client)` queries `pg_control_checkpoint().redo_lsn` (falling back to `pg_current_wal_lsn()` on older PG) for the freshness stamp.
  - Statement-timeout default 30000ms, overridable via `scope.extra.statement_timeout_ms`.

- **Per-tool scrubbing.** Each tool's `scrubScope()` static method replaces `scope.extra.connection_string` with `***REDACTED***` before passing to `ReportWriter`. This is defense-in-depth — even if a future refactor forgets to scrub at the call site, the writer never sees the literal password.

- **STALE-flip targets.** `schema_lens` flips both `DBL/SchemaIndex/*.md` (on table-set drift) and `DBL/DependencyMaps/*.md` (on FK-graph drift). The application-side binding owns `Summaries/` and `APIIndex/` — Postgres binding has no opinion there.

- **`query_plan` mutating-statement guard.** Regex match against `/^\s*(INSERT|UPDATE|DELETE|TRUNCATE|CREATE|DROP|ALTER|GRANT|REVOKE|COPY|VACUUM|CLUSTER|REINDEX)\b/i`. If matched AND mode is `analyze` or `buffers`, errors out — `explain` is still allowed for read-only plan inspection of mutating SQL.

## Verification results

| Check | Status | Detail |
|:---|:---|:---|
| Build | ✅ CLEAN | `npm run clean && npm ci && npm run build` exit 0; `dist/cli/index.js` produced with shebang |
| Unit tests | ✅ 63/63 | 7 test files covering all core classes + `QueryPlan.isMutating` |
| Contract tests | ✅ 13/13 | `SchemaValidation` (5 tools × frontmatter) + **`SecretRedaction` (8 load-bearing checks across ConnectionManager + ReportWriter + CLI subprocess paths)** |
| Integration tests | ⏭️ 18 SKIP | Strategy C — Docker daemon unreachable + `NISSTH_TEST_PG_URL` unset. ITs gracefully skip with stderr note. |
| **Total Jest** | ✅ 76 pass / 18 skip / 0 fail / 94 total | Time ~6.6s |
| CLI runtime | ✅ | `./scripts/nissth-bridge --list-tools` returns exactly the 5 tool names; `--describe schema_lens` prints manifest entry |
| MCP smoke | ✅ ALL CHECKS PASSED | Offline mode — verifies `tools/list` returns 4 MCP tools, `Nissth_Status` works, Gateway/Verify gracefully error with `no_connection_string` |
| Phase 05 regression | ✅ 104/104 PASS | `cd Bindings/SpringBoot && ./mvnw clean test -U -B`; Failsafe ITs deferred (Docker unreachable) |
| Phase 06 regression | ✅ 51/51 PASS | `cd Bindings/Expo && npm test`; 12 suites, 5.005s |

## The load-bearing security contract

`tests/contract/SecretRedaction.test.ts` is the load-bearing first-slice security test. It uses a sentinel password and **grep-asserts ZERO occurrences** across:

1. `ConnectionManager.redactForLog()` output (JSON-stringified).
2. `ConnectionManager.redactedUrl()` output.
3. `ConnectionManager.scrubString()` output.
4. `BridgeError.message` from a parse failure.
5. Every file written by `ReportWriter` when a representative `ReportContext` flows through it.
6. CLI subprocess stdout + stderr when `--scope.extra.connection_string` carries the sentinel through to a doomed-to-fail connection.
7. CLI subprocess stdout + stderr on the missing-`NISSTH_PG_URL` error path.

If any of these channels ever leaks the sentinel, the test fails immediately with the offending excerpt. Future contributors who add new emission paths must extend this suite.

## Divergences from Phase_07 plan §2

| Plan Step / §2 row | Plan said | Actually did | Why |
|:---|:---|:---|:---|
| `tsconfig.json` module | `NodeNext` (per §2 mention by association with Phase 06's tsconfig table) | `CommonJS` | Same reason as Phase 06's deviation — ESM Jest interop pain; ts-jest + CJS is rock-solid. Behavior at the CLI is identical. |
| §3 Step 18 `pg-bootstrap.ts` | `import { PostgreSqlContainer } from "testcontainers"` (implied by plan's "use testcontainers npm package") | `import { PostgreSqlContainer } from "@testcontainers/postgresql"` | `testcontainers@10` moved the per-DB containers into separate sub-packages. Added `@testcontainers/postgresql@^10.16.0` as a devDep. Behavior identical for the strategy-A code path. |
| §3 Step 4 §11.13 vs §11.14 | size-based choice | **§11.14 created** | Cleaner separation; Phase 06's §11.13 reads as a complete "second slice" section, mixing in a Phase 07 paragraph would be misleading. Decision recorded in §1.3 Findings inline as a §11 choice (this divergence note). |

No divergences in test coverage, contract surface, or security guarantees.

## Known limitations / follow-ups

1. **Strategy C ITs.** This session ran on a host without Docker daemon access and without `NISSTH_TEST_PG_URL`. The 18 ITs are written and shape-correct but unexercised against live PG. To re-validate on a future PG-capable host: either `winget install Docker.DockerDesktop && docker start postgres:15` then `npm test`, OR `export NISSTH_TEST_PG_URL='postgresql://...' && npm test`. Either run should turn the 18 SKIPs into PASSes without source changes.

2. **`testcontainers@10`'s `undici` dep audit.** `npm audit` reports 1 moderate + 1 high vulnerability via `testcontainers > undici`. Both are devDep-only and only exercised by strategy-A IT runs (offline-host doesn't reach them). `npm audit fix --force` would bump to `testcontainers@11` (breaking change). Deferred to a future plan that has a live Docker host to validate the bump against.

3. **Cross-binding `nissth-bridge` PATH collision now deeper.** Three launchers exist: `Bindings/SpringBoot/scripts/nissth-bridge`, `Bindings/Expo/scripts/nissth-bridge`, `Bindings/Postgres/scripts/nissth-bridge`. User picks PATH precedence today. Resolution (a unified dispatcher at repo root) is the next backlog item — see §6 status entry.

4. **`index_audit --mode bloat` requires `pgstattuple` extension.** When absent, the report includes a graceful "extension not installed" hint instead of erroring. To enable: `CREATE EXTENSION pgstattuple;` (requires superuser).

5. **`lock_audit` cross-session visibility requires `pg_read_all_stats` role.** Without it, only the connecting session's locks are visible plus a one-row warning. Document this when granting roles to the Nissth diagnostic user.

6. **No connection pool.** Every tool invocation opens a fresh `pg.Client` and closes in `finally`. This is intentional (freshness guarantee, minimal credential lifetime in memory) but means high-frequency invocations have connection-setup overhead (~5-20ms per call typical). Acceptable for diagnostic use; would not be acceptable for an action-tool slice that does many writes — that's a future design decision.

## Implications for future slices

- **Future PG action tools (`index_create`, `vacuum_analyze`, `migration_apply`, etc.)** would extend the binding without changing the contract. They'd subclass the same `ToolHandler` pattern, use `ConnectionManager.withClient`, and add hard-enforce contracts per §11.7. CLAUDE.md §8.3.8 (currently N/A) gets authored at that point with the schema-change-ripple rule.
- **Future stacks (Go binding for Go projects, Python binding for Django, etc.)** can borrow:
  - The `ConnectionManager` pattern if they connect to an external service (Redis, S3, etc.).
  - The dual-mode IT bootstrap (Testcontainers + env var + skip).
  - The MCP shim shape — copy-paste with a `VERIFY_OPS` swap.
- **DBL `Summaries/_state.md` (Example / future projects)** — the Postgres binding's `migration_status --mode auto` produces a clean digest of what's applied vs. failed; consumer projects can cite the latest Bridge report in plan §1 inputs without re-running `psql -c`.

## Comparison to Phase 06

| Dimension | Phase 06 (Expo) | Phase 07 (Postgres) |
|:---|:---|:---|
| Language | TypeScript | TypeScript |
| Build | npm + tsc | npm + tsc |
| Core class count | 9 | 9 (8 shared + new `ConnectionManager`; minus `SubprocessRunner` which Postgres doesn't need) |
| Tool count | 5 (4 diagnostic + 1 action) | 5 (all diagnostic) |
| Test count | 51 (27 unit + 14 IT + 10 contract) | 94 (63 unit + 18 IT + 13 contract); IT skipped on this host |
| Bootstrap | filesystem fixture; offline | dual-mode: Testcontainers OR env var OR skip |
| Cross-cutting? | no — Expo apps only | **yes** — installs alongside any backend binding |
| New core abstraction | none (all ports) | `ConnectionManager` + password-redaction discipline |

## Pointers

- **Plan:** `ImplementationPlans/Phase_07_Bridge_Postgres_FirstSlice.md`
- **Stack rules:** `CLAUDE.md` §8.3
- **§11 paragraph:** `CLAUDE.md` §11.14
- **Binding root:** `Bindings/Postgres/`
- **MCP shim:** `Bindings/Postgres/mcp/`
- **Phase 06 snapshot (reference shape):** `AgentReports/Reports/2026-05-18_phase-06-bridge-expo-snapshot.md`
- **Phase 05 snapshot (original binding):** `AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md`

## Revision history

- 2026-05-18 — initial authoring on phase close.
