# Nissth Bridge — PostgreSQL binding

> **Binding id:** `postgres`
> **Version:** `0.1.0`
> **Language:** TypeScript (Node 20+)
> **Build:** npm
> **Status:** First slice — diagnostic tools only. Five tools, zero action tools. Real-development writes go through pgAdmin / psql / JPA / your project's migration runner.

This binding gives a Nissth agent a structured, read-only view of any reachable PostgreSQL database. It is **general-purpose** — it does not assume a particular application stack and is not coupled to a specific project. Point it at a connection string and it produces Markdown reports under `AgentReports/Bridge/`.

It is the third Nissth binding (after Spring Boot, Phase 05; Expo, Phase 06) and the first to be *cross-cutting* — Postgres is typically a service every application stack already uses, so this binding installs **alongside** the application-side binding, not instead of it.

---

## Tool catalog

| Tool | Kind | Modes | Scope keys | Scope extra | Freshness source |
|:---|:---|:---|:---|:---|:---|
| `schema_lens` | diagnostic | `tables` · `columns` · `relationships` · `full` | `package` (schema, default `public`) · `names` · `type_filter` | `connection_string` · `statement_timeout_ms` | `pg_control_checkpoint().redo_lsn` |
| `query_plan` | diagnostic | `explain` · `analyze` · `buffers` | `package` | `connection_string` · `sql` (required) · `params` · `statement_timeout_ms` | per-connection plan |
| `index_audit` | diagnostic | `usage` · `unused` · `duplicate` · `bloat` | `package` · `names` | `connection_string` · `min_age_days` · `statement_timeout_ms` | `pg_stat_get_db_stat_reset_time()` |
| `lock_audit` | diagnostic | `current` · `waiting` · `long_running` | `package` | `connection_string` · `min_age_seconds` · `statement_timeout_ms` | `pg_locks` + `pg_stat_activity` (live) |
| `migration_status` | diagnostic | `flyway` · `liquibase` · `auto` | `package` | `connection_string` · `migration_table` · `statement_timeout_ms` | `flyway_schema_history` / `databasechangelog` table read |

All tools are **read-only** against the target database. No DDL, no DML, no `pg_terminate_backend`, no advisory locks.

---

## Connection setup

The binding accepts a libpq connection URL in two ways, in order of precedence:

1. **Per-call:** `scope.extra.connection_string` in the JSON command — overrides the env var for that single invocation.
2. **Session-wide:** `NISSTH_PG_URL` environment variable — used when no per-call override is supplied.

```
postgresql://user:password@host:5432/dbname?sslmode=require
```

### Password redaction (load-bearing security guarantee)

Every produced report and every log/stderr line is scrubbed: the password component of the URL is replaced with `***REDACTED***` before any write happens. The frontmatter's `freshness.source` cites the database identity as `postgresql://<user>@<host>:<port>/<dbname>` — never with the password. The `tests/contract/SecretRedaction.test.ts` suite is the load-bearing test for this guarantee; it uses a sentinel password and grep-asserts absence from every output channel.

### Required roles

| Tool | Minimum role |
|:---|:---|
| `schema_lens`, `migration_status` | Any role with `SELECT` on `information_schema` (default for every role) |
| `query_plan` (explain) | `SELECT` on the targeted tables |
| `query_plan` (analyze/buffers) | `SELECT` AND in some cases `INSERT/UPDATE/DELETE` if the query touches them — the binding refuses analyze on mutating statements |
| `index_audit` | `pg_monitor` role membership for full `pg_stat_user_indexes` visibility; `bloat` mode additionally requires the `pgstattuple` extension to be installed |
| `lock_audit` | `pg_read_all_stats` (or superuser) for cross-session visibility; with a plain user the report includes only the connecting session's own locks plus a warning row |

A read-only role with `pg_monitor` + `pg_read_all_stats` covers every tool. Do **not** point the binding at a superuser account in production — the diagnostic surface does not require it.

---

## Install

```sh
# From inside Bindings/Postgres/:
npm install
npm run build
# CLI launcher:
./scripts/nissth-bridge --list-tools          # POSIX
.\scripts\nissth-bridge.ps1 --list-tools      # PowerShell
```

The launcher resolves `dist/cli/index.js` relative to its own location, so it works from any cwd as long as you call it by path.

### Example invocations

```sh
# Set the env var once per session:
export NISSTH_PG_URL='postgresql://nissth_ro:***@localhost:5432/mydb?sslmode=disable'

# Schema overview:
./scripts/nissth-bridge schema_lens --mode full --scope.package public

# Plan a query:
./scripts/nissth-bridge query_plan --mode analyze \
  --scope.extra.sql 'SELECT * FROM users WHERE email = $1' \
  --scope.extra.params '["alice@example.com"]'

# Index hygiene:
./scripts/nissth-bridge index_audit --mode unused --scope.package public

# Or per-call connection override:
./scripts/nissth-bridge migration_status --mode auto \
  --scope.extra.connection_string 'postgresql://user@otherhost/otherdb'
```

Each invocation writes a Markdown report to `<repo-root>/AgentReports/Bridge/<tool>_<ISO8601>.md` (default) and prints the path to stdout. Use `--output.destination return` to print the report body directly.

---

## MCP integration

The binding ships a per-binding MCP shim at `mcp/index.js` that exposes the standard four Nissth MCP tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`).

`Nissth_Verify` operation map:
- `schema` → `schema_lens --mode full`
- `locks` → `lock_audit --mode waiting`
- `migrations` → `migration_status --mode auto`

See `mcp/README.md` for Claude Code registration details.

---

## Pointers

- **Contract spec:** `CLAUDE.md` §11
- **Stack rules:** `CLAUDE.md` §8.3 (PostgreSQL)
- **Plan:** `ImplementationPlans/Phase_07_Bridge_Postgres_FirstSlice.md`
- **JSON schema for inputs:** `Bindings/_schemas/bridge-command.schema.json`
- **Action-tool strictness rule:** `CLAUDE.md` §11.7 (this slice ships zero action tools; the rule applies to future Postgres action tools when added)
