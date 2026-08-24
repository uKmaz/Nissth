# nissth-bridge MCP shim (PostgreSQL binding)

Thin Node MCP server that forwards Model Context Protocol calls to the `nissth-bridge` CLI of the PostgreSQL binding. Implements `CLAUDE.md` §11.6 (the four MCP tool surface).

**Shim version:** `0.1.0` &nbsp;·&nbsp; **Runtime:** Node 20+ &nbsp;·&nbsp; **SDK:** `@modelcontextprotocol/sdk` v1.x

The shim is pure plumbing: every MCP tool call spawns the binding CLI (`dist/cli/index.js`) as a child process, captures stdout/stderr, and returns the result. No in-process state, no caching, no connection pooling.

---

## Install

```bash
cd Bindings/Postgres/mcp
npm install
```

The MCP SDK + zod are the only runtime deps. The binding itself must be built first (`npm run build` from `Bindings/Postgres/`); the shim refuses to start if `dist/cli/index.js` is missing.

## Connection string

Every tool that talks to Postgres needs a connection string. Two paths:

1. **Session-wide:** set `NISSTH_PG_URL` once before launching the MCP server. Every tool call uses it.
2. **Per-call:** pass `connection_string` in `Nissth_Verify` input, or `scope.extra.connection_string` in `Nissth_Gateway`'s command. Overrides the env var for that call.

The password component is always redacted before any report write, stdout/stderr emission, or error message. See `tests/contract/SecretRedaction.test.ts` for the load-bearing contract.

## Smoke test

After install + binding build (+ optionally a live Postgres URL):

```bash
cd Bindings/Postgres/mcp
NISSTH_PG_URL='postgresql://...' node smoke-test.mjs
# Or without a live PG, the smoke test exercises the MCP protocol surface only:
node smoke-test.mjs
```

The smoke test spawns the MCP server over stdio, lists registered tools, invokes each one (or asserts the expected "no connection string" error when no PG URL is supplied), and exits 0 / 1 with a final ALL CHECKS PASSED / FAILED line.

## CLI-only equivalent

```bash
cd ../  # back to Bindings/Postgres/
./scripts/nissth-bridge --list-tools                           # 5 tools
./scripts/nissth-bridge --describe schema_lens
NISSTH_PG_URL='postgresql://...' ./scripts/nissth-bridge schema_lens --mode tables --scope.package public
```

## MCP tools exposed

| MCP tool | Purpose | Inputs |
|:---|:---|:---|
| `Nissth_Gateway` | Forward an arbitrary `{tool, mode, scope, output}` command to the CLI | `command: BridgeCommand` |
| `Nissth_Verify` | Wrapped invocation for common diagnostics | `operation: "schema" \| "locks" \| "migrations"`, `connection_string?: string` |
| `Nissth_ReadReport` | Read a prior Bridge report by name / absolute path / `latest:<tool>` shortcut | `relativePath: string`, `maxChars?: number` |
| `Nissth_Status` | Health probe — installed bindings, recent reports, CLI availability, NISSTH_PG_URL env state | `recent?: number` |

`Nissth_Verify` operation mapping (PostgreSQL binding):

| Operation | Bridge tool | Mode | Why |
|:---|:---|:---|:---|
| `schema` | `schema_lens` | `full` | Tables + columns + FK graph in one call. |
| `locks` | `lock_audit` | `waiting` | Most common operational question — what's blocking what. |
| `migrations` | `migration_status` | `auto` | Auto-detects Flyway or Liquibase history table. |

Note that there is **no `compilation` operation** for this binding — Postgres is a service, not a compilable artifact. Use `schema` to verify the database matches expectations.

## Register with Claude Code

Add to your MCP client's server list (paths are illustrative):

```json
{
  "mcpServers": {
    "nissth-bridge-postgres": {
      "command": "node",
      "args": ["C:\\Users\\admin\\Desktop\\Nissth\\Bindings\\Postgres\\mcp\\index.js"],
      "env": {
        "NISSTH_PG_URL": "postgresql://nissth_ro:***@localhost:5432/mydb?sslmode=disable"
      }
    }
  }
}
```

`Bindings/SpringBoot/mcp/` and `Bindings/Expo/mcp/` ship their own shims with the same four-tool surface; all three can be registered side-by-side. The Gateway routes to whichever binding owns the named tool.

## Pointers

- **Contract spec:** `CLAUDE.md` §11.6
- **Stack rules:** `CLAUDE.md` §8.3
- **Binding CLI:** `../scripts/nissth-bridge` (POSIX) / `../scripts/nissth-bridge.ps1` (PowerShell)
- **Tool catalog:** `../README.md` § Tool catalog
- **Reference shims:** `../../SpringBoot/mcp/` (Phase 05) and `../../Expo/mcp/` (Phase 06)
