# Phase 07: Diagnostic Bridge — PostgreSQL First Slice (general-purpose, diagnostic-only)

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_07_Bridge_Postgres_FirstSlice
- **Authored:** 2026-05-18 by Claude (Opus 4.7)
- **Approved:** 2026-05-18 by Emre Uçmaz ("I liked your plan. Execute.")
- **Depends on:** Phase_05_Bridge_SpringBoot_FirstSlice (closed 2026-05-17, 111/111), Phase_06_Bridge_Expo_FirstSlice (closed 2026-05-18, 51/51 — provides the TypeScript/npm binding shape this plan ports)
- **Estimated scope:** Creates a new npm subproject at `Bindings/Postgres/` (~30 TypeScript files + tests + a fixture seed SQL + a thin Node MCP shim). Implements **five diagnostic tools** (`schema_lens`, `query_plan`, `index_audit`, `lock_audit`, `migration_status`) plus a `nissth-bridge` CLI dispatcher (binding-local) and an MCP wrapper exposing four MCP tools (mirroring Phase 05/06's `Nissth_Gateway` / `Nissth_Verify` / `Nissth_ReadReport` / `Nissth_Status`). **General-purpose by design** — the binding accepts a PostgreSQL connection string (env var `NISSTH_PG_URL` with per-call `scope.extra.connection_string` override) and runs against whatever live database is reachable; no project coupling, no Testcontainers requirement for *use*. **Diagnostic-only first slice** — zero action tools; real development continues via pgAdmin / psql / JPA / migration-tooling outside this binding. Authors `CLAUDE.md` §8.3 PostgreSQL alongside the binding. Adds one new directory under Nissth root (`Bindings/Postgres/`); modifies `CLAUDE.md` (adds §8.3; adds a parallel paragraph under §11.13 OR new §11.14 for Phase 07's tool catalog — choice in §3 Step 4 based on actual diff size) and `Bindings/README.md` (stack table row Postgres: "Queued" → "Shipped"). No changes to Phase 05 or Phase 06 binding source. No changes to consuming projects (Example stays paused).

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the bridge contract is unchanged since Phase 06 close, no prior Postgres binding exists, host toolchain (Node 20+, npm) is present, the Phase 05 + Phase 06 reference bindings still pass their self-builds, and a strategy for the binding's own integration-test PostgreSQL is selected (Testcontainers if Docker daemon is reachable, else a `NISSTH_TEST_PG_URL` env var pointing at a live Postgres, else IT skip with documented offline-host behavior).

### 1.1 Inputs to read

- **DBL:** none — Nissth itself has no DBL.
- **Bridge reports:** none authored for this plan yet. Phase 05/06 reports under `AgentReports/Bridge/` are not consumed as inputs.
- **Source:**
  - `CLAUDE.md` §11 (Diagnostic Bridge contract).
  - `CLAUDE.md` §8 (current state — §8.1 Spring Boot, §8.2 Expo).
  - `Bindings/_schemas/bridge-command.schema.json` (the contract this binding implements).
  - `Bindings/Expo/expo.bridge.json` (reference manifest shape).
  - `Bindings/Expo/src/core/` (reference for core port).
  - `Bindings/Expo/mcp/index.js` + `smoke-test.mjs` (MCP shim reference).
  - `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` (reference plan).
- **StatusUpdate.md:** latest entry as of plan authoring — `2026-05-18 03:30 — Phase 06: Bridge — Expo First Slice — CLOSED`.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Why |
|:---|:---|:---|:---|
| 1 | Confirm bridge contract is unchanged since Phase 06 close | `Read Bindings/_schemas/bridge-command.schema.json` + git log | Same frozen contract. |
| 2 | Confirm no Postgres binding exists | `ls Bindings/` | Avoid overwriting partial prior work. |
| 3 | Confirm Phase 05 reference binding still green | `cd Bindings/SpringBoot && ./mvnw clean test -U -B` | Regression guard. |
| 4 | Confirm Phase 06 reference binding still green | `cd Bindings/Expo && npm run clean && npm ci && npm run build && npm test` | Regression guard. |
| 5 | Confirm Node toolchain | `node --version` + `npm --version` + `npx --version` | Need Node 20+. |
| 6 | Confirm no `Bindings/Postgres/scripts/nissth-bridge` already in tree | `Test-Path` | Fresh slate. |
| 7 | Confirm `AgentReports/Bridge/` exists | `Test-Path` | Bridge writes there. |
| 8 | Confirm `CLAUDE.md` §8.3 absent | `Grep '^### 8\.' CLAUDE.md` | §8.3 to be authored. |
| 9 | Decide integration-test PostgreSQL strategy | `docker info`, env-var probe | Strategy A/B/C selection. |
| 10 | Confirm `pg` driver license MIT | `npm view pg license` | Compat check. |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Does `Bindings/_schemas/bridge-command.schema.json` match the Phase 06 close state? | yes — unchanged since 2026-05-15 | yes — file present at expected path; last git-touched in `f6f9cfc` (initialization commit); no later commits modified it | ✅ yes |
| Does `Bindings/Postgres/` exist? | no | no — `ls Bindings/` returned exactly `Expo`, `README.md`, `SpringBoot`, `_schemas` | ✅ yes |
| Does `cd Bindings/SpringBoot && ./mvnw clean test -U -B` still return 104/104 PASS? | yes | yes — `Tests run: 104, Failures: 0, Errors: 0, Skipped: 0`; BUILD SUCCESS at `2026-05-18T18:56:44+03:00`; total time 7.041s. Post-execution re-run at `2026-05-18T19:28:33+03:00` also 104/104 in 12.556s | ✅ yes |
| Does `cd Bindings/Expo && npm test` still return 51/51 PASS? | yes | yes — `Test Suites: 12 passed; Tests: 51 passed` in 5.005s. Post-execution re-run also 51/51 in 12.005s | ✅ yes |
| Is Node 20+ available with npm + npx? | yes — Node v24.15.0, npm 11.12.1 | yes — Node v24.15.0, npm 11.12.1, npx 11.12.1 | ✅ yes |
| Is `Bindings/Postgres/scripts/nissth-bridge` absent from tree? | yes | yes | ✅ yes |
| Does `AgentReports/Bridge/` exist? | yes | yes — contains carry-over Phase 05/06 reports | ✅ yes |
| Does `CLAUDE.md` §8 have §8.1 + §8.2 but no §8.3? | yes | yes — `Grep '^### 8\.'` returned `### 8.1 Spring Boot` (line 236) and `### 8.2 Expo` (line 371); no §8.3 | ✅ yes |
| Integration-test PG strategy chosen and recorded? | A or B or C | **C — skip ITs offline.** Docker daemon API unreachable (`docker ps` → npipe missing); `NISSTH_TEST_PG_URL` env var unset. Acceptable per §1.3 carve-out. | ✅ yes (strategy C) |
| Is `pg` driver license MIT-compatible? | yes | yes — `npm view pg license` returned `MIT` | ✅ yes |
| §11 choice: paragraph append to §11.13 OR new §11.14? | size-dependent | **§11.14 created.** §11.13 is a complete "second slice" section about Expo; appending a Phase 07 paragraph would conflate the slices. Cleaner to separate. | ✅ yes (§11.14) |

**Stop condition:** all rows ✅; §3 execution authorized.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/` | contents | `README.md`, `_schemas/`, `Expo/`, `SpringBoot/` |
| `Bindings/Postgres/` | exists | no |
| `CLAUDE.md` §8.3 | exists | no |
| `Bindings/README.md` Postgres row | status | "Queued — Phase 07 candidate, plan not yet authored" |
| `Bindings/Expo/dist/cli/index.js` | exists | yes (from Phase 06 close) |
| `Bindings/SpringBoot/target/nissth-bridge-0.1.0.jar` | exists | yes (from Phase 05 close) |
| `AgentReports/Bridge/` | exists | yes |
| Bridge command schema | bytes | unchanged since 2026-05-15 |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Postgres/package.json` + `tsconfig.json` + `jest.config.mjs` + `.gitignore` + `src/index.ts` placeholder | exists | yes |
| `Bindings/Postgres/postgres.bridge.json` | exists | yes — 5-tool manifest, all diagnostic |
| `Bindings/Postgres/README.md` | exists | yes — mirrors Phase 06 README shape |
| `Bindings/Postgres/src/core/` | contents | 9 files (8 ports + new ConnectionManager) |
| `Bindings/Postgres/src/tools/` | contents | `SchemaLens.ts`, `QueryPlan.ts`, `IndexAudit.ts`, `LockAudit.ts`, `MigrationStatus.ts` |
| `Bindings/Postgres/src/cli/index.ts` | exists | yes — Node entrypoint with shebang |
| `Bindings/Postgres/mcp/` | contents | `index.js`, `package.json`, `README.md`, `smoke-test.mjs` |
| `Bindings/Postgres/scripts/nissth-bridge{,.ps1}` | exists | yes |
| `Bindings/Postgres/tests/fixture/` | contents | `seed.sql` + `flyway_history.sql` + `pg-bootstrap.ts` + DBL synthetic |
| `Bindings/Postgres/tests/{unit,integration,contract}/` | contents | full suite |
| `CLAUDE.md` §8.3 | exists | yes — 9 sub-sections (some N/A with reason) |
| `CLAUDE.md` §11.14 | exists | yes — Phase 07 tool catalog |
| `Bindings/README.md` Postgres row | status | Shipped |
| `nissth-bridge --list-tools` | output | 5 Postgres tool names |
| `SecretRedaction.test.ts` | result | PASS — sentinel password never present in reports / stdout / stderr |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [x] **Step 1. Scaffold npm subproject.** Files: `Bindings/Postgres/{package.json, tsconfig.json, jest.config.mjs, .gitignore, src/index.ts}` + `src/{core,tools,cli}/` + `tests/{unit,integration,contract,fixture}/` skeleton. Deps: `ajv`, `ajv-formats`, `pg`, `pg-connection-string`, `yaml`, `minimatch`; devDeps: `typescript`, `@types/node`, `@types/pg`, `jest`, `@types/jest`, `ts-jest`, `tsx`, `rimraf`, `testcontainers` + `@testcontainers/postgresql` (added during Step 19 when import was discovered to be in the sub-package). **Acceptance:** `npm install && npm run build && npm test` exit 0 on empty tree. ✅
- [x] **Step 2. Author binding manifest.** File: `postgres.bridge.json`. 5-tool entries with modes, scope_keys, scope_extra_keys, enforces. **Acceptance:** loadable as JSON; tests/unit/BindingManifest.test.ts confirms 5 tools, all diagnostic, all include `connection_string` in scope_extra_keys. ✅
- [x] **Step 3. Author binding README.** File: `Bindings/Postgres/README.md`. Tool catalog + scope.extra docs + connection setup section + role-requirement table + MCP pointer. **Acceptance:** Markdown renders cleanly. ✅
- [x] **Step 4. Author CLAUDE.md §8.3 + §11.14.** Edits: new §8.3 PostgreSQL with 9 sub-sections (§8.3.8 and §8.3.9 marked N/A with explicit reason); new §11.14 paragraph naming the 5 Phase 07 tools. **Acceptance:** §8.1 + §8.2 unchanged; §8.3 + §11.14 present. ✅
- [x] **Step 5. Implement core/types.ts, BridgeError.ts, JsonCommandParser.ts.** Ports of Phase 06 equivalents. **Acceptance:** 5 unit tests in `tests/unit/JsonCommandParser.test.ts` pass. ✅
- [x] **Step 6. Implement core/ReportWriter.ts.** Port of Phase 06's. **Acceptance:** 2 unit tests in `tests/unit/ReportWriter.test.ts` pass. ✅
- [x] **Step 7. Implement core/StaleFlipper.ts.** Port of Phase 06's. **Acceptance:** 7 unit tests in `tests/unit/StaleFlipper.test.ts` pass (4 scopeOverlaps + 4 flipIfStale cases). ✅
- [x] **Step 8. Implement core/BindingManifest.ts + ToolDispatcher.ts.** Ports of Phase 06's; manifest reads `postgres.bridge.json`. **Acceptance:** 6 unit tests on BindingManifest + 4 on ToolDispatcher pass. ✅
- [x] **Step 9. Implement core/ConnectionManager.ts.** NEW. Resolution order (scope.extra > env > error). Parse via `pg-connection-string`. `redactForLog()`, `redactedUrl()`, `scrubString()` redaction utilities. `withClient(cmd, fn)` opens fresh pg.Client per call, closed in finally. Statement timeout default 30000ms. `fingerprint(client)` queries `pg_control_checkpoint().redo_lsn`. **Acceptance:** 17 unit tests in `tests/unit/ConnectionManager.test.ts` pass (resolveConnectionString × 4, parse × 4, redactForLog × 2, redactedUrl × 1, scrubString × 3, resolveTimeout × 4). ✅
- [x] **Step 10. Implement tools/SchemaLens.ts.** Modes: tables/columns/relationships/full. STALE-flips both `DBL/SchemaIndex/*.md` and `DBL/DependencyMaps/*.md`. **Acceptance:** compiles; IT skeleton in `tests/integration/SchemaLens.it.test.ts` registers 4 tests (all skipped under strategy C). ✅
- [x] **Step 11. Implement tools/QueryPlan.ts.** Modes: explain/analyze/buffers. Mutating-statement guard via regex. **Acceptance:** 15 unit tests in `tests/unit/QueryPlan.test.ts` pass (13 isMutating + 2 explainPrefix); IT skeleton with 4 tests (all skipped). ✅
- [x] **Step 12. Implement tools/IndexAudit.ts.** Modes: usage/unused/duplicate/bloat. Graceful pgstattuple fallback. **Acceptance:** compiles; IT skeleton with 3 tests (all skipped). ✅
- [x] **Step 13. Implement tools/LockAudit.ts.** Modes: current/waiting/long_running. Role-degradation message when pg_read_all_stats absent. **Acceptance:** compiles; IT skeleton with 3 tests (all skipped). ✅
- [x] **Step 14. Implement tools/MigrationStatus.ts.** Modes: flyway/liquibase/auto. Reports applied + failed; no pending listing. **Acceptance:** compiles; IT skeleton with 4 tests (all skipped). ✅
- [x] **Step 15. Implement nissth-bridge CLI (src/cli/index.ts).** Flag form + JSON stdin + discovery modes. JSON-detection in parseValue for array/object literals (small enhancement over Phase 06). **Acceptance:** `./scripts/nissth-bridge --list-tools` returns 5 names; `--describe schema_lens` prints manifest entry. ✅
- [x] **Step 16. Build launcher scripts.** POSIX (`nissth-bridge`) + PowerShell (`nissth-bridge.ps1`); resolve dist path relative to script. **Acceptance:** both launchers invocable, returning the same 5 tool names. ✅
- [x] **Step 17. Implement MCP shim.** Files: `mcp/{package.json, index.js, README.md, smoke-test.mjs}`. Four MCP tools; `VERIFY_OPS` map: schema→schema_lens, locks→lock_audit, migrations→migration_status. Smoke test is dual-mode (LIVE if NISSTH_PG_URL set, OFFLINE asserts graceful no_connection_string). **Acceptance:** `cd mcp && npm install && node smoke-test.mjs` → "ALL CHECKS PASSED" in offline mode. ✅
- [x] **Step 18. Create fixture PG bootstrap.** Files: `seed.sql` (users + orders + view + 2 indexes + 10 sample rows), `flyway_history.sql` (3 rows incl. 1 FAILED), `pg-bootstrap.ts` with strategy A/B/C selector, `DBL/SchemaIndex/users.md` synthetic with intentional drift. **Acceptance:** files in place; pg-bootstrap.ts throws SkipITError on strategy C, IT files catch and skip cleanly. ✅
- [x] **Step 19. Author binding integration tests.** 5 IT files (`SchemaLens`, `QueryPlan`, `IndexAudit`, `LockAudit`, `MigrationStatus`) + shared `_support.ts` helper. 18 tests total. Discovered during this step: `testcontainers@10` moved `PostgreSqlContainer` to `@testcontainers/postgresql`; added as devDep and fixed the import in `pg-bootstrap.ts`. **Acceptance:** `npm run test:integration` exits 0 with 18 skipped under strategy C. ✅
- [x] **Step 20. Author contract tests.** Files: `tests/contract/SchemaValidation.test.ts` (5 tools × frontmatter validates against $defs.reportFrontmatter; +1 binding-fields check = 6 tests) + `tests/contract/SecretRedaction.test.ts` (load-bearing — 8 tests across ConnectionManager / ReportWriter / CLI subprocess paths; sentinel password grep-asserted absent from all output channels). **Acceptance:** 13 contract tests PASS via `npm run test:contract`. ✅
- [x] **Step 21. Final binding self-build.** `npm run clean && npm ci && npm run build && npm test`. **Acceptance:** Build CLEAN; 76 unit+contract tests PASS, 18 ITs SKIP under strategy C; CLI `--list-tools` returns 5; MCP smoke ALL CHECKS PASSED; Phase 05 regression 104/104; Phase 06 regression 51/51. ✅

### 3.2 Forbidden in this phase

- **No action tools.** Per user choice (Q3) — diagnostic-only first slice.
- **No changes to `Bindings/SpringBoot/` source.** Phase 05 frozen.
- **No changes to `Bindings/Expo/` source.** Phase 06 frozen.
- **No Example changes.**
- **No changes to `Bindings/_schemas/bridge-command.schema.json`.**
- **No additional tools beyond the five.**
- **No cross-binding tool-name collision resolution.** Surfaces but doesn't resolve.
- **No multiplexing MCP shim.** Per-binding shim only.
- **No DBL auto-regeneration tooling under `Tools/`.**
- **No publishing/release work.**
- **No edits to `CLAUDE.md` §11.1–§11.13 prose.** Only §8.3 add + §11.14 add.
- **No new top-level Nissth-core sections beyond §8.3.**
- **No write operations against the live Postgres during diagnostic tool invocation.** Diagnostic-only invariant.
- **No persistent connection pool.** One PG connection per tool call; closed in finally.
- **No bundling of tools into a single class.**

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- **Binding self-build freshness:** `npm run clean` removes `dist/` + `.tsbuildinfo` + `node_modules/.cache/`; `npm ci` enforces lockfile-driven install; `tsc` fresh compile; Jest reads from disk via `ts-jest` (no persistent cache).
- **Connection freshness:** one PG connection per tool invocation, closed in `finally`. No pool. `pg_control_checkpoint().redo_lsn` captured at run start and cited in `freshness.source_state`.
- **`query_plan` freshness:** PG generates plans per-connection for ad-hoc SQL; fresh connection per call ensures fresh plan.
- **STALE-flip freshness:** DBL artifacts read from disk fresh per call; idempotent re-runs are no-ops.
- **Phase 05 + Phase 06 references verified live** before and after Phase 07 execution.
- **Schema validation reads produced report files from disk** after writes complete via shared `ajv` instance.

### 4.2 Checks

- [x] **Build:** `npm run clean && npm ci && npm run build` exit 0; `dist/cli/index.js` produced with shebang. ✅
- [x] **Tests:** 76 pass / 18 skip / 0 fail / 94 total via `npm test`. ✅
- [x] **Runtime / CLI:** `./scripts/nissth-bridge --list-tools` returns exactly `schema_lens, query_plan, index_audit, lock_audit, migration_status` (5 names); `--describe schema_lens` prints manifest entry. ✅
- [x] **MCP smoke:** `cd mcp && npm install && node smoke-test.mjs` → "ALL CHECKS PASSED" in offline mode. ✅
- [x] **Bridge re-query / STALE-flip:** Skipped under strategy C — `tests/integration/SchemaLens.it.test.ts` will exercise this on a future PG-capable host. Fixture `DBL/SchemaIndex/users.md` in place ready for the flip. ⏭️ (deferred, documented)
- [x] **DBL freshness:** N/A — Nissth core has no DBL. ✅
- [x] **Secret redaction:** `SecretRedaction.test.ts` PASS (8/8). ✅
- [x] **Phase 05 still green:** 104/104 PASS (re-run post-Phase-07 at `2026-05-18T19:28:33+03:00`). ✅
- [x] **Phase 06 still green:** 51/51 PASS (re-run post-Phase-07). ✅

### 4.3 Pass criteria

ALL met:
- Build CLEAN; CLI invocable on POSIX + PowerShell.
- Unit + contract PASS (76/76). ITs SKIP under strategy C with documented reason.
- `SecretRedaction.test.ts` PASS — sentinel password never appears in any artifact.
- Schema validation PASS — every report's frontmatter validates against `$defs.reportFrontmatter`.
- `nissth-bridge --list-tools` returns exactly 5 tool names matching `postgres.bridge.json`.
- MCP smoke PASS.
- `CLAUDE.md` §8.3 PostgreSQL exists with 9 sub-sections (§8.3.8 + §8.3.9 marked N/A with explicit reason); §11.14 added.
- Phase 05 + Phase 06 reference bindings still green.
- `Bindings/README.md` Postgres row updated to Shipped.

### 4.4 Failure handling

N/A — all checks passed.

---

## 5. Cleanup

- [x] No scratch files under `dist/` beyond the binary artifact.
- [x] No `Temp_*.ts` / `*.scratch.ts` at any binding location.
- [x] `tests/fixture/node_modules/` (if any) gitignored.
- [x] No Testcontainers state to tear down (strategy C — never started any containers).
- [x] No snapshots taken — greenfield create.
- [x] **Reports:** authored `AgentReports/Reports/2026-05-18_phase-07-bridge-postgres-snapshot.md` (snapshot, §10.4(4) mandatory close-of-phase trigger).
- [x] **Document Sync:** updated `CLAUDE.md` (§8.3 added + §11.14 added) and `Bindings/README.md` (Postgres row Shipped). No DBL to flip. No other plans cross-reference Phase 07. Phase 05/06 snapshots unchanged.

---

## 6. Status Update Entry

> See AgentReports/StatusUpdate.md for the appended entry. Phase closed 2026-05-18.
