# Nissth Bridge MCP — Spring Boot binding

Tiny Node MCP server that exposes the spring-boot binding's CLI as four MCP tools, per `CLAUDE.md` §11.6. Pure subprocess plumbing — no in-process JVM, no shared state.

## What it registers

| MCP tool | Purpose | Input schema |
|:---|:---|:---|
| `Nissth_Gateway` | Forward an arbitrary Bridge JSON command (§11.2) to the CLI | `{ command: { tool, mode?, context_id?, scope?, output? } }` |
| `Nissth_Verify` | Run a verification tool by named operation | `{ operation: "compilation" \| "migrations", root_path? }` |
| `Nissth_ReadReport` | Read an existing Bridge report (filename, abs path, or `latest:<tool>`) | `{ relativePath, maxChars? }` |
| `Nissth_Status` | Health probe: installed bindings + N most recent reports | `{ recent? }` |

Every tool returns a single `text` content item; on subprocess failure the result is marked `isError: true` and the body carries the exit code + stderr.

## Prerequisites

1. **Node 20+** (the shim uses ES modules and `node:` namespace imports).
2. **Java 17+** on PATH (the shim spawns `java -jar ...`).
3. **The binding jar must be built.** Run once from `Bindings/SpringBoot/`:
   ```bash
   ./mvnw clean package -DskipTests
   # → target/nissth-bridge-0.1.0.jar
   ```
   The shim's `Nissth_Status` will tell you the jar is `MISSING` if you skipped this.

## Install

```bash
cd Bindings/SpringBoot/mcp
npm install                          # pulls @modelcontextprotocol/sdk + zod (~80 MB into node_modules/)
```

## Register with Claude Code

Add to `~/.claude/mcp.json` (or your IDE's MCP config):

```json
{
  "mcpServers": {
    "nissth-bridge-spring-boot": {
      "command": "node",
      "args": ["/abs/path/to/Nissth/Bindings/SpringBoot/mcp/index.js"]
    }
  }
}
```

The shim reads no MCP-side env vars — it computes `NISSTH_REPO_ROOT` from its own `__dirname` (`<bindings>/SpringBoot/mcp/index.js` → repo root is two directories up from the binding root). Override by exporting `NISSTH_REPO_ROOT` in the parent env before Claude Code launches the shim.

## Smoke test

`smoke-test.mjs` ships with this shim. It spawns `index.js` over stdio (using the same `@modelcontextprotocol/sdk` Claude Code would use), then exercises all four tools end-to-end (`tools/list`, `Nissth_Status`, `Nissth_Gateway` → `endpoint_lens`, `Nissth_ReadReport` → `latest:endpoint_lens`, `Nissth_Verify` → `compilation`):

```bash
node smoke-test.mjs
# → prints per-tool results; exits 0 on success, 1 on any failure
```

Without the shim (raw CLI for comparison):

```bash
# Equivalent of Nissth_Status (no MCP wrapping):
../scripts/nissth-bridge --list-bindings

# Equivalent of Nissth_Gateway with a compile_verify command:
echo '{"tool":"compile_verify","scope":{"root_path":"../tests/fixture"}}' \
  | ../scripts/nissth-bridge --json-stdin
```

Inside an MCP-aware client, call `Nissth_Status` first — it tells you whether the jar is built and which bindings are registered.

## Why this is so thin

The shim is intentionally a 100-line forwarder. Every piece of behavior lives in the Java CLI:
- Argument parsing, scope.extra coercion, exit codes — `NissthBridgeCli.java`
- Tool dispatch, manifest loading, error shape — `ToolDispatcher.java`
- Report writing + STALE-flipping — `ReportWriter.java`, `StaleFlipper.java`

If a new tool lands in the binding, the shim does NOT need to change — `Nissth_Gateway` can already invoke it via the JSON command grammar. `Nissth_Verify`'s named-operation map (`compilation`/`migrations`) is the only place the shim hard-codes tool names.

## Pointers

- Bridge contract: `CLAUDE.md` §11 + `Bindings/_schemas/bridge-command.schema.json`
- CLI surface: `Bindings/SpringBoot/scripts/nissth-bridge --help`
- Tool catalog: `Bindings/SpringBoot/README.md`
