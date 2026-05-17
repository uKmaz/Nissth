# Phase 05: Diagnostic Bridge — Spring Boot First Slice

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_05_Bridge_SpringBoot_FirstSlice
- **Authored:** 2026-05-15 by Claude (Opus 4.7)
- **Approved:** 2026-05-15 by Emre Uçmaz (verbal: "I liked the plan" — Claude session)
- **Revised:** 2026-05-15 — Pivoted binding self-build from Gradle to Maven per pre-flight Match=no on Gradle row (no system Gradle; user explicitly chose Maven over installing Gradle). Build-tool-adaptive behavior added to `compile_verify` and `migration_status` so both Gradle AND Maven target projects are supported. Original §0 Approved authorization holds; pivot is in-scope per user authorization in this turn. Decision rationale: `AgentReports/Reports/2026-05-15_phase-05-maven-pivot.md`.
- **Revised:** 2026-05-16 — Added Apache Maven Wrapper 3.3.2 (`mvnw`, `mvnw.cmd`, `.mvn/wrapper/maven-wrapper.properties` pinning Maven 3.9.9) to `Bindings/SpringBoot/` after the next session's resume-protocol freshness-check discovered no system `mvn` on the machine. Wrapper bootstraps Maven on first use into `~/.m2/wrapper/dists/`; no system install required. Canonical local-build invocation is now `./mvnw <goal>`; system `mvn` is still accepted on hosts that have it. Build-tool detection in `compile_verify` and `migration_status` for TARGET projects is unaffected (target projects still use whatever they have). Decision rationale: `AgentReports/Reports/2026-05-16_phase-05-maven-wrapper.md`.
- **Depends on:** none — first plan touching `Bindings/`. Builds on the bridge contract added to `CLAUDE.md` §11 and `Bindings/_schemas/bridge-command.schema.json` in the 2026-05-15 framework-hardening status entry.
- **Estimated scope:** Creates a new Maven subproject at `Bindings/SpringBoot/` (~30–40 Java files + tests + a fixture project + a thin Node MCP shim). Implements five tools (`compile_verify`, `endpoint_lens`, `entity_lens`, `migration_status`, `entity_field_add`) plus a `nissth-bridge` CLI dispatcher and an MCP wrapper exposing four MCP tools. Build-tool-specific tools (`compile_verify`, `migration_status`) auto-detect whether the target project uses Gradle or Maven and invoke the appropriate commands. Adds one new directory under Nissth root (`Bindings/SpringBoot/`); does not modify any prior framework files except §5 cleanup may update binding-related rows in `CLAUDE.md` §5 file roles if drift is detected. No changes to consuming projects (Süprüz stays paused).

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the bridge contract is stable, no prior SpringBoot binding exists, and the host toolchain (Java 17+, Gradle, Node) is present.

### 1.1 Inputs to read

- **DBL:** none — Nissth itself has no DBL.
- **Bridge reports:** none — bridge runtime is what this plan delivers; no reports can exist yet.
- **Source:**
  - `CLAUDE.md` §11 (Diagnostic Bridge contract; lines covering §11.2 grammar, §11.3 report contract, §11.4 stale-flip, §11.7 hard-enforce, §11.8 binding layout, §11.12 first-slice catalog).
  - `Bindings/_schemas/bridge-command.schema.json` (the contract this binding implements).
  - `Bindings/README.md` (per-stack-binding model).
  - `CLAUDE.md` §8 (Spring Boot stack bindings — §8.5 forbidden patterns, §8.6 verification protocol, §8.9 entity ripple).
- **StatusUpdate.md:** latest entry as of plan authoring — `2026-05-15 — Framework Hardening: §11 Diagnostic Bridge contract added`.

### 1.2 Diagnostic actions

> Prefer Bridge tools and DBL reads over raw source greps. None of the five Bridge tools exists yet, so this phase falls back to filesystem checks and direct command invocations.

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm bridge contract is final | `Read Bindings/_schemas/bridge-command.schema.json` + `Read CLAUDE.md §11.2` | full files | Pre-condition: the binding implements a frozen contract; modifying it mid-phase is forbidden (§3.2). |
| 2 | Confirm no SpringBoot binding exists | `ls Bindings/` | `Bindings/` directory | Avoid overwriting existing work. Expected: only `README.md` and `_schemas/`. |
| 3 | Confirm Java toolchain | `java -version` | host | Need Java 17+ per `CLAUDE.md` §8.1. |
| 4 | Confirm Maven availability | `mvn --version` | host | Bridge is built by Maven and invokes Maven as a subprocess for the Maven path of `compile_verify`/`migration_status`. (Gradle path supported for target projects via auto-detect.) |
| 5 | Confirm Node availability for MCP shim | `node --version` | host | MCP wrapper is a thin Node shim shelling out to the JVM CLI. |
| 6 | Confirm no `nissth-bridge` already on PATH | `which nissth-bridge` (Bash) or `Get-Command nissth-bridge` (PowerShell) | host | Avoid collision with a partial prior install. Expected: not found. |
| 7 | Confirm no `AgentReports/Bridge/` directory | `ls AgentReports/` | `AgentReports/` | Phase output writes here; if it already exists with content, investigate before overwriting. |
| 8 | Confirm Docker daemon availability (Step 17+ only — Testcontainers PostgreSQL) | `docker --version` + `docker ps` | host | Required for Failsafe IT tests; not for Steps 1–16. If installed-but-not-running, Step 17 STOPs and prompts user to start Docker Desktop. |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Does `Bindings/_schemas/bridge-command.schema.json` exist and contain the §11.2 grammar verbatim? | yes — JSON Schema draft 2020-12, `required: ["tool"]`, `scope.extra` open | yes — verified by Read; lines 1–30 show the grammar | ✅ yes |
| Does `Bindings/SpringBoot/` exist? | no — only `README.md` and `_schemas/` in `Bindings/` | no — `ls Bindings/` returned exactly `README.md` and `_schemas` | ✅ yes |
| Is Java 17 or later available? | yes — `java -version` reports ≥ 17 | yes — Java 21.0.7 LTS (Oracle JDK) | ✅ yes |
| Is Gradle 8.x or later available? | yes — Gradle ≥ 8.5 on the host or wrapper resolvable | **no — `gradle: command not found`; no `gradlew` exists yet; wrapper bootstrap from scratch is non-trivial** | ❌ no — resolved by pivot to Maven (see below) |
| Is Node 20.x or later available? | yes — for the MCP shim | yes — Node v22.18.0 | ✅ yes |
| Is `nissth-bridge` already on PATH? | no — fresh install | no — `which nissth-bridge` returned not-found | ✅ yes |
| Does `AgentReports/Bridge/` exist? | no — bridge has never run | no — `AgentReports/` contains only `Archive/`, `Reports/`, `Snapshots/`, `StatusUpdate.md` | ✅ yes |
| **POST-PIVOT** Is Maven 3.9+ with Java 17+ available? | yes — replaces Gradle row after pivot | yes — Apache Maven 3.9.9 + Java 21.0.7 | ✅ yes |
| Is Docker daemon available? (Step 17+ blocker only) | yes — required for Testcontainers PostgreSQL | **installed (Docker 28.4.0) but daemon NOT running** — `dockerDesktopLinuxEngine` pipe not found | ⚠️ partial — non-blocking for Steps 1–16; Step 17 gates on daemon-running |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

**Resolution (2026-05-15):** Pre-flight returned Match=no on the Gradle row. User was presented three options (install Gradle / pivot to Maven / bootstrap wrapper from download). User chose **pivot to Maven** — `CLAUDE.md` §8.1 lists Maven as a supported alternative; `mvn --version` reports Apache Maven 3.9.9 + Java 21.0.7. The plan was revised in-place to use Maven for the binding's self-build (§3 Steps 1, 14, 16, 17, 20 updated; §4.1/§4.2/§5 paths updated); `compile_verify` and `migration_status` were extended with build-tool auto-detection so consumer projects can still use either Gradle or Maven. This is an in-scope plan revision authorized in the same turn; no `Verified: FAIL` status entry is required because §3 execution had not yet begun. Pivot rationale documented in `AgentReports/Reports/2026-05-15_phase-05-maven-pivot.md` (decision Report).

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/` | contents | `README.md`, `_schemas/bridge-command.schema.json` |
| `Bindings/SpringBoot/` | exists | no |
| `AgentReports/Bridge/` | exists | no |
| `nissth-bridge` on PATH | exists | no |
| `CLAUDE.md` §11.12 | tool names | `compile_verify`, `endpoint_lens`, `entity_lens`, `migration_status`, `entity_field_add` |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `Bindings/SpringBoot/pom.xml` | exists | yes — Maven build for the JVM binding (Java 17 target, packaged as executable jar) |
| `Bindings/SpringBoot/spring-boot.bridge.json` | exists | yes — manifest registering the five tools with their modes and scope keys |
| `Bindings/SpringBoot/README.md` | exists | yes — tool catalog + scope.extra keys + install notes |
| `Bindings/SpringBoot/src/main/java/com/nissth/bridge/core/` | contents | `JsonCommandParser.java`, `ScopeValidator.java`, `ReportWriter.java`, `ToolDispatcher.java`, `StaleFlipper.java` (plus tests under `src/test/`) |
| `Bindings/SpringBoot/src/main/java/com/nissth/bridge/tools/` | contents | `CompileVerify.java`, `EndpointLens.java`, `EntityLens.java`, `MigrationStatus.java`, `EntityFieldAdd.java` |
| `Bindings/SpringBoot/src/main/java/com/nissth/bridge/cli/` | contents | `NissthBridgeCli.java` — entry point + flag parser + JSON-stdin path + discovery flags |
| `Bindings/SpringBoot/mcp/` | contents | `index.js` (Node MCP server shim), `package.json`, `README.md` |
| `Bindings/SpringBoot/tests/fixture/` | contents | Minimal Spring Boot 3.x project: 1 `@Entity`, 1 `@RestController`, 1 Flyway `V1__init.sql`, `application.yml`, `build.gradle.kts` |
| `Bindings/SpringBoot/tests/integration/` | contents | One integration test per tool + one hard-enforce contract test for `entity_field_add` |
| `nissth-bridge --list-tools` | output | Lists exactly the five tools, each with at least its default mode |
| `nissth-bridge --describe entity_field_add` | output | Shows the action tool's enforcement contract |
| `AgentReports/Bridge/compile_verify_<ts>.md` | exists post-test | Yes — produced by integration test against fixture; frontmatter passes the `$defs.reportFrontmatter` schema check |
| `entity_field_add` invoked with broken migration write path | exit code | `5` (freshness/contract violation; no `.java` edit committed) |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [ ] **Step 1. Scaffold Maven subproject.** **Files:** `Bindings/SpringBoot/pom.xml`, `Bindings/SpringBoot/.gitignore`, `Bindings/SpringBoot/src/main/java/com/nissth/bridge/` directory tree (with `core/`, `tools/`, `cli/` subdirs), `Bindings/SpringBoot/src/main/resources/`, `Bindings/SpringBoot/src/test/java/com/nissth/bridge/` tree (with `core/`, `it/`, `contract/` subdirs), `Bindings/SpringBoot/src/test/resources/`. **Operation:** add. **Acceptance:** `cd Bindings/SpringBoot && mvn -q validate` exits 0 (validates pom.xml shape and toolchain); `mvn -q -DskipTests compile` exits 0 against an empty source tree.
- [ ] **Step 2. Author binding manifest.** **File:** `Bindings/SpringBoot/spring-boot.bridge.json`. **Operation:** add. **Content:** JSON object with `binding: "spring-boot"`, `binding_version: "0.1.0"`, `tools` array listing the five tools each with `name`, `kind` (diagnostic|action), `modes`, `scope_keys` (top-level keys consumed), `scope_extra_keys` (binding-specific keys), `description`. **Acceptance:** Loadable as JSON; manifest reader (Step 7) lists all five.
- [ ] **Step 3. Author binding README.** **File:** `Bindings/SpringBoot/README.md`. **Operation:** add. **Content:** Tool catalog table (one row per tool, modes column, scope-keys column, freshness-source column); `scope.extra` keys documented per tool; install instructions (Gradle build, native-image option deferred, PATH setup for `nissth-bridge`); pointers to `CLAUDE.md` §11 + §8. **Acceptance:** Renders cleanly; every tool's enforcement contract (for action tools) is stated explicitly.
- [ ] **Step 4. Implement `JsonCommandParser`.** **File:** `src/main/java/com/nissth/bridge/core/JsonCommandParser.java`. **Operation:** add. **Behavior:** Parses incoming JSON (file path, stdin, or string), validates structurally against `Bindings/_schemas/bridge-command.schema.json` using a JSON Schema validator (e.g., `com.networknt:json-schema-validator`), returns a typed `BridgeCommand` record or throws with `stage="parse"` / `stage="validate"`. **Acceptance:** Unit tests cover: malformed JSON → parse error; missing `tool` → validate error; valid command → parsed record with all fields accessible; unknown top-level `scope` key → validate error (per schema `additionalProperties: false`).
- [ ] **Step 5. Implement `ReportWriter`.** **File:** `src/main/java/com/nissth/bridge/core/ReportWriter.java`. **Operation:** add. **Behavior:** Writes a Markdown file to `AgentReports/Bridge/<file_name | tool_<ISO8601-compact>>.md` with frontmatter populated from a `ReportContext` (tool, mode, binding, binding_version, generated_at, scope echo, freshness object, contract_version=1). Validates frontmatter against `$defs.reportFrontmatter`. **Acceptance:** Unit test: writes a report; result parses as valid YAML frontmatter + Markdown body; freshness fields present and non-empty.
- [ ] **Step 6. Implement `StaleFlipper`.** **File:** `src/main/java/com/nissth/bridge/core/StaleFlipper.java`. **Operation:** add. **Behavior:** Given a Bridge report and a project root, scans `DBL/**/*.md` for artifacts whose `covers` overlaps the report's scope. For each match, runs a tool-specific drift check (callback supplied by the tool). If drift detected, rewrites the DBL artifact's `last_regenerated` field to `STALE — superseded by AgentReports/Bridge/<report file>`. Idempotent. **Acceptance:** Unit test: drift detected → frontmatter rewritten with STALE marker; no drift → frontmatter untouched; missing DBL directory → silent no-op.
- [ ] **Step 7. Implement `ToolDispatcher` + manifest reader.** **File:** `src/main/java/com/nissth/bridge/core/ToolDispatcher.java`. **Operation:** add. **Behavior:** Reads `spring-boot.bridge.json`, maps tool names to handler classes, invokes the matching handler with a parsed `BridgeCommand`. Returns `ToolResult` (success path) or `BridgeError` (stage=execute). **Acceptance:** Unit test: dispatch by name to a stub handler returns the stub's output; unknown tool name → exit-4-equivalent error.
- [ ] **Step 8. Implement `compile_verify` (build-tool adaptive).** **File:** `src/main/java/com/nissth/bridge/tools/CompileVerify.java`. **Operation:** add. **Behavior:** Auto-detects target build tool by file presence in working dir (passed via `scope.root_path` or cwd): `pom.xml` → Maven path; `build.gradle` or `build.gradle.kts` → Gradle path; both → `stage="validate"` error (exit 2) citing both files; neither → `stage="validate"` error (exit 2) citing missing build file. **Maven path:** invokes `mvn clean compile test-compile -U -B` (clean `target/`, force snapshot refresh, batch mode). No daemon to stop — Maven has no persistent daemon by default; the freshness contract is "clean + -U + -B". **Gradle path:** invokes `./gradlew --stop` (kills daemon), then `./gradlew clean compileJava compileTestJava --no-daemon`. If `--stop` is skipped/fails for the Gradle path, returns `stage="execute"` error and exits 5 (freshness contract violated). Both paths parse output for errors. Writes report with `freshness.source = "maven|gradle subprocess in <pwd>"`, `freshness.source_state = "<tool-specific stamp at <ts>>"`, `freshness.guarantee = "Clean rebuild ran; classpath fresh; Gradle daemon stopped beforehand or Maven (no daemon) in batch mode with -U"`. Body: status (CLEAN | HAS_ERRORS) + error table (file:line:column, message). **Acceptance:** Integration test against Maven fixture: clean fixture → report.status=CLEAN; break fixture compile (inject a syntax error temporarily) → report.status=HAS_ERRORS with at least one error row; restore fixture. Gradle path exercised via a unit test that stubs the subprocess and verifies the --stop precondition (force-skip-stop → exit 5).
- [ ] **Step 9. Implement `endpoint_lens`.** **File:** `src/main/java/com/nissth/bridge/tools/EndpointLens.java`. **Operation:** add. **Behavior:** Uses JavaParser (`com.github.javaparser:javaparser-core:3.25.10+`) to AST-scan `**/*.java` under `scope.root_path` (default: `src/main/java`) optionally filtered by `scope.package`. Discovers `@RestController` + `@*Mapping` annotations. Emits endpoint table: HTTP method, URL path, controller class, method signature, request/response DTO references, auth annotations (`@PreAuthorize`, `@Secured`). After report write, invokes `StaleFlipper` against `DBL/APIIndex/*.md` whose `covers` overlaps the scope. **Acceptance:** Integration test against fixture's single controller: report lists exactly one endpoint with all fields populated; running against a project with no controllers writes an empty-but-valid report.
- [ ] **Step 10. Implement `entity_lens`.** **File:** `src/main/java/com/nissth/bridge/tools/EntityLens.java`. **Operation:** add. **Behavior:** JavaParser AST-scan for `@Entity` classes. Per entity: table name (from `@Table` or class name), columns (`@Column` fields incl. type, nullability, length), indexes (`@Index`), relationships (`@OneToMany`, `@ManyToOne`, `@OneToOne`, `@ManyToMany` with owning side), `@EntityGraph` mentions. After write, invokes `StaleFlipper` against `DBL/SchemaIndex/*.md`. **Acceptance:** Integration test against fixture's single entity: report shows table, all columns with attributes, no relationships (fixture has only one entity).
- [x] **Step 11. Implement `migration_status` (build-tool adaptive).** **File:** `src/main/java/com/nissth/bridge/tools/MigrationStatus.java`. **Operation:** add. **Behavior:** Detects target build tool (same auto-detection as Step 8). **Pre-check:** verify the target project actually has the Flyway plugin configured. *Maven:* parse target `pom.xml` and assert `flyway-maven-plugin` is present in `<build><plugins>` OR `<pluginManagement>`. *Gradle:* parse `build.gradle`/`build.gradle.kts` and assert `org.flywaydb.flyway` is applied. **If the plugin is missing**, return `stage="execute"` error with `error_code="missing_flyway_plugin"` and a multi-line `error` field that names the offending build file and includes a copy-pasteable plugin block the consumer can drop into their build file, plus a one-line reason ("`migration_status` reads Flyway state by invoking the build tool's Flyway goal; that requires the Flyway plugin to be configured in your target project."). **Maven path:** invokes `mvn flyway:info -B` and `mvn flyway:validate -B`. **Gradle path:** invokes `./gradlew flywayInfo` and `./gradlew flywayValidate`. Parses ASCII table output of either tool, classifies each migration as `APPLIED | PENDING | FAILED | OUT_OF_ORDER`. Detects checksum drift. **Acceptance:** Integration test against Maven fixture (`flyway-maven-plugin` configured in fixture's `pom.xml`): report lists `V1__init.sql` as APPLIED **after the test's `pre-integration-test` phase ran `flyway:migrate`** (see Step 17 ordering); PENDING if migrate did not run. A second test case: temporarily strip the Flyway plugin from a copy of the fixture's pom, invoke `migration_status` → assert `stage="execute"`, `error_code="missing_flyway_plugin"`, error message contains the configuration snippet.
- [x] **Step 12. Implement `entity_field_add` (action, hard-enforce).** **File:** `src/main/java/com/nissth/bridge/tools/EntityFieldAdd.java`. **Operation:** add. **Inputs (via `scope.extra`):** `entity_fqn`, `field_name`, `field_type`, `nullable` (default false), `column_name` (defaults to snake_case of `field_name`), `column_sql_type` (optional override — see table below). **Default SQL type mapping (Java → PostgreSQL):** the tool ships a built-in table mapping Java types to PostgreSQL column types: `String`→`VARCHAR(255)`, `Integer`/`int`→`INTEGER`, `Long`/`long`→`BIGINT`, `Short`/`short`→`SMALLINT`, `Boolean`/`boolean`→`BOOLEAN`, `Double`/`double`→`DOUBLE PRECISION`, `Float`/`float`→`REAL`, `java.math.BigDecimal`→`NUMERIC(19,4)`, `java.time.LocalDate`→`DATE`, `java.time.LocalDateTime`→`TIMESTAMP`, `java.time.OffsetDateTime`/`java.time.Instant`→`TIMESTAMPTZ`, `java.util.UUID`→`UUID`, `byte[]`→`BYTEA`, `java.util.Map`/`java.util.HashMap` with annotation hint→`JSONB`. Unknown types: tool errors with `stage="validate"`, lists the closest supported type, and points to `scope.extra.column_sql_type` for explicit override. **Agent-discipline rule (binding convention):** on the **first invocation of `entity_field_add` per project session**, the agent surfacing the call MUST show this mapping table to the user and request confirmation OR `column_sql_type` overrides before submitting the command. Documented in `Bindings/SpringBoot/README.md`; the tool's `--describe` output (Step 13) also prints the table. Subsequent calls in the same session may use defaults without re-asking. **Behavior, atomic:** (a) parse target `.java` with JavaParser; (b) compute next Flyway version number by scanning `src/main/resources/db/migration/V*.sql`; (c) build the `@Column` field declaration + getter/setter; (d) resolve SQL type via the mapping table (or `column_sql_type` override) and build the matching `V<n>__add_<field>_to_<table>.sql` with `ALTER TABLE <table> ADD COLUMN <col> <sql_type> [NOT NULL DEFAULT ...]`; (e) write BOTH files in a try-block; (f) if either write fails, roll back both (delete the migration if .java rollback succeeded; restore .java original bytes if migration write succeeded) and exit 5 with `stage="execute"` and `error="hard-enforce contract violated: ..."`. **Acceptance:** Integration test: success path — both files exist, field is added, migration is syntactically valid SQL, exit 0; failure-simulation test — read-only migration directory → exit 5, target `.java` unchanged (verify via SHA before/after); concurrent invocation with version collision → exit 5, no partial writes; unknown-type test — pass `field_type="com.example.WeirdType"` without `column_sql_type` → `stage="validate"` error citing the unknown type.
- [x] **Step 13. Implement `NissthBridgeCli`.** **File:** `src/main/java/com/nissth/bridge/cli/NissthBridgeCli.java`. **Operation:** add. **Behavior:** Entry-point parses CLI args per §11.5 (flag form with `--scope.<key> <value>` flattening, `--json-stdin`, `--list-bindings`, `--list-tools [--binding <id>]`, `--describe <tool>`). Constructs a `BridgeCommand`, dispatches via `ToolDispatcher`, prints the report path (destination=file) or body (destination=return) to stdout. Exit codes per §11.5: 0/2/3/4/5. **Acceptance:** CLI unit tests: each flag form produces correct `BridgeCommand`; `--list-tools` lists exactly five; `--describe entity_field_add` prints the enforcement contract; unknown tool → exit 4; freshness violation → exit 5.
- [x] **Step 14. Build `nissth-bridge` launcher.** **Files:** `Bindings/SpringBoot/scripts/nissth-bridge` (POSIX shell), `Bindings/SpringBoot/scripts/nissth-bridge.ps1` (PowerShell). **Operation:** add. **Behavior:** Wrap `java -jar <repo-root>/Bindings/SpringBoot/target/nissth-bridge-0.1.0.jar "$@"`. Resolve the jar path relative to the script's own location so the launcher works from any cwd. **Acceptance:** From any directory containing `Bindings/`, `./Bindings/SpringBoot/scripts/nissth-bridge --list-tools` works.
- [x] **Step 15. Implement MCP wrapper (Node shim).** **Files:** `Bindings/SpringBoot/mcp/package.json`, `Bindings/SpringBoot/mcp/index.js`, `Bindings/SpringBoot/mcp/README.md`. **Operation:** add. **Behavior:** Tiny MCP server using `@modelcontextprotocol/sdk` (Node), registers four tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`). Each tool shells out to `nissth-bridge` with the appropriate JSON payload constructed from the MCP tool arguments. No in-process JVM; pure subprocess. **Acceptance:** MCP smoke test (manual or scripted): launch shim, send `Nissth_Status` → returns binding list and last-N reports; send `Nissth_Gateway` with a `compile_verify` payload → returns the file path produced by the CLI.
- [x] **Step 16. Create fixture Spring Boot project (Maven).** **Directory:** `Bindings/SpringBoot/tests/fixture/`. **Operation:** add. **Contents:** Minimal Spring Boot 3.2.x project with: `pom.xml` (parent: spring-boot-starter-parent 3.2.x; deps: spring-boot-starter-data-jpa, spring-boot-starter-web, flyway-core, postgresql driver, testcontainers, testcontainers-postgresql, spring-boot-starter-test; plugins: spring-boot-maven-plugin, **flyway-maven-plugin with `flyway:migrate` bound to the `pre-integration-test` phase** so the IT tests see APPLIED migrations), `src/main/java/com/example/fixture/FixtureApplication.java`, `Item.java` (`@Entity` with id, name, qty), `ItemController.java` (`@RestController` with one GET endpoint), `ItemRepository.java`, `src/main/resources/application.yml` (Testcontainers JDBC URL pulling a Testcontainers-managed Postgres at test time; profile-aware config so manual `mvn flyway:migrate` runs against an env-supplied URL), `src/main/resources/db/migration/V1__init.sql`, and `src/test/resources/DBL/SchemaIndex/items.md` (synthetic DBL artifact with intentionally-wrong column data to exercise the stale-flip path in §4.2). **Acceptance:** `cd Bindings/SpringBoot/tests/fixture && mvn -q -DskipTests package` succeeds.
- [ ] **Step 17. Author binding integration tests.** **Files:** `Bindings/SpringBoot/src/test/java/com/nissth/bridge/it/<Tool>IT.java` (Maven Failsafe IT-suffix convention) — one per tool. **Operation:** add. **Pre-execution gate:** at the start of `mvn verify`, the Failsafe-bound `pre-integration-test` phase runs a Java-based Docker availability probe (`com.nissth.bridge.it.DockerProbeIT` runs first via class-name ordering or via `@Order`). If Docker daemon is not reachable, the probe **fails fast** with a clear `AssertionError` ("Docker daemon not running; start Docker Desktop and re-run `mvn verify`. Step 17 of Phase 05 requires Testcontainers PostgreSQL — see `CLAUDE.md` §8.6.1.") so the remaining IT tests don't waste minutes on container start timeouts. **Test behavior:** Each test programmatically constructs a `BridgeCommand` for its tool scoped to the fixture project (`Bindings/SpringBoot/tests/fixture/`), runs the tool end-to-end, asserts on the produced report's frontmatter (schema-validated) and key body fields. **Test ordering for `MigrationStatusIT`:** the test's `@BeforeAll` invokes `mvn -q flyway:migrate` (or programmatic Flyway via the same JDBC URL Testcontainers exposed) against the fixture's Testcontainers Postgres before asserting `APPLIED`. A second test case in the same class invokes `migration_status` against the freshly-Testcontainers-provisioned-but-not-migrated database and asserts `PENDING`. **Test ordering for `EntityLensIT`:** the synthetic `src/test/resources/DBL/SchemaIndex/items.md` is copied (not moved) into `target/test-classes/DBL/SchemaIndex/items.md` so the original stays clean; the test invokes `entity_lens`, then asserts the copy's frontmatter contains `last_regenerated: STALE — superseded by ...`. **Acceptance:** All five integration tests PASS via `cd Bindings/SpringBoot && mvn verify -B` (Failsafe runs `*IT.java` in the `integration-test` phase; report read from `target/failsafe-reports/`). If Docker daemon is unavailable: `mvn verify` exits non-zero with the Docker-probe message as the first failure cited in `target/failsafe-reports/`.
- [x] **Step 18. Author hard-enforce contract tests.** **File:** `Bindings/SpringBoot/src/test/java/com/nissth/bridge/contract/EntityFieldAddContractTest.java`. **Operation:** add. **Behavior:** Tests for `entity_field_add`'s hard-enforce contract: (a) read-only migration directory → exit 5, .java SHA unchanged; (b) version-number collision → exit 5, no writes; (c) malformed entity FQN → stage=validate error; (d) success path → both files exist with expected content. Also test for `compile_verify`'s --stop precondition: bypass `--stop` via a test-only flag → tool refuses to return CLEAN. **Acceptance:** All contract tests PASS.
- [x] **Step 19. Schema-validation harness.** **File:** `Bindings/SpringBoot/src/test/java/com/nissth/bridge/contract/SchemaValidationTest.java`. **Operation:** add. **Behavior:** For each of the five integration tests' produced reports, validate the YAML frontmatter against `$defs.reportFrontmatter` in `Bindings/_schemas/bridge-command.schema.json`. **Acceptance:** Validation passes for all five reports.
- [ ] **Step 20. Final binding self-build.** **Command:** `cd Bindings/SpringBoot && mvn clean verify -U -B`. **Operation:** verify. **Acceptance:** Build CLEAN; all unit (Surefire) + integration (Failsafe) + contract tests PASS; jar produced at `target/nissth-bridge-0.1.0.jar`; launcher script can invoke it.

### 3.2 Forbidden in this phase

> Explicitly list what is OUT OF SCOPE. This is the anti-scope-creep guard.

- **No Expo binding work.** `Bindings/Expo/` is Phase 06.
- **No PostgreSQL binding work.** `Bindings/Postgres/` is Phase 07.
- **No Süprüz changes.** The Süprüz directory (`Desktop/Supruz/`) and its Roadmaps stay untouched. Süprüz bootstrap stays paused at its 2026-05-07 `Next:` step.
- **No changes to `Bindings/_schemas/bridge-command.schema.json`.** The contract was frozen in the 2026-05-15 framework-hardening entry. If a contract gap is discovered, STOP, append a status entry, propose a contract revision as a separate plan.
- **No additional tools beyond the five.** No `bean_graph`, `config_lens`, `test_run`, `coverage_report`, `dep_insight`, `audit_deps`, `migration_author`, `endpoint_scaffold`, `test_scaffold` — all of those are later slices.
- **No DBL auto-regeneration tooling under `Tools/`.** Phase 5+ work explicitly out of core (per CLAUDE.md banner).
- **No publishing/release work.** No Maven Central, npm registry, GitHub releases. `binding_version: "0.1.0"` is internal until a separate release plan.
- **No edits to `CLAUDE.md` §11 prose.** The contract documentation is final for this phase. Only §5 file-roles rows may be touched in §5 Cleanup if drift is detected.
- **No `@Autowired` field injection in binding code.** Constructor injection only (per `CLAUDE.md` §8.5).
- **No raw JDBC for fixture/test data.** Use Spring Data + JPA (per `CLAUDE.md` §8.5).
- **No H2 in any test.** Testcontainers PostgreSQL only (per `CLAUDE.md` §8.6.1).
- **No bundling of tools that share an executable.** Each of the five tools is a separate class. Refactoring to a common base class is fine; merging tools is not.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

> How do you know the verifier sees the latest changes? (Addresses the "false CLEAN" failure mode — HR#10.)

- The binding's own build is Maven: `mvn clean verify -U -B` (Step 20). Maven has no persistent daemon (unlike Gradle), so the "stop daemon first" step is N/A for the binding's self-build; `mvn clean` evicts cached compiled classes from `target/`, `-U` forces snapshot dependency refresh, and `-B` (batch mode) avoids interactive output that can mask warnings. This is the Maven equivalent of §8.6's freshness sequence.
- `compile_verify` (Step 8) is build-tool adaptive: for **Gradle** target projects it enforces the original §8.6 sequence (`--stop` + clean compile, exit 5 if `--stop` is skipped); for **Maven** target projects it runs `mvn clean compile test-compile -U -B` (no daemon to stop; the freshness contract is "clean target/ + snapshot refresh + batch mode"). Step 18's contract test exercises both: the Gradle exit-5 path via a stubbed-subprocess unit test; the Maven CLEAN/HAS_ERRORS paths against the Maven fixture and a deliberately-broken Maven sub-fixture.
- `entity_field_add`'s hard-enforce contract is build-tool-independent (exit 5 if the migration write fails atomically). Step 18 verifies this via direct SHA-comparison before and after the failure simulation. Unchanged by the pivot.
- All five integration tests run against the Maven fixture project in a fresh Testcontainers PostgreSQL container per `@SpringBootTest` class — no shared container state (per §8.6.1).
- Schema validation (Step 19) reads the produced report files from disk after writes complete; no in-memory shortcuts.

### 4.2 Checks

- [ ] **Build:** `cd Bindings/SpringBoot && mvn clean package -DskipTests -U -B` — expected exit 0; `Bindings/SpringBoot/target/nissth-bridge-0.1.0.jar` exists.
- [ ] **Tests:** `cd Bindings/SpringBoot && mvn verify -U -B` — expected: all unit (Surefire `*Test.java`), integration (Failsafe `*IT.java`), and contract tests PASS. Read `Bindings/SpringBoot/target/surefire-reports/` and `target/failsafe-reports/`.
- [ ] **Runtime/integration:** `./Bindings/SpringBoot/scripts/nissth-bridge --list-tools` returns exactly the five tool names. `./Bindings/SpringBoot/scripts/nissth-bridge --describe entity_field_add` prints the hard-enforce contract.
- [ ] **MCP smoke:** Launch `Bindings/SpringBoot/mcp/index.js` via `node index.js`, send a `Nissth_Status` MCP call (test harness), verify response shape (binding list, last-N reports).
- [ ] **Bridge re-query:** Run `compile_verify` against the fixture project after the build completes. Expected: report.status=CLEAN with a freshness stamp `<ts>` newer than the integration test's report. No DBL artifacts were modified in Nissth core (it has none); STALE-flip path is exercised via `entity_lens` integration test against the fixture's synthetic `src/test/resources/DBL/SchemaIndex/items.md` (intentionally-wrong column data) — assertion: post-run, that DBL artifact's frontmatter contains `last_regenerated: STALE`.
- [ ] **DBL freshness:** No real DBL exists in Nissth core to regenerate. The fixture's synthetic DBL artifact stays in STALE state after the test — expected end state, demonstrating the stale-flip works. No further regeneration action required this phase.

### 4.3 Pass criteria

ALL of the following must be true:
- Step 20 build CLEAN; jar produced; launcher script invocable on both POSIX shells and PowerShell.
- All 5 integration tests (one per tool) PASS.
- Hard-enforce contract test PASSES — `entity_field_add` exits 5 on simulated migration-write failure with the target `.java` unchanged (SHA-verified).
- `compile_verify` --stop precondition contract test PASSES.
- Schema validation harness PASSES — every produced report's frontmatter validates against `$defs.reportFrontmatter`.
- `nissth-bridge --list-tools` returns exactly five tool names matching `CLAUDE.md` §11.12.
- STALE-flip integration test PASSES — fixture's synthetic DBL artifact's frontmatter contains `last_regenerated: STALE — superseded by ...` after `entity_lens` runs.
- MCP smoke test returns expected `Nissth_Status` shape.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Author an `incident` Report under `AgentReports/Reports/YYYY-MM-DD_phase-05-<slug>.md` per §10.4(1) — required for every `Verified: FAIL`.
4. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [ ] Remove any scratch files created under `Bindings/SpringBoot/target/` during iteration; keep `target/nissth-bridge-0.1.0.jar` (the binary artifact).
- [ ] Verify no `Temp_*.java` or scratch files at any non-target location (`Bindings/SpringBoot/src/**`).
- [ ] Roll snapshots if any were taken (none expected — this is a greenfield create, not a destructive multi-step edit).
- [ ] **Reports check (CLAUDE.md §10):**
  - **§10.4(4) — non-trivial phase close** TRIGGERS a `snapshot` Report. Author `AgentReports/Reports/2026-05-15_phase-05-bridge-springboot-snapshot.md` (use actual phase-close date) summarizing: architecture as built, divergences from this plan (if any), the binding's API surface for downstream phases (Expo, Postgres), known limitations, performance/cost characteristics observed during integration tests. ~1500–2500 tokens.
  - No `decision` Report needed unless an option-choice arose mid-execution.
  - List authored Reports here so they appear in the §6 status entry's `Reports:` line.
- [ ] **Document Sync sweep (Hard Rule #11):**
  - Source files modified in §3: only files under `Bindings/SpringBoot/**` (a new subtree). No existing Nissth-core file is modified by this plan.
  - For each, identify affected stable documents:
    - `DBL/**` artifacts — none exist in Nissth core.
    - Plans in `ImplementationPlans/` cross-referencing the binding — none yet (this is the first).
    - `CLAUDE.md` examples — §11.12 lists the five tools by name; §5 File Roles row mentions `Bindings/<stack>/**`. Verify both stayed consistent with what landed; if a tool's effective name diverged (it should not — forbidden in §3.2), STOP and treat as Verified: FAIL.
  - Action: UPDATE: confirm `CLAUDE.md` §11.12 still matches; if any tool name or kind changed in execution, that is itself a Verified: FAIL.
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [updated: CLAUDE.md §11.12 consistency confirmed; marked stale: none]`.
- [ ] No orphan branches, no leftover debug code (no `System.out.println` outside the report writer's intentional console-destination path).

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 05: Bridge — Spring Boot First Slice

**State:**
- Phase: 5/5+ — bridge first slice complete; Expo and Postgres bindings to follow as later phases
- Build: CLEAN
- Tests: PASS
- Active plan: ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md
- DBL refs: none — Nissth core has no DBL; consuming projects' DBL is untouched this phase
- Bridge reports: AgentReports/Bridge/compile_verify_<ts>.md, endpoint_lens_<ts>.md, entity_lens_<ts>.md, migration_status_<ts>.md, entity_field_add_<ts>.md (one per tool, produced by integration tests)
- Blockers: none

**Report:**
- [condensed from §1 findings — all Match? = yes]
- Five tools (compile_verify, endpoint_lens, entity_lens, migration_status, entity_field_add) implemented end-to-end per §11.12; CLI dispatcher (Java) + MCP shim (Node) built and validated; fixture Spring Boot project + integration/contract/schema tests pass.

**Executed:**
- [condensed from §3, with all 20 checkboxes resolved]

**Verified:**
- Build: CLEAN via `cd Bindings/SpringBoot && mvn clean verify -U -B` reading `target/surefire-reports/` and `target/failsafe-reports/` at <ts>.
- Tests: all unit, integration (5), contract (entity_field_add + compile_verify), and schema-validation tests PASS.
- Bridge re-query: STALE-flip works — fixture's synthetic DBL artifact's frontmatter contains `last_regenerated: STALE — superseded by ...` after `entity_lens` runs.
- MCP smoke: `Nissth_Status` returns expected binding list.
- Freshness: §8.6 protocol followed for the binding's own build; `compile_verify` enforces the same protocol for downstream phases via exit 5.
- Doc sync: [updated: CLAUDE.md §11.12 consistency confirmed; marked stale: none — Nissth core has no DBL].
- Reports: AgentReports/Reports/<ISO>_phase-05-bridge-springboot-snapshot.md (snapshot, per §10.4(4)).

**Issues:**
- none — or list any deferred items (e.g., MCP wrapper's `Nissth_Verify` short-mode mapping if it required a follow-up).

**Next:**
- User decides: (a) resume Süprüz bootstrap (sequence preserved from 2026-05-07 Next: bootstrap framework files + relocate PDFs + author SRS/SDD markdown + add PostGIS to §8.1 + author Phase_00_DBL_Bootstrap.md and request approval), now with the Spring Boot binding available for Süprüz to consume; OR (b) extend the Bridge to a second stack — author Phase_06_Bridge_Expo_FirstSlice.md (frontend) or Phase_07_Bridge_Postgres_FirstSlice.md (database). Both branches plan-required per HR#12.
```
