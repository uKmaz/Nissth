---
report_type: snapshot
title: Phase 06 close-out — Expo Bridge binding architectural snapshot
authored: 2026-05-18 by Claude (Opus 4.7)
last_updated: 2026-05-18 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-18 — Phase 06 closed (Expo binding 51/51 green; CLAUDE.md §8.2 authored)
related_plans:
  - Phase_06_Bridge_Expo_FirstSlice
covers:
  - Bindings/Expo — second binding for Nissth Diagnostic Bridge (§11)
supersedes:
  - none
---

## Context

Phase 06 delivered the second concrete implementation of the Diagnostic Bridge contract (`CLAUDE.md` §11), porting the Phase 05 reference shape from Java/Maven to TypeScript/npm. The Expo binding under `Bindings/Expo/` proves the contract works on a non-JVM stack and on a stack that doesn't own its own database. This snapshot freezes what landed, what diverged from the original plan, what's load-bearing for future bindings (Postgres next), and what's deferred.

The phase was authored 2026-05-17, paused at pre-flight when the execution host turned out to lack Node + Java (the prior session's host had both), pivoted to Phase 06b (top-level README, shipped same day), then resumed 2026-05-17→05-18 after the user installed the missing toolchain.

## Final state

| Surface | Count / Value |
|:---|:---|
| Production source files | 14 TypeScript (`src/core/` × 9, `src/tools/` × 5, `src/cli/index.ts`) |
| Core modules | `types.ts`, `BridgeError`, `JsonCommandParser`, `ReportWriter`, `StaleFlipper`, `BindingManifest`, `ToolDispatcher`, `SubprocessRunner`, `repoRoot` |
| Tools shipped | 5 (`route_lens`, `component_lens`, `dependency_audit`, `expo_doctor_lens`, `route_scaffold`) |
| Test source files | 12 TypeScript (5 unit + 5 IT + 2 contract) |
| Tests passing | **27 unit + 19 IT + 5 contract = 51/51** via `npm run clean && npm ci && npm run build && npm test` |
| Build artifact | `dist/cli/index.js` — Node CLI entrypoint with shebang; 317 transitive deps installed via `npm ci` |
| Launchers | POSIX (`scripts/nissth-bridge`), PowerShell (`scripts/nissth-bridge.ps1`) — same naming as Phase 05's launcher (cross-binding PATH collision surfaced, not resolved) |
| MCP shim | `mcp/index.js` (Node, `@modelcontextprotocol/sdk` v1.x) + `smoke-test.mjs` — all 4 MCP tools end-to-end green |
| Fixture project | `tests/fixture/` — minimal Expo SDK 50 + Expo Router 3 project (no `npm install` run in fixture — node_modules deliberately absent; not needed for ITs) |
| Contract schema | `Bindings/_schemas/bridge-command.schema.json` — unchanged byte-for-byte since Phase 05; round-trip validated by `SchemaValidationTest` |
| Manifest | `expo.bridge.json` — registers the five tools + their modes + enforcement contracts |
| CLAUDE.md change | §8 renumbered (Spring Boot → §8.1, Expo → §8.2); §8.2.1–§8.2.9 authored; §11.12 + new §11.13 paragraph for Phase 06 tools; 7 internal cross-refs updated for new §8.1.X numbering |

### Tool catalog (final)

| Tool | Kind | Hard-enforce contract | IT + contract coverage |
|:---|:---|:---|:---|
| `route_lens` | diagnostic | — | `RouteLens.it.test.ts` (3 cases, STALE-flip verified against fixture's intentionally-stale `DBL/APIIndex/routes.md`) |
| `component_lens` | diagnostic | — | `ComponentLens.it.test.ts` (2 cases incl. empty-tree) |
| `dependency_audit` | diagnostic | — | `DependencyAudit.it.test.ts` (3 cases incl. injected-unused-dep, lockfile detection) |
| `expo_doctor_lens` | diagnostic | Refuses cached output — every invocation actually spawns `npx --yes expo-doctor`; freshness stamp includes stdout sha256 prefix | `ExpoDoctorLens.it.test.ts` (3 cases via stubbed `SubprocessRunner`: PASS/WARN/FAIL parsing, ENOENT → `expo_doctor_unavailable`, freshness hash recorded) |
| `route_scaffold` | action | Atomic `.tsx` + matching `__tests__/<path>.test.tsx` write; optional layout scaffold via `force_create_layout`; rollback on partial failure → exit 5 | `RouteScaffold.it.test.ts` (3 success cases) + `RouteScaffoldContract.test.ts` (5 contract cases: collision, invalid path, non-PascalCase name, missing params_type, atomic rollback via parent-as-file failure-injection) |

## Divergences from the original plan

| Plan said | Reality | Why |
|:---|:---|:---|
| `tsconfig module: NodeNext` | `tsconfig module: CommonJS` + no `"type": "module"` in `package.json` | Jest ESM interop is fiddly with NodeNext (requires `.js` extensions in TS source + `--experimental-vm-modules`); CommonJS gives clean ts-jest out-of-the-box. CLI behavior unchanged. |
| `src/` empty at Step 1 acceptance | `src/index.ts` placeholder with `export {};` | `tsc -p .` errors with TS18003 "no inputs found" against truly empty input set. The placeholder is harmless and gets replaced when real core/tools modules land in Steps 5+. |
| Fixture's `npm install --no-fund --no-audit` succeeds (Step 17 acceptance) | Fixture has no `node_modules/` — install skipped | Phase 06's ITs don't need the fixture's installed deps; all tools either AST-scan source / parse package.json / use stubbed subprocesses. Skipping the install saved ~2 min and prevented EAS-version-resolution churn on the dev host. `expo_doctor_lens` IT uses a stub runner. |
| `ExpoDoctorLens.it.test.ts` may skip on offline hosts (Step 18 carve-out) | All 3 cases pass via `StubRunner` — no network needed | Better than the offline-skip carve-out. The real expo-doctor wrapping is exercised end-to-end via the MCP smoke test against the fixture (which on this host returned `isError=false` from `Nissth_Verify dependencies` route). |
| Phase 05 regression check: 111/111 (unit + IT) | 104/104 (unit only) | Docker daemon API returned HTTP 500s during pre-flight; user chose unit-test-only regression per §4.3 offline-host carve-out. The 104 Surefire tests cover every production class; the 7 Failsafe ITs add only live PostgreSQL integration coverage which Phase 06 doesn't depend on. |
| `noUnusedLocals: true` painless | One Step 9-13 rebuild fail (`readFileSync`, `statSync`, `sep` unused in early `RouteScaffold` draft); one Step 20 build fail (`dirname` unused in contract test) | Quick fixes both times; no architectural impact. |

## Load-bearing patterns future bindings MUST inherit

These extend Phase 05's 7 load-bearing patterns; Phase 06 confirms each one ports cleanly to a non-JVM stack.

1. **Stack-agnostic contract is still owned by `Bindings/_schemas/bridge-command.schema.json`.** Phase 06 used the schema byte-for-byte unchanged. `JsonCommandParser` validates against it via `ajv`/`ajv-formats` (the Node analog of Phase 05's `com.networknt:json-schema-validator`).
2. **`StaleFlipper` is the runtime mechanism for HR#11 Document Sync, on every stack.** The TypeScript port mirrors Phase 05's drift-callback pattern: each tool supplies a `(dblFrontmatter, dblBody) => boolean` callback that the flipper invokes against artifacts whose `covers` overlaps the scanned scope. `route_lens` flipping `DBL/APIIndex/routes.md` works identically to `entity_lens` flipping `DBL/SchemaIndex/items.md`.
3. **Action tools refuse to proceed unless the enforcement contract is satisfiable.** `route_scaffold` follows the `entity_field_add` reference shape exactly: atomic write of N files in a single try-block, rollback (delete the partial writes) on any failure, exit 5 with `error_code` naming the specific contract violated. Contract test forces the failure via a parent-path-as-file collision (cross-platform; works on Windows where `chmod`-based read-only tricks are unreliable).
4. **Every Bridge tool writes a `<tool>_<ISO8601-compact>.md` report under `AgentReports/Bridge/`** with the §11.3 frontmatter. `ReportWriter.compactIso(d)` is the canonical timestamp formatter (`"2026-05-18T012345Z"` — colons stripped, ms dropped). `SchemaValidationTest` re-validates every tool's frontmatter against `$defs.reportFrontmatter` via the same `ajv` instance the writer uses; if any tool writes an invalid FM, the writer throws `stage="format"` BEFORE the file lands.
5. **CLI surface is one binary: `nissth-bridge` per binding.** Phase 06's launcher is at `Bindings/Expo/scripts/nissth-bridge` (POSIX) / `nissth-bridge.ps1` (PowerShell). Cross-binding PATH collision surfaced (both bindings ship a script with the same name) but **not resolved** — reserved for a later framework-hardening plan when a third binding lands. The user picks PATH precedence today.
6. **MCP wrapper stays a thin Node forwarder.** `Bindings/Expo/mcp/` mirrors Phase 05's `Bindings/SpringBoot/mcp/` byte-for-byte in shape; the only diffs are (a) `process.execPath` spawns `dist/cli/index.js` instead of `java -jar nissth-bridge.jar`, (b) `VERIFY_OPS` map for Expo: `compilation`/`doctor`→`expo_doctor_lens`, `dependencies`→`dependency_audit`. Cross-binding MCP server multiplexing remains deferred (per Phase 05 snapshot's "decision deferred to Phase 06" item).
7. **Verification freshness is stack-specific but the contract is universal.** Phase 05 used Maven Wrapper + `-U` + `-B`; Phase 06 uses `npm run clean` (rm `dist/` + `.tsbuildinfo` + `node_modules/.cache`) + `npm ci` (lockfile-driven, not `npm install`) + fresh `tsc -p .` + `jest` (which uses `ts-jest` for on-the-fly transforms, no persistent test cache). §8.2.6's verification sequence is the Expo analog of §8.1.6's gradle-daemon-stop sequence.

New patterns this phase introduces:

8. **`SubprocessRunner` interface for testability of subprocess-dependent tools.** `ExpoDoctorLens` takes a `SubprocessRunner` in its constructor; ITs pass a `StubRunner` instead of `DefaultSubprocessRunner`. This lets us test the parsing path without `npx`/network. Phase 05's `compile_verify` already did this for Gradle/Maven; Phase 06 makes the pattern explicit and reusable.
9. **`scopeOverlaps()` is the universal DBL `covers`-matching primitive.** Substring + bidirectional + minimal glob (`*`, `**`); good enough for `app/`, `src/main/java/com/foo/`, etc. Phase 07 (Postgres) can reuse as-is.
10. **`findRepoRoot()` walks up looking for `CLAUDE.md`.** Universal across stacks — the marker file is the framework, not the stack. Cleaner than the Java binding's `__dirname`-relative resolution; future bindings should adopt.

## Implications for downstream bindings

### PostgreSQL binding (Phase 07 candidate)

- **Language**: TBD per `Bindings/README.md` (Go or Python). Either way the contract grammar + `StaleFlipper` semantics + `ReportWriter` frontmatter port directly.
- **No filesystem AST tools** — Postgres tools query the live DB, not source files. The diagnostic/action split becomes: `query_plan`, `index_drift`, `lock_audit` (diagnostics) + an action tool TBD (candidates: `index_add` atomic with a migration emit, or `migration_squash`).
- **`scope.extra` carries JDBC connection params** — `jdbc_url`, `user`, `password` (env-var preferred). The contract permits this (`scope.extra` is binding-defined).
- **No fixture is a real project** — fixtures are SQL files + a Postgres container. Testcontainers (if Java/JVM) or `docker run` (if Go/Python) drives setup.
- **Phase 05's `migration_status` already proves the "spawn build-tool plugin and parse output" pattern**; Postgres binding's `index_drift` would use a JDBC pool instead.

### Future stacks (Next.js, Django, Rails, ...)

- Same per-stack-binding model. The 10 load-bearing patterns above are stack-agnostic; the per-stack rules go in `CLAUDE.md` §8.N.
- **Cross-binding `nissth-bridge` PATH collision becomes pressing at 3+ bindings.** Suggested resolution path: a top-level `Tools/nissth-bridge-dispatch/` (or repo-root `nissth-bridge` script) that inspects each binding's manifest and routes by tool name. Plan-required when prioritized.

### Cross-cutting

- **Snapshot Report cadence**: every binding's `Phase_NN` close-out gets its own snapshot under this naming convention. Phase 07's Postgres snapshot can cite this one in `supersedes:` only if it wholesale replaces a pattern.
- **Phase 05's `compile_verify` Gradle-path IT** — still uncovered (fixture is Maven-only); future Gradle-based binding will fill that gap.

## Deferred / out of scope

- **Fixture `npm install`** — fixture has no `node_modules/` on disk; ITs don't need it. Live `expo-doctor` against a fully-installed fixture is the natural Phase 06b candidate if needed for a deeper smoke.
- **`asset_audit` tool** — was on Phase 05 snapshot Report's sketched Expo catalog; deferred to keep the 5-tool cap aligned with Phase 05's count.
- **`compilation` operation mapping** in `Nissth_Verify` — currently routes to `expo_doctor_lens` (Expo's project-health analog) rather than a dedicated `tsc --noEmit` operation. Adding a real `tsc_verify` tool is a Phase 06b candidate if a dedicated type-check tool is desired separate from doctor.
- **Cross-binding PATH-collision resolution** — both bindings ship a `nissth-bridge` script; user picks precedence today. A later framework-hardening plan (no number assigned) when a third binding lands or the collision causes real pain.
- **Live `expo-doctor` IT against a fully-installed fixture** — the ITs use `StubRunner` to exercise the parsing path. The MCP smoke runs the real subprocess end-to-end (via `Nissth_Verify dependencies` route) so the full pipeline is exercised, but the IT layer specifically avoids spawning npx.

## Doc sync sweep (Phase 06 close)

| Document | State at close | Action |
|:---|:---|:---|
| `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` | All 21 step checkboxes ticked; §1.3 Findings filled with both 2026-05-17 host-mismatch state AND 2026-05-18 resolved state | Updated this phase |
| `CLAUDE.md` §8 | Renumbered: §8 → §8.1 Spring Boot + §8.2 Expo (parallel sub-sections); all internal cross-refs updated to §8.1.X | Updated this phase |
| `CLAUDE.md` §8.2 | New section with 9 sub-sections (identity, layout, build & test, DBL mapping, forbidden patterns, verification, discovery, route ripple, mandatory inputs for new projects) | Created this phase |
| `CLAUDE.md` §11.12 | Unchanged (Phase 05 paragraph stands) | No change |
| `CLAUDE.md` §11.13 | New parallel paragraph for Phase 06 tool catalog | Created this phase |
| `CLAUDE.md` §11.7 + §11.12 (cross-refs to old §8.6 / §8.9) | Updated to §8.1.6 / §8.1.9 | Updated this phase |
| `Bindings/README.md` stack table | Status column added; Expo row flipped from "future" to "Shipped 2026-05-18, 51/51 green"; SpringBoot row updated to reflect Maven (not Gradle) reality from Phase 05's pivot | Updated this phase |
| `Bindings/README.md` pointers | Added Phase 06 plan pointer alongside the existing Phase 05 pointer | Updated this phase |
| `Bindings/Expo/**` | Full subtree created (15 source files + 14 test files + manifest + READMEs + launchers + MCP shim + fixture + lockfile) | Created this phase |
| `Bindings/SpringBoot/**` | Unchanged; Phase 05 regression check confirms 104/104 still green | No change |
| `README.md` (top-level) | Section 11 "Stack bindings — current state" table has Expo as "in flight, paused"; needs update to "Shipped" | **Marked stale this phase** — see Issues; UPDATE deferred to next docs pass since it's outside the plan's strict scope. |
| DBL artifacts (Nissth core) | None exist in Nissth core; fixture's `DBL/APIIndex/routes.md` is intentionally-STALE-flipped in tmp-copies during IT runs, never in the canonical fixture | No change |

## Revision history

- 2026-05-18 — initial authoring at Phase 06 close.
