---
report_type: snapshot
title: Phase 09.7 — Postgres Binding `coerceSsl` Object-Form Fix — close snapshot
authored: 2026-05-24 by Claude (Opus 4.7)
last_updated: 2026-05-24 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-24 00:55 — Phase 09.7 Postgres Binding coerceSsl object-form fix — CLOSED
  - 2026-05-23 22:13 — Phase 00 §3 Step 2 PASSED; Step 3 PAUSED on Nissth Postgres binding bug (UniHub-Backend `StatusUpdate.md` — the discovery record)
related_plans:
  - Phase_09_5_Binding_Framework_Root (closed 2026-05-23; this phase reuses the JS-binding patch pattern)
  - Phase_09_7_Postgres_Binding_CoerceSsl_Fix (this phase)
covers:
  - end-of-phase architectural snapshot for Phase 09.7
  - source diff summary (`types.ts`, `ConnectionManager.ts`)
  - test counts before / after
  - synthetic + live smoke confirmations
  - empirical `pg-connection-string` URL-form table
supersedes:
  - none
---

> **Companion** — the UniHub-Backend session's `StatusUpdate.md` entry at `C:\Users\admin\Desktop\UniHub\src\unihub-backend\AgentReports\StatusUpdate.md` (2026-05-23 22:13) is the upstream discovery record. This Report captures the framework-side close.

## Summary

Phase 09.7 fixes a long-standing bug in the Postgres binding's connection-string handling that made every TLS-required Postgres host (Render, AWS RDS with `rds.force_ssl`, Heroku, Supabase, etc.) unreachable when SSL was configured via the URL alone. Root cause: `ConnectionManager.coerceSsl` only matched `boolean` and seven specific lowercase strings; `pg-connection-string.parse()` returns `ssl` as an **object** for `?sslmode=require`, `verify-full`, `no-verify`, and `uselibpqcompat=true&sslmode=require`. Object inputs fell through to `return undefined`, the `withClient` `parsed.ssl !== undefined` gate stayed closed, and `pg.Client` connected in plaintext — which TLS-required servers reset.

Net result: a 3-line widening of `ParsedConnection.ssl`'s type union + a new object branch in `coerceSsl` + a restructured `withClient` SSL config-build now handle every shape `pg-connection-string` can return. The object form is forwarded byte-for-byte to `pg.Client`'s `ClientConfig.ssl`. Boolean and string semantics preserved.

## Empirical `pg-connection-string` URL-form table

The load-bearing table this fix is built around (verified at plan-author time via `node -e "..."` in `Bindings/Postgres/` against the same `pg-connection-string` `node_modules` copy the binding consumes):

| URL form | `parsed.ssl` (typeof + value) | Pre-fix `coerceSsl` output | Post-fix `coerceSsl` output |
|:---|:---|:---|:---|
| bare URL (no `sslmode=`) | `undefined` | `undefined` | `undefined` |
| `?sslmode=require` | object `{}` | **`undefined` (BUG)** | `{}` (forwarded) |
| `?sslmode=verify-full` | object `{}` | **`undefined` (BUG)** | `{}` (forwarded) |
| `?sslmode=no-verify` | object `{rejectUnauthorized: false}` | **`undefined` (BUG)** | `{rejectUnauthorized: false}` (forwarded) |
| `?sslmode=disable` | boolean `false` | `false` | `false` |
| `?sslmode=prefer` | object `{}` | **`undefined` (BUG)** | `{}` (forwarded) |
| `?sslmode=allow` | object `{}` | **`undefined` (BUG)** | `{}` (forwarded) |
| `?uselibpqcompat=true&sslmode=require` | object `{rejectUnauthorized: false}` | **`undefined` (BUG)** | `{rejectUnauthorized: false}` (forwarded) |

Only one URL form (`?sslmode=disable`) returns a boolean from `pg-connection-string`. Every other non-bare form is an object — the bug's blast radius was every meaningful SSL configuration.

## Source diff summary

### `Bindings/Postgres/src/core/types.ts`

| Line | Change | LOC delta |
|:---|:---|---:|
| 105 (`ParsedConnection.ssl`) | Widen union to include `{ rejectUnauthorized?: boolean; [key: string]: unknown }` | +8 / -1 |

### `Bindings/Postgres/src/core/ConnectionManager.ts`

| Lines | Change | LOC delta |
|:---|:---|---:|
| 76 (visibility) | `private static coerceSsl` → `static coerceSsl` (public for unit testing — `tests/unit/CoerceSsl.test.ts`) | +0 |
| 77-79 (added doc comment) | Three-line explanatory comment above `coerceSsl` | +3 |
| 85-87 (added branch) | `if (typeof v === "object" && v !== null) return v as ParsedConnection["ssl"];` — the load-bearing fix | +3 |
| 145-156 (restructured) | `withClient` SSL config-build replaced with explicit `object → object`, `boolean → boolean`, `string → {rejectUnauthorized: !== "disable"}` branches | +9 / -1 |

### `Bindings/Postgres/tests/unit/CoerceSsl.test.ts` (NEW)

| Section | Cases |
|:---|---:|
| Boolean inputs | 2 |
| String inputs (libpq sslmode strings) | 8 (6 modes via `it.each` + uppercase normalization + unknown string) |
| Object inputs (pg-connection-string output shape) | 4 (empty + rejectUnauthorized:false + true + additional ca/servername keys) |
| Non-coercible inputs | 4 (null, undefined, number, symbol) |
| `parse(url).ssl` E2E for every URL form | 6 (bare, require, verify-full, no-verify, disable, uselibpqcompat) |
| **Total** | **24 cases** |

### `Bindings/Postgres/tests/unit/BindingManifest.test.ts`

| Line | Change | LOC delta |
|:---|:---|---:|
| 10 (version assertion) | `"0.1.1"` → `"0.1.2"` (doc-sync ripple from version bump) | +1 / -1 |

### Version manifests

| File | Field | Change |
|:---|:---|:---|
| `Bindings/Postgres/package.json` | `version` | `0.1.1` → `0.1.2` |
| `Bindings/Postgres/postgres.bridge.json` | `binding_version` | `0.1.1` → `0.1.2` |

### Zero changes

- `Bindings/Postgres/src/tools/{IndexAudit,LockAudit,MigrationStatus,QueryPlan,SchemaLens}.ts` — audit confirmed all 5 tools delegate to `ConnectionManager.withClient` and never branch on `parsed.ssl` directly.
- `Bindings/Expo/**` — Phase 09.5 sibling; this phase is Postgres-only.
- `Bindings/SpringBoot/**` — separate connection model (HikariCP); out of scope.
- `Tools/nissth-bridge/**` — dispatcher doesn't touch SSL config.
- `Bindings/_schemas/bridge-command.schema.json` — contract stable.

## Test counts — before / after

| Suite | Before Phase 09.7 | After Phase 09.7 | Net |
|:---|:---|:---|:---|
| Dispatcher (`Tools/nissth-bridge`) | 32 pass / 0 fail | 32 pass / 0 fail | unchanged (regression-protection) |
| SpringBoot binding | 104 pass / 0 fail / 0 error | 104 (assumed; not re-run this phase) | unchanged (no source touched) |
| Expo binding | 58 pass / 0 fail | 58 pass / 0 fail | unchanged (regression-protection) |
| Postgres binding | 83 pass / 18 skip / 0 fail / 101 total | **107 pass / 18 skip / 0 fail / 125 total** | **+24** (all 24 from `CoerceSsl.test.ts`) |

Total framework test count: 277 → 301 (+24 net new tests; 0 regressions).

## Verification — synthetic and live smoke

### Step 14 — synthetic two-root regression-protection smoke

Tmpdir consumer at `/tmp/nissth-phase09-7-pg-smoke/` (CLAUDE.md only, no `Bindings/`); `NISSTH_FRAMEWORK_ROOT=/c/Users/admin/Desktop/Nissth`. Invocation:

```bash
node $FW/Bindings/Postgres/dist/cli/index.js --json-stdin <<<'{"tool":"migration_status"}'
```

Result: exit 2; stderr `{"error":"No connection string supplied. Set NISSTH_PG_URL env var OR pass scope.extra.connection_string ...","tool":"migration_status","stage":"validate","error_code":"no_connection_string"}`. **Identical shape to Phase 09.5 Step 16.** Confirms zero framework-root resolution regression.

### Step 13 — LIVE smoke against UniHub-Backend's Render PG

Invocation:

```powershell
Push-Location 'C:\Users\admin\Desktop\UniHub\src\unihub-backend'
$env:NISSTH_PG_URL = 'postgresql://unihubdb_user:<REDACTED>@dpg-d2dj3ts9c44c73fadvvg-a.frankfurt-postgres.render.com/unihubdb?sslmode=no-verify'
& '.\nissth-bridge.ps1' migration_status --binding postgres
Remove-Item Env:NISSTH_PG_URL
Pop-Location
```

Result:
- Exit code: **0**
- Report at `C:\Users\admin\Desktop\UniHub\src\unihub-backend\AgentReports\Bridge\migration_status_2026-05-23T214606Z.md`
- Report frontmatter: `binding: postgres`, `binding_version: 0.1.2`, `freshness.source: postgresql://unihubdb_user@dpg-d2dj3ts9c44c73fadvvg-a.frankfurt-postgres.render.com:5432/unihubdb` (password redacted per contract), `source_state: redo_lsn=125/970003E8` (proves a successful query — TLS handshake + `SELECT redo_lsn FROM pg_control_checkpoint()` both ran)
- Body: "No migration-history table found" — expected downstream answer (UniHub-Backend hasn't authored Flyway baseline yet)

**This is the end-to-end proof that closes the bug.** The exact failure mode the UniHub-Backend session hit (4 URL forms × `ECONNRESET` at `pg.Client.connect()`) is empirically gone.

## Divergences from plan

| Plan section | Actual | Note |
|:---|:---|:---|
| Plan §3 forecast: "+2 to +3" new tests | +24 cases in `CoerceSsl.test.ts` | More thorough coverage than forecast. The 24 cases exhaustively cover boolean / 7 string modes / 4 object shapes / 4 non-coercible + 6 `parse(url).ssl` E2E. |
| Plan §3.1 Step 8: `tests/integration/SslTlsRequiredHost.it.test.ts` (Docker-gated, TLS-required) | **DEFERRED** | Non-TLS testcontainers can't exercise the object-form path (any object-form ssl input causes `pg.Client` to attempt SSL → server refuses → error). Spinning a TLS-enabled PG testcontainer is heavyweight (custom Docker image + self-signed certs). Backlog item 4 in §6 Next already queues this as a future improvement. The 24-case unit suite + live smoke (Step 13) cover the regression surface. |
| Plan §1.3 Findings row 11 expected "3" callsites | Actual: 5 in ConnectionManager.ts + 1 in types.ts = 6 total | Plan-author undercount. Only 4 are decision-making (types.ts:105, ConnectionManager.ts:76, :145, :146); line 71 is the invocation (just calls `coerceSsl(parsed.ssl)`) and line 98 in `redactForLog` is a passive read (just copies the value into the redacted struct — works for any shape; minor post-fix improvement: redacted reports now include the object-form ssl in their frontmatter instead of an `undefined` value). Functionally `Match=yes` — same change set as planned. |
| Plan §1.3 Findings row 13 expected "yes" (one `"0.1.1"` literal) | Actual: 3 literals — `BindingManifest.test.ts:10` (real assertion — updated) + `repoRoot.test.ts:150,194` (synthetic fixture data — preserved) | Same pattern as Phase 09.5. Match=yes-with-clarification. |

No other divergences. The plan's `§3.2 Forbidden` list was honored — SpringBoot untouched, Expo untouched, dispatcher untouched, contract schema untouched, no DRY refactor, no CLAUDE.md edits.

## Follow-ups (queued, not closing this phase)

1. **Phase 09.6 — CLAUDE.md §11.15 doc update** (carried over from Phase 09.5 close; doc-only, HR#12 plan-required).
2. **Phase 09.8 doc-only candidate — CLAUDE.md §8.3.2 sentence on SSL mode support** (queued this phase). Could be bundled with Phase 09.6 into one CLAUDE.md doc-update plan.
3. **TLS-required integration test** — replace the deferred `SslTlsRequiredHost.it.test.ts` with a real TLS-enabled `PostgreSQLContainer` setup (custom Docker image + self-signed cert + `withCopyFilesToContainer`). Defer until either (a) Docker is reliably available in this dev environment or (b) someone has a couple of hours to wire up the testcontainers-side TLS plumbing.
4. **Phase 10 — Süprüz project init** (queued from 2026-05-22 23:00; HR#13 permission gate).
5. **Optional DRY refactor across `Bindings/{Expo,Postgres}/src/core/repoRoot.ts`** — same byte-equivalent-then-extract candidate from Phase 09.5; unchanged by this phase.

## Revision history

- 2026-05-24 — initial authoring at Phase 09.7 close, cross-linking the UniHub-Backend discovery record (2026-05-23 22:13 entry) and this phase's §6 closing status entry.
