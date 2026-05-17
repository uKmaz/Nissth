# Phase 06: Diagnostic Bridge — Expo First Slice

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_06_Bridge_Expo_FirstSlice
- **Authored:** 2026-05-17 by Claude (Opus 4.7)
- **Approved:** 2026-05-17 by Emre Uçmaz (verbal: "I liked the plan" — Claude session)
- **Depends on:** Phase_05_Bridge_SpringBoot_FirstSlice (closed 2026-05-17; provides the reference shape this plan ports to TypeScript/Expo)
- **Estimated scope:** Creates a new npm subproject at `Bindings/Expo/` (~25–35 TypeScript files + tests + a fixture Expo Router project + a thin Node MCP shim). Implements five tools (`route_lens`, `component_lens`, `dependency_audit`, `expo_doctor_lens`, `route_scaffold`) plus a `nissth-bridge` CLI dispatcher (binding-local) and an MCP wrapper exposing four MCP tools (mirroring Phase 05's `Nissth_Gateway` / `Nissth_Verify` / `Nissth_ReadReport` / `Nissth_Status`). Authors `CLAUDE.md` §8.2 Expo alongside the binding (per user choice on stack-rules placement, 2026-05-17). Adds one new directory under Nissth root (`Bindings/Expo/`); modifies `CLAUDE.md` (adds §8.2; renumbers §8 → §8.1 Spring Boot for symmetry) and `Bindings/README.md` (marks Expo row from "future" to "shipped"). No changes to Phase 05's Spring Boot binding code. No changes to consuming projects (Süprüz stays paused).

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the bridge contract is unchanged since Phase 05 close, no prior Expo binding exists, host toolchain (Node 20+, npm, npx) is present, and the Phase 05 reference binding still passes its self-build (so the "shape we are porting" is verified live, not assumed-from-memory).

### 1.1 Inputs to read

- **DBL:** none — Nissth itself has no DBL.
- **Bridge reports:** none authored for this plan yet. Phase 05's `compile_verify` and `endpoint_lens` produce reports the Phase 05 smoke test uses, but those are SpringBoot-specific; this plan does not consume them as inputs.
- **Source:**
  - `CLAUDE.md` §11 (Diagnostic Bridge contract — §11.2 grammar, §11.3 report contract, §11.4 stale-flip, §11.5 CLI, §11.6 MCP wrapper, §11.7 hard-enforce, §11.8 binding layout).
  - `CLAUDE.md` §8 (Spring Boot stack rules — read as the reference shape for the §8.2 Expo section this plan authors).
  - `Bindings/_schemas/bridge-command.schema.json` (the contract this binding implements, byte-for-byte unchanged).
  - `Bindings/README.md` (per-stack-binding model + Expo row in the stack table).
  - `Bindings/SpringBoot/spring-boot.bridge.json` (reference manifest shape for `expo.bridge.json`).
  - `Bindings/SpringBoot/README.md` (reference README shape for the Expo binding README).
  - `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/` (reference for `JsonCommandParser`, `ReportWriter`, `StaleFlipper`, `ToolDispatcher` semantics — port to TypeScript, not Java-to-Java translation).
  - `Bindings/SpringBoot/mcp/index.js` (reference for the per-binding MCP shim).
  - `Bindings/SpringBoot/mcp/smoke-test.mjs` (reference for the per-binding runtime smoke harness).
  - `Bindings/SpringBoot/tests/fixture/` (reference fixture shape — what an "end-to-end-testable minimal target project" looks like).
  - `ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md` (reference plan; this plan mirrors its step taxonomy).
  - `AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md` (load-bearing patterns; "Implications for downstream bindings → Expo" section is the seed for this plan's tool catalog).
- **StatusUpdate.md:** latest entry as of plan authoring — `2026-05-17 — SESSION CLOSE (Phase 05 + MCP smoke; git correction; commit + push)`.

### 1.2 Diagnostic actions

> Prefer Bridge tools and DBL reads over raw source greps. The Expo binding's diagnostic tools do not exist yet; only Phase 05's `compile_verify` / `endpoint_lens` / etc. are installed, and they are SpringBoot-only. This plan's Pre-Flight falls back to filesystem checks and direct command invocations.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm bridge contract is unchanged since Phase 05 close | `Read Bindings/_schemas/bridge-command.schema.json` (full file) + `Read CLAUDE.md §11.2` | full files | Pre-condition: this binding implements the same frozen contract; if the schema or §11.2 changed since 2026-05-15, that is a separate plan. Expected: byte-for-byte identical to Phase 05 close state. |
| 2 | Confirm no Expo binding exists | `ls Bindings/` | `Bindings/` directory | Avoid overwriting partial prior work. Expected: `README.md`, `_schemas/`, `SpringBoot/` only. |
| 3 | Confirm Phase 05 reference binding still green | `cd Bindings/SpringBoot && ./mvnw clean verify -U -B` | binding root | The "shape we are porting" must be live, not assumed. Expected: 111/111 tests PASS (104 Surefire + 7 Failsafe); jar at `target/nissth-bridge-0.1.0.jar` (~5.83 MB). If this fails, STOP — Phase 05 regressed and Phase 06 cannot start. |
| 4 | Confirm Node toolchain | `node --version` | host | Need Node 20+ per Phase 05 MCP shim baseline. Expected: ≥ v20.0.0. |
| 5 | Confirm npm + npx availability | `npm --version` + `npx --version` | host | Both required: npm for the binding's own deps; npx for `expo_doctor_lens` to spawn `npx expo-doctor` against target projects. |
| 6 | Confirm no `nissth-bridge` from Expo binding already on PATH | `which nissth-bridge` (Bash) / `Get-Command nissth-bridge` (PS) | host | Phase 05's `Bindings/SpringBoot/scripts/nissth-bridge` may already be on PATH — that is acceptable. What is forbidden: an `Bindings/Expo/scripts/nissth-bridge` already existing (would mean a partial prior install). |
| 7 | Confirm `AgentReports/Bridge/` exists (from Phase 05) | `ls AgentReports/Bridge/` | `AgentReports/Bridge/` directory | Phase 05 created it; Phase 06 writes to it too. If missing, Phase 05 environment was wiped — investigate before continuing. |
| 8 | Confirm `CLAUDE.md` §8 still exists and contains Spring Boot rules | `Read CLAUDE.md` §8 section header (line range) | `CLAUDE.md` | Reference for §8.2 Expo authoring. Expected: §8.1 vs §8 — currently `§8. Stack Bindings — Spring Boot` (un-numbered sub-section). Plan must renumber as part of §3 (see §3 Step 4). |
| 9 | Confirm an actual Expo project's directory layout convention is current | `Read https://docs.expo.dev/router/introduction/` mentally / from existing knowledge; cross-check with `npx create-expo-app` output if uncertain | external | The fixture (§3 Step 17) must follow current Expo Router conventions (`app/`, `_layout.tsx`, file-based routing). If the agent's training-data understanding of Expo Router has drifted from 2026 reality, fix the fixture before tests author. Expected: Expo Router v3+ with `app/` directory + `expo-router` package + `app/_layout.tsx` as the root layout. |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Does `Bindings/_schemas/bridge-command.schema.json` match the Phase 05 close state? | yes — unchanged since 2026-05-15 | yes — file exists at `Bindings/_schemas/bridge-command.schema.json`, last touched in commit `f6f9cfc` (initial framework); no edits since | ✅ yes |
| Does `Bindings/Expo/` exist? | no — only `README.md`, `_schemas/`, `SpringBoot/` in `Bindings/` | no — `ls Bindings/` returned exactly `README.md`, `SpringBoot/`, `_schemas/` | ✅ yes |
| Does `cd Bindings/SpringBoot && ./mvnw clean verify -U -B` still return 111/111 PASS? | yes — 104 Surefire + 7 Failsafe; jar present at expected size | **CANNOT VERIFY** — `java` not on PATH on this host; `Get-Command java` returns nothing; `C:\Program Files\Java\` and `C:\Program Files\Eclipse Adoptium\` both absent | ⚠️ unverifiable — host lacks Java toolchain |
| Is Node 20+ available? | yes — Node v22.18.0 per `2026-05-17 — MCP shim runtime smoke PASS` status entry | **no** — `node --version` not found in Bash or PowerShell; `C:\Program Files\nodejs\`, `%LOCALAPPDATA%\Programs\nodejs\`, `%LOCALAPPDATA%\nvm\`, `%APPDATA%\nvm\` all absent | ❌ no |
| Are npm + npx available? | yes — bundled with Node | **no** — bundled with Node; absent for the same reason | ❌ no |
| Is `Bindings/Expo/scripts/nissth-bridge` already on PATH or in tree? | no — fresh slate | no — `Bindings/Expo/` does not exist | ✅ yes |
| Does `AgentReports/Bridge/` exist? | yes — Phase 05 created it | yes — contains one carry-over file (`entity_lens_2026-05-16T233810Z.md`) | ✅ yes |
| Does `CLAUDE.md` §8 contain the Spring Boot rules and is currently un-numbered sub-section style (`§8. Stack Bindings — Spring Boot`)? | yes — confirmed at plan-authoring time | yes — confirmed by Read prior to authoring | ✅ yes |
| Does the agent's understanding of Expo Router conventions match current docs (app/ + _layout.tsx + file-based)? | yes — Expo Router v3+ as of 2026 | yes — from training-data knowledge (cutoff January 2026); not re-verified live this turn since execution is blocked upstream | ✅ yes (provisional) |

**Stop condition fired (2026-05-17):** Two `Match? = no` rows (Node, npm/npx) and one `unverifiable` row (Phase 05 regression check requires Java). Root cause is host context, not plan-authoring error: the plan was authored from the prior session's status entries which described host `C:\Users\Ucmaz pc\Desktop\Nissth` (had Node v22.18.0 + Java 17 + Docker). The current execution host is `C:\Users\admin\Desktop\Nissth` on machine `DESKTOP-DQBFP0O` (user `admin`) — none of those tools are installed. The session-close entry (2026-05-17 last entry) explicitly flagged this scenario as a hypothetical the user asked about: "moving to a PC without Docker/Node ... binding remains fully usable for build + 104 unit tests + 4 of 5 Bridge tools + CLI launcher. Lost: ... MCP integration with Claude Code. If the other PC has Java 17 + internet (Maven Wrapper bootstraps), nothing else is required." On this host neither Java nor Node is present.

**Resolution path:** §3 execution has not started; the plan itself is valid. Per Phase 05's `2026-05-15` Gradle-row precedent, in-session resolution does NOT require a `Verified: FAIL` status entry (Phase 05 §1.3 Resolution: "no `Verified: FAIL` status entry is required because §3 execution had not yet begun"). The user picks one of three paths and the plan continues or pauses accordingly: (a) install Node 20+ on this host and re-run pre-flight; (b) switch to the Node-capable PC and resume there; (c) defer Phase 06 and pick a different backlog item that is doable on a docs-only host (Task #15 — engineer-facing top-level README — fits this constraint). All three are documented; the user's choice is recorded in the next status entry.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/` | contents | `README.md`, `_schemas/`, `SpringBoot/` |
| `Bindings/Expo/` | exists | no |
| `CLAUDE.md` §8 | sub-section numbering | un-numbered (`## 8. Stack Bindings — Spring Boot`) |
| `CLAUDE.md` §8.2 | exists | no |
| `Bindings/README.md` Expo row | status implication | "future stack" — directory does not exist |
| `AgentReports/Bridge/` | exists | yes (from Phase 05) |
| `Bindings/SpringBoot/target/nissth-bridge-0.1.0.jar` | exists, size | yes, 5,835,135 bytes ± rebuild jitter |
| Bridge command schema | bytes | unchanged since 2026-05-15 |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/Expo/package.json` | exists | yes — npm subproject, `"private": true`, `"type": "module"`, scripts: `build`, `clean`, `test`, `lint`, `prepare` |
| `Bindings/Expo/tsconfig.json` | exists | yes — `target: ES2022`, `module: NodeNext`, `outDir: dist`, `strict: true`, `incremental: true` |
| `Bindings/Expo/expo.bridge.json` | exists | yes — manifest registering the five tools with their modes, scope keys, scope_extra keys, hard-enforce contracts |
| `Bindings/Expo/README.md` | exists | yes — tool catalog + scope.extra keys + install + MCP integration pointer (mirrors `Bindings/SpringBoot/README.md` shape) |
| `Bindings/Expo/src/core/` | contents | `types.ts`, `JsonCommandParser.ts`, `ReportWriter.ts`, `StaleFlipper.ts`, `ToolDispatcher.ts`, `BindingManifest.ts`, `SubprocessRunner.ts`, `BridgeError.ts` (TypeScript ports of Phase 05's `core/`) |
| `Bindings/Expo/src/tools/` | contents | `RouteLens.ts`, `ComponentLens.ts`, `DependencyAudit.ts`, `ExpoDoctorLens.ts`, `RouteScaffold.ts` |
| `Bindings/Expo/src/cli/index.ts` | exists | yes — CLI entrypoint with shebang `#!/usr/bin/env node`; flag parser; `--list-tools`, `--describe`, `--json-stdin` discovery modes |
| `Bindings/Expo/mcp/` | contents | `index.js` (Node MCP server shim, copying Phase 05 pattern), `package.json`, `README.md`, `smoke-test.mjs` |
| `Bindings/Expo/scripts/nissth-bridge` | exists (POSIX) | yes — wraps `node <dist>/cli/index.js "$@"`; resolves dist path relative to script location |
| `Bindings/Expo/scripts/nissth-bridge.ps1` | exists (PowerShell) | yes — equivalent |
| `Bindings/Expo/tests/fixture/` | contents | Minimal Expo Router project: `app/_layout.tsx`, `app/index.tsx`, `components/Greeting.tsx`, `package.json` (expo, expo-router, react, react-native, react-native-safe-area-context, react-native-screens), `app.json`, `tsconfig.json`, plus `tests/fixture/DBL/APIIndex/routes.md` (intentionally-stale synthetic DBL artifact exercising the stale-flip path) |
| `Bindings/Expo/tests/unit/` | contents | Jest unit tests for `core/*` (one `*.test.ts` per core class) |
| `Bindings/Expo/tests/integration/` | contents | One `*.it.test.ts` per tool — five files |
| `Bindings/Expo/tests/contract/` | contents | `RouteScaffoldContract.test.ts` (atomicity + rollback), `SchemaValidation.test.ts` (every report's frontmatter validates against `$defs.reportFrontmatter`) |
| `CLAUDE.md` §8.1 | section header | renamed from `§8. Stack Bindings — Spring Boot` to `§8.1. Spring Boot` (sub-section under a new `§8. Stack Bindings` header) |
| `CLAUDE.md` §8.2 | exists | yes — Expo stack-rules section (identity, layout, build & test commands, DBL mapping, forbidden patterns, verification protocol, common discovery patterns, route ripple, mandatory inputs for new Expo projects) — mirrors §8.1 shape |
| `CLAUDE.md` §11.12 | content | adds a parallel "Phase 06 first slice" paragraph naming the five Expo tools; does not modify the existing Phase 05 paragraph |
| `Bindings/README.md` stack table | Expo row | "Bindings/Expo/" cell no longer implies "future"; cross-reference to this plan added |
| `nissth-bridge --list-tools` (run from `Bindings/Expo/scripts/`) | output | Lists exactly five Expo tools |
| `nissth-bridge --describe route_scaffold` | output | Prints route_scaffold's hard-enforce contract (atomic route + test write) |
| `route_scaffold` invoked with a read-only test-file directory | exit code | `5` (freshness/contract violation; no `app/<path>.tsx` written) |
| `AgentReports/Bridge/route_lens_<ts>.md` exists post-IT | frontmatter | passes the `$defs.reportFrontmatter` schema check; `binding: "expo"`, `binding_version: "0.1.0"`, `contract_version: 1` |
| `Bindings/Expo/tests/fixture/DBL/APIIndex/routes.md` post-IT | frontmatter `last_regenerated` | contains `STALE — superseded by AgentReports/Bridge/<route_lens report>` (verifies stale-flip on a non-Java stack) |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [ ] **Step 1. Scaffold npm subproject.** **Files:** `Bindings/Expo/package.json`, `Bindings/Expo/tsconfig.json`, `Bindings/Expo/.gitignore`, `Bindings/Expo/jest.config.mjs`, `Bindings/Expo/src/{core,tools,cli}/.gitkeep`, `Bindings/Expo/tests/{unit,integration,contract,fixture}/.gitkeep`. **Operation:** add. **`package.json` deps:** `dependencies`: `ajv` (JSON Schema), `ts-morph` (TypeScript AST), `yaml` (frontmatter parse/write); `devDependencies`: `typescript`, `@types/node`, `jest`, `@types/jest`, `ts-jest`, `tsx` (for running TS directly during dev). **`scripts`:** `build: "tsc -p ."`, `clean: "rimraf dist .tsbuildinfo node_modules/.cache"` (and `rimraf` added as dev dep), `test: "jest"`, `test:unit: "jest tests/unit"`, `test:integration: "jest tests/integration"`, `test:contract: "jest tests/contract"`, `prepare: "npm run build"`. **`.gitignore`:** `dist/`, `node_modules/`, `.tsbuildinfo`, `coverage/`, `*.log`. **Acceptance:** `cd Bindings/Expo && npm install && npm run build` exits 0 against the empty source tree (tsc with no input files is valid; produces empty `dist/`). `npm test` exits 0 (no test files yet → Jest reports "no tests found" with `passWithNoTests: true` in jest.config).
- [ ] **Step 2. Author binding manifest.** **File:** `Bindings/Expo/expo.bridge.json`. **Operation:** add. **Content:** JSON object with `binding: "expo"`, `binding_version: "0.1.0"`, `contract_version: 1`, `language: "typescript"`, `node_min: 20`, `build_tool: "npm"`, `description`, and `tools` array listing the five tools each with `name`, `kind` (diagnostic|action), `modes`, `scope_keys` (top-level keys consumed), `scope_extra_keys` (binding-specific keys), `description`, `enforces` (list of CLAUDE.md sections). **`scope_extra_keys_doc`** sub-object for `route_scaffold` documenting `route_path`, `component_name`, `has_params`, `params_type`, `force_create_layout`. **Acceptance:** Loadable as JSON; `JSON.parse` succeeds; `BindingManifest.ts` (Step 7) returns all five tool entries.
- [ ] **Step 3. Author binding README.** **File:** `Bindings/Expo/README.md`. **Operation:** add. **Content:** Mirrors `Bindings/SpringBoot/README.md` shape — header (binding id, version, language, build), tool catalog table (one row per tool, modes column, scope-keys column, freshness-source column), `scope.extra` keys table for `route_scaffold`, default route-component scaffold template shown inline (the `function <Name>()` stub the action emits), install instructions (npm install + npm run build + PATH setup for `nissth-bridge`), MCP integration pointer to `mcp/`, pointers to `CLAUDE.md` §11 + §8.2. **Acceptance:** Renders cleanly in a Markdown preview; every tool's enforcement contract (for action tools) is stated explicitly; mirrors the structure of `Bindings/SpringBoot/README.md` (same H2/H3 hierarchy).
- [ ] **Step 4. Author CLAUDE.md §8.2 Expo (and renumber §8 → §8.1).** **File:** `CLAUDE.md`. **Operation:** modify. **Edits:** (a) Renumber the existing `## 8. Stack Bindings — Spring Boot` to `## 8. Stack Bindings`, then nest the existing content under a new `### 8.1 Spring Boot` sub-header — preserving every existing §8.1–§8.9 sub-section as §8.1.1–§8.1.9 (or equivalent depth shift; choose whichever keeps the diff minimal — see Step 4's freshness note). (b) Add `### 8.2 Expo` immediately after §8.1's last sub-section, containing nine sub-sections parallel to §8.1: §8.2.1 Stack identity (Expo SDK 50+, React Native 0.74+, TypeScript 5+, npm + Jest, Detox optional for E2E); §8.2.2 Conventional layout (`app/` for routes via Expo Router, `components/`, `hooks/`, `assets/`, `__tests__/`, `app.json`, `tsconfig.json`); §8.2.3 Build & test commands (`npx expo start`, `npm test` for Jest, `npx tsc --noEmit`, `npx expo-doctor`, `npx expo prebuild` if native deps); §8.2.4 DBL mapping (`Summary` per `app/` directory + per `components/` group; `DependencyMap` for `app ↔ components ↔ hooks` boundary; `APIIndex` for Expo Router route table; **no `SchemaIndex`** — Expo apps typically delegate persistence to backend); §8.2.5 Forbidden patterns (no `npm install --legacy-peer-deps` without a one-line justification comment in package.json; no committed `node_modules/`, `.expo/`, `dist/`; no `package-lock.json` deletes without rationale; no untyped routes — every Expo Router screen with params declares its `Params` type; no `expo-cli` (deprecated 2024) — use `npx expo`; no `@react-navigation/*` for top-level navigation — Expo Router supersedes; no inline `require()` of platform-specific code — use `.ios.tsx` / `.android.tsx` resolution); §8.2.6 Verification protocol — freshness guarantee (lockfile in sync with package.json via `npm ci`; `node_modules/` hash matches lockfile; `.tsbuildinfo` cleared before `tsc --noEmit` for fresh type check); §8.2.7 Common discovery patterns (table mapping questions like "what routes exist?" → DBL/APIIndex/routes.md → fallback `Glob 'app/**/*.tsx'`); §8.2.8 Route ripple — Hard Rule #11 specialization (new screen route ⇒ matching test file under `__tests__/<same-path>.test.tsx`; both must appear in the closing status entry's Doc Sync line; analog of §8.1.9 entity ripple); §8.2.9 Mandatory inputs for new Expo projects under Nissth (parallel to §8.1.8 — SRS+SDD; Phase_00_DBL_Bootstrap; populate `APIIndex/routes.md` from `app/` scan; no baseline-migration step since no DB). **Operation note:** if the renumber-and-nest in (a) would touch >100 lines of CLAUDE.md, restructure as: keep §8 header as-is (`§8. Stack Bindings — Spring Boot`) and add §8.2 Expo as a sibling header (`## 8.2. Stack Bindings — Expo`). This minimizes churn at cost of symmetry. The agent chooses based on actual diff size at execution time; record the choice in the §1.3 Findings table inline as a comment. **Acceptance:** `Read CLAUDE.md §8.2` returns the new section with all nine sub-sections; existing §8 / §8.1 Spring Boot content unchanged in semantics (only the section-numbering header moved); no other CLAUDE.md sections touched.
- [ ] **Step 5. Implement `core/types.ts` + `JsonCommandParser`.** **Files:** `src/core/types.ts` (TypeScript types mirroring Phase 05's `BridgeCommand`, `ToolResult`, `BridgeError`, `BindingManifest`, `ReportContext`), `src/core/JsonCommandParser.ts`. **Operation:** add. **Behavior:** `JsonCommandParser.parse(input: string | object): BridgeCommand` — validates structurally against `Bindings/_schemas/bridge-command.schema.json` using `ajv` with `draft-2020-12` support (`ajv` requires `ajv-formats` + the 2020-12 meta-schema; wire those in too). Returns typed `BridgeCommand` or throws `BridgeError` with `stage: "parse" | "validate"`. **Acceptance:** Unit tests under `tests/unit/JsonCommandParser.test.ts` cover: malformed JSON → `stage="parse"`; missing `tool` → `stage="validate"`; unknown top-level `scope` key → `stage="validate"` (per schema's `additionalProperties: false`); valid command → typed `BridgeCommand` with all fields accessible.
- [ ] **Step 6. Implement `ReportWriter`.** **File:** `src/core/ReportWriter.ts`. **Operation:** add. **Behavior:** Writes a Markdown file to `<repo-root>/AgentReports/Bridge/<file_name | tool_<ISO8601-compact>>.md`. Constructs YAML frontmatter from a `ReportContext` (tool, mode, binding="expo", binding_version (read from `package.json`'s `version`), generated_at, scope echo, freshness, contract_version=1) using the `yaml` package for stable serialization. Validates frontmatter against `$defs.reportFrontmatter` via the same `ajv` instance from Step 5. **Acceptance:** Unit test writes a report; result parses as valid YAML + Markdown; frontmatter validates; freshness fields present and non-empty; tmp-dir-based test cleans up after itself.
- [ ] **Step 7. Implement `StaleFlipper`.** **File:** `src/core/StaleFlipper.ts`. **Operation:** add. **Behavior:** Given a Bridge report path and a project root, scans `<project_root>/DBL/**/*.md` for artifacts whose `covers` overlaps the report's scope (glob match via `minimatch` or simple substring on the scope path). For each match, runs a tool-supplied drift-check callback `(dblArtifact, bridgeReport) => boolean`. On drift, rewrites the DBL artifact's frontmatter `last_regenerated` field to `STALE — superseded by AgentReports/Bridge/<report basename>`. Idempotent (re-running against an already-STALE artifact is a no-op). **Acceptance:** Unit test: drift detected → frontmatter rewritten with STALE marker; no drift → frontmatter untouched; missing DBL directory → silent no-op (no error). Test cases mirror Phase 05's `StaleFlipperTest`.
- [ ] **Step 8. Implement `BindingManifest` + `ToolDispatcher`.** **Files:** `src/core/BindingManifest.ts`, `src/core/ToolDispatcher.ts`. **Operation:** add. **`BindingManifest`:** reads `Bindings/Expo/expo.bridge.json` from a path resolved relative to the running CLI's location (`__dirname` traversal). Exposes `tools: ToolDescriptor[]`, `bindingVersion: string`. **`ToolDispatcher`:** maps tool names to handler classes (registered via a constructor argument or a registry pattern). `dispatch(cmd: BridgeCommand): Promise<ToolResult>` invokes the matching handler; unknown tool name → throws with `stage="execute"` and an exit-4-equivalent error code. **Acceptance:** Unit tests: dispatch by name to a stub handler returns the stub's output; unknown tool → `stage="execute"`, `error_code="unknown_tool"`; manifest contains the five expected tools.
- [ ] **Step 9. Implement `route_lens`.** **File:** `src/tools/RouteLens.ts`. **Operation:** add. **Behavior:** Filesystem scan of `<scope.root_path | "app"><scope.package as sub-path (Expo Router uses paths, not packages)>` for `*.tsx` (or `*.ts`) files. For each route file, classifies by Expo Router conventions: static route (`app/profile.tsx`), dynamic route (`app/[id].tsx`), catch-all (`app/[...rest].tsx`), group (`app/(tabs)/`), layout (`_layout.tsx`). Uses `ts-morph` to AST-parse each route file: extracts the default-exported function name, props/params type, any `useLocalSearchParams` calls. Emits route table: route path (URL form), file path, component name, params type, layout parent, classification. After report write, invokes `StaleFlipper` against `<project_root>/DBL/APIIndex/*.md` whose `covers` overlaps the scanned `app/` subtree; drift-check callback compares the live route set to the DBL artifact's documented route set. **Acceptance:** Integration test `tests/integration/RouteLens.it.test.ts` against fixture's `app/` (one index route + one layout): report lists exactly one user-facing route + one layout, all fields populated; running against a project with no `app/` directory writes an empty-but-valid report (no error).
- [ ] **Step 10. Implement `component_lens`.** **File:** `src/tools/ComponentLens.ts`. **Operation:** add. **Behavior:** `ts-morph` AST-scan of `**/*.{ts,tsx}` under `scope.root_path` (default: `components/`) optionally filtered by sub-path via `scope.package`. Discovers React components by these signals: (a) default export of a function returning JSX/React.Element; (b) `const X = (props: ...) => <...>`; (c) `function X({...}: ...): JSX.Element`; (d) explicit `React.FC` / `FunctionComponent` typing. Per component: name, file path, props type (if declared), exported (default vs named), hook usage list (calls matching `/^use[A-Z]/`). After report write, invokes `StaleFlipper` against `<project_root>/DBL/Summaries/*.md`. **Acceptance:** Integration test against fixture's `components/Greeting.tsx`: report lists exactly one component with name, file path, props type populated. A second test against a directory with zero components writes an empty-but-valid report.
- [ ] **Step 11. Implement `dependency_audit`.** **File:** `src/tools/DependencyAudit.ts`. **Operation:** add. **Behavior:** Parses `<scope.root_path>/package.json` + `package-lock.json` (or `yarn.lock` / `pnpm-lock.yaml` — read whichever exists; document precedence in code comment). Cross-checks with import scan of `**/*.{ts,tsx,js,jsx}` (via `ts-morph` import-declaration nodes + regex for dynamic `require()` / `import()`). Classifies each declared dep as: `used` (imported somewhere), `unused` (declared but no import), `dev_in_prod` (imported in `src/` or `app/` but listed only in `devDependencies`); classifies each imported module as: `declared`, `missing` (imported but not in any deps section), `transitive` (in `node_modules/` but not declared — common offender). Emits findings table. **Acceptance:** Integration test against fixture: report classifies fixture's `expo`, `expo-router`, `react`, `react-native` as `used`; injects a temp `lodash` to fixture's package.json, re-runs, sees `unused: ["lodash"]`; cleans up.
- [ ] **Step 12. Implement `expo_doctor_lens`.** **File:** `src/tools/ExpoDoctorLens.ts`. **Operation:** add. **Behavior:** Spawns `npx --yes expo-doctor` against `<scope.root_path>` via `SubprocessRunner` (mirroring Phase 05's `DefaultSubprocessRunner`). Captures stdout + stderr + exit code. Parses output sections — expo-doctor emits a series of named checks each with PASS/WARN/FAIL — into a findings table: check name, status, message. **Freshness contract:** if subprocess invocation fails to spawn (e.g., npx not on PATH, network down for `--yes` fetch of expo-doctor), returns `stage="execute"` with `error_code="expo_doctor_unavailable"` and a one-line remediation hint. Does NOT return a PASS report from cached output — every invocation actually runs the subprocess. **Acceptance:** Integration test against fixture: report contains expo-doctor's checks (at least 5; expo-doctor's default check set is stable); status field populated. A second test simulates network failure (via `EXPO_OFFLINE=1` or a stubbed `SubprocessRunner`) → `error_code="expo_doctor_unavailable"`, exit code 3.
- [ ] **Step 13. Implement `route_scaffold` (action, hard-enforce).** **File:** `src/tools/RouteScaffold.ts`. **Operation:** add. **Inputs (via `scope.extra`):** `route_path` (e.g., `"settings/account"` — relative under `app/`), `component_name` (e.g., `"AccountScreen"`), `has_params` (default `false`), `params_type` (optional, e.g., `"{ id: string }"`), `force_create_layout` (default `false` — if a parent `_layout.tsx` is missing, error out unless this is `true`). **Behavior, atomic:** (a) resolve `app/<route_path>.tsx` and `__tests__/<route_path>.test.tsx` paths; (b) verify parent `_layout.tsx` exists (or `force_create_layout=true` triggers a stack-layout scaffold as a THIRD atomic write); (c) refuse if either target file already exists → `stage="validate"`, `error_code="route_already_exists"`; (d) construct route file content (a `function <component_name>() { return <View>...</View>; }` stub with imports for `View`, `Text`, optionally `useLocalSearchParams` when `has_params=true`); (e) construct test file content (a Jest smoke test rendering the component); (f) write BOTH files (or all three with layout) inside a try-block; (g) if any write fails, delete any files that were successfully written this call → exit 5 with `stage="execute"`, `error_code="hard-enforce_route_pair_atomicity"`, `error` field naming which file failed. **Acceptance:** Integration test (success path) — both files exist with expected content, exit 0. Contract test (failure-simulation, in Step 19) — read-only `__tests__/` dir → exit 5, no `app/<path>.tsx` written (verify via fs.stat). Contract test (collision) — pre-create the route file, invoke → exit 2, no writes.
- [ ] **Step 14. Implement `nissth-bridge` CLI (`src/cli/index.ts`).** **File:** `src/cli/index.ts`. **Operation:** add. **Behavior:** Node entrypoint with shebang `#!/usr/bin/env node`. Parses CLI args per `CLAUDE.md` §11.5: flag form with `--scope.<key> <value>` flattening (dotted notation; nested for `scope.extra.<key>`), `--mode <m>`, `--output.<key> <value>`, `--json-stdin`, `--list-bindings`, `--list-tools` (optionally `--binding expo`), `--describe <tool>`. Constructs `BridgeCommand`, dispatches via `ToolDispatcher`, prints report path (destination=file) or body (destination=return) to stdout. Exit codes per §11.5: 0/2/3/4/5. **Acceptance:** Unit tests in `tests/unit/cli.test.ts`: each flag form produces correct `BridgeCommand`; `--list-tools` lists exactly five Expo tools; `--describe route_scaffold` prints the hard-enforce contract; unknown tool → exit 4; freshness violation → exit 5; invalid scope key → exit 2.
- [ ] **Step 15. Build `nissth-bridge` launcher scripts.** **Files:** `Bindings/Expo/scripts/nissth-bridge` (POSIX), `Bindings/Expo/scripts/nissth-bridge.ps1` (PowerShell). **Operation:** add. **Behavior:** POSIX: `#!/usr/bin/env sh` + resolve script dir + `exec node "$DIR/../dist/cli/index.js" "$@"`. PowerShell: `& node "$PSScriptRoot/../dist/cli/index.js" @args`. Resolve `dist/cli/index.js` relative to the script's own location so the launcher works from any cwd. **Acceptance:** From any directory containing `Bindings/`, `./Bindings/Expo/scripts/nissth-bridge --list-tools` works (POSIX); `.\Bindings\Expo\scripts\nissth-bridge.ps1 --list-tools` works (PowerShell). Both return the same five tool names.
- [ ] **Step 16. Implement MCP wrapper (Node shim, per-binding).** **Files:** `Bindings/Expo/mcp/package.json`, `Bindings/Expo/mcp/index.js`, `Bindings/Expo/mcp/README.md`, `Bindings/Expo/mcp/smoke-test.mjs`. **Operation:** add. **Behavior:** Copy Phase 05's shim shape verbatim, swap CLI path from SpringBoot's launcher to `Bindings/Expo/scripts/nissth-bridge` (resolved relative to shim location). Registers four MCP tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`); each shells out to the binding's CLI. `Nissth_Verify` maps `operation: "compilation"` → spawns `npx tsc --noEmit` against `scope.root_path` (the Expo binding's analog to SpringBoot's compile_verify); `operation: "doctor"` → `expo_doctor_lens` invocation. `smoke-test.mjs` mirrors Phase 05's runtime smoke: spawns `index.js`, sends `tools/list` (expects 4 MCP tools) + each tool call against the fixture, prints pass/fail tally, exits 0/1. **Acceptance:** `cd Bindings/Expo/mcp && npm install && node smoke-test.mjs` exits 0 with "ALL CHECKS PASSED"; each MCP tool returns `isError=false` and a real report path.
- [ ] **Step 17. Create fixture Expo project.** **Directory:** `Bindings/Expo/tests/fixture/`. **Operation:** add. **Contents:** Minimal Expo Router project: `package.json` (deps: `expo` ^50.0, `expo-router` ^3.0, `react`, `react-native`, `react-native-safe-area-context`, `react-native-screens`; devDeps: `@types/react`, `typescript`); `app.json` (Expo config: `expo.name`, `expo.scheme`, `expo.plugins: ["expo-router"]`); `tsconfig.json`; `app/_layout.tsx` (Stack from expo-router with `<Stack.Screen name="index" />`); `app/index.tsx` (one `function IndexScreen() { return <View>...</View>; }`); `components/Greeting.tsx` (one function component with typed props); `__tests__/index.test.tsx` (one Jest smoke test for the IndexScreen route); `DBL/APIIndex/routes.md` (synthetic DBL artifact with intentionally-wrong route data to exercise stale-flip — declares a non-existent `app/settings.tsx` route that route_lens will not find). **Acceptance:** `cd Bindings/Expo/tests/fixture && npm install --no-fund --no-audit` succeeds; `npx tsc --noEmit` passes (catches the fixture's own type errors, not the binding's). Don't run `expo start` — not needed for the test surface and would block on Metro bundler waiting for a device.
- [ ] **Step 18. Author binding integration tests (Jest).** **Files:** `tests/integration/{RouteLens,ComponentLens,DependencyAudit,ExpoDoctorLens,RouteScaffold}.it.test.ts` — one per tool. **Operation:** add. **Pre-execution check:** `tests/integration/_fixtureSetup.ts` (Jest `globalSetup`) verifies fixture's `node_modules/` exists; if not, runs `npm install` in fixture once. If `expo-doctor` first-run fails to fetch from npm registry (offline host), `ExpoDoctorLens.it.test.ts` short-circuits with `test.skip` and emits a stderr note ("Phase 06 Step 18: ExpoDoctorLens IT skipped — expo-doctor unfetchable. Run on a network-connected host or pre-populate ~/.npm cache."). Acceptance criterion below still requires this test PASS on a network-connected host. **Test behavior:** Each IT programmatically constructs a `BridgeCommand` for its tool scoped to `Bindings/Expo/tests/fixture/`, invokes the tool end-to-end (via `ToolDispatcher`), asserts on the produced report's frontmatter (schema-validated via the shared `ajv` instance) and key body fields. **`RouteScaffold.it.test.ts`** specifically: success path scaffolds `app/settings/account.tsx` + `__tests__/settings/account.test.tsx` in a tmp-dir copy of the fixture, asserts both exist and contain expected stubs, then deletes the tmp dir; failure-simulation cases live in the contract test (Step 19), not here. **`RouteLens.it.test.ts`** specifically: copies fixture's `DBL/APIIndex/routes.md` (intentionally-stale) into the tmp-dir-fixture before invoking; asserts the copy's frontmatter contains `last_regenerated: STALE — superseded by ...` after the run. **Acceptance:** `cd Bindings/Expo && npm run test:integration` exits 0 with 5 PASS (or 4 PASS + 1 skip on offline hosts; the skip is acceptable for this acceptance criterion if and only if the host has no network — document the host state in the §1.3 Findings table). Jest report read from `coverage/` or stdout.
- [ ] **Step 19. Author hard-enforce contract tests.** **File:** `tests/contract/RouteScaffoldContract.test.ts`. **Operation:** add. **Behavior:** Real-FS tests (not mocked) for `route_scaffold`'s hard-enforce contract: (a) read-only `__tests__/` dir → exit 5, `app/<path>.tsx` SHA unchanged (i.e., not written); (b) target route file already exists → exit 2 (`stage="validate"`, `error_code="route_already_exists"`), no writes; (c) malformed `route_path` (e.g., contains `..` or absolute path) → `stage="validate"`, no writes; (d) `force_create_layout=true` AND parent `_layout.tsx` missing → all three files written atomically; (e) `force_create_layout=true` AND parent `_layout.tsx` write failure → all rolled back, exit 5. Each case uses a tmp-dir copy of the fixture; tmp dirs are cleaned up in `afterEach`. **Acceptance:** All five contract cases PASS via `npm run test:contract`.
- [ ] **Step 20. Schema-validation harness.** **File:** `tests/contract/SchemaValidation.test.ts`. **Operation:** add. **Behavior:** For each of the five integration tests' produced reports (collected by running ITs first, or by re-invoking each tool from this test against the tmp-fixture), validate the YAML frontmatter against `$defs.reportFrontmatter` in `Bindings/_schemas/bridge-command.schema.json` using the same `ajv` instance used by `ReportWriter`. **Acceptance:** Validation passes for all five reports; failure to validate any of them is a FAIL.
- [ ] **Step 21. Final binding self-build.** **Command sequence:** `cd Bindings/Expo && npm run clean && npm ci && npm run build && npm test`. **Operation:** verify. **Acceptance:** Build CLEAN; all unit + integration + contract tests PASS (count: unit ≈ 12 ports of Phase 05 patterns + 5 IT + 5 contract = ~22 minimum; record actual count in §1.3 Findings); `dist/cli/index.js` exists and is executable; launcher script `./scripts/nissth-bridge --list-tools` returns the five tools; `Bindings/Expo/mcp/smoke-test.mjs` exits 0.

### 3.2 Forbidden in this phase

> Explicitly list what is OUT OF SCOPE. This is the anti-scope-creep guard.

- **No changes to `Bindings/SpringBoot/` source.** Phase 05's binding is frozen reference. The only acceptable touch is in `Bindings/SpringBoot/README.md` if a cross-link to the new Expo binding is genuinely useful — and even that lives in `Bindings/README.md` instead.
- **No PostgreSQL / Postgres binding work.** `Bindings/Postgres/` is Phase 07+.
- **No Süprüz changes.** Süprüz consumes the bindings; it does not get touched by binding-development phases.
- **No changes to `Bindings/_schemas/bridge-command.schema.json`.** The contract was frozen 2026-05-15 and Phase 05 closed against it. If a gap is discovered during execution, STOP, append a status entry, propose a separate contract-revision plan.
- **No additional tools beyond the five.** No `asset_audit`, `screen_scaffold`, `bundle_size_lens`, `manifest_lens`, `eas_lens` — all later slices. (`asset_audit` was on the original Phase 05 snapshot Report's sketched catalog; deferred this phase per user choice on Q1 — 5 tools, not 6.)
- **No cross-binding tool-name collision resolution.** Phase 05's launcher and Phase 06's launcher both ship `Bindings/<stack>/scripts/nissth-bridge`. The user picks PATH precedence; cross-binding dispatch is reserved for a later framework-hardening plan once a third binding lands or the user explicitly requests it (per Phase 05 snapshot Report's "TBD: enforced when a second binding lands" — this Phase 06 SURFACES the collision but does not RESOLVE it).
- **No multiplexing MCP shim.** Per-binding shim only, mirroring Phase 05 (per user choice on Q2).
- **No DBL auto-regeneration tooling under `Tools/`.** Phase 5+ work explicitly out of core per CLAUDE.md banner.
- **No publishing/release work.** No npm registry publish, no GitHub releases. `binding_version: "0.1.0"` is internal until a separate release plan.
- **No edits to `CLAUDE.md` §11 prose.** §11 contract documentation is final since Phase 05. The §11.12 paragraph about Phase 06's tool catalog IS an in-scope addition (Step 4 implicit; explicit acceptance via §1.3 Doc Sync confirmation).
- **No new top-level Nissth-core sections in CLAUDE.md beyond §8.2.** No §12, no §13, no expansion of §1–§7.
- **No Detox / E2E test infra in the fixture.** Jest unit + integration is sufficient for first slice; Detox is in §8.2.1's "optional" line for future Expo project authors, not a requirement for the binding's own tests.
- **No `node_modules/` committed.** Standard. Fixture's `node_modules/` is gitignored; CI / re-clones require `npm install` in fixture during test setup.
- **No `@react-navigation/*` imports.** Per §8.2.5 forbidden patterns being authored in Step 4; binding code does not import navigation libraries (it scans them in target projects, but never depends on them).
- **No bundling of tools into a single class.** Each of the five tools is a separate `.ts` file. Refactoring to a shared base class is fine; merging tools is not.
- **No expo-cli (deprecated 2024).** Use `npx expo-doctor`, `npx expo`, etc. — never `expo` as a global command.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

> How do you know the verifier sees the latest changes? (Addresses the "false CLEAN" failure mode — HR#10.)

- **Binding self-build freshness:** `npm run clean` (removes `dist/`, `.tsbuildinfo`, `node_modules/.cache/`) before every verification run. The "false CLEAN" analog for tsc is the incremental-build cache (`.tsbuildinfo`) which can mask source changes; clean removes it. `npm ci` (not `npm install`) for the verification run — `ci` enforces lockfile-driven install and removes `node_modules/` first, guaranteeing dep state matches `package-lock.json`. Then `npm run build` (fresh tsc compile) and `npm test` (Jest reads compiled output from `dist/` or runs via `ts-jest` against source — record which in §1.3 Findings).
- **`expo_doctor_lens` freshness:** every invocation actually spawns `npx --yes expo-doctor`; never returns cached output. The freshness stamp in the report cites the subprocess exit code + stdout hash so callers can verify no caching layer intervened.
- **`route_scaffold`'s hard-enforce contract is freshness-agnostic** (it edits files; freshness is about reads, not writes). Step 19's contract tests verify atomicity via SHA-comparison before and after each failure-simulation case.
- **Phase 05 reference is verified live** (per §1.2 Action #3) — the "shape we are porting" is not assumed-from-memory; the agent re-runs `./mvnw clean verify -U -B` in `Bindings/SpringBoot/` and records the 111/111 result in §1.3 before any Expo source is written.
- **Schema validation (Step 20) reads produced report files from disk** after writes complete; no in-memory shortcuts. The `ajv` instance is the same one used by `ReportWriter`, so the validation path matches the production write path.

### 4.2 Checks

- [ ] **Build:** `cd Bindings/Expo && npm run clean && npm ci && npm run build` — expected: exit 0; `Bindings/Expo/dist/cli/index.js` exists with shebang `#!/usr/bin/env node`; `dist/` contains compiled `core/`, `tools/`, `cli/` subdirs.
- [ ] **Tests:** `cd Bindings/Expo && npm test` — expected: all unit (`tests/unit/`), integration (`tests/integration/`), and contract (`tests/contract/`) tests PASS. Read pass count from Jest stdout summary; record actual count in §1.3 Findings. Minimum: ~22 tests (12 unit + 5 IT + 5 contract).
- [ ] **Runtime / CLI:** `./Bindings/Expo/scripts/nissth-bridge --list-tools` returns exactly the five Expo tool names. `./Bindings/Expo/scripts/nissth-bridge --describe route_scaffold` prints the hard-enforce contract (atomic route + test write).
- [ ] **MCP smoke:** `cd Bindings/Expo/mcp && npm install && node smoke-test.mjs` exits 0 with "ALL CHECKS PASSED"; each of the four MCP tools returns `isError=false` against the fixture.
- [ ] **Bridge re-query:** Re-run `route_lens` against the fixture project after the build completes. Expected: report.routes contains the same routes IT-Step-18 found, with a fresh `generated_at` timestamp. The fixture's `DBL/APIIndex/routes.md` (intentionally-stale) remains STALE-flagged (no need to re-flip; flip is idempotent).
- [ ] **DBL freshness:** No real DBL exists in Nissth core to regenerate. The fixture's synthetic DBL artifact stays in STALE state after the test — expected end state, demonstrating the stale-flip works on a non-Java stack. No further regeneration action required this phase.
- [ ] **Phase 05 still green:** `cd Bindings/SpringBoot && ./mvnw clean verify -U -B` returns 111/111 (regression check — Phase 06 must not have broken Phase 05's reference shape). Required gate before §5 Cleanup.

### 4.3 Pass criteria

ALL of the following must be true:
- Step 21 build CLEAN; `dist/cli/index.js` exists; launcher script invocable on both POSIX shells and PowerShell.
- All 5 integration tests PASS (or 4 PASS + 1 skip if host is offline AND `ExpoDoctorLens.it.test.ts` is the only skip AND offline state is documented in §1.3 Findings).
- Hard-enforce contract tests PASS — `route_scaffold` exits 5 on simulated test-dir-write failure with the route `.tsx` unwritten (verified via fs.stat).
- Schema validation harness PASSES — every produced report's frontmatter validates against `$defs.reportFrontmatter`.
- `nissth-bridge --list-tools` returns exactly five tool names matching `expo.bridge.json`.
- STALE-flip integration test PASSES — fixture's synthetic `DBL/APIIndex/routes.md` frontmatter contains `last_regenerated: STALE — superseded by ...` after `route_lens` runs.
- MCP smoke test returns expected `Nissth_Status` shape and all 4 MCP tools work end-to-end.
- `CLAUDE.md` §8.2 Expo exists with all nine sub-sections; §8.1 Spring Boot semantics unchanged.
- Phase 05 reference binding still 111/111 green (no regressions).
- `Bindings/README.md` Expo row reflects what landed.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location (Jest report path, CLI output, MCP shim stderr, etc.).
3. Author an `incident` Report under `AgentReports/Reports/YYYY-MM-DD_phase-06-<slug>.md` per §10.4(1) — required for every `Verified: FAIL`. Body includes the failing artifact contents, root-cause hypothesis, and remediation options.
4. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

Special case: if Phase 05's reference binding regresses during Phase 06 execution (Check #7), the regression is itself a `Verified: FAIL` even if Phase 06's own checks all pass — Phase 06's contract is "ports Phase 05 without breaking it." Treat this case with priority equal to a Phase 06 internal failure.

---

## 5. Cleanup

- [ ] Remove any scratch files created under `Bindings/Expo/dist/` during iteration; keep `dist/cli/index.js` and its sibling compiled files (the binary artifact).
- [ ] Verify no `Temp_*.ts` / `*.scratch.ts` files at any `Bindings/Expo/src/**` or `Bindings/Expo/tests/**` location.
- [ ] Verify `Bindings/Expo/tests/fixture/node_modules/` is gitignored (matches `**/node_modules/` in repo-root `.gitignore`); do not commit it.
- [ ] Roll snapshots if any were taken under `AgentReports/Snapshots/` during execution (none expected — this is a greenfield create, not a destructive multi-step edit on existing files; the only existing-file edits are CLAUDE.md §8 renumber and Bindings/README.md row, both small).
- [ ] **Reports check (CLAUDE.md §10):**
  - **§10.4(4) — non-trivial phase close** TRIGGERS a `snapshot` Report. Author `AgentReports/Reports/YYYY-MM-DD_phase-06-bridge-expo-snapshot.md` (use actual phase-close date) summarizing: architecture as built, divergences from this plan (if any), the binding's API surface for downstream phases (Postgres, future stacks), known limitations, MCP shim runtime smoke result, comparison-to-Phase-05 ("what we re-used, what we re-implemented, what's stack-specific"). ~1500–2500 tokens. Cite Phase 05's snapshot in `supersedes:` only if Phase 06 invalidates any pattern from there (it should not).
  - **§10.4(3) — spec ingestion**: if §3 Step 17 (fixture creation) required reading the current Expo Router docs to resolve the §1.2 Question 9 uncertainty, author a `spec_digest` Report under `AgentReports/Reports/YYYY-MM-DD_expo-router-v3-conventions.md` capturing the conventions that drove the fixture shape. Optional but recommended if any Expo-Router-specific decision was non-trivial.
  - No `decision` Report needed unless an option-choice arose mid-execution that was not covered by Phase 06's pre-flight design questions (Q1–Q3 answered at plan authoring).
  - List authored Reports here so they appear in the §6 status entry's `Reports:` line.
- [ ] **Document Sync sweep (Hard Rule #11):**
  - Source files modified in §3:
    - **Created:** all under `Bindings/Expo/**` (new subtree).
    - **Modified (pre-existing files):** `CLAUDE.md` (§8 renumber + §8.2 add + §11.12 paragraph for Expo); `Bindings/README.md` (Expo row update).
  - For each modified file, identify affected stable documents:
    - `CLAUDE.md` — itself is the source-of-truth; cross-check that §8.1 Spring Boot's semantic content is byte-equal (modulo header-renumber) to its pre-Phase-06 state. If any §8.1 content changed inadvertently, that is a Verified: FAIL.
    - `Bindings/README.md` — its stack table now lists `Bindings/Expo/` as live; cross-link to this plan added.
    - `Bindings/SpringBoot/README.md` — unchanged (no cross-link added); verify no accidental edit.
    - `DBL/**` — Nissth core has no DBL; nothing to flip.
    - Other plans in `ImplementationPlans/` — none cross-reference Phase 06 yet; this is the first plan to mention it.
    - `AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md` — does NOT need editing; it sketched Phase 06 as "candidate" and Phase 06's own snapshot Report closes that thread.
  - Action: UPDATE — both `CLAUDE.md` and `Bindings/README.md` are updated as part of §3 Steps 4 + 21 (Step 21 includes the README row touch as a cleanup-adjacent action; if not in Step 21, move to Step 22 or add to §5 explicitly).
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [updated: CLAUDE.md §8 + §11.12, Bindings/README.md Expo row; created: Bindings/Expo/** (full subtree), AgentReports/Reports/<phase-06 snapshot>; marked stale: none]`.
- [ ] No orphan branches, no leftover debug code (no `console.log` outside the report writer's intentional console-destination path; no `console.error` outside actual error paths). `console.log` in `dist/cli/index.js` is permitted only for tool output when `output.destination=console` or `--list-tools` / `--describe` outputs.

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 06: Bridge — Expo First Slice

**State:**
- Phase: 6/6+ — Expo binding first slice complete; Postgres binding next candidate
- Build: CLEAN
- Tests: PASS
- Active plan: ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md
- DBL refs: none — Nissth core has no DBL; fixture's synthetic DBL/APIIndex/routes.md exercises the stale-flip path
- Bridge reports: AgentReports/Bridge/route_lens_<ts>.md, component_lens_<ts>.md, dependency_audit_<ts>.md, expo_doctor_lens_<ts>.md, route_scaffold_<ts>.md (one per tool, produced by integration tests)
- Blockers: none

**Report:**
- [condensed from §1 findings — all Match? = yes, including Phase 05 reference still 111/111 green]
- Five tools (route_lens, component_lens, dependency_audit, expo_doctor_lens, route_scaffold) implemented in TypeScript per §11.12 Phase 06 paragraph; CLI dispatcher (Node) + MCP shim (Node, per-binding) built and validated; fixture Expo Router project + integration/contract/schema tests pass.
- CLAUDE.md §8.2 Expo authored alongside the binding (per user choice 2026-05-17 on stack-rules placement); §8 renumbered such that §8.1 Spring Boot and §8.2 Expo are parallel sub-sections.

**Executed:**
- [condensed from §3, with all 21 checkboxes resolved]

**Verified:**
- Build: CLEAN via `cd Bindings/Expo && npm run clean && npm ci && npm run build && npm test` reading Jest stdout summary at <ts>.
- Tests: ~22 (12 unit + 5 IT + 5 contract) PASS; or 4 PASS + 1 skip on offline host (document state).
- Bridge re-query: STALE-flip works on TypeScript stack — fixture's synthetic `DBL/APIIndex/routes.md` frontmatter contains `last_regenerated: STALE — superseded by ...` after `route_lens` runs.
- MCP smoke: `node mcp/smoke-test.mjs` exits 0; all 4 MCP tools end-to-end against fixture.
- Phase 05 regression check: `cd Bindings/SpringBoot && ./mvnw clean verify -U -B` still 111/111 PASS (no regressions).
- Freshness: §8.2.6 Expo verification protocol followed for the binding's own build (`npm ci` + `tsc` fresh after `npm run clean` + `.tsbuildinfo` removed); `expo_doctor_lens` every-call subprocess invocation verified by stdout hash mismatch test.
- Doc sync: [updated: CLAUDE.md §8 (renumber + §8.2 add) + §11.12 (Phase 06 paragraph), Bindings/README.md Expo row; created: Bindings/Expo/** full subtree, AgentReports/Reports/<phase-06 snapshot>; marked stale: none]
- Reports: AgentReports/Reports/<ISO>_phase-06-bridge-expo-snapshot.md (snapshot, per §10.4(4)); optionally AgentReports/Reports/<ISO>_expo-router-v3-conventions.md (spec_digest, if Expo Router conventions needed live-doc resolution per §1.2 Question 9).

**Issues:**
- none — or list any deferred items (e.g., `asset_audit` deferred to Phase 06b; cross-binding `nissth-bridge` PATH collision SURFACED but not RESOLVED — see §3.2 Forbidden).

**Next:**
- User decides: (a) author Task #15 — engineer-facing top-level `README.md` (now doubly-unblocked: Phase 05 + Phase 06 both closed); (b) author Phase 07 — `Bindings/Postgres/` first slice (PostgreSQL diagnostic tools — `query_plan`, `index_drift`, `lock_audit`, plus an action tool TBD); (c) resume Süprüz project work (`Desktop/Supruz/`) now that both binding stacks Süprüz actually uses (Spring Boot backend + Expo frontend) are available; (d) framework-hardening plan to resolve the cross-binding `nissth-bridge` PATH collision via a unified dispatcher (priority bumps if user installs both bindings on the same machine and hits the collision in practice). All four paths plan-required per HR#12.
```
