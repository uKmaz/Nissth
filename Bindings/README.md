# Bindings — Nissth Diagnostic Bridge implementations

This directory holds **per-stack implementations** of the Nissth Diagnostic Bridge. Every binding speaks the same JSON command grammar (`{tool, mode, scope, output}`) and produces reports under `AgentReports/Bridge/` with the same mandatory frontmatter. What changes per binding is the **tool catalog** and the **language/runtime** the implementation is written in.

The contract is owned by Nissth core (see `CLAUDE.md` §11 and `_schemas/bridge-command.schema.json`). Bindings consume the contract; they never modify it.

---

## The per-stack-binding model

| Layer | Owner | Lives in |
|:---|:---|:---|
| **Contract** (command grammar, report frontmatter, error shape, freshness rules, stale-flip semantics) | Nissth core | `CLAUDE.md` §11 + `Bindings/_schemas/` |
| **Binding** (tool catalog + implementations for one stack) | This directory | `Bindings/<stack>/` |
| **Project-local tools** (project-specific custom diagnostics) | Each consuming project | `<project>/Tools/` |

A binding is a real subproject. Its language matches the stack:

| Stack | Binding directory | Language | Build system | Status |
|:---|:---|:---|:---|:---|
| Spring Boot (Java 17+ / Kotlin) | `Bindings/SpringBoot/` | Java | Maven | **Shipped** (Phase 05 closed 2026-05-17, 111/111 green) |
| Expo / React Native | `Bindings/Expo/` | TypeScript | npm | **Shipped** (Phase 06 closed 2026-05-18, 51/51 green) |
| PostgreSQL (general-purpose, diagnostic-only) | `Bindings/Postgres/` | TypeScript | npm | **Shipped** (Phase 07 closed 2026-05-18, 76/76 unit+contract green; 18 ITs skipped on hosts without Docker / `NISSTH_TEST_PG_URL`) |

Future stacks add a new subdirectory; no changes to the contract.

---

## Layout of a binding

```
Bindings/<stack>/
├── README.md                       ← Stack-specific tool catalog, install notes, scope.extra keys
├── src/                            ← Implementation source (plan-required to modify — HR#12)
├── tests/                          ← Binding self-tests (run against a fixture project)
└── <stack>.bridge.json             ← Tool registration manifest read by nissth-bridge
```

The `<stack>.bridge.json` manifest is what `nissth-bridge --list-tools` reads. It enumerates the tools this binding registers, their modes, the scope fields they consume, and the binding's version. The schema for this manifest is **per-binding** and not part of the cross-stack contract.

---

## How a consumer project pulls in a binding

A Nissth-using project (e.g., Süprüz at `Desktop/Supruz/`) does **not** copy a binding into its own tree. Three supported integration paths:

1. **Git submodule.** Pin to a tag.  Easy to update, easy to audit.
2. **Version-pinned dependency.** When the binding is published (Maven, npm, Go module, PyPI), the consumer declares a normal dependency. Best for stable bindings.
3. **`gradle includeBuild` / `npm link` / equivalent.** For local development on the binding itself.

Whichever path: the consumer never modifies the binding's source. Project-specific diagnostics live in the consumer's own `Tools/` directory.

---

## Cross-binding dispatcher

A unified `nissth-bridge` dispatcher at the repo root (`./nissth-bridge` POSIX / `./nissth-bridge.ps1` PowerShell) globs `Bindings/*/*.bridge.json` and routes a `<tool>` invocation to the binding that owns it. This is the canonical PATH entry; per-binding launchers under `Bindings/<stack>/scripts/nissth-bridge` remain as escape hatches.

The dispatcher requires every binding manifest to carry a `cli_entry` field — `{"runtime": "node" | "java-jar", "path": "<rel-to-binding-root>"}` — telling it how to spawn the binding's CLI. Tool names are expected to be unique across the framework; `--binding <stack>` disambiguates when two bindings register the same name (the only current case is `migration_status`, registered by both `spring-boot` and `postgres`).

See `Tools/nissth-bridge/README.md` for the dispatcher's full flag reference and conflict-resolution semantics, and `CLAUDE.md` §11.15 for the framework-level spec.

---

## Adding a new binding

1. Pick a stack id (snake_case, hyphen-free preferred): `spring-boot`, `expo`, `postgres`, etc.
2. Create `Bindings/<StackId>/` with the four files in the layout above.
3. Implement at least one diagnostic tool end-to-end so the contract surface is exercised — typically `compile_verify` or its analog.
4. Validate every tool's report against `Bindings/_schemas/bridge-command.schema.json` (for input) and against the report frontmatter rules in `CLAUDE.md` §11.3.
5. Author a `Phase_NN_*.md` plan in the **Nissth repo's own** `ImplementationPlans/` covering the binding's first slice. HR#12 governs: binding source under `src/` is plan-required to modify.
6. Document the binding's `scope.extra` keys, tool catalog, and freshness sources in the binding's own README.
7. Add a `cli_entry` object to the binding's `.bridge.json` so the unified dispatcher can spawn the binding's CLI: `{"runtime": "node" | "java-jar", "path": "<rel-path>"}`. The next `./nissth-bridge --list-bindings` will pick up the new binding automatically.

---

## Pointers

- **Contract spec (prose):** `CLAUDE.md` §11
- **Contract spec (machine-readable):** `Bindings/_schemas/bridge-command.schema.json`
- **First binding plan:** `ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md` (closed 2026-05-17)
- **Second binding plan:** `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` (closed 2026-05-18)
- **Third binding plan:** `ImplementationPlans/Phase_07_Bridge_Postgres_FirstSlice.md` (closed 2026-05-18)
- **Action-tool strictness rule:** `CLAUDE.md` §11.7 (hard-enforce; no warn-and-proceed)
- **Stale-flip mechanism:** `CLAUDE.md` §11.4 (bridge reports auto-flip DBL artifacts to STALE on drift)
