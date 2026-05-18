# `nissth-bridge` — cross-binding dispatcher

The unified entry point for invoking any Nissth Diagnostic Bridge tool from any installed binding. Discovers `Bindings/*/*.bridge.json` manifests, builds a tool-name → binding map, and routes a `<tool>` invocation to the binding's CLI.

**Implements** the model already documented in `CLAUDE.md` §11.5: "The Bridge is invoked through a single binary, `nissth-bridge`, which dispatches to the correct binding based on the tool name." Phase 08 is what made that single binary real.

Plain JavaScript (no TypeScript, no transpile, no `node_modules/`). Zero runtime dependencies — pure Node 20+ stdlib (`fs`, `path`, `url`, `child_process`). Tests via Node's built-in `node --test` runner.

---

## Why this exists

After Phase 05/06/07, three bindings each shipped a launcher named `nissth-bridge` at `Bindings/<stack>/scripts/nissth-bridge`. Adding all three to PATH meant only one won. The unified dispatcher solves this:

- **One canonical PATH entry:** the repo-root `nissth-bridge` (POSIX) / `nissth-bridge.ps1` (PowerShell).
- **Per-binding launchers stay** as escape hatches for direct binding access. They're not expected to be on PATH alongside the unified launcher.

---

## Discovery model

The dispatcher globs `<repo-root>/Bindings/*/*.bridge.json` (skipping any directory whose name starts with `_`, e.g., `_schemas/`). Each `.bridge.json` is parsed; its `binding`, `binding_version`, `tools`, and `cli_entry` fields determine routing.

```
Bindings/
├── _schemas/                       ← skipped (starts with _)
├── Expo/expo.bridge.json           ← binding "expo",         cli_entry runtime "node"
├── Postgres/postgres.bridge.json   ← binding "postgres",     cli_entry runtime "node"
└── SpringBoot/spring-boot.bridge.json ← binding "spring-boot", cli_entry runtime "java-jar"
```

The `cli_entry` field (added by Phase 08) tells the dispatcher how to launch the binding:

| `runtime` | What the dispatcher spawns |
|:---|:---|
| `"node"` | `node <binding-root>/<cli_entry.path>` — for TypeScript-compiled-to-JS bindings (Expo, Postgres) |
| `"java-jar"` | `java -jar <binding-root>/<cli_entry.path>` — for Java-built bindings (Spring Boot) |

New runtimes can be added by extending `buildSpawnSpec()` in `dispatcher.js`.

---

## CLI surface

```
nissth-bridge <tool> [--binding <stack>] [tool-specific flags...]
nissth-bridge --list-bindings
nissth-bridge --list-tools [--binding <stack>]
nissth-bridge --describe <tool> [--binding <stack>]
nissth-bridge --help
```

| Flag | Purpose |
|:---|:---|
| `--list-bindings` | Print installed binding ids, one per line, alphabetical. |
| `--list-tools` | Print the union of tool names across all installed bindings, deduplicated, alphabetical. |
| `--list-tools --binding <stack>` | Restrict to one binding's catalog. |
| `--describe <tool>` | Print the binding manifest entry for `<tool>`. |
| `--describe <tool> --binding <stack>` | Disambiguate when `<tool>` is registered by multiple bindings. |
| `--binding <stack>` | Force routing to a specific binding. Used as a flag during dispatch. |
| `--help` / `-h` | Print usage. |
| `--dry-run` | (testability) Print `would exec: <command> <args>` instead of spawning. Used by `test.mjs`. |

Anything not in the table above is forwarded verbatim to the binding's CLI (which has its own flag surface — see each binding's README).

### Exit codes

Match `CLAUDE.md` §11.5:

| Code | Meaning |
|:---|:---|
| 0 | Success. |
| 2 | Parse/validate error (bad flags, **tool-name conflict** without `--binding`, missing required field). |
| 3 | Execute error (the binding's CLI failed to spawn). |
| 4 | Unknown tool, unknown binding. |
| 5 | Freshness contract violated (propagated from the binding's CLI). |

---

## Tool-name conflicts

Tool names are expected to be unique within the framework. When two bindings register the same name, the dispatcher refuses to guess:

```sh
$ nissth-bridge migration_status
Tool 'migration_status' is registered by multiple bindings: postgres, spring-boot. Use --binding <stack> to disambiguate.
$ echo $?
2
```

The current shipped bindings have one such conflict by design: **`migration_status`** is registered by both the SpringBoot binding (Phase 05 — reads Flyway/Liquibase tables of the Spring Boot project's database) and the Postgres binding (Phase 07 — reads the same tables but as a general-purpose Postgres diagnostic). The two implementations have different freshness semantics (per-project vs. per-DB-instance), and renaming either would break their published contracts. The disambiguator is the solution:

```sh
nissth-bridge migration_status --binding spring-boot
nissth-bridge migration_status --binding postgres
```

When adding a new binding, prefer tool names that don't collide with existing ones (see `--list-tools` for the current catalog).

---

## Worked examples

### List what's installed

```sh
$ nissth-bridge --list-bindings
expo
postgres
spring-boot

$ nissth-bridge --list-tools | wc -l
14   # 15 total tool registrations, 1 deduped (migration_status)
```

### Run a Postgres diagnostic

```sh
export NISSTH_PG_URL='postgresql://nissth_ro:***@localhost:5432/mydb'
nissth-bridge schema_lens --mode tables --scope.package public
```

### Scaffold a new Expo route

```sh
nissth-bridge route_scaffold \
  --scope.root_path ../my-app \
  --scope.extra.route_path 'settings/account' \
  --scope.extra.component_name AccountScreen
```

### Verify Spring Boot compile state

```sh
nissth-bridge compile_verify --scope.root_path ../my-spring-app
```

### Inspect a single tool's contract

```sh
nissth-bridge --describe schema_lens
nissth-bridge --describe migration_status --binding spring-boot
```

### Disambiguated migration check

```sh
nissth-bridge migration_status --binding spring-boot --scope.root_path ../my-spring-app
nissth-bridge migration_status --binding postgres --scope.extra.connection_string 'postgresql://...'
```

---

## Adding a new binding

1. Author the binding under `Bindings/<NewStack>/`. Add a `<stack>.bridge.json` manifest at the binding root following the existing shape.
2. Include a `cli_entry` object: `{"runtime": "node"|"java-jar", "path": "<rel-to-binding-root>"}`.
3. Build the binding so the `cli_entry.path` resolves to a real artifact.
4. `nissth-bridge --list-bindings` should now include the new binding name on its next invocation. No dispatcher rebuild, no manifest cache to invalidate.

If the new binding registers a tool name that collides with an existing one, the dispatcher will surface the conflict on the next invocation of that tool — either rename the tool or document `--binding <stack>` as the access pattern.

---

## Tests

```sh
cd Tools/nissth-bridge
npm test
```

Tests use Node's built-in `node --test` runner. No `npm install` needed (zero deps).

Coverage:
- Manifest discovery (real `Bindings/*/*.bridge.json` files + synthetic `_fixtures/`).
- Tool-name routing (real bindings).
- Conflict detection (real `migration_status` conflict + synthetic fixtures).
- `--list-bindings`, `--list-tools`, `--describe` flag behavior.
- `--binding <stack>` override.
- Unknown-tool / unknown-binding error paths.
- Exit-code propagation.

The tests do NOT actually spawn binding CLIs — they use `--dry-run` to verify the dispatch decision without paying the binding-startup cost. Phase 08 §3.1 Step 11 covers end-to-end exec against a real binding.

---

## Pointers

- **Contract spec:** `CLAUDE.md` §11.5 (existing) + §11.15 (added by Phase 08).
- **Phase 08 plan:** `ImplementationPlans/Phase_08_Unified_Bridge_Dispatcher.md`.
- **Phase 08 snapshot Report:** `AgentReports/Reports/2026-05-18_phase-08-unified-dispatcher-snapshot.md`.
- **Per-binding READMEs:** `Bindings/SpringBoot/README.md`, `Bindings/Expo/README.md`, `Bindings/Postgres/README.md`.
- **Per-binding escape-hatch launchers:** `Bindings/<stack>/scripts/nissth-bridge`.
