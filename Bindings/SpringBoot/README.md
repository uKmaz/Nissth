# Nissth Diagnostic Bridge — Spring Boot Binding

Implements the Nissth Diagnostic Bridge contract (`CLAUDE.md` §11, `Bindings/_schemas/bridge-command.schema.json`) for Spring Boot projects.

**Binding ID:** `spring-boot` &nbsp;·&nbsp; **Version:** `0.1.0` &nbsp;·&nbsp; **Language:** Java 17+ &nbsp;·&nbsp; **Build:** Maven 3.9+

This binding's own build is Maven. The diagnostic and action tools it provides auto-detect Gradle vs Maven in the **target project** (the consumer's Spring Boot codebase), so the binding works against either.

---

## Tool catalog

| Tool | Kind | Modes | Key scope fields | Freshness source |
|:---|:---|:---|:---|:---|
| `compile_verify` | diagnostic | `default` | `root_path` | Subprocess: `mvn clean compile test-compile -U -B` OR `./gradlew --stop && ./gradlew clean compileJava compileTestJava --no-daemon` |
| `endpoint_lens` | diagnostic | `default`, `with_dto` | `root_path`, `package`, `max_depth` | AST parse (JavaParser) of `**/*.java` under scope |
| `entity_lens` | diagnostic | `default`, `with_relationships` | `root_path`, `package`, `max_depth` | AST parse (JavaParser) of `**/*.java` under scope |
| `migration_status` | diagnostic | `default` | `root_path` | Subprocess: `mvn flyway:info` OR `./gradlew flywayInfo` |
| `entity_field_add` | **action** | `default` | `root_path` + `scope.extra` (see below) | Direct file edit + Flyway migration emit, atomic |

### Hard-enforce contracts

Two tools enforce previously-soft `CLAUDE.md` rules at the runtime layer (`CLAUDE.md` §11.7):

- **`compile_verify`** — Gradle path refuses to return `CLEAN` if `--stop` was skipped. Exit code 5 with `stage="execute"` error. Automates `CLAUDE.md` §8.6 (false-CLEAN protocol). For Maven targets, no daemon exists; the freshness contract is "`clean` + `-U` + `-B`".
- **`entity_field_add`** — refuses to commit a partial state. Writes the `@Entity` edit and the matching Flyway migration in one atomic operation; on partial failure (e.g., migration directory read-only, version-number collision, target file disappeared mid-write), rolls back the `.java` edit and exits 5. Enforces `CLAUDE.md` §8.9 (entity/migration ripple).

---

## `scope.extra` keys (per tool)

The cross-stack contract (`CLAUDE.md` §11.2) allows binding-specific filters in `scope.extra`. This binding consumes them only for `entity_field_add`:

| Tool | Key | Type | Required | Default | Meaning |
|:---|:---|:---|:---:|:---|:---|
| `entity_field_add` | `entity_fqn` | string | yes | — | Fully qualified Java class name (e.g., `com.example.fixture.Item`) |
| `entity_field_add` | `field_name` | string | yes | — | Java field name, camelCase |
| `entity_field_add` | `field_type` | string | yes | — | Java type (e.g., `String`, `java.time.LocalDate`, `Integer`) |
| `entity_field_add` | `nullable` | boolean | no | `false` | Drives `@Column(nullable=...)` AND `NOT NULL` in the migration |
| `entity_field_add` | `column_name` | string | no | snake_case of `field_name` | DB column name |
| `entity_field_add` | `column_sql_type` | string | no | derived from `field_type` | Override the SQL type mapping (e.g., `JSONB` for a `Map` field) |

The other four tools do not consume any `scope.extra` keys — only top-level `scope.*` fields.

### Default Java → PostgreSQL SQL type mapping (`entity_field_add`)

When `entity_field_add` resolves a column's SQL type, it uses this built-in mapping. Override per-call with `scope.extra.column_sql_type`. **Agent-discipline rule:** on the first `entity_field_add` invocation per project session, the calling agent surfaces this table to the user and requests confirmation or overrides — *do not silently default* in the first call.

| Java type | PostgreSQL type |
|:---|:---|
| `String` | `VARCHAR(255)` |
| `Integer`, `int` | `INTEGER` |
| `Long`, `long` | `BIGINT` |
| `Short`, `short` | `SMALLINT` |
| `Boolean`, `boolean` | `BOOLEAN` |
| `Double`, `double` | `DOUBLE PRECISION` |
| `Float`, `float` | `REAL` |
| `java.math.BigDecimal` | `NUMERIC(19,4)` |
| `java.time.LocalDate` | `DATE` |
| `java.time.LocalDateTime` | `TIMESTAMP` |
| `java.time.OffsetDateTime`, `java.time.Instant` | `TIMESTAMPTZ` |
| `java.util.UUID` | `UUID` |
| `byte[]` | `BYTEA` |
| `java.util.Map` (with JSON hint via `scope.extra`) | `JSONB` |

Unknown Java types: tool errors with `stage="validate"`, names the closest supported type, and points the caller to `scope.extra.column_sql_type` for explicit override.

`scope.extra.nullable` (default `false`) drives both `@Column(nullable=...)` and the migration's `NOT NULL`/`NULL` clause. A non-nullable column requires `scope.extra.column_default` (string, raw SQL expression) so existing rows have a value during the migration.

---

## Install

### Prerequisites

- **Java 17+** (tested with Eclipse Temurin 17.0.19 and Oracle JDK 21.0.7)
- **Maven 3.9+** — system `mvn` OR the bundled Maven Wrapper (`./mvnw`). The wrapper auto-bootstraps Apache Maven 3.9.9 into `~/.m2/wrapper/dists/` on first use; no system install required.
- The target project's own toolchain — Gradle and/or Maven — for tools that drive build subprocesses

### Build

```bash
cd Bindings/SpringBoot
./mvnw clean verify -U -B        # POSIX shells, Git Bash
.\mvnw.cmd clean verify -U -B    # PowerShell / cmd
# or, if you have a system Maven on PATH:
mvn clean verify -U -B
```

Produces:
- `target/nissth-bridge-0.1.0.jar` — executable jar (`Main-Class: com.nissth.bridge.cli.NissthBridgeCli`)
- `target/surefire-reports/` — unit test results
- `target/failsafe-reports/` — integration test results

### Run from the repo

```bash
# POSIX shell
./scripts/nissth-bridge --list-tools
./scripts/nissth-bridge compile_verify --scope.root_path ../../tests/fixture

# PowerShell
.\scripts\nissth-bridge.ps1 --list-tools
.\scripts\nissth-bridge.ps1 compile_verify --scope.root_path ..\..\tests\fixture
```

### Put on PATH

Add the absolute path to `Bindings/SpringBoot/scripts/` to your shell's PATH, or symlink `nissth-bridge` into `/usr/local/bin/` (POSIX). The launcher resolves the jar path relative to its own location, so it works from any cwd.

### MCP integration

The Node MCP shim under `mcp/` registers four MCP tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`) that shell out to `nissth-bridge`. See `mcp/README.md` for the MCP server registration block.

---

## Where reports land

Every tool writes its report to `<repo-root>/AgentReports/Bridge/<tool>_<ISO8601>.md` by default (or `output.file_name` if provided). The report's mandatory frontmatter conforms to `Bindings/_schemas/bridge-command.schema.json` `$defs.reportFrontmatter` (see `CLAUDE.md` §11.3). Validation happens at write time; a failing write is `stage="format"` error.

---

## Pointers

- **Contract spec:** `CLAUDE.md` §11
- **JSON Schema:** `Bindings/_schemas/bridge-command.schema.json`
- **Spring Boot stack rules:** `CLAUDE.md` §8 (especially §8.5 forbidden patterns, §8.6 false-CLEAN protocol, §8.9 entity ripple)
- **Plan that built this binding:** `ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md`
- **Maven pivot rationale:** `AgentReports/Reports/2026-05-15_phase-05-maven-pivot.md`
