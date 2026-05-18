# Nissth — Claude Code Project Instructions

> Token-lean deterministic execution framework. Claude Code is the primary harness; `AGENTS.md` provides a thin redirect for any non-Claude agent that wanders in. This file is the COMPLETE reference for rules, schemas, and templates.
>
> **Status:** Phase 4 of 4 — Spring Boot stack bound. Framework is operational. Plan-before-execute gate (Hard Rule #12) and Reports taxonomy (§10) added 2026-05-06. Diagnostic Bridge contract (§11) added 2026-05-15 — runtime layer above raw source, per-stack bindings under `Bindings/`. Permission gate (Hard Rule #13) added 2026-05-15 — explicit user consent required before any new Nissth-bound project init begins (§9.1 Step 0). Phase 5+ (Claude Code hook-based enforcement under `.claude/`, DBL auto-regeneration tooling under `Tools/`) is hardening, not core.

---

## 1. Boot Protocol — MANDATORY

Before any other action — before reading code, running tools, browsing directories, or proposing work — execute these steps in order:

1. **Read `AgentReports/StatusUpdate.md`.** The **latest entry** (bottom of file) is the current project state. For long files, use `Read` with an offset to read only the tail (last entry block).
2. **The `**Next:**` field of that latest entry is your first instruction this session.**
3. The `**State:**` block tells you phase, build/test status, active plan, DBL refs, and blockers.
4. If `Active plan` is set, read `ImplementationPlans/<plan>.md` next.
5. Read DBL artifacts listed in `DBL refs` — and only those. Do not browse `DBL/` opportunistically.

Skipping step 1 is a violation of this framework, regardless of how trivial the user's request seems. If `StatusUpdate.md` does not exist, this directory is uninitialized — tell the user and stop.

---

## 2. Philosophy — One Sentence

**Agents must never explore — they must operate.**

Concretely: pre-structured context, deterministic workflows, minimal token usage. State persists in files (`StatusUpdate.md`, `ImplementationPlans/`, `DBL/`), not in chat history. The agent is a deterministic executor over a pre-computed knowledge graph, not a search agent over raw source.

---

## 3. The Loop — Non-Negotiable

Every unit of work follows this loop. There is no exception.

```
REPORT  →  EXECUTE  →  VERIFY  →  UPDATE STATUS
```

| Step | What it means | Output |
|:---|:---|:---|
| REPORT | State current state, target state, planned changes. Cite the DBL artifacts and source ranges you used. | A report block (in chat, or §1 of the active plan). |
| EXECUTE | Make the changes — only the changes named in REPORT. No opportunistic refactors. | Modified files. |
| VERIFY | Build / test / runtime check. Read the actual result. State the freshness guarantee. | Pass/fail outcome citing the artifact. |
| UPDATE STATUS | Append a NEW entry to `AgentReports/StatusUpdate.md`. The latest entry's `**State:**` block becomes the new current state by virtue of being the latest. | StatusUpdate.md committed. |

### Loop-Lock — Forbidden Sequences

- FORBIDDEN: Execute without a preceding Report.
- FORBIDDEN: Move to a new task before Verify on the previous one returns a result.
- FORBIDDEN: End the session (or hand off) without appending a status entry.
- FORBIDDEN: Edit, reorder, or delete past status entries — `StatusUpdate.md` is strictly append-only.
- FORBIDDEN: Treat "it should work" or "the code looks right" as Verify. Verify means an artifact was produced and read.

---

## 4. Hard Rules

1. **Read StatusUpdate.md first.** (§1.)
2. **No Silent Deviations.** If about to take an action not covered by this framework — using tools or shell commands beyond the authorized set, inventing a file location, breaking a Loop-Lock, etc. — STOP and tell the user what's missing. Do not self-approve a workaround.
3. **`StatusUpdate.md` is strictly append-only.** Whole file. No editable header section, no "current state" zone that gets overwritten. New entries are appended at the bottom only. If a past entry is wrong, write a new entry that supersedes it.
4. **Query the structured layers before reading source.** Nissth provides two pre-structured layers above raw source: **DBL** (§7) for stable architectural intent (modules, APIs, schemas) and the **Diagnostic Bridge** (§11) for live runtime state (current bean graph, migration status, compile status, query plans). If either layer answers the question, use it — DBL for "what is this project supposed to look like," Bridge for "what is this project doing right now." Re-reading raw files when a structured answer exists is a token leak.
5. **Scope every read.** When source must be read: target file + line range. No full-tree dumps. No `**/*` globs without an explicit reason recorded in your Report.
6. **Verify against the artifact, not the plan.** Build/test output is authoritative. The plan file is a contract, not evidence.
7. **One actionable Next.** The `**Next:**` field is a single concrete step, not a list.
8. **Convert relative dates** (today, yesterday, Thursday) to ISO format (`2026-05-05`) when writing to any persistent file.
9. **Snapshot before destructive multi-step work.** Save a rollback artifact under `AgentReports/Snapshots/` and reference it in your Report.
10. **Respect verifier freshness.** "Verify ran fine" only counts if the verifier saw your latest changes. State the freshness guarantee explicitly in the Verify line. (See `_TEMPLATE.md` §4.1.)
11. **Document Sync Mandate.** Before appending the closing status entry, run a Document Sync sweep: list source files modified during this task → identify stable documents that reference them (`DBL/` artifacts via `covers`, `ImplementationPlans/` cross-refs, `CLAUDE.md` examples) → for each affected document, either UPDATE it now or MARK it stale (DBL: `last_regenerated: STALE — [reason]` in frontmatter) and add regeneration to the next plan. Log the result in the status entry's `**Verified:**` block: `Doc sync: [updated: X, Y; marked stale: Z]`. A status entry without a `Doc sync:` line is a Loop-Lock violation.
12. **Plan-before-execute (no source change without an approved plan).** No source-modifying execution begins until an `ImplementationPlans/Phase_NN_*.md` plan exists, conforms to `_TEMPLATE.md`, and has `Approved: <ISO date>` in §0. This applies to every unit of work in every Nissth project, not just the first one. Bootstrap itself is mechanical (copy framework files, verify §9 inputs) and does not require a plan; the **first authored plan** in a new Nissth project is `Phase_00_DBL_Bootstrap.md` and it must run before any code change. "Source-modifying execution" means edits to anything outside `ImplementationPlans/`, `AgentReports/`, `DBL/`, `.claude/`, or bridge contract documentation (`Bindings/_schemas/`, `Bindings/README.md`, `Bindings/*/README.md`) — i.e., real product code (including binding implementations under `Bindings/<stack>/src/`), configs, schemas, or migrations. Authoring or revising plans, status entries, DBL artifacts, Reports, and bridge contract documentation themselves is plan-exempt because they ARE the planning surface. A Loop §3 EXECUTE step that touches product code without a matching approved plan is a Loop-Lock violation.
13. **Permission gate at project initialization.** Before any initialization action on a new Nissth-bound project — before reading inputs, authoring SRS/SDD, bootstrapping, creating files, running commands, or copying framework artifacts — the agent MUST explicitly ask the user for full permission to proceed. The agent enumerates the expected actions (e.g., "I'm about to: author SRS+SDD from your prompt, copy Nissth framework files into `<path>`, relocate the spec PDFs, author `Phase_00_DBL_Bootstrap.md`, request approval before executing it. Confirm?") and waits for unambiguous consent. Silence, partial responses, or "sounds good"-type answers do NOT satisfy the gate; explicit consent is required (e.g., "yes, proceed"). The gate fires **once per project** at initialization time; session resumes are governed by the boot protocol (§1), not this gate. Applies equally to greenfield projects and to adopting Nissth into existing code. The full initialization sequence is in §9.1 — this gate is its Step 0.

---

## 5. Project Structure

```
Nissth/
├── CLAUDE.md                       ← This file. Auto-loaded by Claude Code.
├── AGENTS.md                       ← Thin redirect for non-Claude agents — points here.
├── .claude/                        ← Claude Code config (settings.json, skills, hooks). Phase 5+.
├── ImplementationPlans/            ← Phase plans. Every plan MUST follow `_TEMPLATE.md` (§6).
│   └── _TEMPLATE.md                ← Canonical plan skeleton. Copy + rename to `Phase_NN_Slug.md`.
├── AgentReports/
│   ├── StatusUpdate.md             ← Single source of truth for state. Strictly append-only. (§1, §3.)
│   ├── Reports/                    ← Long-form human-authored reports (decisions, incidents, audits). (§10.)
│   ├── Bridge/                     ← Auto-generated Diagnostic Bridge tool reports. (§11.)
│   ├── Snapshots/                  ← Pre-change rollback artifacts (Hard Rule #9).
│   └── Archive/                    ← Rotated logs when StatusUpdate.md exceeds ~100KB.
├── DBL/                            ← Diagnostic Bridge Layer (stable). Pre-computed knowledge artifacts.
│   ├── Summaries/                  ← Per-module summaries.
│   ├── DependencyMaps/             ← Cross-module dependency graphs.
│   ├── APIIndex/                   ← API surface index.
│   └── SchemaIndex/                ← DB schema index.
├── Bindings/                       ← Per-stack Diagnostic Bridge implementations. (§11.)
│   ├── README.md                   ← Per-stack-binding model and contract pointers.
│   ├── _schemas/                   ← Machine-readable bridge contract (JSON Schema).
│   │   └── bridge-command.schema.json
│   └── <stack>/                    ← One subproject per stack (e.g., SpringBoot/, Expo/, Postgres/).
├── Tests/                          ← Verification artifacts and test sources.
├── Tools/                          ← Framework tooling — DBL generators, hooks, validators. Phase 5+.
└── Axios/                          ← Reference predecessor framework (Unity-specific). Read-only.
```

### File Roles

| File | Read by | Contains |
|:---|:---|:---|
| `CLAUDE.md` | Claude Code (auto-loaded) | This complete reference. |
| `AGENTS.md` | Other agents (Cursor, Codex, etc.) | Boot protocol summary + redirect to `CLAUDE.md`. |
| `AgentReports/StatusUpdate.md` | Every agent on session start | Append-only history. Latest entry IS the current state. |
| `AgentReports/Reports/<date>_<slug>.md` | Anyone needing detail behind a status entry | Long-form decision records, incident reports, design reviews, audits — anything that details the project. (§10.) |
| `AgentReports/Bridge/<tool>_<ts>.md` | Agent during Report step | Auto-generated live diagnostic reports from bridge tools. Disposable, high-volume. (§11.) |
| `ImplementationPlans/_TEMPLATE.md` | Plan author | Canonical phase plan skeleton. Source of truth for §6 below. |
| `ImplementationPlans/Phase_NN_*.md` | Agent executing a phase | One concrete plan per phase, in the template format. |
| `DBL/**` | Agent during Report step | Pre-computed answers to common questions about the project (stable layer). |
| `Bindings/_schemas/bridge-command.schema.json` | Bridge implementations and validators | Machine-readable command contract that every binding implements. (§11.2.) |
| `Bindings/<stack>/**` | Bridge CLI/MCP runtime | Per-stack diagnostic and action tool implementations. (§11.8.) |
| `.claude/settings.json` | Claude Code harness | Hooks, permissions, skills (loop-lock enforcement, Phase 5+). |

---

## 6. Implementation Template

Every file in `ImplementationPlans/` (except `_TEMPLATE.md` itself) MUST conform to `ImplementationPlans/_TEMPLATE.md`. The template enforces the Loop (§3) at the plan level: §1 is REPORT, §3 is EXECUTE, §4 is VERIFY, §6 is the pre-filled UPDATE STATUS entry.

### Required sections (template-enforced)

| § | Section | Maps to Loop step | Empty allowed? |
|:---|:---|:---|:---|
| 0 | Metadata (Plan ID, Authored, Approved, Depends on, Scope) | — | no |
| 1 | Pre-Flight Diagnostic (Inputs · Diagnostic actions · Findings table) | REPORT | no |
| 2 | Expected State (Before / After tables) | REPORT | no |
| 3 | Execution (Step list · Forbidden list) | EXECUTE | no |
| 4 | Post-Flight Verification (Freshness · Checks · Pass criteria · Failure handling) | VERIFY | no |
| 5 | Cleanup | — | no |
| 6 | Status Update Entry (paste-ready) | UPDATE STATUS | no |

### Authoring rules

- Plan files are named `Phase_NN_Slug.md` (zero-padded number, snake_case slug).
- A new plan must declare its dependencies in §0 `Depends on` (use prior plan IDs, or `none`).
- §3.2 **Forbidden in this phase** is mandatory and prevents scope creep — list explicitly out-of-scope changes that the agent might be tempted to bundle in.
- §4.4 **Failure handling** mandates: on any Verify failure, STOP and append a `Verified: FAIL` status entry. Do not proceed to Cleanup. Do not retry silently.
- A plan is `Approved: pending` until the user fills in the Approved date. The executing agent must not start §3 on an unapproved plan.

### Execution sequence (when a plan is approved)

1. Read `CLAUDE.md` (already in context) + the latest `StatusUpdate.md` entry.
2. Read the plan file end-to-end.
3. Execute §1 Pre-Flight. Fill in §1.3 Findings. If any `Match? = no`, STOP.
4. Execute §3 step list in order. Tick checkboxes as completed.
5. Execute §4 Verification. If any check fails, follow §4.4.
6. Execute §5 Cleanup.
7. Append the §6 entry (filled in) to `AgentReports/StatusUpdate.md`.

---

## 7. DBL Specification

The Diagnostic Bridge Layer is the agent's first stop for project knowledge. Every artifact here answers a question that would otherwise require raw source reads. Agents query DBL in the Report step (Hard Rule #4) and only fall through to source when DBL doesn't cover the question.

### 7.1 Artifact types

| Type | Directory | One file per | Answers |
|:---|:---|:---|:---|
| Summary | `DBL/Summaries/` | Module / component | What does this module do? Public API? Gotchas? |
| Dependency Map | `DBL/DependencyMaps/` | Boundary or scope | What imports what? Which imports are forbidden? |
| API Index | `DBL/APIIndex/` | API namespace | What endpoints/methods exist? Signatures? Auth? |
| Schema Index | `DBL/SchemaIndex/` | Database / schema | What tables exist? Columns, indexes, foreign keys? |

Each subdirectory contains a `_TEMPLATE.md` showing the per-type body shape. Copy + rename when authoring a new artifact.

### 7.2 Mandatory frontmatter

Every DBL artifact starts with YAML frontmatter. **No exceptions.** Without it, an agent cannot know whether the artifact is fresh — and reading a stale artifact is worse than reading source.

```yaml
---
artifact_type: summary | dependency_map | api_index | schema_index
name: <human-readable name>
last_regenerated: YYYY-MM-DD by [agent name | user]
source_state: <git commit hash, OR "uncommitted state at YYYY-MM-DD HH:MM">
covers:
  - <glob pattern or explicit path>
  - <another path>
stale_when:
  - <condition that invalidates this artifact>
  - <another condition>
---
```

### 7.3 Freshness check — MANDATORY before relying on a DBL artifact

Before citing an artifact in Report:

1. Read its frontmatter (only — not the body yet).
2. Compare `source_state` to current state. If the project has changed beneath any path in `covers`, treat the artifact as **STALE**.
3. If STALE: state this in your Report. Do NOT fabricate from the stale artifact. Either re-read the affected source range directly, or make DBL regeneration the first step of your plan.
4. After regenerating, update the frontmatter (`last_regenerated`, `source_state`).

### 7.4 Authoring rules

- **Hand-maintained for now.** Phase 5+ will introduce regeneration tooling under `Tools/`. Until then, each artifact is created and refreshed by a human or an agent that explicitly took on the task.
- **One artifact per file.** Do not bundle multiple modules into one Summary or multiple APIs into one Index.
- **File names are kebab-case `.md`** (e.g., `auth-module.md`, `user-service-api.md`).
- **Token budget per artifact: 200–800 tokens.** If a single artifact exceeds ~1500 tokens, split it (e.g., split a Summary into `<module>-overview.md` + `<module>-details.md`).
- **Answer questions; do not replicate source.** "Field list lives in `User.java:14-32`" is a valid answer when the field list is volatile.
- **Plan-level closure (`§5 Cleanup`)** must regenerate or flag staleness for any DBL artifact whose `covers` overlap with files modified during the phase.

### 7.5 What does NOT belong in DBL

- Implementation details that change with every refactor (the DBL would churn).
- Volatile state like "current open issues" or "active blockers" — belongs in `AgentReports/StatusUpdate.md`.
- User preferences or feedback — belongs in agent memory.
- Phase plans, decisions in flight — belongs in `ImplementationPlans/`.

### 7.6 First-population guidance

When a project starts using Nissth, populate DBL in this order:

1. One `Summary/` per top-level module — even a minimal one (purpose + file list).
2. One `DependencyMap/` for the project's primary boundary (frontend ↔ backend, or core ↔ adapters).
3. `APIIndex/` artifacts when an HTTP/RPC surface exists.
4. `SchemaIndex/` artifacts when a database is in play.

A project with no DBL is not a Nissth project; it is a candidate for Nissth's Phase 0 (DBL bootstrap).

---

## 8. Stack Bindings

Each stack Nissth supports gets its own sub-section here — agent-facing rules covering stack identity, layout, build/test commands, DBL mapping, forbidden patterns, verification protocol, common discovery patterns, ripple rules, and mandatory inputs for new projects under Nissth. Per-stack diagnostic and action tools live under `Bindings/<stack>/`; the rules in this section are what each binding implements.

Currently shipped: §8.1 Spring Boot (binding closed, 111/111 green at last regression check). In flight: §8.2 Expo (binding under active development per `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md`). Queued: PostgreSQL (no §8.3 yet authored — Phase 07 candidate).

### 8.1 Spring Boot

#### 8.1.1 Stack identity

| Field | Value |
|:---|:---|
| Language | Java 17+ (or Kotlin 1.9+ if explicitly chosen at SRS time) |
| Framework | Spring Boot 3.x |
| Build tool | Gradle with Kotlin DSL (preferred) — or Maven |
| Test runner | JUnit 5 + Spring Boot Test |
| Database | PostgreSQL 15+ |
| JDBC driver | `org.postgresql:postgresql` (version matched to Spring Boot 3.x release) |
| Persistence | Spring Data JPA + Hibernate over PostgreSQL — **`JpaRepository` is the default access pattern**; raw JDBC requires justification (§8.1.5) |
| Migrations | Flyway (preferred) or Liquibase |

#### 8.1.2 Conventional layout

```
src/
├── main/
│   ├── java/com/<org>/<project>/
│   │   ├── <ProjectName>Application.java     ← @SpringBootApplication entry
│   │   ├── api/                              ← @RestController classes
│   │   ├── service/                          ← @Service classes
│   │   ├── domain/                           ← @Entity / domain models
│   │   ├── repository/                       ← Spring Data interfaces
│   │   ├── dto/                              ← request/response shapes
│   │   ├── config/                           ← @Configuration classes
│   │   └── exception/                        ← custom exceptions + handlers
│   └── resources/
│       ├── application.yml                   ← single canonical config
│       ├── application-<profile>.yml         ← profile overrides
│       └── db/migration/                     ← Flyway SQL migrations (V<n>__*.sql)
└── test/
    ├── java/.../                             ← parallel structure to main/
    └── resources/                            ← test fixtures
```

A project that diverges from this layout records the divergence (and its reason) in `DBL/Summaries/_layout.md`.

#### 8.1.3 Build & test commands

| Action | Gradle | Maven |
|:---|:---|:---|
| Clean build | `./gradlew clean build` | `mvn clean package` |
| Run tests | `./gradlew test` | `mvn test` |
| Integration tests | `./gradlew integrationTest` (if configured) | `mvn verify` |
| Run application | `./gradlew bootRun` | `mvn spring-boot:run` |
| Dependency tree | `./gradlew dependencies` | `mvn dependency:tree` |

Always invoke from project root. The Gradle daemon caches stale state — see §8.1.6.

#### 8.1.4 DBL mapping for Spring Boot

| DBL type | Source signal | What to extract per artifact |
|:---|:---|:---|
| Summary | one per top-level package under `service/`, `api/`, or per feature module | purpose; public methods (signatures); constructor-injected deps; gotchas (e.g., transactional boundaries) |
| DependencyMap | one per architectural boundary (`api ↔ service ↔ repository`) | package-to-package import graph; explicit forbidden directions (e.g., `repository` MUST NOT import `service`) |
| APIIndex | one per `@RestController` group OR per logical API surface (`users-api.md`) | endpoint table from `@*Mapping` annotations; request/response shape pointers; auth from `@PreAuthorize` / `@Secured` |
| SchemaIndex | one per `@Entity` cluster (typically per bounded context) | tables from `@Table`; columns from `@Column`; indexes from `@Index`; relationships from `@OneToMany`/`@ManyToOne` |

#### 8.1.5 Forbidden patterns

1. **No raw shell search for beans/endpoints/entities.** Spring's annotation-driven discovery makes grep unreliable (mappings can be on the class, the method, or inherited via `@RequestMapping`). Query `DBL/APIIndex/` and `DBL/Summaries/` first.
2. **No gradle invocations without `clean` when verifying.** The daemon caches compiled classes; without `clean`, "test passed" can mean "tests passed against an old jar." See §8.1.6.
3. **No edits to `application.yml` / `application-*.yml` without a status entry.** Config drift is invisible in source diffs and breaks integration silently. Every config change is recorded.
4. **No bypassing migrations.** Schema changes go through Flyway/Liquibase, never directly to the DB or to `@Entity` without a corresponding migration file.
5. **No skipping integration tests when modifying entities.** FK and `@Transactional` semantics pass unit tests and fail at runtime.
6. **No `@Autowired` on fields.** Use constructor injection — required for testability and for DBL `Summaries` to capture deps cleanly.
7. **No `@Repository` business logic.** Repository interfaces stay query-only; logic lives in `@Service`. Violations show up in DependencyMap as repository-to-repository imports.
8. **No `spring.jpa.hibernate.ddl-auto=update` (or `create`/`create-drop`) outside the `dev` profile.** Schema is owned by Flyway, never by Hibernate auto-DDL. Production and staging profiles use `validate` (or `none`).
9. **No raw `JdbcTemplate` / `NamedParameterJdbcTemplate` for CRUD when a `JpaRepository` already covers the entity.** If raw JDBC is needed (bulk ops, reporting queries, PostgreSQL features JPA can't express cleanly), justify it in a code comment AND log it in the relevant `DBL/Summaries/` artifact.
10. **No N+1 queries.** When traversing relationships in a known-eager path, use `@EntityGraph` on the repository method or JPQL `JOIN FETCH`. Lazy-loading inside loops is a defect.
11. **No `nativeQuery = true` without justification.** Prefer derived query methods or JPQL. If native SQL is required (PostgreSQL `jsonb`, arrays, `ON CONFLICT ... RETURNING`, window functions, full-text search, etc.), the repository method's Javadoc states the reason in one line.
12. **No `@Transactional` on repositories.** Repository interfaces are propagation participants, not boundaries. Transaction boundaries belong on `@Service` methods. (Spring Data's default repository proxy adds `REQUIRED` propagation already.)

#### 8.1.6 Verification protocol — freshness guarantee

The Spring Boot equivalent of the "false CLEAN" trap (Hard Rule #10) is the **stale Gradle daemon**: a daemon caches compiled classes, classpath, and even some test results. An agent that runs `./gradlew test` after editing source can see a green build that actually ran against the previous compile.

**Required verification sequence:**

1. `./gradlew --stop` — kills any cached daemon.
2. `./gradlew clean build` — full clean rebuild. Compilation errors surface here.
3. `./gradlew test` (or `integrationTest`) — runs against the freshly built classes.
4. Read `build/reports/tests/test/index.html` — the report file is the verifier's artifact, not stdout.

**Freshness statement** (paste into `_TEMPLATE.md` §4.1):
> "Daemon stopped via `./gradlew --stop`; clean rebuild via `./gradlew clean build`; migrations validated via `./gradlew flywayValidate`; tests via `./gradlew test` against a fresh Testcontainers PostgreSQL container; report read from `build/reports/tests/test/index.html` at YYYY-MM-DD HH:MM."

##### 8.1.6.1 Database verification — PostgreSQL specifics

- **Tests run against real PostgreSQL via Testcontainers**, not H2. H2's `MODE=PostgreSQL` silently lies about `jsonb`, `ARRAY` types, `ON CONFLICT ... RETURNING`, partial indexes, full-text search, and `timestamptz` semantics. Tests that pass on H2 and fail in production is the canonical PostgreSQL false-CLEAN trap. Use `org.testcontainers:postgresql` + `PostgreSQLContainer<>` with the same major version as production.
- **Validate migrations before tests.** Run `./gradlew flywayValidate` (or `mvn flyway:validate`) before `./gradlew test`. Catches migration drift, out-of-order checksums, and missing files BEFORE the test runner gives a false signal.
- **Never share container state across test classes.** Each `@SpringBootTest` class starts fresh; cross-class state leaks turn flaky into intermittent. If shared fixtures are required, use `@DynamicPropertySource` + a singleton container with explicit transactional rollback.

#### 8.1.7 Common discovery patterns

Each maps to a DBL query first; raw source read is the fallback.

| Question | DBL artifact to query | Fallback (if DBL stale or missing) |
|:---|:---|:---|
| What endpoints exist? | `DBL/APIIndex/<api>.md` | `Grep '@.*Mapping' src/main/java/.../api/` (record reason in Report) |
| What entities exist? | `DBL/SchemaIndex/<schema>.md` | `Grep '@Entity' src/main/java/.../domain/` |
| What services depend on X? | `DBL/DependencyMaps/<scope>.md` | `Grep 'import .*${X}' src/main/java/.../service/` |
| What's the shape of endpoint Y? | `DBL/APIIndex/<api>.md` (cites DTO file) | open the DTO file directly |
| What migrations have run? | `DBL/SchemaIndex/<schema>.md` `Migrations baseline` | `Glob src/main/resources/db/migration/V*.sql` |
| Where is config X read? | `DBL/Summaries/_config.md` | `Grep '@Value.*${X}' src/main/java/` |
| What repositories exist? | `DBL/Summaries/<package>.md` (lists deps) | `Grep 'extends (JpaRepository\|CrudRepository\|PagingAndSortingRepository)' src/main/java/.../repository/` |
| Where is the JPA / DataSource configured? | `DBL/Summaries/_config.md` | `Grep 'spring\.datasource\|spring\.jpa' src/main/resources/application*.yml` |
| Which entity owns table X? | `DBL/SchemaIndex/<schema>.md` (cites `@Table` source) | `Grep '@Table.*name = "X"' src/main/java/.../domain/` |

#### 8.1.8 Mandatory inputs for new Spring Boot projects under Nissth

When initializing a new Spring Boot project that uses Nissth:

1. Per §9, generate `ImplementationPlans/SRS.md` and `ImplementationPlans/SDD.md` from the user's prompt. STOP for user approval.
2. After approval, author `ImplementationPlans/Phase_00_DBL_Bootstrap.md` (using `_TEMPLATE.md`). Its §3 Execution populates:
   - `DBL/Summaries/` — one file per top-level package
   - `DBL/APIIndex/` — one file per controller group
   - `DBL/SchemaIndex/` — one file per `@Entity` cluster
   - `DBL/DependencyMaps/` — at least one file mapping the `api ↔ service ↔ repository` boundary
   - `DBL/Summaries/_config.md` — current `application.yml` + profile overrides audit, **including PostgreSQL connection settings, JPA properties (`hibernate.ddl-auto`, `show-sql`, dialect), and Flyway settings per profile**
3. **Flyway baseline check.** If the project has existing tables but no migration history (common in brownfield projects), generate `V1__baseline.sql` from the live schema (`pg_dump --schema-only`) and verify it's idempotent against a fresh database BEFORE Phase_01 runs. Record the baseline source state in the `DBL/SchemaIndex/` frontmatter.
4. The first non-bootstrap plan (`Phase_01_*`) executes only after Phase 0 closes. **No source changes before DBL is in place.**

#### 8.1.9 `@Entity` change ripple (Hard Rule #11 specialization for this stack)

Any modification to an `@Entity` class — adding/removing a field, changing a column type, altering a relationship, adjusting an index — triggers updates to BOTH of:

1. The `DBL/SchemaIndex/` artifact for that entity's cluster (column table, indexes, relationships, `last_regenerated`)
2. A corresponding Flyway migration file at `src/main/resources/db/migration/V<n>__<slug>.sql`

Both MUST appear in the closing status entry's `Doc sync:` line. A status entry listing one but not the other means the change is incomplete — the entity and the database have diverged. The reviewing agent (or user) treats this as a Loop-Lock failure.

### 8.2 Expo

#### 8.2.1 Stack identity

| Field | Value |
|:---|:---|
| Language | TypeScript 5+ (`.tsx` for components/routes; `.ts` for hooks/utilities) |
| Framework | Expo SDK 50+ (React Native 0.74+) |
| Router | Expo Router 3+ (file-based routing under `app/`) |
| Build tool | npm (or pnpm if explicitly chosen at SRS time) |
| Test runner | Jest with `@testing-library/react-native` (component tests); Detox optional for E2E |
| Type checker | `tsc --noEmit` — the canonical compile-time check; Metro bundles at runtime but tsc gates types |
| State management | Project-chosen (Redux Toolkit, Zustand, Jotai, plain context — recorded in `DBL/Summaries/_state.md`) |
| Persistence | Project-chosen; typically backend-delegated. Local-only options: `expo-sqlite`, `@react-native-async-storage/async-storage` |

#### 8.2.2 Conventional layout

```
project/
├── app/                                  ← Expo Router file-based routes
│   ├── _layout.tsx                       ← Root layout (Stack from expo-router)
│   ├── index.tsx                         ← / (home route)
│   ├── (tabs)/                           ← Route group (no URL segment)
│   │   ├── _layout.tsx                   ← Tab navigator layout
│   │   ├── home.tsx                      ← /home
│   │   └── settings/
│   │       ├── _layout.tsx
│   │       ├── index.tsx                 ← /settings
│   │       └── [id].tsx                  ← /settings/:id (dynamic)
│   └── [...rest].tsx                     ← catch-all fallback
├── components/                           ← Shared React components (non-route)
├── hooks/                                ← Custom hooks (use*)
├── assets/                               ← Static assets (images, fonts)
├── __tests__/                            ← Jest tests (mirroring app/ + components/ paths)
├── app.json                              ← Expo config (scheme, plugins, splash, icon)
├── tsconfig.json                         ← extends 'expo/tsconfig.base'
├── package.json
└── package-lock.json (or yarn.lock / pnpm-lock.yaml)
```

A project that diverges from this layout records the divergence (and its reason) in `DBL/Summaries/_layout.md`.

#### 8.2.3 Build & test commands

| Action | Command |
|:---|:---|
| Dev server | `npx expo start` (Metro bundler; QR code for Expo Go) |
| Type check | `npx tsc --noEmit` |
| Run unit tests | `npm test` (Jest; matches `**/__tests__/**/*.test.tsx` + `*.test.ts`) |
| Project health check | `npx expo-doctor` (the canonical Expo project validator) |
| Lockfile-driven install | `npm ci` — use for verification runs; avoids `npm install`'s lockfile mutation |
| Build for production | `eas build` (EAS service; out-of-scope for this section — see EAS docs) |
| Prebuild native projects | `npx expo prebuild` (only when ejecting from managed workflow) |

Always invoke from project root. Metro and tsc cache aggressively — see §8.2.6.

#### 8.2.4 DBL mapping for Expo

| DBL type | Source signal | What to extract per artifact |
|:---|:---|:---|
| Summary | one per `app/` sub-tree (e.g., `app/(tabs)/settings/`) + one per `components/` grouping + one per `hooks/` grouping | purpose; exported component / hook list; props or signature types; hooks consumed; gotchas (e.g., Suspense boundaries, error boundaries) |
| DependencyMap | one per architectural boundary (`app/ ↔ components/ ↔ hooks/`) | file-to-file import graph; explicit forbidden directions (e.g., `components/` MUST NOT import from `app/`) |
| APIIndex | one global `DBL/APIIndex/routes.md` OR one per route group (e.g., `routes-tabs.md`, `routes-modal.md`) | route table from Expo Router file scan: URL path, file path, component name, params type, layout parent, classification (static / dynamic / catch-all / layout / group) |
| **No SchemaIndex by default** | — | Expo apps typically delegate persistence to a backend (the Spring Boot binding owns its `SchemaIndex/` independently). For apps with local SQLite via `expo-sqlite`, borrow the §8.1.4 SchemaIndex pattern on a per-project basis. |

#### 8.2.5 Forbidden patterns

1. **No `npm install --legacy-peer-deps` without a one-line justification.** Expo's peer-dep graph is tight; using this flag to work around a conflict masks a real version mismatch. If required (rare), record `// expo-peer-dep-justification: <reason>` in `package.json` adjacent to the offending dep.
2. **No committed `node_modules/`, `.expo/`, `dist/`, or `coverage/`.** The repo-root `.gitignore` covers these; if missing, add them.
3. **No `package-lock.json` deletes without rationale.** Lockfile drift across PRs is a stealth-bug class. If a regenerate is needed, record the reason in the next status entry.
4. **No untyped routes.** Every Expo Router screen that takes params declares its `Params` type and reads them via `useLocalSearchParams<Params>()`. Untyped param access is a defect.
5. **No `expo-cli` (deprecated 2024).** Use `npx expo`, `npx expo-doctor`, `npx expo start`. The global `expo` command is no longer supported.
6. **No `@react-navigation/*` for top-level navigation.** Expo Router (built on top of React Navigation) supersedes; mixing the two creates confusing route resolution. Nested React Navigation inside an Expo Router screen is fine.
7. **No inline `require()` of platform-specific code.** Use Metro's `.ios.tsx` / `.android.tsx` / `.web.tsx` extension resolution; the bundler picks the right file at build time.
8. **No `console.log` in production code paths.** Use `@react-native-async-storage/async-storage`-backed loggers or a real telemetry pipeline. `console.log` lives in dev tools and Expo's `LogBox`, not in production builds.
9. **No native module additions without `expo prebuild` consideration.** The managed workflow's `app.json` must list any additional native modules under `expo.plugins`; otherwise the EAS build fails.
10. **No `.env`-style secret files committed.** Use EAS Secrets or `expo-constants`'s `extra` config for runtime config; credentials never enter the repo.
11. **No skipping `npx expo-doctor` after dependency changes.** Expo's compatibility matrix is tight; a bumped SDK or React Native version can cascade through `react-native-screens`, `react-native-safe-area-context`, etc. `expo-doctor` catches incompatibilities the type checker won't.

#### 8.2.6 Verification protocol — freshness guarantee

The Expo equivalent of the "false CLEAN" trap (Hard Rule #10) is **lockfile drift + Metro bundler cache + `.tsbuildinfo` incremental cache**. An agent that runs `npm test` after editing source can see green tests against stale compiled output or a stale `node_modules/` tree.

**Required verification sequence:**

1. `npm run clean` — removes `dist/`, `.tsbuildinfo`, and `node_modules/.cache/`. Bindings define this script; consumer projects should too.
2. `npm ci` — clean lockfile-driven install. Replaces `npm install` for verification; guarantees `node_modules/` matches `package-lock.json` byte-for-byte.
3. `npx tsc --noEmit` — fresh type check against `tsconfig.json`. Catches type errors before the test runner does.
4. `npm test` — Jest runs against the current source tree (`ts-jest` transforms on the fly; no persistent test cache).
5. (Optional) `npx expo-doctor` — project health validator. PASS on all checks signals a clean Expo setup.

**Freshness statement** (paste into `_TEMPLATE.md` §4.1):
> "Cleaned via `npm run clean` (`dist/`, `.tsbuildinfo`, `node_modules/.cache/` cleared); fresh install via `npm ci`; type check via `npx tsc --noEmit`; tests via `npm test` against the freshly-compiled source; `expo-doctor` PASS confirmed at YYYY-MM-DD HH:MM."

#### 8.2.7 Common discovery patterns

Each maps to a DBL query first; raw source read is the fallback.

| Question | DBL artifact to query | Fallback (if DBL stale or missing) |
|:---|:---|:---|
| What routes exist? | `DBL/APIIndex/routes.md` | `Glob 'app/**/*.tsx'` + classify by Expo Router conventions |
| What components exist under `components/`? | `DBL/Summaries/components.md` | `Grep 'export (default )?function ' components/` |
| What hooks does this component use? | `DBL/Summaries/<component>.md` (hooks line) | `Grep 'use[A-Z]\w+\(' <component>.tsx` |
| What's the dependency tree? | `DBL/DependencyMaps/<scope>.md` | `nissth-bridge dependency_audit` (when Expo binding installed) |
| What's the shape of params for route Y? | `DBL/APIIndex/routes.md` (params_type column) | open the route's `.tsx`, find `useLocalSearchParams<...>()` call |
| Is the project Expo-healthy? | `nissth-bridge expo_doctor_lens` (when Expo binding installed) | `npx expo-doctor` directly |
| Where is `app.json` config X read? | `DBL/Summaries/_config.md` | `Grep 'Constants\.expoConfig\.X' src/` |
| What components consume hook Y? | `DBL/DependencyMaps/<scope>.md` (reverse-lookup) | `Grep '<Y>\(' app/ components/` |

#### 8.2.8 Route ripple (Hard Rule #11 specialization for this stack)

Any new screen route added under `app/` — static (`app/profile.tsx`), dynamic (`app/[id].tsx`), or catch-all (`app/[...rest].tsx`) — triggers updates to BOTH of:

1. The `DBL/APIIndex/routes.md` artifact for that route's grouping (URL path, file path, component, params type, layout parent, `last_regenerated`).
2. A corresponding Jest test file at `__tests__/<same-route-path>.test.tsx` that at minimum renders the component and asserts no crash. (Layouts at `_layout.tsx` are exempt — they're scaffolding, not user-facing screens.)

Both MUST appear in the closing status entry's `Doc sync:` line. A status entry listing one but not the other means the change is incomplete — the routing surface and its test coverage have diverged. The reviewing agent (or user) treats this as a Loop-Lock failure.

The `route_scaffold` action tool in `Bindings/Expo/` enforces this rule atomically: it refuses to commit a route without its matching test, exiting 5 on partial failure (`CLAUDE.md` §11.7). The §8.1.9 entity-ripple rule is the Spring Boot analog.

#### 8.2.9 Mandatory inputs for new Expo projects under Nissth

When initializing a new Expo project that uses Nissth:

1. Per §9, generate `ImplementationPlans/SRS.md` and `ImplementationPlans/SDD.md` from the user's prompt. STOP for user approval.
2. After approval, author `ImplementationPlans/Phase_00_DBL_Bootstrap.md` (using `_TEMPLATE.md`). Its §3 Execution populates:
   - `DBL/Summaries/` — one file per `app/` sub-tree + one per `components/` grouping + one per `hooks/` grouping
   - `DBL/APIIndex/routes.md` — full Expo Router route table from `app/` filesystem scan (URL path, file path, component name, params type, layout parent, classification)
   - `DBL/DependencyMaps/` — at least one file mapping the `app/ ↔ components/ ↔ hooks/` boundary
   - `DBL/Summaries/_config.md` — current `app.json` audit including Expo plugins list, scheme, build properties, splash/icon config
3. **No baseline-migration step** — Expo apps typically have no DB owned in-app. If the project uses `expo-sqlite` for local persistence, borrow the §8.1.8 Flyway-baseline pattern for the local schema and record it in `DBL/SchemaIndex/sqlite.md` (with `last_regenerated` reflecting the SQLite migration runner's state, not Flyway).
4. The first non-bootstrap plan (`Phase_01_*`) executes only after Phase 0 closes. **No source changes before DBL is in place.**

### 8.3 PostgreSQL

The PostgreSQL binding is **cross-cutting and general-purpose**: it does not author project code or own a project layout. It is installed *alongside* whatever application-side binding owns the backend (Spring Boot §8.1, future Django/Rails/Go bindings, etc.) and gives the agent a structured, read-only view of any reachable PostgreSQL database via a libpq connection string. Real-development writes continue to flow through pgAdmin / psql / JPA / the project's migration runner; this binding observes, never modifies.

#### 8.3.1 Binding identity

| Field | Value |
|:---|:---|
| Language | TypeScript 5+ (Node 20+) |
| Driver | `pg` (MIT, the canonical Node Postgres client) |
| URL parser | `pg-connection-string` |
| Build tool | npm |
| Test runner | Jest with `ts-jest` |
| Integration-test PG | `testcontainers` (optional, requires Docker) OR `NISSTH_TEST_PG_URL` env var pointing at a live Postgres OR skip-with-note for offline hosts |
| PostgreSQL versions supported | 13+ (older versions lack `pg_blocking_pids()` and `pg_control_checkpoint()` shape used in freshness fingerprints) |

#### 8.3.2 Connection input shape

The binding has no on-disk "project" of its own to introspect. Every invocation requires a connection string in one of two forms (resolved in this order):

1. **Per-call:** `scope.extra.connection_string` — full libpq URL. Highest precedence.
2. **Session-wide:** `NISSTH_PG_URL` environment variable — fallback when no per-call override is supplied.

If neither is supplied the tool errors with `stage="validate"`, `error_code="no_connection_string"` and a one-line remediation hint (no other diagnostic work happens).

```
postgresql://user:password@host:5432/dbname?sslmode=require
```

**Password redaction is always-on.** Before any report write, log line, stderr emission, or error message, the password component is replaced with `***REDACTED***`. The frontmatter's `freshness.source` cites `postgresql://<user>@<host>:<port>/<dbname>` — never with the password. The `tests/contract/SecretRedaction.test.ts` suite is the load-bearing test for this guarantee.

#### 8.3.3 Diagnostic commands

| Tool | Modes | One-line purpose |
|:---|:---|:---|
| `schema_lens` | `tables` · `columns` · `relationships` · `full` (default) | Tables/views/materialized views + per-table columns + FK graph |
| `query_plan` | `explain` (default) · `analyze` · `buffers` | `EXPLAIN (FORMAT JSON)` for a SQL statement |
| `index_audit` | `usage` (default) · `unused` · `duplicate` · `bloat` | Index hygiene from `pg_stat_user_indexes` + `pg_index` + optional `pgstattuple` |
| `lock_audit` | `current` (default) · `waiting` · `long_running` | Live lock state from `pg_locks` JOIN `pg_stat_activity` |
| `migration_status` | `flyway` · `liquibase` · `auto` (default) | Applied/failed migrations from `flyway_schema_history` or `databasechangelog` |

Every tool is **read-only**. No DDL, no DML, no `pg_terminate_backend`, no advisory locks.

#### 8.3.4 DBL mapping for PostgreSQL

The Postgres binding **only flips** DBL artifacts; it does not own them. The application-side binding (Spring Boot §8.1, etc.) is the artifact author. This binding's tools STALE-flip:

| DBL type | When this binding flips it | Why |
|:---|:---|:---|
| `DBL/SchemaIndex/*.md` | `schema_lens` finds tables/columns/indexes that diverge from the artifact's documented set | Live schema is authoritative; the artifact is now stale |
| `DBL/DependencyMaps/*.md` | `schema_lens --mode relationships` finds FK relationships that diverge from a `dependency_map` whose `covers` overlaps the scanned schema | FK graph drift is a load-bearing architectural signal |

**Does NOT flip** `DBL/Summaries/*.md` or `DBL/APIIndex/*.md` — those are owned by the application-side binding (Spring Boot's `entity_lens`, Expo's `route_lens`, etc.). The Postgres binding has no opinion about application-layer summaries or API surfaces.

#### 8.3.5 Forbidden patterns

1. **No DDL via diagnostic tools.** This binding's tools issue only `SELECT` statements (plus `EXPLAIN` for `query_plan`). DDL is the application-side binding's job, gated by its own action-tool contracts.
2. **`query_plan` refuses `analyze` and `buffers` modes on mutating SQL.** Statements matching `^\s*(INSERT|UPDATE|DELETE|TRUNCATE|CREATE|DROP|ALTER|GRANT|REVOKE|COPY|VACUUM|CLUSTER|REINDEX)\b/i` return `stage="validate"`, `error_code="mutating_sql_refused_for_analyze"`. `explain` mode is still allowed (no execution).
3. **No `pg_terminate_backend`, no `pg_cancel_backend`, no `pg_advisory_lock` from diagnostic queries.** `lock_audit` reads `pg_locks` and `pg_stat_activity`; it never acts on the locks it observes. Advisory locks would leave hanging state if the binding crashed mid-query.
4. **Never log the connection-string password.** Anywhere. Reports, stdout, stderr, error messages, exception stack traces — all routed through the redaction utility before emission. Reviewed by `SecretRedaction.test.ts` on every CI run.
5. **No persistent connection pool across tool invocations.** One PG connection per tool call, closed in a `finally` block. Avoids per-connection plan-cache leakage between unrelated invocations and minimizes credential lifetime in memory.
6. **No queries against the user's own tables.** This binding's queries target only `information_schema.*`, `pg_catalog.*`, `pg_stat_*` views, and the `flyway_schema_history` / `databasechangelog` tables. It never reads (much less writes) application data. `query_plan` is the one exception — but it only *plans* the user's SQL; the user provides the statement.
7. **No assumption of superuser.** A read-only role with `pg_monitor` + `pg_read_all_stats` covers the full diagnostic surface. Tools degrade gracefully when the role is missing: `lock_audit` reports only the connecting session, `index_audit --mode bloat` reports "pgstattuple extension not available."

#### 8.3.6 Verification protocol — freshness guarantee

PostgreSQL's "false CLEAN" trap is **per-connection plan cache + stale statistics**: a long-lived session can keep a stale plan or read from stats that were valid 30 minutes ago. The binding's freshness contract:

1. **One PG connection per tool invocation, closed in `finally`.** No pool, no statement cache leakage.
2. **`pg_control_checkpoint().redo_lsn` queried at the start of every run** and recorded in `freshness.source_state`. The LSN advances on every transaction; a stale cache would surface as an LSN mismatch in re-queries.
3. **`pg_stat_get_db_stat_reset_time()` recorded for `index_audit`** — `pg_stat_user_indexes.idx_scan` accumulates from the last reset; the report cites the reset timestamp so the agent can judge whether "0 scans" means "truly unused" or "recently reset."
4. **`query_plan` always uses `EXPLAIN (FORMAT JSON)`** so the plan structure is parseable data, not prose that might shift across PG versions.
5. **`statement_timeout` set per-invocation** (default 30s, overridable via `scope.extra.statement_timeout_ms`). Runaway diagnostic queries cannot hold connections beyond the timeout.

**Freshness statement** (paste into `_TEMPLATE.md` §4.1):
> "Fresh PG connection per tool invocation; `pg_control_checkpoint().redo_lsn` captured at start of run; statement_timeout 30000ms; password redacted from report frontmatter via ConnectionManager.redactForLog(); ran at YYYY-MM-DD HH:MM."

#### 8.3.7 Common discovery patterns

Each maps to a Bridge tool first; raw `psql` is the fallback when the Bridge isn't installed or live state matters more than a curated answer.

| Question | Bridge tool | Fallback (if Bridge not installed) |
|:---|:---|:---|
| What tables exist in schema X? | `schema_lens --mode tables --scope.package X` | `psql -c "\dt X.*"` |
| What's the column layout of table Y? | `schema_lens --mode columns --scope.names Y` | `psql -c "\d X.Y"` |
| What's the FK graph for schema X? | `schema_lens --mode relationships --scope.package X` | query `information_schema.referential_constraints` |
| Why is this query slow? | `query_plan --mode analyze --scope.extra.sql '<sql>'` | `psql -c "EXPLAIN ANALYZE <sql>"` |
| Which indexes are unused? | `index_audit --mode unused --scope.package X` | query `pg_stat_user_indexes WHERE idx_scan = 0` |
| Are there duplicate indexes? | `index_audit --mode duplicate --scope.package X` | window query on `pg_index` |
| What sessions are blocking each other right now? | `lock_audit --mode waiting` | join `pg_locks` + `pg_blocking_pids(pid)` |
| What migrations have been applied? | `migration_status --mode auto` | `psql -c "SELECT * FROM flyway_schema_history"` |
| Is the application-side `DBL/SchemaIndex/X.md` stale? | run `schema_lens --mode full --scope.package X` — if the live schema diverges, the artifact gets STALE-flipped automatically | manual diff |

#### 8.3.8 Schema-change ripple — N/A this slice

This binding is **diagnostic-only**; it has no action tools that author migrations. The schema-change ripple rule (HR#11 specialization — analogous to §8.1.9 for Spring Boot entities) belongs to the **application-side binding** that owns the migration file authoring (Spring Boot's `entity_field_add`, future Django binding's `model_field_add`, etc.). The Postgres binding **observes** the schema and may STALE-flip a downstream DBL artifact, but it does not write SQL. If a future slice adds an action tool (`index_create`, `vacuum_analyze`, `migration_apply`), this sub-section gets authored at that time.

#### 8.3.9 Mandatory inputs for new Postgres-using projects under Nissth — N/A this slice

The Postgres binding is a *second* binding installed alongside the project's primary application-side binding. The application-side binding's §8.x.8 already covers project-init requirements (SRS/SDD, Phase_00_DBL_Bootstrap, schema baseline). The Postgres binding can be installed mid-project at any time — it has no project-init contract of its own and does not need to be present from Phase 0. Set `NISSTH_PG_URL` and the binding is operational against any reachable PG instance.

---

## 9. Mandatory Inputs for New Projects Built Under Nissth

When the user starts a new project _using_ Nissth (vs. working on Nissth itself), the agent MUST verify the existence of:

- `ImplementationPlans/SRS.md` — Software Requirements Specification
- `ImplementationPlans/SDD.md` — Software Design Document

If either is missing:
1. Generate them from the user's prompt.
2. Save to `ImplementationPlans/`.
3. STOP and ask the user to confirm before any execution.

This rule does NOT apply to work on Nissth itself — Nissth's own spec lives in this `CLAUDE.md` and the user's design memo.

### 9.1 Project initialization sequence

The full sequence for spinning up a new Nissth-bound project — exactly once, at project creation:

0. **Permission gate (Hard Rule #13).** Before reading inputs, authoring SRS/SDD, bootstrapping, creating files, or running any command, the agent MUST explicitly ask the user for full permission to proceed with Nissth-bound project initialization. The agent enumerates the expected actions and waits for unambiguous consent. Silence, ambiguous responses, or "sounds good"-style answers do NOT satisfy the gate. The gate fires once per project at init time; session resumes are governed by §1.
1. **Pre-bootstrap inputs.** SRS + SDD exist in `ImplementationPlans/` (this §9). If absent, author them, STOP for user approval, do not proceed.
2. **Bootstrap (mechanical, plan-exempt).** Copy framework files into the project root: `CLAUDE.md`, `AGENTS.md`, `ImplementationPlans/_TEMPLATE.md`, `AgentReports/StatusUpdate.md` (with schema preamble + first entry), `AgentReports/Reports/`, `DBL/{Summaries,DependencyMaps,APIIndex,SchemaIndex}/_TEMPLATE.md`, `Tests/`, `Tools/`, `.claude/`. No source code. Append a status entry titled "Bootstrap" — this is the only execution allowed without an approved plan, and only because there is no source code to modify yet.
3. **First plan: `Phase_00_DBL_Bootstrap.md`.** Author per `_TEMPLATE.md`, request user approval, only then execute. Its §3 populates the initial DBL artifacts (per §7.6 / per-stack §8.x DBL mapping).
4. **First product plan: `Phase_01_*.md`.** Authored after Phase 0 closes. Hard Rule #12 governs from this point onward — every code change rides on an approved plan.

A Nissth project that has skipped any step above is malformed. Stop and remediate before continuing.

---

## 10. Reports Taxonomy

`AgentReports/StatusUpdate.md` is the short ledger — one block per task, dense by design, append-only. Reports are the long-form companions: any document that **details the project** in a way too substantial for a status entry yet too durable to live only in chat.

The user requirement (2026-05-06): *"Mostly anything that details the project is an important report."* Read broadly — when in doubt, write the Report.

### 10.1 What is a Report

A standalone Markdown file under `AgentReports/Reports/` that captures any of:

| Kind | Triggered when | Examples |
|:---|:---|:---|
| **Decision record** | An architecture or design choice has options the team would otherwise re-debate later | "Why JPA over JOOQ", "Why Modular Monolith over microservices", "Why iyzico over Stripe" |
| **Incident report** | A `Verified: FAIL`, a production incident, a discovered defect class | "Migration V7 partial-apply post-mortem", "PostGIS index regression" |
| **Design review** | A non-trivial subsystem is being introduced or refactored | "Reservation engine pessimistic-lock design", "Auth flow with Google/Apple SSO only" |
| **Audit / analysis** | A cross-cutting investigation: dependency hygiene, performance, security, license | "Phase 3 dependency audit", "N+1 query sweep, results" |
| **Spec digest** | A long external spec (PDF, RFC, vendor doc) needs a project-tailored summary | "iyzico API surface used by Süprüz", "Apple SSO requirements digest" |
| **Project state snapshot** | A periodic or on-request comprehensive state dump beyond what a status entry holds | "End-of-Phase-3 architecture snapshot", "Pre-handoff project summary" |
| **Verification report** | A verification run produced output worth preserving (test breakdowns, coverage, perf numbers) | "Phase 2 integration test results, Testcontainers PG 15" |

If a status entry's `**Issues:**`, `**Report:**`, or `**Verified:**` block is starting to swell past ~10 lines, that is the signal to spin off a Report and have the status entry link to it.

### 10.2 Where Reports live

```
AgentReports/Reports/
├── 2026-05-06_phase-00-dbl-bootstrap-scope.md
├── 2026-05-12_jpa-vs-jooq-decision.md
└── 2026-06-03_v7-migration-postmortem.md
```

- File name: `YYYY-MM-DD_kebab-case-slug.md`. ISO date is the **authored** date, not the event date — for incidents, mention the event date inside the file.
- One topic per file. Cross-link related Reports in their bodies; do not bundle.
- Reports are **append-friendly**, not strictly append-only. Updating a Report (e.g., a long-running incident) is allowed — but every revision must update the `last_updated` frontmatter line and add a one-line `## Revision history` entry.

### 10.3 Mandatory Report frontmatter

```yaml
---
report_type: decision | incident | design_review | audit | spec_digest | snapshot | verification | other
title: <human-readable title>
authored: YYYY-MM-DD by [agent name | user]
last_updated: YYYY-MM-DD by [agent name | user]
related_status_entries:
  - <YYYY-MM-DD HH:MM — Status Entry Title>
related_plans:
  - <Phase_NN_Slug | none>
covers:
  - <subsystem | module | concern — what this Report is *about*>
supersedes:
  - <prior report filename | none>
---
```

The `related_status_entries` field is the back-reference that lets future agents discover the Report from `StatusUpdate.md`.

### 10.4 When a Report is required (not optional)

Reports are usually a judgment call, but in these cases authoring one is **mandatory**:

1. **Verified: FAIL.** Every status entry with `Verified: FAIL` MUST be paired with a Report (kind: `incident`) that captures the failing artifact contents, root-cause hypothesis, and remediation options. The status entry's `**Issues:**` line cites the Report by filename.
2. **Architecture decisions.** Any choice between named alternatives that the team or future agents could reasonably re-litigate (framework selection, persistence strategy, deployment model). Choose-one-of-many → Report (kind: `decision`).
3. **Spec ingestion.** When the agent reads a long external spec (PDF, RFC, vendor doc) and bases plans on it, a `spec_digest` Report is authored so future agents do not re-extract from the source. Süprüz's SRS and SDD intake on 2026-05-05 is the canonical example — the markdown derivatives in `ImplementationPlans/SRS.md` and `SDD.md` are the digest; a `spec_digest` Report can additionally summarize "what changed between revisions."
4. **End of phase.** Closing any `Phase_NN_*.md` plan whose §3 produced more than incremental code change requires a `snapshot` Report summarizing the architectural state at phase end. Routine bug-fix or single-file phases do not need this.
5. **Cross-phase pivot.** Any change that invalidates an earlier plan's premise (e.g., switching DBs, pulling in a new module) requires a `decision` Report explaining the pivot before the new plan is authored.

### 10.5 Report ↔ status entry linkage

Every authored Report must be referenced from the closing status entry of the task that produced it. Add a line under `**Verified:**`:

```
Reports: AgentReports/Reports/2026-05-12_jpa-vs-jooq-decision.md (decision)
```

If the same task produced multiple Reports, list them all. A status entry's `**Issues:**` line citing a `Verified: FAIL` MUST also cite the matching incident Report by filename. Reports authored *outside* a task closure (e.g., user asks for a snapshot mid-stream) get their own status entry whose `**Executed:**` block is just the Report authoring.

### 10.6 What does NOT belong as a Report

- **Status entries.** Those stay in `StatusUpdate.md`. Reports are the long-form *behind* a status entry, not a replacement for it.
- **Plans.** Live in `ImplementationPlans/`. A Report describes outcome or rationale; a Plan describes intent and a contract for execution.
- **DBL artifacts.** Those answer pre-defined questions about project structure (modules, APIs, schemas). Reports answer free-form questions about decisions and incidents. If a DBL artifact would do the job, prefer it.
- **User memory** or **agent memory.** Personal preferences or cross-session reminders live in the memory layer.
- **Volatile working notes.** If it would not still be useful in 6 months, do not write a Report — make it a status entry line instead.

### 10.7 Authoring rules

- Token budget per Report: 500–3000 tokens. If exceeding 3000, split.
- Reports must have **dense, scannable structure**: H2/H3 headers, tables for option matrices, bullet lists. No wall-of-text essays. The agent reading this Report 6 months from now should find the answer in <30 seconds.
- For `decision` Reports: use a fixed body shape — `## Context` → `## Options considered (table)` → `## Decision` → `## Consequences` → `## Revision history`.
- For `incident` Reports: `## Summary` → `## Timeline` → `## Root cause` → `## Remediation` → `## Follow-ups` → `## Revision history`.
- Reports are subject to **Hard Rule #11 (Document Sync Mandate)**: when a Report is invalidated by later events, mark it superseded — set `last_updated` and add a `Superseded by: <new report filename>` line in frontmatter; do not delete it.

---

## 11. Diagnostic Bridge

The Diagnostic Bridge is Nissth's **live runtime layer** above raw source. Where DBL (§7) answers "what is this project supposed to look like" from hand-curated artifacts, the Bridge answers "what is this project doing right now" by running structured commands against the actual running stack — compiler, test runner, database, dev server — and writing the result to a report file the agent can read in a single turn.

The Bridge is a port of the Axiom (Unity 6) Diagnostic Bridge into general software development. Axiom's core insight: the agent should never grep raw scene YAML; it should send a JSON command to a single gateway and read a clean Markdown report. Nissth carries the same shape — same `{tool, mode, scope, output}` grammar, same `AgentReports/` discipline — but the implementations behind each tool are per-stack (Spring Boot, Expo, PostgreSQL, future stacks) rather than per-game-engine.

### 11.1 Two layers above source — when to query which

| Question shape | Query | Why |
|:---|:---|:---|
| What modules exist? What's the public API contract? Which imports are forbidden? | **DBL** (§7) | Stable architectural intent; curated; survives across runs |
| Does this endpoint actually exist in the compiled bytecode right now? | **Bridge** | Live state — only the running compiler/classpath knows |
| Are there pending Flyway migrations? Does the schema match the entities? | **Bridge** | Live state — runtime-only signal |
| What's the bean dependency graph in this profile? | **Bridge** | Profile-dependent; only the live `ApplicationContext` knows |
| What does this query's EXPLAIN plan look like at current row counts? | **Bridge** | Statistics-dependent; only the live DB knows |
| Why was JPA chosen over JOOQ? | Reports (§10) | Decision rationale, not state |

The Bridge does NOT replace DBL. DBL is the durable layer; Bridge reports are transient inputs. **Hard Rule #4 covers both**: query the structured layer before reading source.

### 11.2 Command grammar (the contract)

Every Bridge invocation — CLI, MCP, or programmatic — speaks the same JSON command. This is the stack-agnostic contract; the machine-readable version lives at `Bindings/_schemas/bridge-command.schema.json`.

```json
{
  "tool": "string (required) — tool name registered by a binding",
  "mode": "string (optional) — mode name or letter shortcode; binding defines valid values",
  "context_id": "string (optional) — caller-supplied id for cross-call correlation",
  "scope": {
    "package": "string — fully-qualified package/namespace/module path",
    "root_path": "string — filesystem or logical path scoping the query",
    "names": ["array — specific symbol/file/identifier names to target"],
    "file_extension": "string — e.g., '.java', '.tsx', '.sql'",
    "tag_filter": "string — binding-defined classifier (e.g., test tag, route group)",
    "type_filter": "string — type/kind classifier (e.g., '@Entity', 'Controller')",
    "max_depth": "integer — recursion limit (-1 = unlimited)",
    "profile": "string — runtime profile selector (e.g., 'dev', 'test', 'prod')",
    "extra": { "<string>": "<any>" }
  },
  "output": {
    "format": "string — 'markdown' (default) | 'json' | 'flat_text'",
    "destination": "string — 'file' (default) | 'return' | 'console'",
    "file_name": "string — custom filename without extension or path"
  }
}
```

**Field rules:**
- `tool` is the only required field. Defaults: `mode` per-tool, `scope` empty (whole project), `output.format=markdown`, `output.destination=file`.
- `scope` fields are all optional. A binding interprets the fields it supports and ignores the rest. New stack-agnostic fields can be added; stack-specific filters belong in `scope.extra`.
- `output.destination=file` → response is the absolute path written to `AgentReports/Bridge/`. `=return` → response is the report body string. `=console` → printed to stdout AND a file path returned.
- Error shape (any tool, any path): `{"error": "description", "tool": "<echoed>", "stage": "parse|validate|execute|format"}`.

**What's contractually fixed vs. per-binding:**

| Element | Contract (Nissth core) | Per-binding |
|:---|:---|:---|
| JSON shape | ✓ | — |
| Field names in `scope` (top level) | ✓ | only `scope.extra` keys |
| `output.*` semantics | ✓ | — |
| Error shape | ✓ | — |
| Report frontmatter (§11.3) | ✓ | — |
| Tool names, mode names | — | ✓ |
| Tool catalog | — | ✓ |
| Body content of reports | — | ✓ (within frontmatter contract) |

### 11.3 Report contract

Every Bridge report is a Markdown file at `AgentReports/Bridge/<tool>_<ISO8601>.md` with mandatory YAML frontmatter:

```yaml
---
tool: <tool name as invoked>
mode: <mode or 'default'>
binding: <stack id, e.g., 'spring-boot'>
binding_version: <semver of the binding that produced this report>
generated_at: YYYY-MM-DDTHH:MM:SS<tz>
scope: <echoed scope object as JSON inline or YAML below>
freshness:
  source: <how the data was sourced — e.g., 'live process pid 1234', 'AST parse of files under src/', 'jdbc query to postgres://...'>
  source_state: <hash, timestamp, or commit ref pinning the data — e.g., git SHA, schema_version row, file mtime range>
  guarantee: <human-readable freshness claim — e.g., 'compiled classes built at 2026-05-15T14:29:11; classpath unchanged since'>
contract_version: 1
---
```

Body is Markdown. Dense tables preferred over prose. The agent reads the frontmatter first to validate freshness, then reads the body.

**Path discipline:**
- File name format: `<tool>_<ISO8601-compact>.md` (e.g., `endpoint_lens_2026-05-15T1430Z.md`). Custom names go through `output.file_name`.
- `AgentReports/Bridge/` is auto-managed. It is **not** append-only and may be garbage-collected by tooling (Phase 5+).
- Reports under `AgentReports/Bridge/` are **never hand-authored**. Hand-written content goes in `Reports/` (§10).

### 11.4 Freshness and stale-flip — the load-bearing mechanism

The Bridge's most important property: it makes DBL staleness mechanically detectable instead of agent-disciplined.

**Rule:** When a Bridge report's content contradicts a DBL artifact whose `covers` overlaps the queried scope, the Bridge writes `last_regenerated: STALE — superseded by AgentReports/Bridge/<report>` into the DBL artifact's frontmatter. The agent treats any DBL artifact with `last_regenerated: STALE` as not-readable until regenerated.

This converts Hard Rule #11 (Document Sync Mandate) from a discipline rule into a runtime enforcement:
- The agent cannot accidentally read a stale DBL artifact and act on it; the staleness is in the frontmatter.
- The "next plan must regenerate stale DBL" requirement is now triggered by Bridge output, not by agent memory.

**Drift detection is per-binding.** Each binding declares which DBL artifact types its tools can cross-check. For example, the Spring Boot binding's `endpoint_lens` cross-checks against any `DBL/APIIndex/*.md` whose `covers` overlaps the scanned package — if the live endpoint list disagrees with the DBL table, STALE-flip fires.

**Re-fresh.** Clearing a STALE marker requires either (a) re-running the DBL generator under `Tools/` (Phase 5+), or (b) hand-revising the DBL artifact and setting `last_regenerated: YYYY-MM-DD by <author>`. The Bridge does not auto-clear.

### 11.5 CLI surface

The Bridge is invoked through a single binary, `nissth-bridge`, which dispatches to the correct binding based on the tool name (each binding registers its tools at install time).

```bash
# Flag form — scope keys flattened with dotted notation
nissth-bridge endpoint_lens \
  --mode full \
  --scope.package com.supruz.reservation \
  --scope.max-depth 2 \
  --output.destination file

# JSON stdin form (same payload as MCP):
echo '{"tool":"endpoint_lens","scope":{"package":"com.supruz.reservation"}}' | nissth-bridge --json-stdin

# Discovery:
nissth-bridge --list-bindings           # which bindings are installed
nissth-bridge --list-tools              # all tools across all bindings
nissth-bridge --list-tools --binding spring-boot   # one binding's catalog
nissth-bridge --describe endpoint_lens  # tool's modes, scope fields, example invocation
```

Exit codes: `0` success; `2` parse/validate error; `3` execute error (binding raised); `4` no binding registered for tool; `5` freshness contract violated (binding could not satisfy the freshness stamp it promised).

### 11.6 MCP wrapper

For Claude Code (and any other MCP-aware client), the Bridge exposes four MCP tools — direct ports of Axiom's gateway tools:

| MCP tool | Purpose | Payload |
|:---|:---|:---|
| `Nissth_Gateway` | Primary entry point — forwards the §11.2 JSON command to the CLI | The full command JSON |
| `Nissth_Verify` | Wrapped invocation of verification tools (e.g., `compile_verify`) with auto-refresh semantics | `{"operation": "compilation" \| "tests" \| ...}` |
| `Nissth_ReadReport` | Read a Bridge report by name or by `latest:<tool>` | `{"relativePath": "...", "maxChars": 50000}` |
| `Nissth_Status` | Health probe — list installed bindings, last N reports, current binding versions | none |

Registration happens automatically when `nissth-bridge` is on PATH and the project root contains a `Bindings/` directory. No per-project MCP server config required.

### 11.7 Action tools — hard-enforce by default

Bridge tools come in two kinds: **diagnostic** (read-only) and **action** (state-modifying). Action tools are subject to a contract Nissth treats as non-negotiable: **an action tool MUST refuse to proceed unless its enforcement contract is satisfied.**

**Examples (Spring Boot binding, illustrative — see §8 for stack-specific rules these enforce):**

| Action tool | Enforcement contract |
|:---|:---|
| `entity_field_add` | Edits the `@Entity` AND emits a matching Flyway `V<n>__add_<field>.sql` in one atomic operation. Refuses if migration write fails. Enforces §8.1.9. |
| `compile_verify` | Refuses to return `CLEAN` if `./gradlew --stop` was not run first. Enforces §8.1.6's freshness sequence. |
| `migration_author` | Refuses to emit a migration whose version number collides with an existing file. |
| `endpoint_scaffold` | Refuses to scaffold a controller without also wiring its DTOs and a Testcontainers integration test. |

This is the structural enforcement Nissth has always wanted: rules that were soft (`§8.1.6: agent should run --stop first`) become hard (`compile_verify exits 5 if --stop wasn't run`). The user's standing feedback applies — **enforce via structure, not instructions.**

**Bindings may not ship "warn-and-proceed" modes.** If an action tool's contract is unsatisfiable, the tool errors out. The agent then either fixes the precondition or escalates to the user. No silent override.

### 11.8 Where bindings live

```
Bindings/
├── README.md                       ← Per-stack-binding model overview
├── _schemas/
│   └── bridge-command.schema.json  ← Machine-readable §11.2 grammar
└── <stack>/                        ← One subproject per stack
    ├── README.md                   ← Stack-specific tool catalog and install notes
    ├── src/                        ← Implementation source (plan-required to modify)
    ├── tests/                      ← Binding self-tests
    └── <stack>.bridge.json         ← Tool registration manifest read by nissth-bridge
```

**Rules:**
- Each binding is a real subproject in this repo (Gradle/npm/Go/Python module — whichever fits the stack).
- A binding **implements** the contract; it does not modify it. Adding a new stack requires zero changes to `Bindings/_schemas/` or to `CLAUDE.md` §11.
- Consumer projects (e.g., the Süprüz project at `Desktop/Supruz/`) pull a binding in by git submodule, version-pinned dependency, or `gradle includeBuild`. The binding's source does not get copied into the consumer.
- The first binding is **Spring Boot** (reference implementation), introduced via `Phase_05_Bridge_SpringBoot_FirstSlice.md`. Subsequent bindings follow the same contract.

**Per-stack `CLAUDE.md` section (§8.x) is still the agent-facing rule sheet** — it documents stack-specific forbidden patterns and verification protocols. The binding implements the diagnostic and action tooling those rules describe.

### 11.9 Plan integration

The Implementation Template (`_TEMPLATE.md`) treats Bridge reports as first-class inputs alongside DBL artifacts. §1 Inputs may cite:

- **DBL artifacts** — durable, stable references (architectural intent).
- **Bridge reports** — disposable, freshly-generated snapshots of live state. Cite by file path + freshness stamp.

Bridge reports are referenced from plans only when they were produced for that plan. They are not durable knowledge; do not link a Bridge report from a Report (§10) — link the underlying decision or finding instead, with the report contents inlined or summarized.

### 11.10 Forbidden patterns

1. **Don't hand-author files under `AgentReports/Bridge/`.** That directory is owned by the Bridge runtime. Manual notes go in `Reports/` (§10).
2. **Don't add stack-specific fields to `scope` top-level.** Stack-specific filters go in `scope.extra`. If a filter is genuinely cross-stack (every binding will need it), widen the contract in `Bindings/_schemas/` and document in §11.2 — this is a contract change and requires user approval.
3. **Don't bypass the Bridge** for questions a Bridge tool covers. Grepping `@*Mapping` directly when `endpoint_lens` is installed is the Bridge analog of bypassing DBL (Hard Rule #4).
4. **Don't trust an action tool's "success" message without re-running its verifier.** Action tools enforce their own contracts; verifying after the fact is still required (Hard Rule #6 — verify against the artifact).
5. **Don't run a Bridge tool against an unbuilt project.** Most diagnostic tools depend on compiled classes / a running app / migrated DB. If the precondition is missing, the binding will error; agents should treat that as signal, not as a missing tool.
6. **Don't commit `AgentReports/Bridge/` to version control.** It's a working directory. Add to `.gitignore` at consumer-project bootstrap time.

### 11.11 Relationship to existing rules

- **Hard Rule #4** (Query DBL): broadened in 2026-05-15 edit to cover Bridge. See HR#4 body.
- **Hard Rule #6** (Verify against artifact): Bridge reports ARE artifacts; reading them counts as verification only when the freshness stamp is current.
- **Hard Rule #10** (Verifier freshness): the Bridge enforces this for compile-style tools by refusing to return success if the freshness precondition (e.g., `gradle --stop` before clean build) was skipped. See §11.7.
- **Hard Rule #11** (Document Sync Mandate): Bridge stale-flipping (§11.4) is the runtime mechanism that backs this rule. The agent no longer has to manually scan for affected DBL artifacts — the Bridge flips them.
- **Hard Rule #12** (Plan-before-execute): Authoring or revising bridge contract documentation (this §11, `Bindings/_schemas/`, binding READMEs) is plan-exempt. Adding or modifying binding implementation source under `Bindings/<stack>/src/` IS plan-required.

### 11.12 What's in the first slice (Spring Boot reference binding)

`Phase_05_Bridge_SpringBoot_FirstSlice.md` (not yet authored at the time §11 is written) delivers five tools:

| Tool | Kind | Purpose |
|:---|:---|:---|
| `compile_verify` | diagnostic | Daemon-stop + clean compile; reports CLEAN / HAS_ERRORS with file:line table. Automates §8.1.6. |
| `endpoint_lens` | diagnostic | AST-scan of `@*Mapping` annotations under a package; endpoint table (URL, verb, auth, DTOs). |
| `entity_lens` | diagnostic | `@Entity` table: class → table, columns, indexes, relationships, owning side. |
| `migration_status` | diagnostic | Parsed `./gradlew flywayInfo` — applied/pending/failed, checksum drift. |
| `entity_field_add` | action | Adds `@Column` to an entity AND emits matching Flyway migration. Atomic; refuses if either half fails. Enforces §8.1.9. |

These five prove the contract end-to-end: a diagnostic tool, an action tool with hard-enforce, the freshness/stale-flip path (entity_lens cross-checks `SchemaIndex/`), and the CLI + MCP invocation surfaces. Subsequent slices add frontend (Expo) and database (PostgreSQL) bindings using the same contract.

### 11.13 What's in the second slice (Expo binding)

`ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` (closed 2026-05-18) delivers five tools and ports the Phase 05 contract shape to TypeScript + npm:

| Tool | Kind | Purpose |
|:---|:---|:---|
| `route_lens` | diagnostic | Filesystem + ts-morph AST scan of an Expo Router `app/` tree; classifies routes (static / dynamic / catch-all / group / layout); STALE-flips `DBL/APIIndex/*.md` on drift. |
| `component_lens` | diagnostic | ts-morph AST scan for React components under `components/`; emits name, props type, exported kind, hook usage; STALE-flips `DBL/Summaries/*.md`. |
| `dependency_audit` | diagnostic | Parses `package.json` + lockfile + import scan; classifies declared deps (used / unused / dev_in_prod) and imports (declared / missing / transitive). |
| `expo_doctor_lens` | diagnostic | Wraps `npx --yes expo-doctor`; parses checks into PASS/WARN/FAIL findings table; freshness contract guarantees every invocation actually spawns the subprocess. |
| `route_scaffold` | action | Atomically writes `app/<route_path>.tsx` + `__tests__/<route_path>.test.tsx` (and optional layout); refuses to commit a partial state. Enforces §8.2.8 (route ripple). |

The same four MCP tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`) are exposed via a per-binding Node shim under `Bindings/Expo/mcp/`, mirroring the Phase 05 shape. `Nissth_Verify` maps `operation: "compilation" | "doctor"` → `expo_doctor_lens` (Expo's project-health analog) and `operation: "dependencies"` → `dependency_audit`.

### 11.14 What's in the third slice (PostgreSQL binding)

`ImplementationPlans/Phase_07_Bridge_Postgres_FirstSlice.md` (closed 2026-05-18) delivers five **diagnostic-only** tools and introduces the first cross-cutting binding — general-purpose, not coupled to any application stack, installable alongside whatever application-side binding owns the backend:

| Tool | Kind | Purpose |
|:---|:---|:---|
| `schema_lens` | diagnostic | Tables/views/materialized views + per-table columns + FK relationship graph from `information_schema` + `pg_indexes`. STALE-flips `DBL/SchemaIndex/*.md` and `DBL/DependencyMaps/*.md` on drift. |
| `query_plan` | diagnostic | `EXPLAIN (FORMAT JSON)` / `ANALYZE` / `BUFFERS` for a user-supplied SQL statement. Refuses analyze/buffers modes on mutating statements (INSERT/UPDATE/DELETE/TRUNCATE/CREATE/DROP/ALTER/etc.). |
| `index_audit` | diagnostic | Index hygiene from `pg_stat_user_indexes` + `pg_index` + optional `pgstattuple`. Modes: usage / unused / duplicate / bloat. |
| `lock_audit` | diagnostic | Live lock state from `pg_locks` JOIN `pg_stat_activity`; waiting mode includes `pg_blocking_pids()` graph. Read-only — never `pg_terminate_backend`. |
| `migration_status` | diagnostic | Applied + failed migrations from `flyway_schema_history` or `databasechangelog`; auto-detects whichever exists. Does NOT list pending migrations (filesystem access belongs to the application-side binding). |

Connection model: env var `NISSTH_PG_URL` default; per-call `scope.extra.connection_string` override; password redacted from every produced report and log line (load-bearing contract — `tests/contract/SecretRedaction.test.ts`). The same four MCP tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`) are exposed via a per-binding Node shim under `Bindings/Postgres/mcp/`. `Nissth_Verify` maps `operation: "schema"` → `schema_lens`, `operation: "locks"` → `lock_audit`, `operation: "migrations"` → `migration_status`. No action tools this slice — real-development writes flow through pgAdmin / psql / JPA / the project's migration runner.

### 11.15 Unified `nissth-bridge` dispatcher (Phase 08)

`ImplementationPlans/Phase_08_Unified_Bridge_Dispatcher.md` (closed 2026-05-18) delivers the cross-binding launcher that §11.5 has always specified. Resolves the PATH collision between the three per-binding launchers (`Bindings/<stack>/scripts/nissth-bridge`) by providing a single canonical entry point at the repo root.

| Component | Lives at |
|:---|:---|
| Dispatcher logic | `Tools/nissth-bridge/dispatcher.js` (plain JS, zero runtime deps, ~330 lines) |
| Canonical launcher (POSIX) | `./nissth-bridge` at repo root |
| Canonical launcher (PowerShell) | `./nissth-bridge.ps1` at repo root |
| Per-binding launchers | `Bindings/<stack>/scripts/nissth-bridge` — **kept** as escape hatches; not expected on PATH |
| Tests | `Tools/nissth-bridge/test.mjs` via Node's built-in `node --test` (24 cases) |

**Discovery model.** The dispatcher globs `Bindings/*/*.bridge.json` (skipping `_schemas/` and any `_`-prefixed subdir), parses each manifest, and builds a `Map<toolName, bindingId[]>`. A new manifest field — `cli_entry: {runtime: "node" | "java-jar", path: "<rel-to-binding-root>"}` — tells the dispatcher how to spawn the binding's CLI. The contract schema (`Bindings/_schemas/bridge-command.schema.json`) is unchanged; `cli_entry` is per-binding manifest metadata.

**Conflict resolution.** Tool names are expected to be unique. When two bindings register the same name (the current bindings have one such case: `migration_status` is on both Spring Boot and Postgres), the dispatcher refuses to guess — exit 2 with the message `Tool 'X' is registered by multiple bindings: <a>, <b>. Use --binding <stack> to disambiguate.` The `--binding <stack>` flag is the disambiguator.

**Exit codes** match §11.5: 0 success · 2 parse/validate (incl. tool conflict) · 3 execute · 4 unknown tool/binding · 5 freshness contract violated (propagated from the spawned binding).

**Adding a new binding** requires no dispatcher change: drop a `<stack>.bridge.json` with a valid `cli_entry` into a new `Bindings/<NewStack>/` directory, and the next `nissth-bridge --list-bindings` picks it up.

**Framework-root resolution (Phase 09+).** Consumer projects that install Nissth as a git submodule rather than vendoring `Bindings/` need a way to tell the dispatcher where the framework lives. The dispatcher distinguishes two roots: the **repo root** (the consumer project, containing `CLAUDE.md`; where reports are written) and the **framework root** (containing `Bindings/`; where the tool catalog comes from). Framework-root resolution checks in order: (1) `NISSTH_FRAMEWORK_ROOT` env var — absolute path, must contain a `Bindings/` subdir; (2) `<repoRoot>/Tools/Nissth/` submodule convention; (3) `<repoRoot>` fallback (Nissth's own dogfooding). The dispatcher's tool catalog comes from the resolved framework root; reports always go to `<repoRoot>/AgentReports/Bridge/` — so a consumer project's Bridge reports stay in the consumer project's tree, not the framework's. An invalid `NISSTH_FRAMEWORK_ROOT` (path exists but lacks `Bindings/`) exits 2 with `error_code: invalid_framework_root` rather than falling through silently. The consumer-side launcher template at `Tools/nissth-bridge/consumer-launcher/` ships the `git submodule add ... Tools/Nissth` + launcher-copy recipe.
