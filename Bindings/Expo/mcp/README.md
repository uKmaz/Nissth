# nissth-bridge MCP shim (Expo binding)

Thin Node MCP server that forwards Model Context Protocol calls to the `nissth-bridge` CLI of the Expo binding. Implements `CLAUDE.md` §11.6 (the four MCP tool surface).

**Shim version:** `0.1.0` &nbsp;·&nbsp; **Runtime:** Node 20+ &nbsp;·&nbsp; **SDK:** `@modelcontextprotocol/sdk` v1.x

The shim is pure plumbing: every MCP tool call spawns the binding CLI (`dist/cli/index.js`) as a child process, captures stdout/stderr, and returns the result. No in-process state, no caching.

---

## Install

```bash
cd Bindings/Expo/mcp
npm install
```

The MCP SDK + zod are the only runtime deps. The binding itself must be built first (`npm run build` from `Bindings/Expo/`); the shim refuses to start if `dist/cli/index.js` is missing.

## Smoke test

After install + binding build:

```bash
cd Bindings/Expo/mcp
node smoke-test.mjs
```

The smoke test spawns the MCP server over stdio, lists registered tools, invokes each one against the in-repo fixture, and exits 0 / 1 with a final ALL CHECKS PASSED / FAILED line.

CLI-only equivalent (if you don't want to install the MCP deps yet):

```bash
cd ../  # back to Bindings/Expo/
./scripts/nissth-bridge --list-tools                           # 5 tools
./scripts/nissth-bridge --describe route_scaffold              # action contract
./scripts/nissth-bridge route_lens --scope.root_path tests/fixture
```

## MCP tools exposed

| MCP tool | Purpose | Inputs |
|:---|:---|:---|
| `Nissth_Gateway` | Forward an arbitrary `{tool, mode, scope, output}` command to the CLI | `command: BridgeCommand` |
| `Nissth_Verify` | Wrapped invocation for common verifications | `operation: "compilation" \| "doctor" \| "dependencies"`, `root_path?: string` |
| `Nissth_ReadReport` | Read a prior Bridge report by name / absolute path / `latest:<tool>` shortcut | `relativePath: string`, `maxChars?: number` |
| `Nissth_Status` | Health probe — installed bindings, recent reports, CLI availability | `recent?: number` |

`Nissth_Verify` operation mapping (Expo binding):

| Operation | Bridge tool | Why |
|:---|:---|:---|
| `compilation` | `expo_doctor_lens` | Expo binding has no dedicated compile_verify tool; expo-doctor's check battery is the project-health analog. |
| `doctor` | `expo_doctor_lens` | Synonym; explicitly named for the canonical Expo workflow. |
| `dependencies` | `dependency_audit` | npm dep hygiene + import-scan cross-check. |

## Register with Claude Code

Add to your MCP client's server list (paths are illustrative):

```json
{
  "mcpServers": {
    "nissth-bridge-expo": {
      "command": "node",
      "args": ["C:\\Users\\admin\\Desktop\\Nissth\\Bindings\\Expo\\mcp\\index.js"]
    }
  }
}
```

`Bindings/SpringBoot/mcp/` ships its own shim with the same four-tool surface; both can be registered side-by-side. The Gateway routes to whichever binding owns the named tool.

## Pointers

- **Contract spec:** `CLAUDE.md` §11.6
- **Binding CLI:** `../scripts/nissth-bridge` (POSIX) / `../scripts/nissth-bridge.ps1` (PowerShell)
- **Tool catalog:** `../README.md` § Tool catalog
- **Reference shim:** `../../SpringBoot/mcp/` (Phase 05 pattern this shim ports)
