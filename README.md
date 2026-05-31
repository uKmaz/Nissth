# Nissth

A deterministic execution framework for AI coding agents — operate, don't explore.

**Status:** Phase 6/6+ complete. Spring Boot binding shipped (`Bindings/SpringBoot/`, 111/111 green at last verification). Expo binding shipped (`Bindings/Expo/`, 51/51 green at last verification). PostgreSQL binding queued (Phase 07 candidate). Framework itself is operational; hardening continues under `.claude/` and `Tools/`.

This README is the 30-minute landing page. The complete reference is [`CLAUDE.md`](CLAUDE.md); the latest project state is the last entry of [`AgentReports/StatusUpdate.md`](AgentReports/StatusUpdate.md).

---

## Table of contents

1. [What this is](#what-this-is)
2. [What this is not](#what-this-is-not)
3. [Quick start — arriving agents](#quick-start--arriving-agents)
4. [Quick start — arriving humans](#quick-start--arriving-humans)
5. [The boot protocol](#the-boot-protocol)
6. [The Loop](#the-loop)
7. [Project structure](#project-structure)
8. [Two layers above source — DBL and the Diagnostic Bridge](#two-layers-above-source--dbl-and-the-diagnostic-bridge)
9. [The Implementation Template and plan-before-execute](#the-implementation-template-and-plan-before-execute)
10. [Reports — long-form companions to status entries](#reports--long-form-companions-to-status-entries)
11. [Stack bindings — current state](#stack-bindings--current-state)
12. [Installing and using a binding](#installing-and-using-a-binding)
13. [Working in this repo as an agent](#working-in-this-repo-as-an-agent)
14. [Working in this repo as a human](#working-in-this-repo-as-a-human)
15. [Maintaining the repo](#maintaining-the-repo)
16. [Hard Rules at a glance](#hard-rules-at-a-glance)
17. [Pointers](#pointers)

---

## What this is

Nissth is a small, file-based protocol that pre-structures the context an AI coding agent operates in, so the agent spends tokens executing work instead of rediscovering the project on every turn.

Three load-bearing ideas:

1. **State persists in files, not chat history.** [`AgentReports/StatusUpdate.md`](AgentReports/StatusUpdate.md) is the append-only ledger; its last entry is the project's current state. An agent that reads only the latest entry already knows the phase, the build status, the active plan, blockers, and the single most important next action. The framework is designed so this is true even after months of intermittent work and multiple agents.
2. **Two structured layers sit above raw source.** The **DBL** ([Diagnostic Bridge Layer](DBL/)) holds curated, stable architectural intent — module summaries, dependency maps, API indices, schema indices. The **Diagnostic Bridge** (`Bindings/`) runs structured commands against the live stack — compiler, test runner, database, dev server — and emits Markdown reports under `AgentReports/Bridge/`. Agents query one of those layers first; raw `grep` over source is the fallback, not the default.
3. **Plan-before-execute, append-only state, document-sync discipline.** Every unit of work goes through a four-step Loop (Report → Execute → Verify → Update Status). Source-modifying execution requires an approved `ImplementationPlans/Phase_NN_*.md`. Closing a status entry sweeps the documents the change touched and either updates them now or marks them stale. Discipline is enforced by file shape, not by goodwill.

What you actually get when you adopt Nissth: an agent that, on every new session, follows a fixed boot sequence (read latest status entry → read its `**Next:**` field → read the named plan), produces deterministic outputs, and never silently skips verification. This README explains the protocol. [`CLAUDE.md`](CLAUDE.md) is the complete rule set.

---

## What this is not

Nissth is **not**:

- A new programming language or framework you build _on_. It's a workflow protocol that wraps whatever stack you're already using (currently Spring Boot, soon Expo and PostgreSQL).
- An IDE plugin or CLI tool you install globally. The protocol is a set of files in your project root. The only optional binary is `nissth-bridge` — a unified cross-binding dispatcher at the repo root (`./nissth-bridge`), which delegates to whichever binding owns the tool you invoke.
- A "prompt engineering library." It doesn't tell the model what to say. It tells the agent which files to read, in which order, and what to write back when work is done.
- A code generator. Plans live in `ImplementationPlans/`; the agent writes the code. The framework specifies the workflow, not the output.
- A replacement for code review, CI, or tests. It specifies how an agent reports work, but the agent's output still goes through your normal quality gates.
- A multi-tenant or hosted service. Everything is local files in one git repository.
- A general-purpose AI agent harness. It's tuned for software engineering work where state is durable (a codebase, a roadmap, a set of plans). It would be over-structured for one-off chat tasks.

---

## Quick start — arriving agents

If you are an AI coding agent (Claude Code, Cursor, Codex, anything else) and you have just been pointed at this repository:

1. Read [`AgentReports/StatusUpdate.md`](AgentReports/StatusUpdate.md). Skip to the bottom; the **last entry** is the current project state.
2. The last entry's `**Next:**` field is your first instruction this session. It is a single concrete action — not a list, not a goal, an action.
3. The same entry's `**State:**` block tells you: phase, build/test status, active plan, DBL artifacts to read, blockers.
4. If `**State:**` lists an `Active plan: ImplementationPlans/Phase_NN_<slug>.md`, read that file next.
5. Read the DBL artifacts listed in `**State:** DBL refs:` — and only those. Do not browse `DBL/` opportunistically.
6. Read [`CLAUDE.md`](CLAUDE.md) end-to-end. It is the canonical rule set. Other agent-specific instruction files do not exist.

You are now operating. Every unit of work follows the Loop (see [The Loop](#the-loop)). Every source-modifying action requires an approved plan (see [The Implementation Template and plan-before-execute](#the-implementation-template-and-plan-before-execute)). Every task closure appends one new entry to `StatusUpdate.md` — never edits, reorders, or deletes prior entries.

If `StatusUpdate.md` does not exist, the directory is uninitialized; tell the user and stop. Do not improvise a bootstrap.

[`AGENTS.md`](AGENTS.md) is a 30-line redirect that says the same thing for non-Claude agents. The detail is here and in `CLAUDE.md`.

---

## Quick start — arriving humans

If you are a human reading this for the first time:

1. **Read this README to the bottom.** It is the friendly tour. The full rulebook is `CLAUDE.md`; you can defer that to your second pass.
2. **Open [`AgentReports/StatusUpdate.md`](AgentReports/StatusUpdate.md) and scroll to the bottom.** The latest entry tells you what was last worked on, what built green, what's next. Status entries are dense; one entry typically reads in 90 seconds.
3. **Look at [`ImplementationPlans/`](ImplementationPlans/).** Each `Phase_NN_<slug>.md` is one chunk of work, structured by [`ImplementationPlans/_TEMPLATE.md`](ImplementationPlans/_TEMPLATE.md). Start with the highest-numbered approved plan.
4. **Look at [`Bindings/SpringBoot/`](Bindings/SpringBoot/) for a concrete deliverable.** That is the first stack binding the framework produced; its `README.md` tells you what was built and how it's used.
5. **Look at one Report under [`AgentReports/Reports/`](AgentReports/Reports/).** Specifically `2026-05-17_phase-05-bridge-springboot-snapshot.md` — that is what an end-of-phase Report looks like.

Once you've done those five things, you can run `./nissth-bridge --list-tools` (the unified cross-binding dispatcher at repo root) and see the 14 live diagnostic + action tools across the three shipped bindings (Spring Boot, Expo, PostgreSQL). See [Installing and using a binding](#installing-and-using-a-binding).

You are not the agent. You hold the approval gate. When the agent says "plan authored, approval requested," you read the plan, push back or say "go," and that consent is logged in the plan's `§0 Approved` line. When the agent says "Verified: FAIL," you decide re-plan / fix-forward / rollback.

---

## The boot protocol

Hard Rule #1 (`CLAUDE.md` §4). Before any other action — before reading code, running tools, browsing directories, or proposing work:

```
1. Read AgentReports/StatusUpdate.md (use Read with an offset to read only the last entry block)
2. Take the **Next:** field as your first instruction
3. Read State:Active plan if set
4. Read State:DBL refs (only the named ones)
5. Read CLAUDE.md end-to-end (auto-loaded for Claude Code; explicit for other agents)
```

This sequence is non-negotiable. It is the contract that lets state persist in files instead of chat history. Skip step 1 and you are operating from prior-session assumptions; in a long-running project, those assumptions are wrong.

The **latest entry** is "current state" by virtue of being last. There is no editable "current state" zone at the top of `StatusUpdate.md` that gets overwritten; the file is strictly append-only (Hard Rule #3). If a past entry was wrong, the correction is a new entry, not a mutation.

If `StatusUpdate.md` does not exist, this directory is uninitialized. The agent stops and tells the user. The framework does not auto-bootstrap.

---

## The Loop

Hard Rule (`CLAUDE.md` §3). Every unit of work follows this four-step loop. There are no exceptions.

```
REPORT  →  EXECUTE  →  VERIFY  →  UPDATE STATUS
```

| Step | What it means | Output |
|:---|:---|:---|
| **REPORT** | State the current state, the target state, the planned changes. Cite the DBL artifacts and source ranges you used. | A report block (in chat, or §1 of the active plan). |
| **EXECUTE** | Make the changes — only the changes named in REPORT. No opportunistic refactors. | Modified files. |
| **VERIFY** | Build, test, or runtime check. Read the actual result. State the freshness guarantee. | Pass/fail outcome citing the artifact. |
| **UPDATE STATUS** | Append one new entry to `AgentReports/StatusUpdate.md`. The latest entry's `**State:**` block becomes the new current state by virtue of being the latest. | StatusUpdate.md committed. |

### Loop-Lock — what the framework refuses

- **No Execute without a preceding Report.** The Report is the artifact that proves the agent thought about the change before making it.
- **No moving to a new task before Verify returns a result on the previous one.** Half-finished tasks are a class of bug the framework refuses to enable.
- **No ending a session (or handing off) without appending a status entry.** The append-only ledger is how the next agent learns where things stand.
- **No editing, reordering, or deleting past status entries.** The file is strictly append-only. Corrections are forward-supersedences, not mutations.
- **No "it should work" or "the code looks right" as Verify.** Verify means an artifact was produced (build log, test report, runtime check) and read.

The Loop is what makes the protocol _deterministic_. Two agents working in two months on the same project will produce coherent, sequenced history — because the workflow's joints are file writes, not chat memory.

### A status entry's shape

What the agent appends to `StatusUpdate.md` at the close of every task. The format is fixed; the content varies.

```
### YYYY-MM-DD HH:MM — <task title>

**State:**
- Phase: <n/N or descriptor>
- Build: CLEAN | HAS_ERRORS | unchanged
- Tests: PASS | FAIL | unchanged
- Active plan: ImplementationPlans/Phase_NN_<slug>.md  | none
- DBL refs: <files read or updated during this task; or "none">
- Bridge reports: <files under AgentReports/Bridge/ produced this task; or "none">
- Blockers: <or "none">

**Report:**
- <condensed from the plan's §1 findings, or from the task's pre-execution diagnostic>

**Executed:**
- <condensed from the plan's §3, with all checkboxes resolved>

**Verified:**
- <condensed from the plan's §4 results, including the freshness statement>
- Doc sync: [updated: X, Y; marked stale: Z] | none — no source files modified
- Reports: [AgentReports/Reports/<filename> (kind), ...] | none

**Issues:**
- <or "none"; if Verified: FAIL, cite the matching incident Report filename>

**Next:**
- <one actionable next step — not a list, not a goal>
```

The `**State:**` block is what a future agent reads first on session start. It is the entire current state of the project — phase, build status, active plan, DBL artifacts in play, blockers. Six lines. The `**Next:**` is that future agent's first instruction.

The other blocks are accountability: what was checked (`**Report:**`), what was done (`**Executed:**`), what was verified and how fresh the verification is (`**Verified:**`), and what came up that didn't make it into the task (`**Issues:**`).

---

## Project structure

`CLAUDE.md` §5 has the canonical tree. The short version:

```
Nissth/
├── CLAUDE.md                       Canonical rules (this file's longer sibling)
├── AGENTS.md                       30-line redirect for non-Claude agents
├── README.md                       This file — engineer landing page
├── .claude/                        Claude Code config (hooks, permissions). Phase 5+.
├── ImplementationPlans/            One Phase_NN_*.md per chunk of work
│   └── _TEMPLATE.md                Canonical plan skeleton — copy + rename
├── AgentReports/
│   ├── StatusUpdate.md             Single source of truth for state. APPEND-ONLY.
│   ├── Reports/                    Long-form decisions, incidents, audits, snapshots
│   ├── Bridge/                     Auto-generated Diagnostic Bridge tool reports
│   ├── Snapshots/                  Pre-change rollback artifacts (Hard Rule #9)
│   └── Archive/                    Rotated logs when StatusUpdate.md exceeds ~100KB
├── DBL/                            Diagnostic Bridge Layer (stable knowledge artifacts)
│   ├── Summaries/                  Per-module summaries
│   ├── DependencyMaps/             Cross-module dependency graphs
│   ├── APIIndex/                   HTTP/RPC surface index
│   └── SchemaIndex/                Database schema index
├── Bindings/                       Per-stack Diagnostic Bridge implementations
│   ├── README.md                   Per-stack-binding model and contract pointers
│   ├── _schemas/                   Machine-readable bridge contract (JSON Schema)
│   │   └── bridge-command.schema.json
│   ├── SpringBoot/                 First reference binding (Java/Maven) — SHIPPED
│   ├── Expo/                       (Phase 06 in flight; not yet on disk)
│   └── Postgres/                   (Phase 07 queued; not yet authored)
├── Tests/                          Verification artifacts
├── Tools/                          Framework tooling — Phase 5+ hardening
└── Axiom/                          Reference predecessor framework (Unity). Read-only.
```

Roles, terse:

| File / Directory | Who reads it | What it holds |
|:---|:---|:---|
| `CLAUDE.md` | Claude Code (auto-loaded), every agent | Complete rule set. The source of truth this README summarizes. |
| `AGENTS.md` | Non-Claude agents | Boot protocol + redirect to `CLAUDE.md`. |
| `AgentReports/StatusUpdate.md` | Every agent on session start | Append-only ledger. Latest entry IS the current state. |
| `AgentReports/Reports/<date>_<slug>.md` | Anyone wanting context behind a status entry | Decisions, incidents, design reviews, audits, spec digests, snapshots. |
| `AgentReports/Bridge/<tool>_<ts>.md` | Agent during Report step | Auto-generated reports from `nissth-bridge` tool invocations. Disposable. |
| `ImplementationPlans/_TEMPLATE.md` | Plan author | Canonical phase plan skeleton. The shape every plan must conform to. |
| `ImplementationPlans/Phase_NN_*.md` | Executing agent | One concrete plan per phase, in the template format. |
| `DBL/**` | Agent during Report step | Pre-computed answers to common project-structure questions. |
| `Bindings/_schemas/bridge-command.schema.json` | Bridge implementations | Machine-readable command contract. Every binding implements this. |
| `Bindings/<stack>/**` | Bridge CLI/MCP runtime | Per-stack diagnostic and action tool implementations. |
| `.claude/settings.json` | Claude Code harness | Hooks, permissions, skills (Phase 5+ enforcement). |

Two directories deserve called-out attention because they back the framework's "don't grep raw source" rule:

- **`DBL/`** is the stable layer. Hand-curated artifacts (small, ~200–800 tokens each) that answer "what is this project _supposed_ to look like." Module summaries, API indices, schema tables, dependency graphs. Each artifact has YAML frontmatter (`source_state`, `covers`, `stale_when`) so an agent can check freshness without reading the body. Nissth's own DBL is empty (the project has no production source); consumer projects populate theirs as Phase_00 work.
- **`Bindings/`** is the live layer. Each subproject (`Bindings/SpringBoot/`, eventually `Bindings/Expo/`, `Bindings/Postgres/`) implements a contract defined in `_schemas/bridge-command.schema.json` and exposes a small set of tools (compile checks, endpoint scans, entity scans, migration status, plus action tools that hard-enforce framework rules). Tools write Markdown reports under `AgentReports/Bridge/` that the agent reads in one turn instead of running 20 greps.

The split is deliberate. DBL is the architectural intent that survives a refactor. Bridge is the runtime state that depends on the current commit, the current classpath, the current database row counts. An agent asking "what entities exist" reads `DBL/SchemaIndex/`. An agent asking "are there pending Flyway migrations against this database right now" runs `nissth-bridge migration_status`.

---

## Two layers above source — DBL and the Diagnostic Bridge

The single most important framework property to internalize:

> When a structured layer (DBL or Bridge) covers the question, querying it is free in tokens, deterministic in output, and faster than reading source. Falling through to `grep` and full-file reads should be the exception, not the default.

This is Hard Rule #4 (`CLAUDE.md` §4 + §7 + §11). It is what separates Nissth from a normal "AI reads the repo" workflow.

### When to query which

| Question shape | Query | Why |
|:---|:---|:---|
| What modules exist? Public API contract? Which imports are forbidden? | **DBL** (`DBL/Summaries/`, `DBL/DependencyMaps/`, `DBL/APIIndex/`, `DBL/SchemaIndex/`) | Stable architectural intent; curated; survives across refactors. |
| Does this endpoint actually exist in the compiled bytecode right now? | **Bridge** (`nissth-bridge endpoint_lens`) | Live state — only the running compiler/classpath knows. |
| Are there pending Flyway migrations? Does the schema match the entities? | **Bridge** (`nissth-bridge migration_status`, `entity_lens`) | Live state — runtime-only signal. |
| What's the dependency graph in this profile? | **Bridge** | Profile-dependent; only the live `ApplicationContext` knows. |
| What does this query's EXPLAIN plan look like at current row counts? | **Bridge** (future Postgres binding) | Statistics-dependent; only the live DB knows. |
| Why was JPA chosen over jOOQ? | **Reports** (`AgentReports/Reports/`) | Decision rationale, not state. |

### DBL — the stable layer

Per `CLAUDE.md` §7. DBL artifacts answer durable questions about project structure. Four types:

| Type | Directory | One file per | Answers |
|:---|:---|:---|:---|
| Summary | `DBL/Summaries/` | Module / component | What does this module do? Public API? Gotchas? |
| Dependency Map | `DBL/DependencyMaps/` | Boundary / scope | What imports what? Which imports are forbidden? |
| API Index | `DBL/APIIndex/` | API namespace | What endpoints/methods exist? Signatures? Auth? |
| Schema Index | `DBL/SchemaIndex/` | Database / schema | Tables, columns, indexes, foreign keys? |

Every artifact starts with YAML frontmatter:

```yaml
---
artifact_type: summary | dependency_map | api_index | schema_index
name: <human-readable name>
last_regenerated: YYYY-MM-DD by [agent | user]
source_state: <git commit hash, or "uncommitted state at YYYY-MM-DD HH:MM">
covers:
  - <glob pattern or explicit path>
stale_when:
  - <condition that invalidates this artifact>
---
```

Before relying on an artifact, the agent reads its frontmatter (only — not the body yet), compares `source_state` to current state, and treats it as STALE if the project has changed beneath any path in `covers`. Reading a stale artifact is worse than reading source.

Token budget per artifact: 200–800 tokens. Bigger artifacts get split. The point is that an agent in its Report step can read 5–10 artifacts and have a complete picture of the project's architecture for the cost of half a source file.

Hand-maintained today. Phase 5+ introduces regeneration tooling under `Tools/`.

### Bridge — the live layer

Per `CLAUDE.md` §11. The Bridge is a single binary (`nissth-bridge`, per-binding) that speaks one stack-agnostic JSON command grammar and produces Markdown reports with a frozen frontmatter contract. Every binding implements the same contract; only the tool catalog differs.

The command shape (`Bindings/_schemas/bridge-command.schema.json`):

```json
{
  "tool": "endpoint_lens",
  "mode": "with_dto",
  "scope": {
    "package": "com.example.api",
    "max_depth": 2
  },
  "output": {
    "destination": "file"
  }
}
```

The report shape (every Bridge report):

```yaml
---
tool: endpoint_lens
mode: with_dto
binding: spring-boot
binding_version: 0.1.0
generated_at: 2026-05-17T14:30:22+03:00
scope: {package: com.example.api, max_depth: 2}
freshness:
  source: AST parse of files under src/main/java/com/example/api
  source_state: git SHA bf9f859 / file mtime range 14:29:00–14:30:18
  guarantee: classpath unchanged since compile; AST built fresh this call
contract_version: 1
---

## Endpoints
[table follows]
```

Tools come in two kinds. **Diagnostic** tools read state and write a report. **Action** tools modify the project (edit code, emit migrations) and refuse to proceed unless their enforcement contract is satisfied — `entity_field_add` (Spring Boot binding) atomically writes both the `@Entity` field AND the matching Flyway migration, rolling back both on partial failure. There is no "warn and proceed" mode. This is `CLAUDE.md` §11.7.

### The stale-flip mechanism

The most load-bearing Bridge feature: when a diagnostic tool's result contradicts a DBL artifact whose `covers` overlaps the queried scope, the Bridge rewrites that DBL artifact's `last_regenerated` field to `STALE — superseded by AgentReports/Bridge/<report>`. The agent then treats the DBL artifact as unreadable until regenerated.

This converts the Document Sync Mandate (Hard Rule #11) from agent discipline into a runtime enforcement. The agent cannot accidentally read a stale DBL artifact and act on it; the staleness is mechanically in the frontmatter.

### Today's tools

The Spring Boot binding ships five tools (`CLAUDE.md` §11.12):

| Tool | Kind | What it does |
|:---|:---|:---|
| `compile_verify` | diagnostic | Daemon-stop + clean compile; reports CLEAN / HAS_ERRORS with a `file:line` error table. |
| `endpoint_lens` | diagnostic | AST-scan of `@*Mapping` annotations; emits endpoint table. |
| `entity_lens` | diagnostic | `@Entity` table: class → table, columns, indexes, relationships. STALE-flips `DBL/SchemaIndex/` on column drift. |
| `migration_status` | diagnostic | Parsed `mvn flyway:info` — applied/pending/failed, checksum drift. |
| `entity_field_add` | action | Adds `@Column` to an entity AND emits a matching Flyway migration. Atomic; rolls back both halves on any failure. |

See [`Bindings/SpringBoot/README.md`](Bindings/SpringBoot/README.md) for the full catalog and `scope.extra` keys per tool.

The Expo binding (Phase 06, closed 2026-05-18) ships `route_lens`, `component_lens`, `dependency_audit`, `expo_doctor_lens`, and `route_scaffold` (action). The Postgres binding (Phase 07, queued) will ship `query_plan`, `index_drift`, `lock_audit`, plus an action tool TBD.

---

## The Implementation Template and plan-before-execute

Per `CLAUDE.md` §6, every plan file in `ImplementationPlans/` (except `_TEMPLATE.md`) MUST conform to the same six-section skeleton. The skeleton enforces the Loop at the plan level: §1 is REPORT, §3 is EXECUTE, §4 is VERIFY, §6 is the pre-filled UPDATE STATUS entry.

### Sections

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

- Plan files are named `Phase_NN_Slug.md`. Zero-padded number, snake_case slug.
- A plan declares its dependencies in `§0 Depends on` (prior plan IDs, or `none`).
- `§3.2 Forbidden in this phase` is mandatory and explicitly lists out-of-scope changes the agent might be tempted to bundle in. This is the anti-scope-creep guard.
- `§4.4 Failure handling` mandates: on any Verify failure, STOP and append a `Verified: FAIL` status entry. No silent retry. No proceeding to Cleanup.
- A plan is `Approved: pending` until the user fills in the Approved date. The executing agent does not start §3 on an unapproved plan.

### Plan-before-execute — Hard Rule #12

The framework's most important workflow gate: **no source-modifying execution begins until an `ImplementationPlans/Phase_NN_*.md` plan exists, conforms to `_TEMPLATE.md`, and has `Approved: <ISO date>` in §0**.

"Source-modifying execution" means edits to anything outside the plan-exempt zones:

- `ImplementationPlans/` — plans
- `AgentReports/` — status entries, Reports, Bridge reports, snapshots
- `DBL/` — DBL artifacts
- `.claude/` — Claude Code config
- `Bindings/_schemas/`, `Bindings/README.md`, `Bindings/<stack>/README.md` — bridge contract documentation

Edits to real product code, configs, schemas, migrations, or binding implementation source under `Bindings/<stack>/src/` are NOT plan-exempt. A Loop §3 EXECUTE step that touches such files without a matching approved plan is a Loop-Lock violation.

This rule is what makes Nissth practical for autonomous agents. It guarantees that every code-changing turn is preceded by an artifact (the plan) that the user has read and approved. The plan is the contract; the agent does only what is in §3.

### Execution sequence (when a plan is approved)

1. Read `CLAUDE.md` (already in context) + the latest `StatusUpdate.md` entry.
2. Read the plan file end-to-end.
3. Execute §1 Pre-Flight. Fill in §1.3 Findings. If any `Match? = no`, STOP.
4. Execute §3 step list in order. Tick checkboxes as completed.
5. Execute §4 Verification. If any check fails, follow §4.4.
6. Execute §5 Cleanup.
7. Append the §6 entry (filled in) to `AgentReports/StatusUpdate.md`.

The pre-flight stop is the most under-appreciated part of this sequence. A plan authored last week against a snapshot of the codebase that has since drifted will fail at §1.3 — and that failure is cheap (no source change yet) and recoverable (re-plan against current state). Without the pre-flight gate, the same plan would start executing against drifted state and produce broken code that passes its own checks against the wrong baseline.

### A plan's shape, abbreviated

`Phase_NN_<slug>.md`, conforming to `_TEMPLATE.md`:

```
## 0. Metadata
- Plan ID: Phase_NN_<slug>
- Authored: YYYY-MM-DD by <agent or user>
- Approved: YYYY-MM-DD by <user>  | pending
- Depends on: <prior plan IDs, or "none">
- Estimated scope: <files touched, components affected — one paragraph>

## 1. Pre-Flight Diagnostic (REPORT)
### 1.1 Inputs to read
  - DBL: ...
  - Bridge reports: ...
  - Source: ... (specific files + line ranges)
  - StatusUpdate.md: latest entry as of plan authoring
### 1.2 Diagnostic actions
  | # | Action | Tool/command | Scope | Why |
### 1.3 Findings (filled during execution)
  | Question | Expected | Actual | Match? |
  Stop condition: if any row's Match? = no, STOP.

## 2. Expected State
### Before (current state, per Pre-Flight)
### After (post-execution target)

## 3. Execution (EXECUTE)
### 3.1 Step list
  - [ ] Step 1. <action>. File: <path>. Operation: add|modify|remove. Acceptance: <criterion>.
  - [ ] Step 2. ...
### 3.2 Forbidden in this phase
  - <thing not to touch>

## 4. Post-Flight Verification (VERIFY)
### 4.1 Freshness guarantee
### 4.2 Checks
  - [ ] Build: <command> — expected: <result>
  - [ ] Tests: <command> — expected: <result>
### 4.3 Pass criteria
### 4.4 Failure handling
  STOP. Append Verified: FAIL. Author incident Report. No silent retries.

## 5. Cleanup
  - Reports check, Document Sync sweep, scratch cleanup.

## 6. Status Update Entry
  <paste-ready status entry block, filled in at task closure>
```

Look at [`ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md`](ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md) for the canonical filled-in example — 20 steps, every checkbox resolved, post-flight verification reading actual artifacts.

---

## Reports — long-form companions to status entries

Per `CLAUDE.md` §10. `AgentReports/StatusUpdate.md` is the short ledger — one block per task, dense by design, append-only. **Reports** are the long-form companions: any document that **details the project** in a way too substantial for a status entry yet too durable to live only in chat.

Read the rule broadly: when in doubt, write the Report.

### Kinds

| Kind | Triggered when | Examples |
|:---|:---|:---|
| **Decision record** | A choice between named alternatives the team might re-debate | "JPA vs jOOQ", "Modular monolith vs microservices" |
| **Incident report** | A `Verified: FAIL`, a production incident, a discovered defect class | "Migration V7 partial-apply post-mortem" |
| **Design review** | A non-trivial subsystem is being introduced or refactored | "Reservation engine pessimistic-lock design" |
| **Audit / analysis** | A cross-cutting investigation: dependency, perf, security, license | "N+1 query sweep, results" |
| **Spec digest** | A long external spec needs a project-tailored summary | "iyzico API surface used by Süprüz" |
| **Project snapshot** | A periodic or on-request comprehensive state dump | "End-of-Phase-3 architecture snapshot" |
| **Verification report** | A verification run produced output worth preserving | "Phase 2 integration test results, Testcontainers PG 15" |

### Mandatory Reports

Reports are usually a judgment call, but five cases mandate one (`CLAUDE.md` §10.4):

1. **Every `Verified: FAIL` status entry** MUST be paired with an `incident` Report.
2. **Architecture decisions between named alternatives** require a `decision` Report.
3. **Long external spec ingestion** produces a `spec_digest` Report so future agents don't re-extract from the source.
4. **End of a non-trivial phase** triggers a `snapshot` Report.
5. **Cross-phase pivot** (a change that invalidates an earlier plan's premise) requires a `decision` Report.

### File shape

`AgentReports/Reports/YYYY-MM-DD_kebab-case-slug.md`. ISO date is authored-date, not event-date. One topic per file. Mandatory YAML frontmatter (`report_type`, `title`, `authored`, `last_updated`, `related_status_entries`, `related_plans`, `covers`, `supersedes`). Body is dense, scannable, table-friendly. Token budget 500–3000; split if larger.

### Linkage

Every authored Report is referenced from the closing status entry of the task that produced it (`Reports:` line under `**Verified:**`). The status entry's `**Issues:**` line cites the matching incident Report by filename when `Verified: FAIL`.

### Examples in this repo

- [`AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md`](AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md) — canonical end-of-phase snapshot
- [`AgentReports/Reports/2026-05-15_phase-05-maven-pivot.md`](AgentReports/Reports/2026-05-15_phase-05-maven-pivot.md) — decision Report (Gradle vs Maven for the SpringBoot binding's own build)
- [`AgentReports/Reports/2026-05-15_diagnostic-bridge-architecture.md`](AgentReports/Reports/2026-05-15_diagnostic-bridge-architecture.md) — design review

---

## Stack bindings — current state

The Diagnostic Bridge is implemented per-stack under `Bindings/`. Three stacks are in scope. Today, one is shipped.

| Stack | Binding directory | Status | Language / build | Tool count |
|:---|:---|:---|:---|:---|
| Spring Boot 3.x (Java 17+, Maven, Flyway, PostgreSQL) | [`Bindings/SpringBoot/`](Bindings/SpringBoot/) | **Shipped** (Phase 05 closed 2026-05-17, 111/111 green) | Java 17+ / Maven | 5 |
| Expo / React Native (TypeScript) | [`Bindings/Expo/`](Bindings/Expo/) | **Shipped** (Phase 06 closed 2026-05-18, 51/51 green) | TypeScript / npm | 5 |
| PostgreSQL (incl. PostGIS) | `Bindings/Postgres/` (not on disk yet) | **Queued** — Phase 07 candidate, plan not yet authored | TBD (Go or Python) | TBD |

The contract that every binding implements is owned by [`Bindings/_schemas/bridge-command.schema.json`](Bindings/_schemas/bridge-command.schema.json) and `CLAUDE.md` §11. Bindings consume the contract; they never modify it. Adding a new stack requires zero changes to the contract or to `CLAUDE.md` §11. The per-stack rule sheet for the Spring Boot stack is `CLAUDE.md` §8 (forbidden patterns, verification protocol, DBL mapping, common discovery patterns); the Expo equivalent (§8.2) ships inside Phase 06's execution.

A consumer project (e.g., the Süprüz reservation system at `Desktop/Supruz/`) does NOT copy a binding into its own tree. Three supported integration paths exist:

1. **Git submodule** — pin to a tag, easy to update and audit.
2. **Version-pinned dependency** — when a binding is published (Maven Central, npm, etc.), declare a normal dependency.
3. **Local dev linking** — `gradle includeBuild` / `npm link` / equivalent, for active development on the binding itself.

In all three cases the consumer never modifies the binding's source. Project-specific custom diagnostics live in the consumer's own `Tools/` directory.

---

## Installing and using a binding

Three bindings ship today (Spring Boot, Expo, PostgreSQL). The canonical entry point is the **unified cross-binding dispatcher** at the repo root (`./nissth-bridge` POSIX / `./nissth-bridge.ps1` PowerShell). It auto-discovers every installed binding and routes `<tool>` invocations to the binding that owns the tool.

This section walks through the Spring Boot binding's build first (it's the most-involved of the three), then shows unified-dispatcher examples across all bindings.

### Prerequisites

- **Java 17+.** Tested with Eclipse Temurin 17.0.19 and Oracle JDK 21.0.7.
- **Maven 3.9+.** The bundled Maven Wrapper (`./mvnw`) auto-bootstraps Apache Maven 3.9.9 into `~/.m2/wrapper/dists/` on first use; no system install required.
- **Docker** (only if you want to run the 7 Failsafe integration tests, which use Testcontainers PostgreSQL). The 104 unit tests run without Docker.
- **Node 20+** (only if you want the MCP shim for Claude Code integration). The CLI launcher works without Node.

### Build

```bash
cd Bindings/SpringBoot
./mvnw clean verify -U -B        # POSIX shells, Git Bash
.\mvnw.cmd clean verify -U -B    # PowerShell / cmd
# or, if you have a system Maven on PATH:
mvn clean verify -U -B
```

Produces:

- `target/nissth-bridge-0.1.0.jar` — executable jar, `Main-Class: com.nissth.bridge.cli.NissthBridgeCli`
- `target/surefire-reports/` — unit test results
- `target/failsafe-reports/` — integration test results

Expected: 104 unit + 7 IT = 111/111 PASS. Jar at ~5.83 MB.

### Use the unified dispatcher

```bash
# POSIX shell
./nissth-bridge --list-bindings              # 3 bindings: expo, postgres, spring-boot
./nissth-bridge --list-tools                 # 14 unique tool names (15 total, 1 conflict: migration_status)
./nissth-bridge --describe entity_field_add  # works — entity_field_add is unique to spring-boot

# PowerShell
.\nissth-bridge.ps1 --list-tools
```

Tool routing is automatic. The dispatcher reads `Bindings/*/*.bridge.json` manifests, builds a `Map<toolName, bindingId>`, and forwards your invocation to the binding's CLI.

### Run a diagnostic across any binding

```bash
# Spring Boot — endpoint lens
./nissth-bridge endpoint_lens \
  --scope.root_path /path/to/my-spring-app \
  --scope.package com.example.api

# Expo — route lens
./nissth-bridge route_lens --scope.root_path /path/to/my-expo-app

# Postgres — schema lens (general-purpose; reads NISSTH_PG_URL or scope.extra.connection_string)
export NISSTH_PG_URL='postgresql://nissth_ro:***@localhost:5432/mydb'
./nissth-bridge schema_lens --mode full --scope.package public
```

The report path is the only stdout; reports themselves live at `<repo-root>/AgentReports/Bridge/<tool>_<ISO8601>.md`.

### Tool-name conflicts

The framework expects tool names to be unique. The current bindings have one naturally-occurring conflict — **`migration_status`** is registered by both Spring Boot (reads its project's Flyway/Liquibase tables) and Postgres (general-purpose Postgres binding reads the same tables from any DB). Both implementations are intentional; the disambiguator is `--binding <stack>`:

```bash
./nissth-bridge migration_status --binding spring-boot --scope.root_path /path/to/my-spring-app
./nissth-bridge migration_status --binding postgres  # uses NISSTH_PG_URL
```

Without `--binding`, the dispatcher errors out (exit 2) with the conflict message.

### Escape hatch: per-binding launcher

Each binding still ships its own launcher under `Bindings/<stack>/scripts/nissth-bridge` for direct access. These bypass the dispatcher entirely — useful for binding-specific debugging or when the unified dispatcher isn't on PATH. They're not expected to be on PATH alongside the repo-root launcher.

```bash
./Bindings/SpringBoot/scripts/nissth-bridge --list-tools   # 5 tools, SpringBoot only
./Bindings/Postgres/scripts/nissth-bridge schema_lens ...   # Postgres only, same flags as unified
```

### Put the launcher on PATH

Add the absolute path to the repo root (e.g., `~/Desktop/Nissth`) to your shell's PATH, or symlink `./nissth-bridge` into `/usr/local/bin/` (POSIX). The launcher resolves `Tools/nissth-bridge/dispatcher.js` relative to its own location, so it works from any cwd.

### MCP integration with Claude Code

```bash
cd Bindings/SpringBoot/mcp
npm install
node smoke-test.mjs   # runs the 4-tool end-to-end runtime smoke
```

The shim under `Bindings/SpringBoot/mcp/` registers four MCP tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`) that shell out to `nissth-bridge`. See [`Bindings/SpringBoot/mcp/README.md`](Bindings/SpringBoot/mcp/README.md) for registration with Claude Code.

### Cross-binding dispatcher (Phase 08 — closed 2026-05-18)

The PATH-collision callout that lived here through Phases 05–07 is **resolved by the unified dispatcher** shipped in Phase 08. See `Tools/nissth-bridge/README.md` for the dispatcher's discovery model + flag reference, `CLAUDE.md` §11.15 for the framework-level spec, and the section above for usage examples.

Per-binding launchers remain available as escape hatches; the dispatcher is the canonical PATH entry.

---

## Working in this repo as an agent

If you are an agent (Claude Code or otherwise) about to do work, this is your operational checklist.

### Every session, no exceptions

1. **Read `AgentReports/StatusUpdate.md` — only the latest entry block.** Use a Read tool with an offset to avoid reading the whole file.
2. **Treat the latest entry's `**Next:**` field as your first instruction.** Single action, not a list.
3. **Read the active plan** if `**State:** Active plan` is set. End to end.
4. **Read named DBL artifacts.** Only the ones listed in `**State:** DBL refs`. Do not browse `DBL/` opportunistically.
5. **Read `CLAUDE.md`.** Claude Code auto-loads it; other agents read it explicitly.

### Before any source-modifying action

1. **Check if a plan covers it.** No plan → STOP. Author one in `ImplementationPlans/`, request user approval. Plan-authoring itself is plan-exempt.
2. **Check `§3.2 Forbidden in this phase`.** Is what you're about to do explicitly forbidden? If yes, STOP — even if it seems harmless.
3. **Run `§1 Pre-Flight Diagnostic`.** Fill in `§1.3 Findings`. Any `Match? = no` → STOP.
4. **Execute `§3 Step list` in order.** One step at a time. Tick checkboxes as done.

### Before treating a verification as PASS

1. **Read the actual artifact.** Build log, test report, runtime check output. Not "the code looks right."
2. **State the freshness guarantee.** How do you know the verifier saw your latest changes? (HR#10.)
3. **If anything fails, STOP, append `Verified: FAIL`, author an incident Report.** Do not retry silently.

### Before closing the task

1. **Run the Document Sync sweep.** List source files modified → identify affected stable documents (DBL, plans, CLAUDE.md examples) → UPDATE now or MARK STALE.
2. **Check the Reports mandate.** Did this task trigger §10.4(1)/(2)/(3)/(4)/(5)? Author the Report.
3. **Append a new status entry to `AgentReports/StatusUpdate.md`.** Use the `§6` paste-ready block from the plan, filled in. Include the `Doc sync:` line.
4. **One actionable `**Next:**`.** Not a list. Not a goal. One concrete next step.

### Things that are forbidden — silent versions

- Editing past status entries. (HR#3.)
- Re-reading raw source when DBL or Bridge covers the question. (HR#4.)
- Full-tree `Glob '**/*'` reads without an explicit reason in your Report. (HR#5.)
- Saying "verify ran fine" when no artifact was produced. (HR#6 + HR#10.)
- Bundling multiple instructions into `**Next:**`. (HR#7.)
- Writing "Thursday" or "yesterday" in a file. ISO dates only. (HR#8.)
- Skipping the snapshot before a destructive multi-step change. (HR#9.)
- Touching product code without an approved plan. (HR#12.)

When in doubt: STOP and ask. Hard Rule #2 is "No Silent Deviations."

---

## Working in this repo as a human

You are not the agent. Your role is different.

### Your daily checklist

1. **You hold the approval gate.** When the agent says "plan authored, approval requested," read the plan, push back or say "go." Approval consents the agent to execute §3.
2. **You decide on Verified: FAIL.** Re-plan, fix-forward, or rollback. The agent stops; it doesn't decide.
3. **You read the snapshot Reports.** End-of-phase snapshots are 500–3000 tokens each. Read them at the end of each phase — they're the durable "what we built and why" record.
4. **You set the priority.** The agent's `**Next:**` is its first instruction this session, but you tell it what to work on next-after-that. Priorities live in the latest status entry's `**Next:**` paragraph as a list.

### When you onboard the project

1. Read this README to the end.
2. Read `CLAUDE.md` once — fully. After that, refer to it by section.
3. Skim the last 5 status entries in `AgentReports/StatusUpdate.md`.
4. Skim the most recent approved plan in `ImplementationPlans/`.
5. Skim one Report — start with `2026-05-17_phase-05-bridge-springboot-snapshot.md`.
6. Run `./nissth-bridge --list-bindings` once to confirm the unified dispatcher sees all three bindings (`expo`, `postgres`, `spring-boot`).

### When you hit a snag

- "The agent did the wrong thing." → look at the agent's most recent status entry. The `**Executed:**` block tells you what they actually did; the `**Issues:**` block tells you what they noticed. If the deviation isn't logged, it's a framework violation (HR#2 — No Silent Deviations); push back.
- "I don't know what's current." → read the last entry of `StatusUpdate.md`. Its `**State:**` block IS the current state. There is no other view.
- "The plan is wrong." → push back before approving. Once approved, the plan is a contract. Mid-execution amendments require a new approval round.
- "A document is stale." → check its frontmatter. DBL artifacts have `last_regenerated` and `source_state`; Reports have `last_updated`. If outdated, mark stale or supersede with a new artifact; don't silently edit.

### How to give feedback that sticks

Two channels, durable across sessions:

- **The agent's memory layer** (under `~/.claude/projects/.../memory/` for Claude Code). Use this for personal preferences and ways-of-working: "don't mock the DB," "defer docs to end of phase," "prefer single bundled PRs for refactors."
- **A Report under `AgentReports/Reports/`.** Use this for project-level decisions and rationale: "Why we chose Maven over Gradle," "Why JPA over jOOQ." Reports survive memory wipes and onboard the next agent.

Things in chat-only memory are lost on context compression. Things in files survive.

---

## Maintaining the repo

The framework is opinionated about what stays consistent and how.

### `AgentReports/StatusUpdate.md`

- **Strictly append-only.** No edits, no reorders, no deletes. (Hard Rule #3.)
- **One entry per task closure.** Dense — header H3, then `**State:**`, `**Report:**`, `**Executed:**`, `**Verified:**`, `**Issues:**`, `**Next:**` blocks.
- **Latest entry IS current state.** No editable "current state" header at the top.
- **Corrections are forward-supersedences.** If a past entry was wrong, the new entry says so explicitly.
- **Doc sync line is mandatory.** Every entry's `**Verified:**` block includes a `Doc sync: [updated: X; marked stale: Y]` line. Missing line is a Loop-Lock violation.
- **ISO dates only.** Convert "Thursday" / "yesterday" to `YYYY-MM-DD` when writing.
- **Archive at ~100KB.** When the file gets big, the oldest entries rotate to `AgentReports/Archive/StatusUpdate_<date>.md` and a header at the top of the live file points back. The current file stays under 100KB so a single Read with no offset gets the recent history.

### `ImplementationPlans/`

- **One `Phase_NN_<slug>.md` per chunk of work.** Sequential numbering, snake_case slug, zero-padded.
- **Conform to `_TEMPLATE.md`.** All 6 sections, no omissions. If a section is irrelevant, write `N/A — [reason]`.
- **Approved date in §0 is the consent gate.** No `§3` execution on a plan with `Approved: pending`.
- **§3.2 Forbidden is mandatory** — anti-scope-creep.
- **§4.4 Failure handling is mandatory** — STOP on Verify failure, no silent retries.

### `DBL/`

- **One artifact per file.** Don't bundle multiple modules into a Summary.
- **Frontmatter is non-negotiable.** Every artifact has `artifact_type`, `last_regenerated`, `source_state`, `covers`, `stale_when`.
- **200–800 tokens per artifact.** Bigger artifacts get split (e.g., `<module>-overview.md` + `<module>-details.md`).
- **Answer questions; don't replicate source.** "Field list lives in `User.java:14-32`" is a valid answer when the field list is volatile.
- **Plan-level closure regenerates affected artifacts.** Any plan's `§5 Cleanup` regenerates or flags-stale DBL artifacts whose `covers` overlap files modified during the phase. The Bridge's stale-flip mechanism handles the common cases automatically.

### `AgentReports/Reports/`

- **One topic per file.** Cross-link related Reports in their bodies; don't bundle.
- **Frontmatter is non-negotiable.** Every Report has `report_type`, `title`, `authored`, `last_updated`, `related_status_entries`, `related_plans`, `covers`, `supersedes`.
- **Append-friendly, not append-only.** Updates allowed — update `last_updated`, add a `## Revision history` line.
- **500–3000 tokens.** Bigger Reports get split.
- **Mandatory in five cases.** See [Reports](#reports--long-form-companions-to-status-entries) above.

### `AgentReports/Bridge/`

- **Auto-managed by the Bridge runtime.** Never hand-author files here.
- **Disposable.** Reports accumulate; a future GC tool under `Tools/` (Phase 5+) will prune.
- **Gitignored.** Don't commit the working directory.

### `CLAUDE.md`

- **Plan-exempt to edit** when the edits are framework-shaping (adding a Hard Rule, adjusting the Project Structure section, adding a stack section).
- **Cross-references in code or other docs** can rot when CLAUDE.md sections are renumbered. Renumber-heavy edits should explicitly list everywhere that cites the changed sections.

### Git hygiene

- **`AgentReports/Bridge/`, `AgentReports/Snapshots/`, `node_modules/`, `target/`, `dist/`, `.tsbuildinfo`** — all gitignored. The repo-root `.gitignore` is authoritative.
- **Commits should be coherent.** Authoring a plan + executing it + status entry → one PR or one commit cluster, not 30 micro-commits. Phase 05 closed with three commits: code + tests + fixture; plans + status entries + reports; framework permission allowlist additions.
- **`git push` is destructive enough to warrant explicit user OK.** Agents do not push on their own without standing instructions.

---

## Hard Rules at a glance

Per `CLAUDE.md` §4. Thirteen rules, listed in execution order:

| # | Rule | What it means in one line |
|:---|:---|:---|
| 1 | Read StatusUpdate.md first | Every session starts with the latest entry, before any other action. |
| 2 | No Silent Deviations | If you're about to do something the framework doesn't cover, STOP and tell the user. |
| 3 | StatusUpdate.md is strictly append-only | New entries only. No edits, reorders, or deletes — corrections are forward-supersedences. |
| 4 | Query structured layers before reading source | DBL for stable intent, Bridge for live state. Raw source is the fallback. |
| 5 | Scope every read | Targeted file + line range. No full-tree dumps. No `**/*` globs without a recorded reason. |
| 6 | Verify against the artifact, not the plan | Build/test output is authoritative. The plan is a contract, not evidence. |
| 7 | One actionable `**Next:**` | A single concrete step, not a list. |
| 8 | Convert relative dates | "Thursday" / "yesterday" → ISO `YYYY-MM-DD` when writing to any persistent file. |
| 9 | Snapshot before destructive multi-step work | Rollback artifact under `AgentReports/Snapshots/`, referenced in the Report. |
| 10 | Respect verifier freshness | "Verify ran fine" only counts if the verifier saw the latest changes. State the freshness guarantee. |
| 11 | Document Sync Mandate | Every task closure sweeps stable docs (DBL, plans, CLAUDE.md examples) — UPDATE or MARK STALE. Logged on the `Doc sync:` line. |
| 12 | Plan-before-execute | No source-modifying execution without an approved `Phase_NN_*.md` plan. Bootstrap and plan/Report/DBL/bridge-contract authoring are plan-exempt. |
| 13 | Permission gate at project initialization | Before any initialization on a new Nissth-bound project, the agent asks the user for full permission and waits for unambiguous consent. Fires once per project. |

Memorize the numbers; in this repo's prose, rules are cited as "HR#NN."

---

## Pointers

For deeper reading, in roughly the order you'd want it:

| Need | File |
|:---|:---|
| Complete rulebook | [`CLAUDE.md`](CLAUDE.md) |
| Non-Claude agent redirect | [`AGENTS.md`](AGENTS.md) |
| Latest project state | [`AgentReports/StatusUpdate.md`](AgentReports/StatusUpdate.md) (tail) |
| Plan template | [`ImplementationPlans/_TEMPLATE.md`](ImplementationPlans/_TEMPLATE.md) |
| Canonical phase plan example | [`ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md`](ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md) |
| In-flight plan example (paused at pre-flight) | [`ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md`](ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md) |
| Canonical end-of-phase snapshot Report | [`AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md`](AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md) |
| Canonical decision Report | [`AgentReports/Reports/2026-05-15_phase-05-maven-pivot.md`](AgentReports/Reports/2026-05-15_phase-05-maven-pivot.md) |
| Diagnostic Bridge contract (machine-readable) | [`Bindings/_schemas/bridge-command.schema.json`](Bindings/_schemas/bridge-command.schema.json) |
| Per-stack-binding model | [`Bindings/README.md`](Bindings/README.md) |
| Spring Boot binding catalog and install | [`Bindings/SpringBoot/README.md`](Bindings/SpringBoot/README.md) |
| Spring Boot binding MCP shim | [`Bindings/SpringBoot/mcp/README.md`](Bindings/SpringBoot/mcp/README.md) |

For framework rules cited in this README, the section numbers in `CLAUDE.md` are stable enough to cite directly:

| Topic | `CLAUDE.md` section |
|:---|:---|
| Boot protocol | §1 |
| Philosophy (one sentence) | §2 |
| The Loop and Loop-Lock | §3 |
| Hard Rules (full text, all 13) | §4 |
| Project structure (canonical tree + roles) | §5 |
| Implementation Template (6 sections) | §6 |
| DBL Specification | §7 |
| Stack bindings — Spring Boot | §8 |
| Mandatory inputs for new Nissth-bound projects | §9 |
| Reports taxonomy | §10 |
| Diagnostic Bridge contract | §11 |

---

Welcome aboard. The framework is small enough that the rulebook fits in one file, but opinionated enough that following it produces a coherent project ledger over months and across agents. Start at `AgentReports/StatusUpdate.md` and let the Loop carry you.
