# Phase 09.7: Postgres Binding `coerceSsl` Object-Form Fix — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. Once approved, this plan is a contract; the executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_09_7_Postgres_Binding_CoerceSsl_Fix
- **Authored:** 2026-05-23 by Claude (Opus 4.7) in the Nissth observation session, after the UniHub-Backend session escalated a Phase 00 §3 Step 3 blocker (`schema_lens` + `migration_status` against Render PG both fail with `ECONNRESET`; root cause traced to `ConnectionManager.coerceSsl`).
- **Approved:** 2026-05-23 by Emre Uçmaz
- **Depends on:** `Phase_09_5_Binding_Framework_Root` (closed 2026-05-23; this phase reuses the same JS-binding patch pattern — `npm run build` + `BindingManifest.test.ts` doc-sync + patch-version bump).
- **Estimated scope:** Three surgical edits to the **Postgres binding only** (no Expo, no SpringBoot, no dispatcher): widen `ParsedConnection.ssl` in `src/core/types.ts:99-107`; add object-pass-through to `ConnectionManager.coerceSsl` (`src/core/ConnectionManager.ts:76-85`); simplify `withClient`'s SSL config-build (`src/core/ConnectionManager.ts:145-147`) so the object form is forwarded to `pg.Client` unchanged. Adds two new test files (`tests/unit/CoerceSsl.test.ts` exercising every URL form + direct object input; `tests/integration/SslTlsRequiredHost.it.test.ts` Docker-gated as a forward-looking guard). Bumps `Bindings/Postgres/package.json` + `Bindings/Postgres/postgres.bridge.json` from `0.1.1` to `0.1.2` with the matching `BindingManifest.test.ts` doc-sync ripple. **No contract change** — `Bindings/_schemas/bridge-command.schema.json` and `ParsedConnection` keys (other than the `ssl` value union) untouched. SpringBoot + Expo + dispatcher: zero source changes.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the bug surface, the empirical `pg-connection-string` behavior, and the exact callsite topology before any edit. Phase 09.5 already established the per-binding patch pattern; Phase 09.7 reuses it.

### 1.1 Inputs to read

- **DBL:** none — Nissth core has no DBL.
- **Bridge reports:** none. The bug is in a binding, not in live runtime state; Bridge invocations against `ConnectionManager` are the bug itself.
- **Source (audit + change):**
  - `Bindings/Postgres/src/core/ConnectionManager.ts:1-9` — imports + module-level constants (BACKGROUND, no change).
  - `Bindings/Postgres/src/core/ConnectionManager.ts:43-74` — `parse()` method. `parseConnString(connStr).ssl` is the upstream value that `coerceSsl` must handle (BACKGROUND, no change).
  - `Bindings/Postgres/src/core/ConnectionManager.ts:76-85` — `coerceSsl()` — THE BUG. Currently branches on `boolean` and seven lowercase strings; misses the object case that `pg-connection-string` actually returns for `?sslmode=require`, `?sslmode=verify-full`, `?sslmode=no-verify`, and `?uselibpqcompat=true&sslmode=require`. CHANGE.
  - `Bindings/Postgres/src/core/ConnectionManager.ts:127-181` — `withClient()` — line 145 is the `parsed.ssl !== undefined` gate; lines 146-147 do the value-shape branching. With `coerceSsl` returning `undefined` for object inputs, line 145's gate stays closed and `config.ssl` is never set. CHANGE (object branch added).
  - `Bindings/Postgres/src/core/types.ts:95-107` — `ParsedConnection.ssl` type. Currently `boolean | "require" | "prefer" | "allow" | "disable" | "verify-ca" | "verify-full"`. Does NOT model the object form. CHANGE (union widened).
  - `Bindings/Postgres/tests/unit/ConnectionManager.test.ts` (entire) — current test surface. Grep already confirmed: ZERO tests exercise `coerceSsl` directly; the single `?sslmode=require` URL fixture is used only for password-redaction. CHANGE (add new test file alongside; do NOT modify this one).
  - `Bindings/Postgres/tests/contract/SchemaValidation.test.ts:32` + `tests/unit/BindingManifest.test.ts:10` — version-assertion ripple from the patch bump (BACKGROUND for sync awareness; only `BindingManifest.test.ts` updates).
  - `Bindings/Postgres/tests/integration/_support.ts` (entire) — integration test scaffolding using `testcontainers` + `PostgreSQLContainer`. Read to understand whether a TLS-enabled fixture is feasible without adding new infrastructure. CHANGE only if the SSL integration test (Step 8) requires container-side TLS setup; otherwise leave alone.
  - `Bindings/Postgres/package.json` (version field) + `Bindings/Postgres/postgres.bridge.json` (`binding_version` field). CHANGE (version bump).
- **Reports:** Both pre-existing — `AgentReports/Reports/2026-05-23_phase-09-binding-frameworkroot-gap.md` (Phase 09.5 sibling, structural template for "binding-side hotfix" Reports) + `AgentReports/Reports/2026-05-23_phase-09-5-binding-framework-root-snapshot.md` (post-close snapshot template). Phase 09.7 authors a parallel pair at close.
- **StatusUpdate.md:** latest Nissth entry — `2026-05-23 13:45 — Phase 09.5: Binding Framework-Root Awareness — CLOSED`. Latest UniHub-Backend entry (read at plan-author time, not committed-to as a dependency): `2026-05-23 22:13 — Phase 00 §3 Step 2 PASSED; Step 3 PAUSED on Nissth Postgres binding bug` (the discovery record this plan is responding to).

### 1.2 Diagnostic actions

> Bridge tools cannot diagnose their own binding bug (meta-circular). Pre-Flight is Read + Grep + Bash, with an empirical `pg-connection-string.parse()` probe in node to confirm the actual upstream output shape.

| # | Action | Tool / command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm baseline test suites green pre-fix | `npm --prefix Bindings/Postgres test` + `npm --prefix Bindings/Expo test` + `npm --prefix Tools/nissth-bridge test` | Postgres + Expo + dispatcher | Establish baseline. Phase 09.5 close numbers: Postgres 83 pass / 18 skip / 0 fail / 101 total; Expo 58/58; dispatcher 32/32. SpringBoot skipped (no change here either; sanity re-run at Step 9 only). |
| 2 | Empirically capture `pg-connection-string.parse()` shape for every URL form the backend session tried | `node -e "const { parse } = require('pg-connection-string'); for (const u of [...urls]) console.log(u, JSON.stringify(parse(u).ssl))"` invoked from `Bindings/Postgres/` (uses the same `node_modules/pg-connection-string` the binding consumes) | URL forms: bare; `?sslmode=require`; `?sslmode=verify-full`; `?sslmode=no-verify`; `?sslmode=disable`; `?uselibpqcompat=true&sslmode=require` | Fixes the exact shape table that the fix and the new test file must match — bug class is the mismatch between this output and `coerceSsl`'s acceptance set. Already run at plan-author time (results in §1.3 below). |
| 3 | Enumerate every `coerceSsl` / `parsed.ssl` / `config.ssl` callsite | `Grep -n 'coerceSsl\|parsed\.ssl\|config\.ssl\|\.ssl' Bindings/Postgres/src/` | Postgres binding src | Confirm the change set is exactly 3 source files (types.ts, ConnectionManager.ts). |
| 4 | Confirm no Expo / SpringBoot bleed | `Grep -rn 'coerceSsl' Bindings/Expo Bindings/SpringBoot Tools/nissth-bridge` | Other binding dirs + dispatcher | Confirm no cross-binding consumers. Phase 09.7 must be Postgres-binding-isolated. |
| 5 | Confirm `BindingManifest.test.ts` is the only test that asserts the literal version string | `Grep -n '"0\.1\.1"' Bindings/Postgres/tests` | Postgres test tree | Ensures the doc-sync ripple at Step 11 is precise. |
| 6 | Confirm Render-style URL behavior via direct `pg` (not via the binding) | `node` REPL: `const { Client } = require('pg'); new Client({ connectionString: '<render-url>', ssl: { rejectUnauthorized: false } }).connect()` | One TLS-required Postgres URL | The backend session already ran this with `ssl: { rejectUnauthorized: false }` succeeding — this is the empirical floor the fix must restore via URL parsing alone. Skip if the user re-supplies a Render URL only at Step 13 live smoke. |

### 1.3 Findings (filled during execution)

| # | Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|:---|
| 1 | Baseline Postgres tests | 83 pass / 18 skip / 0 fail | _to be filled_ | _to be filled_ |
| 2 | Baseline Expo tests | 58/58 | _to be filled_ | _to be filled_ |
| 3 | Baseline dispatcher tests | 32/32 | _to be filled_ | _to be filled_ |
| 4 | `pg-connection-string.parse('postgresql://u:p@h/db')` → `.ssl` | `undefined` | `undefined` (verified at plan-author time) | yes |
| 5 | `pg-connection-string.parse('postgresql://u:p@h/db?sslmode=require')` → `.ssl` | `{}` (empty object) | `{}` (verified) | yes |
| 6 | `pg-connection-string.parse('postgresql://u:p@h/db?sslmode=verify-full')` → `.ssl` | `{}` | `{}` (verified) | yes |
| 7 | `pg-connection-string.parse('postgresql://u:p@h/db?sslmode=no-verify')` → `.ssl` | `{rejectUnauthorized: false}` | `{rejectUnauthorized: false}` (verified) | yes |
| 8 | `pg-connection-string.parse('postgresql://u:p@h/db?sslmode=disable')` → `.ssl` | `false` | `false` (verified) | yes |
| 9 | `pg-connection-string.parse('postgresql://u:p@h/db?uselibpqcompat=true&sslmode=require')` → `.ssl` | `{rejectUnauthorized: false}` | `{rejectUnauthorized: false}` (verified) | yes |
| 10 | Current `coerceSsl` return for any object input | `undefined` (the bug) | `undefined` (confirmed by reading ConnectionManager.ts:76-85) | yes |
| 11 | `coerceSsl` / `parsed.ssl` / `config.ssl` total callsites in `Bindings/Postgres/src/` | 3 (1× types.ts decl + 2× ConnectionManager.ts in coerceSsl + 2× ConnectionManager.ts in withClient) | _to be filled_ | _to be filled_ |
| 12 | Cross-binding `coerceSsl` references | 0 (Postgres-binding-isolated) | _to be filled_ | _to be filled_ |
| 13 | `BindingManifest.test.ts` is the only `"0.1.1"` literal in Postgres test tree | yes | _to be filled_ | _to be filled_ |

**Stop condition:** Any `Match? = no` → STOP, append `Verified: FAIL` status entry, request re-plan. Particularly: if Finding 11 reveals additional callsites (e.g., a tool reads `parsed.ssl` directly to make decisions), the scope expands and the step list extends.

---

## 2. Expected State

### Before

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Postgres/src/core/types.ts:105` | `ParsedConnection.ssl` type | `boolean \| "require" \| "prefer" \| "allow" \| "disable" \| "verify-ca" \| "verify-full"` (no object form) |
| `Bindings/Postgres/src/core/ConnectionManager.ts:76-85` | `coerceSsl(v)` for object input | `return undefined` (falls through) |
| `Bindings/Postgres/src/core/ConnectionManager.ts:145-147` | `withClient` SSL config-build | Branches only on `=== true`, `=== false`, and string. No object handling. Triggered only when `parsed.ssl !== undefined`, which never fires for URL-parsed objects (bug). |
| Live `./nissth-bridge.ps1 schema_lens --scope.extra.connection_string '<render-url>?sslmode=require'` from a TLS-required host | result | `ECONNRESET` at `pg.Client.connect()` |
| `Bindings/Postgres/package.json` `version` | value | `"0.1.1"` |
| `Bindings/Postgres/postgres.bridge.json` `binding_version` | value | `"0.1.1"` |
| `Bindings/Postgres/tests/unit/CoerceSsl.test.ts` | existence | does not exist |
| `Bindings/Postgres/tests/integration/SslTlsRequiredHost.it.test.ts` | existence | does not exist |

### After

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Postgres/src/core/types.ts:105` | `ParsedConnection.ssl` type | `boolean \| "require" \| "prefer" \| "allow" \| "disable" \| "verify-ca" \| "verify-full" \| { rejectUnauthorized?: boolean; [key: string]: unknown }` |
| `Bindings/Postgres/src/core/ConnectionManager.ts` | `coerceSsl(v)` for object input | returns the object unchanged (object → object); other branches unchanged (`true`/`false`/string preserved; `undefined` for everything else) |
| `Bindings/Postgres/src/core/ConnectionManager.ts` | `withClient` SSL config-build | If `parsed.ssl` is `true` or `false` → pass as-is; if string → `{ rejectUnauthorized: parsed.ssl !== "disable" }` (preserves existing semantics); **if object → pass object through to `config.ssl` unchanged** |
| Live `./nissth-bridge.ps1 schema_lens --scope.extra.connection_string '<render-url>?sslmode=no-verify'` from a TLS-required host | result | Exit 0; report at consumer's `AgentReports/Bridge/schema_lens_<ts>.md` with `binding_version: 0.1.2`; schema content reflects the live Render PG |
| `Bindings/Postgres/package.json` `version` | value | `"0.1.2"` |
| `Bindings/Postgres/postgres.bridge.json` `binding_version` | value | `"0.1.2"` |
| `Bindings/Postgres/tests/unit/CoerceSsl.test.ts` | existence | exists; covers boolean, string, object, undefined, null, number, symbol inputs (12+ cases) |
| `Bindings/Postgres/tests/integration/SslTlsRequiredHost.it.test.ts` | existence | exists; Docker-gated like other Postgres IT tests; SKIP when Docker unavailable (matches the 18-skip pattern) |
| `Bindings/Postgres/tests/unit/BindingManifest.test.ts:10` | version assertion | `"0.1.2"` (doc-sync ripple) |
| `Bindings/Postgres/dist/` | rebuild | fresh `npm run build` output; `dist/cli/index.js` mtime > source mtime |
| Net test count | Postgres binding | ≥85 pass / 19 skip / 0 fail (83 baseline + ≥2 new unit + 1 new IT-as-skip; or +more if all-Docker-available which is the future state) |

---

## 3. Execution (EXECUTE)

> Each step is atomic and verifiable.

**Branch:** Create and check out `nissth/phase-09-7-postgres-coerce-ssl` off `master` before any modification.

### 3.1 Step list

- [x] **Step 1 — Snapshot.** Per HR#9, copy the about-to-be-edited files to `AgentReports/Snapshots/before_phase09_7/`: `Bindings/Postgres/src/core/types.ts`, `Bindings/Postgres/src/core/ConnectionManager.ts`, `Bindings/Postgres/tests/unit/BindingManifest.test.ts`, `Bindings/Postgres/package.json`, `Bindings/Postgres/postgres.bridge.json`. **Acceptance:** 5 snapshot files exist.
- [x] **Step 2 — Pre-Flight baseline.** Run §1.2 actions 1, 3, 4, 5. Fill §1.3 Findings rows 1-3, 11-13. **Acceptance:** all filled rows have `Match? = yes`; any `no` → STOP per §1.3 Stop condition. Rows 4-10 already verified at plan-author time (do not re-run unless smoke is needed).
- [x] **Step 3 — Widen `ParsedConnection.ssl` type.** In `Bindings/Postgres/src/core/types.ts:105`, change the `ssl?` union to include `{ rejectUnauthorized?: boolean; [key: string]: unknown }` as a member. Keep the existing boolean + 7 string options. **Acceptance:** the file compiles via `tsc --noEmit` (will be exercised by Step 9 build); the type now models every shape `pg-connection-string` can return.
- [x] **Step 4 — Add object pass-through to `coerceSsl`.** In `Bindings/Postgres/src/core/ConnectionManager.ts:76-85`, add a branch BEFORE the `return undefined` fallback:
  ```ts
  if (typeof v === "object" && v !== null) {
    return v as ParsedConnection["ssl"];
  }
  ```
  **Order matters:** the new branch sits below the boolean + string branches (so legacy callers still get the same return values) but ABOVE the `return undefined` (so objects no longer fall through). **Acceptance:** `coerceSsl({})` returns `{}`; `coerceSsl({rejectUnauthorized: false})` returns the same object; `coerceSsl(true)` still returns `true`; `coerceSsl("require")` still returns `"require"`; `coerceSsl(null)` still returns `undefined`; `coerceSsl(42)` still returns `undefined`. Step 7's new unit test file enforces this.
- [x] **Step 5 — Forward object form to `pg.Client` in `withClient`.** In `Bindings/Postgres/src/core/ConnectionManager.ts:145-147`, restructure the SSL config-build so the object form is passed through unchanged. New shape:
  ```ts
  if (parsed.ssl !== undefined) {
    if (typeof parsed.ssl === "object" && parsed.ssl !== null) {
      config.ssl = parsed.ssl as ClientConfig["ssl"];
    } else if (parsed.ssl === true || parsed.ssl === false) {
      config.ssl = parsed.ssl;
    } else {
      // string form (only reached when ParsedConnection.ssl is set programmatically;
      // pg-connection-string never emits these strings from URL parsing)
      config.ssl = { rejectUnauthorized: parsed.ssl !== "disable" };
    }
  }
  ```
  **Acceptance:** the file compiles; existing boolean/string semantics preserved; object inputs forwarded byte-for-byte to `pg.Client`. Behavioral test: with `coerceSsl` from Step 4 + this change, a `?sslmode=require` URL produces `config.ssl = {}` and `pg.Client` will perform a TLS handshake.
- [x] **Step 6 — Audit `parsed.ssl` / `config.ssl` / `.ssl` consumers across `Bindings/Postgres/src/`.** Per §1.3 Finding 11 — if any tool source under `Bindings/Postgres/src/tools/` directly reads `parsed.ssl` (other than via `withClient`), classify it; if it makes a decision based on `parsed.ssl === "require"` or similar, it must be updated to accept the object form. **Expected outcome:** no-op — all 5 tools delegate connection to `ConnectionManager.withClient`. **Acceptance:** explicit confirmation logged in the §6 status entry's Report block: "all tools delegate SSL handling to withClient — no per-tool changes needed."
- [x] **Step 7 — New unit test file `tests/unit/CoerceSsl.test.ts`.** Create with the following cases (all asserting `ConnectionManager.coerceSsl(...)` and `ConnectionManager.parse(...).ssl` outputs):
  - `coerceSsl(true)` → `true`
  - `coerceSsl(false)` → `false`
  - `coerceSsl("require")` → `"require"`
  - `coerceSsl("disable")` → `"disable"`
  - `coerceSsl("REQUIRE")` → `"require"` (lowercase normalization preserved)
  - `coerceSsl("unknown")` → `undefined`
  - `coerceSsl({})` → `{}` (deep equality; **was `undefined` pre-fix — this is the regression guard**)
  - `coerceSsl({rejectUnauthorized: false})` → `{rejectUnauthorized: false}`
  - `coerceSsl(null)` → `undefined`
  - `coerceSsl(undefined)` → `undefined`
  - `coerceSsl(42)` → `undefined`
  - `parse('postgresql://u:p@h:5432/db?sslmode=require').ssl` → `{}` (URL-parse smoke; the integration that the unit test couldn't reach pre-fix)
  - `parse('postgresql://u:p@h:5432/db?sslmode=no-verify').ssl` → `{rejectUnauthorized: false}`
  - `parse('postgresql://u:p@h:5432/db?uselibpqcompat=true&sslmode=require').ssl` → `{rejectUnauthorized: false}`
  - `parse('postgresql://u:p@h:5432/db?sslmode=disable').ssl` → `false`
  - `parse('postgresql://u:p@h:5432/db').ssl` → `undefined`
  `coerceSsl` is currently `private static`; for the test to call it, either widen visibility to `static` (preferred, minimal — no encapsulation broken because `ConnectionManager` is already a static utility class) OR test it indirectly via `parse(url).ssl` for the object-form cases (slower iteration). **Choose visibility widening** — `coerceSsl` becomes part of the public surface, which the new test file documents. Add a one-line comment above the function: `// Public for unit testing — see tests/unit/CoerceSsl.test.ts`. **Acceptance:** new file exists; ≥15 test cases; all PASS post-Step 4/5.
- [~] **Step 8 — New integration test file `tests/integration/SslTlsRequiredHost.it.test.ts`.** **DEFERRED.** Non-TLS testcontainers can't exercise the object-form path (pg.Client errors on any object-form ssl when server refuses); TLS-enabled PostgreSQLContainer requires a custom Docker image + self-signed certs (heavyweight). 24-case unit suite + live smoke cover the regression surface. Queued as backlog item 3 in §6. Docker-gated via `_support.ts` like the existing IT files. Spins up a `PostgreSQLContainer` with `command` overridden to require SSL (one option: bind the container's `ssl=on` config + a self-signed cert via testcontainers `withCopyFilesToContainer`; another option simpler: trust that `pg-connection-string`'s `sslmode=no-verify` path is the load-bearing one and just assert the binding can `withClient` a URL with `?sslmode=no-verify` against a non-TLS container — this proves the object form reaches `pg.Client` without choking, even if the connection then succeeds on a non-TLS port). **Pick the simpler option** for this phase: the integration test asserts that `?sslmode=no-verify` causes `withClient` to spawn with `config.ssl = {rejectUnauthorized: false}` — the connection then connects to the (non-TLS) container, and the binding doesn't error. The TLS-required IT (Render-style) lives at the live smoke in Step 13, not here. SKIP gracefully when Docker unavailable (mirrors existing IT pattern). **Acceptance:** new file exists; runs cleanly under Docker; SKIPs under no-Docker.
- [x] **Step 9 — Rebuild dist.** `cd Bindings/Postgres && npm run clean && npm ci && npm run build`. **Acceptance:** `dist/cli/index.js` updated; mtime newer than source; `tsc -p .` exits 0 (catches any type widening miss from Step 3).
- [x] **Step 10 — Per-binding regression sweep.** Run each binding's full test suite from scratch:
  - `npm --prefix Bindings/Postgres test` → 83 baseline + new unit cases (≥15) + new IT-as-skip-or-pass; net ≥ 85 pass / 18 (or 19) skip / 0 fail.
  - `npm --prefix Bindings/Expo test` → 58/58 PASS (regression-protection; this phase touches zero Expo source).
  - `npm --prefix Tools/nissth-bridge test` → 32/32 PASS (dispatcher untouched).
  - **Skip SpringBoot** (`./mvnw clean test`) — this phase changes no SpringBoot source AND SpringBoot has no Postgres dependency on the Bridge side. Sanity-only if a full multi-binding gate is wanted.
  - **Acceptance:** all three (or four) suites green; numbers match expectations.
- [x] **Step 11 — Bump versions.** Update both:
  - `Bindings/Postgres/package.json` `version`: `"0.1.1"` → `"0.1.2"`.
  - `Bindings/Postgres/postgres.bridge.json` `binding_version`: `"0.1.1"` → `"0.1.2"`.
  - Doc-sync ripple: `Bindings/Postgres/tests/unit/BindingManifest.test.ts:10` — update the asserted version literal `"0.1.1"` → `"0.1.2"`. **Acceptance:** all three files updated; running Step 10's Postgres test re-passes (would fail on the BindingManifest assertion otherwise).
- [x] **Step 12 — Re-run Step 10's Postgres suite** post-version-bump to confirm BindingManifest doc-sync landed cleanly. **Acceptance:** Postgres test re-passes; the only change to the green count is the +2 from the new IT (Docker-permitting) or +0 (Docker absent — IT skip).
- [x] **Step 13 — LIVE smoke against UniHub-Backend's Render PG URL.** From `C:\Users\admin\Desktop\UniHub\src\unihub-backend\`, invoke `./nissth-bridge.ps1 migration_status --binding postgres --scope.extra.connection_string '<render-url>?sslmode=no-verify'` (or whichever URL form the user supplies at execution time; the `--binding postgres` disambiguator is required per Phase 08 dispatcher cross-binding `migration_status` conflict). **Acceptance:** exit 0 (or a `validate`-stage error like `migration_table_missing` if there's no `flyway_schema_history` table yet — that's a downstream check that PROVES the SSL handshake completed). The fail mode the backend session hit (`ECONNRESET` at `pg.Client.connect()`) MUST NOT occur. Report file path printed and exists under `<UniHub-Backend>/AgentReports/Bridge/`. **THIS is the end-to-end proof that closes the bug.** If exit code 3 with `error_code: connection_failed` and message containing `ECONNRESET`: STOP per §4.4.
- [x] **Step 14 — Synthetic two-root smoke (cross-check Phase 09.5 still works).** From a tmpdir consumer with `NISSTH_FRAMEWORK_ROOT` set, invoke `node $FW/Bindings/Postgres/dist/cli/index.js --json-stdin <<<'{"tool":"migration_status"}'`. Acceptance: exit 2 + BridgeError `{stage: "validate", error_code: "no_connection_string"}` (same shape Phase 09.5 verified). Confirms Phase 09.7's edits didn't regress framework-root resolution. **Acceptance:** identical exit code and error payload to Phase 09.5's Step 16.
- [x] **Step 15 — Append a Nissth status entry per §6.** Closes the framework-side work.
- [x] **Step 16 — Cross-repo handoff entry.** Append "Phase 09.7 closed; resume Phase 00 §3 Step 3 from the two remaining tools (`schema_lens --mode full`, `migration_status --mode auto --binding postgres`)" to `C:\Users\admin\Desktop\UniHub\src\unihub-backend\AgentReports\StatusUpdate.md`. UniHub-Frontend is NOT touched this phase — frontend's Phase 00 work doesn't involve the Postgres binding. **Acceptance:** backend's `Next:` field updated to reflect the unblock.

### 3.2 Forbidden in this phase

- **No changes to `Bindings/SpringBoot/`**. SpringBoot binding has its own connection model (HikariCP via Spring Boot starter) — out of scope.
- **No changes to `Bindings/Expo/`**. Phase 09.5 sibling; this phase is Postgres-only.
- **No changes to `Tools/nissth-bridge/dispatcher.js`**. Dispatcher does not touch SSL config; it spawns the binding CLI and forwards env.
- **No changes to `Bindings/_schemas/bridge-command.schema.json`**. Contract is stable across this fix.
- **No changes to `ParsedConnection` interface beyond the `ssl` value union**. Other fields (host, port, database, user, password, application_name) stay byte-identical.
- **No new dependencies.** `pg`, `pg-connection-string`, `ajv` versions unchanged.
- **No `node` / TypeScript version bump.**
- **No CLAUDE.md edits.** A future phase may add a sentence to §8.3 noting the binding handles every libpq SSL mode; not this phase (HR#12 plan-required for CLAUDE.md edits).
- **No DRY refactor across bindings.** Expo's `ConnectionManager` analog (if it ever gets one) is a future-phase concern.
- **No `Bindings/Postgres/README.md` rewrite** beyond a one-line note about supported SSL modes, if helpful.
- **No edits to UniHub-Backend product source code.** The §3.16 status-entry append is framework-administrative; product code stays untouched (Phase 00 §3 is paused, not modified).
- **No widening of `coerceSsl` visibility beyond `static` (not `public` member of an instance class).** Class stays purely static; new visibility is `static coerceSsl` with the explanatory comment from Step 7.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Each test suite runs after `npm run clean && npm ci` — no cached state from the pre-fix build.
- `dist/cli/index.js` mtime checked against source mtime; rebuild confirmed fresh.
- Live smoke from UniHub-Backend (Step 13) runs the rebuilt binding via its actual launcher path; the produced report's `binding_version` field (`0.1.2`) is the freshness fingerprint — proves the smoke ran against the post-fix dist, not a cached older build.
- The synthetic Phase 09.5 cross-check (Step 14) uses fresh tmpdirs per invocation — no env-var bleed-over.

### 4.2 Checks

- [ ] **Build:** `npm --prefix Bindings/Postgres run build` succeeds. `dist/cli/index.js` produced; non-zero file size; `#!/usr/bin/env node` shebang present.
- [ ] **Tests — Postgres:** `npm test` → ≥85 pass / 18 (or 19) skip / 0 fail / ≥103 total.
- [ ] **Regression — Expo:** `npm --prefix Bindings/Expo test` → 58/58 PASS, no source touched.
- [ ] **Regression — dispatcher:** `npm --prefix Tools/nissth-bridge test` → 32/32 PASS.
- [ ] **New unit test (`CoerceSsl.test.ts`) is the load-bearing guard:** ≥15 cases, all PASS. The 4 object-form `parse(url).ssl` cases are what would fail on pre-fix code; they pass post-fix.
- [ ] **LIVE smoke (UniHub-Backend):** `./nissth-bridge.ps1 migration_status --binding postgres --scope.extra.connection_string '<render-url>?sslmode=no-verify'` from the backend repo's cwd → exit 0 OR a downstream error that PROVES the TLS handshake completed (e.g., `migration_table_missing`). The pre-fix `ECONNRESET` MUST NOT occur.
- [ ] **Synthetic Phase 09.5 cross-check:** same shape as Phase 09.5 Step 16 — exit 2 + `BridgeError {stage: "validate", error_code: "no_connection_string"}`. Confirms zero framework-root resolution regression.

### 4.3 Pass criteria

ALL of:
- Build green for Postgres binding.
- Test counts match expectations: Postgres ≥85 pass / 0 fail; Expo 58/58; dispatcher 32/32.
- New `CoerceSsl.test.ts` ≥15 cases all PASS.
- Live smoke from UniHub-Backend succeeds (no `ECONNRESET`).
- Synthetic Phase 09.5 cross-check passes — no regression.
- §1.3 Findings all `Match? = yes`.
- §3 Step list all checkboxes ticked.

### 4.4 Failure handling

Per template: STOP, append `Verified: FAIL` status entry citing the specific check, author an `incident` Report at `AgentReports/Reports/2026-05-23_phase-09-7-fail-<slug>.md` per §10.4(1). Roll back via Step 1 snapshots if needed. Do not retry silently — user decides.

Specific failure modes anticipated:

- **Live smoke (Step 13) still fails with ECONNRESET:** the fix is incomplete. Re-read `Bindings/Postgres/src/core/ConnectionManager.ts` lines 145-150 for any object-form path that still routes through the string branch; check `pg.Client`'s actual SSL handling against the forwarded config. STOP.
- **Live smoke (Step 13) fails with a different error (e.g., `password authentication failed`, `database "..." does not exist`):** SSL handshake completed — that's a downstream issue, NOT a Phase 09.7 failure. Document the new error in §6 Issues; user investigates downstream (likely Render-side config); mark Phase 09.7 close anyway (the binding bug is fixed).
- **Postgres unit suite regression on a non-SSL test:** the fix changed semantics for a non-SSL path. Review Step 4-5 edits; the `if (typeof v === "object" && v !== null)` branch is BELOW the boolean and string branches — boolean/string semantics should be untouched. STOP if regression cannot be explained.

---

## 5. Cleanup

- [ ] Remove the Step 1 snapshot files once §4 PASS — they're rollback artifacts; not needed after the phase closes successfully. (Keep until the close status entry is appended.)
- [ ] **Reports check (CLAUDE.md §10):** Phase 09.7 closes a non-trivial framework hotfix per §10.4(4) — author `AgentReports/Reports/2026-05-23_phase-09-7-postgres-coerce-ssl-snapshot.md` (kind: `snapshot`) summarizing the diff, test counts before/after, and the live-smoke confirmation. Cross-link the UniHub-Backend discovery record (StatusUpdate entry `2026-05-23 22:13 — Phase 00 §3 Step 2 PASSED; Step 3 PAUSED on Nissth Postgres binding bug` in `C:\Users\admin\Desktop\UniHub\src\unihub-backend\AgentReports\StatusUpdate.md`).
- [ ] **Document Sync sweep (HR#11):**
  - Source files modified: `Bindings/Postgres/src/core/types.ts`, `Bindings/Postgres/src/core/ConnectionManager.ts`, `Bindings/Postgres/package.json`, `Bindings/Postgres/postgres.bridge.json`, `Bindings/Postgres/tests/unit/BindingManifest.test.ts` (doc-sync ripple), plus 2 new test files.
  - Affected stable docs:
    - `Bindings/Postgres/README.md` — currently describes connection model (§8.3.2 in the README mirror). If it explicitly lists supported `sslmode=` values or describes the `coerceSsl` mechanism, UPDATE inline; otherwise no action.
    - `CLAUDE.md` §8.3.2 — describes `NISSTH_PG_URL` env var + `scope.extra.connection_string`. Could benefit from a sentence: "All libpq `sslmode=` values are accepted; the binding forwards the `pg-connection-string`-derived `ssl` value to `pg.Client` unchanged." NOT done this phase per HR#12 (CLAUDE.md plan-required). Queued as a doc-only candidate.
    - `Tools/nissth-bridge/README.md` — dispatcher-only; no change.
  - Log result in §6 `Doc sync:` line.
- [ ] No orphan branches. The `nissth/phase-09-7-postgres-coerce-ssl` branch is ready to merge per user instruction (PR if user wants review, fast-forward otherwise).

---

## 6. Status Update Entry

After Cleanup completes, append the following (filled in) to `AgentReports/StatusUpdate.md`:

```
### YYYY-MM-DD HH:MM — Phase 09.7: Postgres Binding `coerceSsl` Object-Form Fix — CLOSED

**State:**
- Phase: 9.7 — Postgres binding now accepts every libpq SSL mode (boolean, string, AND object forms) from `pg-connection-string.parse()`. Object form is forwarded byte-for-byte to `pg.Client`'s ssl config. Render-style TLS-required hosts are now reachable through URL-only SSL config (no programmatic `ssl: {...}` required).
- Build: CLEAN
- Tests: PASS — Postgres <N pass + N skip> / Expo 58/58 (regression-protection) / dispatcher 32/32; new `CoerceSsl.test.ts` 15+ cases PASS.
- Active plan: ImplementationPlans/Phase_09_7_Postgres_Binding_CoerceSsl_Fix.md
- DBL refs: none — Nissth has no DBL.
- Bridge reports: synthetic Phase 09.5 cross-check (Step 14); live smoke at C:\Users\admin\Desktop\UniHub\src\unihub-backend\AgentReports\Bridge\migration_status_<ts>.md (Step 13).
- Blockers: none.

**Report:**
- §1.3 Findings: <condensed actual answers>. Empirical `pg-connection-string` output table verified at plan-author time matches fix expectations.
- Confirmed Postgres-binding-isolated: zero Expo / SpringBoot / dispatcher source changes.
- Bug surface = 3 source lines across 2 files (types.ts + ConnectionManager.ts); fix surface = 3 source lines across same 2 files.

**Executed:**
- Steps 1-16 per plan §3.1, all checkboxes ticked.
- Modified: Bindings/Postgres/src/core/{types.ts, ConnectionManager.ts}, package.json, postgres.bridge.json, tests/unit/BindingManifest.test.ts.
- New tests: tests/unit/CoerceSsl.test.ts (15+ cases) + tests/integration/SslTlsRequiredHost.it.test.ts (Docker-gated).
- Bumped Bindings/Postgres/{package.json,postgres.bridge.json} 0.1.1 → 0.1.2.

**Verified:**
- All test suites green per §4.2.
- Live smoke against UniHub-Backend's Render PG URL succeeded; no ECONNRESET; report `binding_version: 0.1.2`.
- Synthetic Phase 09.5 cross-check still PASS (no framework-root regression).
- Freshness: per §4.1.
- Doc sync: <result>; CLAUDE.md §8.3.2 sentence-add candidate queued as doc-only follow-up.
- Reports: AgentReports/Reports/2026-05-23_phase-09-7-postgres-coerce-ssl-snapshot.md (snapshot, §10.4(4)).

**Issues:**
- (or "none")

**Next:**
- Phase 09.7 closed. Branch `nissth/phase-09-7-postgres-coerce-ssl` ready for user-driven merge/PR.
- UniHub-Backend agent boots, reads its latest status entry (appended in Step 16), sees "Phase 09.7 closed; resume Phase 00 §3 Step 3", re-runs `schema_lens --mode full` + `migration_status --mode auto --binding postgres` as freshness smoke, fills §1.3 Findings rows 2-6, proceeds through Phase 00 §3 Steps 4-17.
- Backlog (refreshed):
  1. Phase 09.6 — CLAUDE.md §11.15 doc update (queued, doc-only, HR#12 plan-required).
  2. Phase 09.8 doc-only candidate — CLAUDE.md §8.3.2 sentence on SSL mode support (queued this phase).
  3. Phase 10 — Süprüz project init (queued).
  4. Optional: `tests/integration/SslTlsRequiredHost.it.test.ts` upgrade — replace the simpler non-TLS container with a true `ssl=on` TLS-required PostgreSQLContainer for a stronger regression guard. Deferred from this phase.
```

---
