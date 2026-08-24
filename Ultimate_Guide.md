# Ultimate Guide to Nissth — Efficient Use

> A practical, opinionated guide to using Nissth efficiently. Pair this with `CLAUDE.md` (the reference spec). This document is **how**; CLAUDE.md is **what**.
>
> Audience: anyone adopting Nissth in their own project (human owner OR an AI agent operating in a Nissth-bound repository).
>
> Current state (2026-05-18): **Phases 1–8 closed.** Three Diagnostic Bridge bindings ship (Spring Boot, Expo/React Native, PostgreSQL) + unified cross-binding dispatcher at the repo root. Framework is operational.

---

## Table of contents

1. [The 30-second pitch](#1-the-30-second-pitch)
2. [Mental model — five concepts](#2-mental-model--five-concepts)
3. [When to use Nissth (and when not to)](#3-when-to-use-nissth-and-when-not-to)
4. [Quick start — adopting Nissth in your own project](#4-quick-start--adopting-nissth-in-your-own-project)
5. [Quick start — agent picks up a session](#5-quick-start--agent-picks-up-a-session)
6. [Daily workflows](#6-daily-workflows)
7. [Using the Diagnostic Bridge efficiently](#7-using-the-diagnostic-bridge-efficiently)
8. [Writing plans that don't waste time](#8-writing-plans-that-dont-waste-time)
9. [Writing status entries that age well](#9-writing-status-entries-that-age-well)
10. [Reports — when, when not, and the body shape](#10-reports--when-when-not-and-the-body-shape)
11. [DBL — what to author, what to skip](#11-dbl--what-to-author-what-to-skip)
12. [Stack cheat sheets](#12-stack-cheat-sheets)
13. [Patterns that work / patterns that fail](#13-patterns-that-work--patterns-that-fail)
14. [Troubleshooting](#14-troubleshooting)
15. [The 14 Hard Rules in one table](#15-the-14-hard-rules-in-one-table)
16. [Pointers](#16-pointers)

---

## 1. The 30-second pitch

Nissth turns an AI coding agent from a **search agent over raw source** into a **deterministic executor over pre-computed knowledge**. It does this with four very simple things, all of which live as files in your project root:

| File / directory | What it does |
|:---|:---|
| `CLAUDE.md` | The rule sheet. Auto-loaded by Claude Code at the start of every session. |
| `AgentReports/StatusUpdate.md` | Append-only ledger. The latest entry IS the current project state. |
| `ImplementationPlans/` | Phase plans. Every non-trivial change is preceded by an approved plan. |
| `DBL/` + `AgentReports/Bridge/` | Two layers above source — DBL is curated architectural intent, Bridge is live runtime state. |

Every unit of work follows a four-step Loop: **REPORT → EXECUTE → VERIFY → UPDATE STATUS**. The agent never just edits a file and walks away.

You add Nissth to a project once. After that, every session — whether the same agent, a different agent, or a human — picks up exactly where the previous one left off. Zero context recovery cost.

---

## 2. Mental model — five concepts

If you internalize these five concepts, you can use Nissth efficiently without re-reading CLAUDE.md.

### 2.1 The Loop

```
REPORT  →  EXECUTE  →  VERIFY  →  UPDATE STATUS
```

| Step | Means | Output |
|:---|:---|:---|
| **REPORT** | State current state, target state, planned changes. Cite DBL artifacts + source ranges you used. | A report block (in chat, or §1 of the active plan). |
| **EXECUTE** | Make the changes — only the changes named in REPORT. No opportunistic refactors. | Modified files. |
| **VERIFY** | Build / test / runtime check. Read the actual artifact. | Pass/fail outcome citing the artifact. |
| **UPDATE STATUS** | Append a NEW entry to `AgentReports/StatusUpdate.md`. | Status entry committed. |

The Loop is non-negotiable. The most common violation is "executed without a REPORT" — fixing a bug, noticing a related issue, fixing that too. Don't. One Loop per change.

### 2.2 Two layers above source

| Question shape | Read | Why |
|:---|:---|:---|
| What modules exist? Public API contract? Forbidden imports? | **DBL** (`DBL/`) | Curated architectural intent. Stable across runs. |
| Does this endpoint exist in compiled bytecode right now? | **Bridge** (`./nissth-bridge <tool>`) | Live runtime state. Only the running compiler/classpath knows. |
| Are there pending Flyway migrations? Schema match entities? | **Bridge** | Live state. |
| Why was JPA chosen over JOOQ? | **Reports** (`AgentReports/Reports/`) | Decision rationale, not state. |

**Read the structured layers before reading raw source.** This is Hard Rule #4. Re-reading source when DBL or Bridge already answers the question is a token leak.

### 2.3 Plan-before-execute (HR#12)

```
NO source-modifying execution begins until an
ImplementationPlans/Phase_NN_*.md plan exists, conforms to _TEMPLATE.md,
and has Approved: <ISO date> in §0.
```

Plan-exempt: editing plans themselves, status entries, DBL artifacts, Reports, bridge contract docs.

Plan-required: real product code, configs, schemas, migrations, binding implementations.

### 2.4 Append-only ledger (HR#3)

`AgentReports/StatusUpdate.md` is strictly append-only. **Whole file.** No "current state" header that gets overwritten. New entries at the bottom. If a past entry is wrong, write a new one that supersedes it — never edit the old one.

The **latest entry's `**Next:**` field is the first instruction of the next session.** That's the entire context-recovery mechanism.

### 2.5 Document Sync Mandate (HR#11)

When you modify source files, you must either UPDATE the documents that reference them OR MARK them stale. Logged in the closing status entry's `**Verified:**` block as:

```
Doc sync: [updated: X, Y; marked stale: Z]
```

A status entry without a `Doc sync:` line is a Loop-Lock violation.

---

## 3. When to use Nissth (and when not to)

### Use it when

- You work with an AI coding agent (Claude Code, Cursor, Codex) regularly on the same codebase.
- Your project will grow past ~50 source files.
- You want repeatable, auditable AI-driven changes.
- Multi-session, multi-day work where context recovery matters.
- Multiple agents or multiple humans collaborate on the same project.

### Don't use it for

- One-off scripts.
- Prototypes you'll throw away in a week.
- Codebases where you're the only developer and never use AI coding tools.
- Projects with a very different lifecycle (e.g., research notebooks that change daily — the framework overhead won't pay off).

### The trade-off

You pay overhead: maintain DBL artifacts, write plans for non-trivial changes, append status entries on every task. You get back zero context recovery cost between sessions and a permanent audit trail of every change.

For a one-developer hobby project: skip Nissth. For a serious project with any AI involvement: Nissth pays for itself within a week.

---

## 4. Quick start — adopting Nissth in your own project

### 4.1 The full sequence

This is `CLAUDE.md` §9.1 in narrative form. The agent walks through these once per project, at creation:

**Step 0 — Permission gate.** The agent asks you for explicit consent before doing anything. Required by HR#13. Just say "yes, proceed."

**Step 1 — SRS + SDD.** The agent generates `ImplementationPlans/SRS.md` (Software Requirements Spec) and `ImplementationPlans/SDD.md` (Software Design Document) from your prompt. Review them. STOP and approve.

**Step 2 — Bootstrap (mechanical).** The agent copies the framework files into your project:

```
your-project/
├── CLAUDE.md                       ← the rule sheet
├── AGENTS.md                       ← thin redirect for non-Claude agents
├── ImplementationPlans/
│   ├── _TEMPLATE.md                ← canonical plan skeleton
│   ├── SRS.md
│   └── SDD.md
├── AgentReports/
│   ├── StatusUpdate.md             ← (with schema preamble + first entry)
│   ├── Reports/                    ← long-form decisions, incidents, audits
│   ├── Bridge/                     ← auto-generated Bridge tool reports
│   └── Snapshots/                  ← pre-change rollback artifacts
├── DBL/
│   ├── Summaries/_TEMPLATE.md
│   ├── DependencyMaps/_TEMPLATE.md
│   ├── APIIndex/_TEMPLATE.md
│   └── SchemaIndex/_TEMPLATE.md
├── Tests/
├── Tools/
└── .claude/                        ← Claude Code config
```

Appends a status entry titled "Bootstrap". This is the only execution allowed without an approved plan.

**Step 3 — `Phase_00_DBL_Bootstrap.md`.** First authored plan. Its §3 populates the initial DBL artifacts. You approve it, the agent executes.

**Step 4 — `Phase_01_*.md`.** First real product change. Plan-required.

### 4.2 Common mistakes during adoption

| Mistake | What goes wrong | Fix |
|:---|:---|:---|
| Skipping the permission gate | HR#13 violation; agent has no consent to operate | Agent must STOP and ask explicitly |
| Authoring source before Phase 00 closes | DBL doesn't exist yet → agent has to read raw source forever | Don't skip the bootstrap plan |
| Editing `StatusUpdate.md` past entries | Append-only ledger broken | Write a NEW entry that supersedes |
| Inventing a different layout | Other agents can't find what they need | Use the canonical layout above |

---

## 5. Quick start — agent picks up a session

If you're an agent invoked in a Nissth-bound directory, do this. In order. **Before anything else.**

```
1. Read AgentReports/StatusUpdate.md, latest entry (use Read with offset
   for large files; tail-only is fine).

2. The **Next:** field is your first instruction this session.

3. The **State:** block tells you phase, build/test status, active plan,
   DBL refs, blockers.

4. If "Active plan" is set, read ImplementationPlans/<plan>.md next.

5. Read DBL artifacts in "DBL refs" — and ONLY those. Do not browse DBL/
   opportunistically.
```

This is Hard Rule #1. **Skipping it is a framework violation, regardless of how trivial the user's request seems.**

If `StatusUpdate.md` doesn't exist, the directory is uninitialized. Tell the user and stop.

---

## 6. Daily workflows

Common operations and the efficient Nissth-shaped way to do each.

### 6.1 "Add a new endpoint to my Spring Boot backend"

```
1. Author plan: ImplementationPlans/Phase_NN_AddEndpoint.md (use _TEMPLATE.md).
2. §1 Pre-Flight:
   - Query DBL/APIIndex/<api>.md to confirm the endpoint doesn't already exist.
   - Query DBL/Summaries/<package>.md for the service layer dependencies.
   - Run: ./nissth-bridge endpoint_lens --scope.package com.example.api
     (Bridge confirms the live endpoint set; STALE-flips the DBL artifact if drift exists.)
3. Get plan approved.
4. §3 Execute: write the @RestController + @Service + DTO + test.
5. §4 Verify: ./mvnw clean test (104+ tests).
6. §5 Cleanup: update DBL/APIIndex/<api>.md with the new endpoint row.
7. Append status entry.
```

The **wrong** way: open the IDE, write the controller, write the test, commit. No plan, no DBL update, no status entry. This is a Loop-Lock violation.

### 6.2 "Find a bug a user reported"

```
1. Read AgentReports/StatusUpdate.md latest entry. Is the bug context already there?
2. Read related DBL artifacts. Where does the suspected component live?
3. Run the relevant Bridge tool:
   - Spring Boot: ./nissth-bridge compile_verify, endpoint_lens, entity_lens
   - Expo: ./nissth-bridge route_lens, dependency_audit
   - Postgres: ./nissth-bridge query_plan --mode analyze --scope.extra.sql '...'
4. Now read raw source — but only the file:line ranges the Bridge / DBL pointed at.
5. Once you've found root cause, author a plan (even small bugfixes need one for
   non-trivial changes). Plan §1 includes the Bridge report path you read.
```

### 6.3 "Refactor: rename a service"

This is a multi-file change with ripple effects. Treat it as a real phase:

```
1. Plan §1 Pre-Flight uses dependency_audit / DependencyMaps DBL to find every caller.
2. §3 Execute lists every call site as a checkbox.
3. §5 Cleanup regenerates affected DBL/Summaries/*.md and DBL/DependencyMaps/*.md.
4. Status entry's Doc sync line lists every flipped/updated DBL artifact.
```

### 6.4 "Add a new database column"

This triggers `@Entity` ripple (Spring Boot §8.1.9) or schema-change-ripple (Postgres §8.3.8 is N/A in current slice). Use the binding's action tool:

```
./nissth-bridge entity_field_add --binding spring-boot \
  --scope.extra.entity_class com.example.domain.User \
  --scope.extra.field_name lastLoginAt \
  --scope.extra.field_type 'Instant' \
  --scope.extra.column_type 'timestamptz' \
  --scope.extra.nullable true
```

This atomically:
1. Adds the `@Column` to the entity.
2. Emits a matching `V<N>__add_user_last_login_at.sql` Flyway migration.
3. Refuses (exit 5) if either half fails.

Then update `DBL/SchemaIndex/users.md` to reflect the new column.

### 6.5 "Diagnose a slow query in production"

```
export NISSTH_PG_URL='postgresql://nissth_ro:***@prod-db:5432/app'

# What does the planner think?
./nissth-bridge query_plan --mode analyze \
  --scope.extra.sql 'SELECT ... your slow query ...' \
  --scope.extra.params '[1, "2026-05-01"]'

# Are there indexes that should help but aren't?
./nissth-bridge index_audit --mode usage --scope.package public

# Any sessions blocking each other?
./nissth-bridge lock_audit --mode waiting

# Are pending migrations causing trouble?
./nissth-bridge migration_status --binding postgres
```

Every invocation produces a Markdown report at `AgentReports/Bridge/<tool>_<ts>.md` with mandatory frontmatter including a freshness fingerprint (`pg_control_checkpoint().redo_lsn`). Cite the report path in your plan's §1 Pre-Flight inputs.

### 6.6 "Add a new Expo screen"

Use the `route_scaffold` action tool — atomically creates the route AND its test:

```
./nissth-bridge route_scaffold --binding expo \
  --scope.root_path /path/to/my-app \
  --scope.extra.route_path 'settings/account' \
  --scope.extra.component_name AccountScreen \
  --scope.extra.has_params true \
  --scope.extra.params_type '{ userId: string }'
```

Produces:
- `app/settings/account.tsx` — the route component
- `__tests__/settings/account.test.tsx` — Jest smoke test rendering the component

Refuses (exit 5) if it can only write one of the two. Enforces Expo §8.2.8 route-ripple.

---

## 7. Using the Diagnostic Bridge efficiently

### 7.1 The one canonical entry point

```bash
./nissth-bridge --list-bindings        # expo, postgres, spring-boot
./nissth-bridge --list-tools           # 14 unique tool names
./nissth-bridge --describe <tool>      # full manifest entry for one tool
./nissth-bridge <tool> [flags...]      # invoke
./nissth-bridge --help
```

The repo-root `./nissth-bridge` is the canonical PATH entry. Add the absolute path of the repo root to your shell's PATH, or symlink the launcher into `/usr/local/bin/`.

Per-binding launchers under `Bindings/<stack>/scripts/nissth-bridge` remain as escape hatches for direct binding access — they bypass the dispatcher entirely.

### 7.2 The 14 tools today

| Tool | Binding | Kind | Purpose |
|:---|:---|:---|:---|
| `compile_verify` | spring-boot | diagnostic | `./mvnw clean test` wrap; fresh daemon; CLEAN/HAS_ERRORS report |
| `endpoint_lens` | spring-boot | diagnostic | AST scan of `@*Mapping` annotations; endpoint table |
| `entity_lens` | spring-boot | diagnostic | `@Entity` table: columns, indexes, relationships |
| `entity_field_add` | spring-boot | **action** | Atomic `@Entity` + Flyway migration; refuses partial state (exit 5) |
| `route_lens` | expo | diagnostic | Expo Router `app/` scan; classifies routes; STALE-flips `DBL/APIIndex/*.md` |
| `component_lens` | expo | diagnostic | React component AST scan under `components/` |
| `dependency_audit` | expo | diagnostic | npm dep hygiene + import-scan cross-check |
| `expo_doctor_lens` | expo | diagnostic | Wraps `npx expo-doctor`; PASS/WARN/FAIL findings |
| `route_scaffold` | expo | **action** | Atomic route `.tsx` + test file; refuses partial state (exit 5) |
| `schema_lens` | postgres | diagnostic | Tables, columns, FK graph from `information_schema` |
| `query_plan` | postgres | diagnostic | `EXPLAIN (FORMAT JSON)`; refuses ANALYZE on mutating SQL |
| `index_audit` | postgres | diagnostic | Index usage / unused / duplicate / bloat |
| `lock_audit` | postgres | diagnostic | Live `pg_locks` + `pg_stat_activity` |
| `migration_status` | **spring-boot + postgres** ⚠ | diagnostic | Flyway/Liquibase history rows |

⚠ **`migration_status` is registered by both Spring Boot and PostgreSQL bindings.** Without `--binding <stack>` the dispatcher refuses (exit 2). Use:

```bash
./nissth-bridge migration_status --binding spring-boot --scope.root_path /path/to/spring-app
./nissth-bridge migration_status --binding postgres                    # uses NISSTH_PG_URL
```

### 7.3 The freshness / stale-flip mechanism

Bridge tools auto-flip stale DBL artifacts. When `schema_lens` runs and the live schema diverges from `DBL/SchemaIndex/users.md`, the artifact's frontmatter gets rewritten:

```yaml
last_regenerated: STALE — superseded by AgentReports/Bridge/schema_lens_2026-05-18T1430Z.md
```

The agent treats any `STALE` DBL artifact as not-readable until regenerated. **You don't need to remember to flag staleness — the Bridge does it.**

To clear a STALE marker: re-author the DBL artifact (set `last_regenerated: YYYY-MM-DD by <author>`).

### 7.4 Action tool contracts — hard-enforce

Action tools (`entity_field_add`, `route_scaffold`) are subject to a strict contract: **the tool refuses to proceed unless its enforcement contract is satisfied.** No "warn and proceed" mode.

Example: `entity_field_add` atomically writes both the `@Column` annotation AND the Flyway migration file. If the migration write fails after the annotation write, the annotation gets rolled back. Exit code 5 with `hard-enforce` error code.

This converts soft rules (`should run --stop first`) into hard rules (`exits 5 if --stop wasn't run`). **Trust the action tools.**

### 7.5 The Postgres binding's password redaction

The Postgres binding ships `tests/contract/SecretRedaction.test.ts` — a load-bearing security contract. The connection-string password is grep-asserted absent from every output channel: produced reports, stdout, stderr, error messages, stack traces.

Set the connection string once:

```bash
export NISSTH_PG_URL='postgresql://nissth_ro:hunter2@db.example.com:5432/mydb?sslmode=require'
```

Or override per-call:

```bash
./nissth-bridge schema_lens --scope.extra.connection_string 'postgresql://...'
```

Every report records the database identity as `postgresql://<user>@<host>:<port>/<dbname>` — never with the password. The `freshness.source_state` cites the `pg_control_checkpoint().redo_lsn` so re-runs reveal cache staleness.

### 7.6 Discovery cheat sheet

| Question | Bridge tool |
|:---|:---|
| What's slow in production? | `query_plan --mode analyze` against the slow SQL |
| What's blocking what? | `lock_audit --mode waiting` |
| Is the schema what I think? | `schema_lens --mode full --scope.package public` |
| Are my migrations applied? | `migration_status --mode auto --binding postgres` (or `--binding spring-boot`) |
| Did my Spring Boot build break? | `compile_verify` |
| What endpoints does my API have right now? | `endpoint_lens --scope.package com.example.api` |
| What routes does my Expo app expose? | `route_lens --scope.root_path .` |
| Is `expo-doctor` happy? | `expo_doctor_lens --scope.root_path .` |
| Are there unused npm deps? | `dependency_audit --scope.root_path .` |
| What components live under `components/`? | `component_lens --scope.root_path .` |
| Which indexes are unused? | `index_audit --mode unused --scope.package public` |
| Add a column atomically? | `entity_field_add --binding spring-boot ...` |
| Add an Expo screen atomically? | `route_scaffold --binding expo ...` |

---

## 8. Writing plans that don't waste time

### 8.1 The template enforces the Loop

`ImplementationPlans/_TEMPLATE.md` has six sections:

| § | Section | Maps to Loop step |
|:---|:---|:---|
| 0 | Metadata (Plan ID, Authored, Approved, Depends on, Scope) | — |
| 1 | Pre-Flight Diagnostic (Inputs · actions · findings table) | REPORT |
| 2 | Expected State (Before / After tables) | REPORT |
| 3 | Execution (step list · forbidden list) | EXECUTE |
| 4 | Post-Flight Verification (freshness · checks · pass criteria · failure handling) | VERIFY |
| 5 | Cleanup | — |
| 6 | Status Update Entry (paste-ready) | UPDATE STATUS |

All six are required. If a section is irrelevant, write `N/A — [reason]` — don't delete it.

### 8.2 What makes a plan efficient

**Plans live or die on §1 Pre-Flight and §3.2 Forbidden.**

- **§1 Pre-Flight Diagnostic** is what stops you from authoring a plan against stale state. Every "Action" row predicts an answer. If the actual answer is "no", STOP — your plan was built on wrong assumptions.
- **§3.2 Forbidden in this phase** prevents scope creep. List explicitly out-of-scope changes you might be tempted to bundle in.

A well-authored plan looks like:

```markdown
### 1.3 Findings

| Question | Expected | Actual | Match? |
|:---|:---|:---|:---|
| Does endpoint X exist? | no | no — confirmed via endpoint_lens report ABC | ✅ yes |
| Is the user-service interface stable? | yes | yes — DBL/Summaries/user-service.md shows v0.1.0 | ✅ yes |
```

If any row says `Match? = no`, stop and re-plan.

### 8.3 Plan sizes — when to be detailed, when to be thin

| Plan kind | Lines | Example |
|:---|:---|:---|
| Trivial (one-file fix, no ripple) | 80–120 | Bugfix in a single utility function |
| Medium (single subsystem, multi-file) | 150–250 | New endpoint + DTO + tests |
| Phase-scale (cross-cutting feature) | 250–400 | New binding first slice (Phases 05/06/07) |
| Framework hardening | 200–300 | Phase 08 unified dispatcher |

Don't over-engineer thin plans. Don't under-engineer phase-scale plans.

### 8.4 Get approval before executing

A plan is `Approved: pending` until the user fills in `Approved: <ISO date>`. **The executing agent does not start §3 on an unapproved plan.** HR#12.

Approval can be terse — "I liked your plan. Execute." is fine. Just unambiguous consent.

### 8.5 Plan-exempt work

You DO NOT need a plan for:

- Authoring or revising plans themselves
- Authoring or revising DBL artifacts
- Authoring status entries
- Authoring Reports
- Editing bridge contract documentation (`Bindings/_schemas/`, `Bindings/README.md`, `CLAUDE.md` §11)

You DO need a plan for:

- Real product code
- Configs
- Schemas / migrations
- Binding implementation source (`Bindings/<stack>/src/`)

---

## 9. Writing status entries that age well

### 9.1 The status entry shape

```markdown
### YYYY-MM-DD HH:MM — <Phase>: <Task Name>

**State:**
- Phase: <n>/<N>
- Build: CLEAN | HAS_ERRORS
- Tests: PASS | FAIL
- Active plan: <path>
- DBL refs: <files>
- Bridge reports: <files>
- Blockers: <or "none">

**Report:**
- <condensed §1 findings>

**Executed:**
- <condensed §3, with checkboxes resolved>

**Verified:**
- <condensed §4 results, including freshness statement>
- Doc sync: [updated: ...; marked stale: ...] | none — no source files modified
- Reports: [AgentReports/Reports/<filename> (kind), ...] | none — no Report-worthy artifacts

**Issues:**
- <or "none"; if Verified: FAIL, cite the matching incident Report filename>

**Next:**
- <the single next concrete step — not a list>
```

### 9.2 The two non-negotiables

- **`Doc sync:` line in every Verified: block.** A status entry without it is a Loop-Lock violation (HR#11).
- **`Next:` is ONE actionable step.** Not a list, not a wishlist. HR#7.

### 9.3 What goes IN, what stays OUT

| In a status entry | NOT in a status entry |
|:---|:---|
| What was done, what changed, what was verified | How code works (that's the code) |
| Build/test pass/fail with the exact metric | Implementation walkthrough |
| Blockers and decisions made | Architectural philosophy (that's Reports) |
| Doc sync list | Long incident narratives (spin off a Report) |
| Single next step | Roadmap (that's Reports / SRS / SDD) |

If `**Issues:**` or `**Report:**` is starting to swell past ~10 lines, spin it off into a Report under `AgentReports/Reports/` and have the status entry link to it.

### 9.4 Common status-entry anti-patterns

- **The "we'll figure out Doc sync later" line.** Don't. Loop-Lock violation.
- **The list-of-five-things `**Next:**`.** Pick one. The others go in Reports or are picked up in the next plan.
- **Editing yesterday's entry to "clarify" something.** Append-only. Write a new entry that supersedes.
- **Skipping the `**Verified:**` block because "tests passed obviously."** "Obviously" doesn't count — cite the artifact.

---

## 10. Reports — when, when not, and the body shape

Reports are long-form companions to status entries. They live under `AgentReports/Reports/` and follow the naming convention `YYYY-MM-DD_kebab-case-slug.md`.

### 10.1 Mandatory triggers

Author a Report when ANY of these fires:

| Trigger | Report kind |
|:---|:---|
| `Verified: FAIL` | `incident` — root cause, remediation options, failing artifact contents |
| Architecture decision between named alternatives | `decision` — context, options matrix, decision, consequences |
| Long external spec ingested (PDF, RFC, vendor doc) | `spec_digest` |
| Closing a non-trivial phase | `snapshot` |
| Cross-phase pivot that invalidates an earlier plan's premise | `decision` |

### 10.2 Optional triggers

- The status entry's `**Issues:**` block is approaching ~10 lines.
- An ad-hoc analysis (perf sweep, dependency audit, security review) produced findings worth preserving.
- You answered a recurring question with a non-obvious framework — write it down so future-you doesn't re-derive it.

### 10.3 The body shape

For `decision` Reports:
```
## Context
## Options considered (table)
## Decision
## Consequences
## Revision history
```

For `incident` Reports:
```
## Summary
## Timeline
## Root cause
## Remediation
## Follow-ups
## Revision history
```

For `snapshot` and other kinds, structure as makes sense. Dense, scannable, ~500–3000 tokens.

### 10.4 Mandatory frontmatter

```yaml
---
report_type: decision | incident | design_review | audit | spec_digest | snapshot | verification | other
title: <human-readable title>
authored: YYYY-MM-DD by [agent or user]
last_updated: YYYY-MM-DD by [agent or user]
related_status_entries:
  - <YYYY-MM-DD HH:MM — Status Entry Title>
related_plans:
  - <Phase_NN_Slug | none>
covers:
  - <subsystem | module | concern>
supersedes:
  - <prior report filename | none>
---
```

### 10.5 Reports vs other artifacts

| Question | Goes to |
|:---|:---|
| What changed and when? | Status entry |
| Why did we pick X over Y? | Report (`decision`) |
| What's the API surface of this module? | DBL (`Summaries/`, `APIIndex/`) |
| What's the live schema right now? | Bridge (`schema_lens` report) |
| What are the user's preferences? | Agent memory (not in repo) |

---

## 11. DBL — what to author, what to skip

### 11.1 The four artifact types

| Type | Directory | One file per | Answers |
|:---|:---|:---|:---|
| Summary | `DBL/Summaries/` | Module / component | What does this module do? Public API? Gotchas? |
| Dependency Map | `DBL/DependencyMaps/` | Boundary or scope | What imports what? Forbidden directions? |
| API Index | `DBL/APIIndex/` | API namespace | What endpoints/methods exist? Signatures? Auth? |
| Schema Index | `DBL/SchemaIndex/` | Database / schema | Tables, columns, indexes, foreign keys |

### 11.2 First-population priority

When a project starts:

1. One `Summary/` per top-level module.
2. One `DependencyMap/` for the primary architectural boundary.
3. `APIIndex/` artifacts when an HTTP/RPC surface exists.
4. `SchemaIndex/` artifacts when a database is in play.

A project with no DBL is not yet a Nissth project — it's a candidate for Phase 0 (DBL bootstrap).

### 11.3 Mandatory frontmatter

```yaml
---
artifact_type: summary | dependency_map | api_index | schema_index
name: <human-readable name>
last_regenerated: YYYY-MM-DD by [agent | user]
source_state: <git commit hash, OR "uncommitted state at YYYY-MM-DD HH:MM">
covers:
  - <glob pattern or explicit path>
stale_when:
  - <condition that invalidates this artifact>
---
```

### 11.4 Sizing

- **200–800 tokens per artifact.** If a single artifact exceeds ~1500 tokens, split it.
- **One artifact per file.** No bundling.
- **Kebab-case filenames** (`auth-module.md`, `user-service-api.md`).

### 11.5 What does NOT belong in DBL

- Implementation details that change every refactor (DBL would churn).
- Volatile state ("current open issues", "active blockers") → `StatusUpdate.md`.
- User preferences → agent memory.
- Phase plans, in-flight decisions → `ImplementationPlans/`.
- Decisions and incident write-ups → `AgentReports/Reports/`.

### 11.6 The freshness check

Before citing a DBL artifact in your plan's §1 Pre-Flight:

```
1. Read its frontmatter only — not the body.
2. Compare source_state to current state. If the project has changed
   beneath any path in `covers`, treat the artifact as STALE.
3. If STALE: state this in your Report. Do NOT fabricate from the
   stale artifact. Either re-read the source directly, or make DBL
   regeneration the first step of your plan.
4. After regenerating, update last_regenerated + source_state.
```

The Bridge tools automate this — when they detect drift, they flip the artifact's frontmatter to `STALE`. You don't have to remember.

---

## 12. Stack cheat sheets

Three bindings ship today. Each has its own §8.x in CLAUDE.md with the full stack rules. These are TL;DR versions.

### 12.1 Spring Boot — `CLAUDE.md` §8.1

**Verify protocol (the freshness sequence):**

```bash
./gradlew --stop                        # kill the stale daemon
./gradlew clean build                   # full clean rebuild
./gradlew flywayValidate                # migrations validated
./gradlew test                          # against Testcontainers PostgreSQL
# Read build/reports/tests/test/index.html, NOT stdout
```

Or via the binding:

```bash
./nissth-bridge compile_verify --scope.root_path /path/to/my-spring-app
```

**Forbidden patterns (the load-bearing ones):**

- No raw shell search for `@*Mapping` / `@Entity` — query DBL or `endpoint_lens` / `entity_lens` first.
- No `gradle test` without `clean` — daemon cache lies.
- No `spring.jpa.hibernate.ddl-auto=update` outside `dev` profile — Flyway owns schema.
- No raw `JdbcTemplate` for CRUD when a `JpaRepository` covers the entity (without explicit justification).
- No N+1 queries — use `@EntityGraph` or JPQL `JOIN FETCH`.
- No `@Transactional` on repositories — boundaries belong on `@Service`.

**Entity ripple (HR#11 specialization):**

Any `@Entity` change triggers updates to BOTH:
1. `DBL/SchemaIndex/<cluster>.md`
2. A new `V<N>__*.sql` Flyway migration

Both MUST appear in the closing status entry's `Doc sync:` line.

### 12.2 Expo — `CLAUDE.md` §8.2

**Verify protocol:**

```bash
npm run clean       # rimraf dist .tsbuildinfo node_modules/.cache
npm ci              # lockfile-driven install
npx tsc --noEmit    # type check
npm test            # Jest
npx expo-doctor     # project health
```

Or via the binding:

```bash
./nissth-bridge expo_doctor_lens --scope.root_path /path/to/my-expo-app
```

**Forbidden patterns:**

- No `npm install --legacy-peer-deps` without one-line justification.
- No committed `node_modules/`, `.expo/`, `dist/`, `coverage/`.
- No untyped routes — every screen with params declares `Params` type.
- No `expo-cli` (deprecated 2024). Use `npx expo`.
- No `@react-navigation/*` for top-level navigation — Expo Router supersedes.
- No `console.log` in production code paths.
- No skipping `npx expo-doctor` after dependency changes.

**Route ripple (HR#11 specialization):**

Any new screen route triggers updates to BOTH:
1. `DBL/APIIndex/routes.md`
2. A matching `__tests__/<same-path>.test.tsx`

The `route_scaffold` action tool enforces this atomically.

### 12.3 PostgreSQL — `CLAUDE.md` §8.3

**Connection:**

```bash
export NISSTH_PG_URL='postgresql://nissth_ro:hunter2@db.example.com:5432/mydb?sslmode=require'
# Or per-call: --scope.extra.connection_string '...'
```

**Verify protocol — freshness guarantee:**

- One PG connection per tool invocation, closed in `finally`. No pool.
- `pg_control_checkpoint().redo_lsn` captured at run start and cited in `freshness.source_state`.
- `query_plan` always uses `EXPLAIN (FORMAT JSON)`.
- Statement timeout 30000ms default (overridable).

**Forbidden patterns:**

- No DDL via diagnostic tools — they're read-only.
- `query_plan` refuses `analyze` / `buffers` on mutating SQL.
- No `pg_terminate_backend`, `pg_cancel_backend`, `pg_advisory_lock`.
- **Never log the connection-string password.** Anywhere. Reviewed by `SecretRedaction.test.ts` on every CI run.
- No persistent connection pool across tool invocations.
- No queries against the user's own tables — only `information_schema.*`, `pg_catalog.*`, `pg_stat_*` views.

**Required roles:**

| Tool | Minimum role |
|:---|:---|
| `schema_lens`, `migration_status` | Any role with SELECT on `information_schema` |
| `query_plan` | SELECT on targeted tables |
| `index_audit` | `pg_monitor` for full pg_stat_user_indexes visibility |
| `lock_audit` | `pg_read_all_stats` for cross-session visibility |

Read-only role with `pg_monitor` + `pg_read_all_stats` covers everything.

---

## 13. Patterns that work / patterns that fail

Lessons from Phases 01–08. These are the patterns that have repeatedly paid off (or burned us).

### 13.1 Patterns that work

**1. "Bridge first, source last."** Every plan starts with a Bridge tool invocation (or DBL read), not a `grep -r`. Saves 80% of the token budget.

**2. "Plan §1 is half the battle."** A precise Pre-Flight Diagnostic with predicted-vs-actual answers prevents 90% of mid-execution surprises. The other 10% you can't predict — those become Reports.

**3. "One Loop per change."** Resist the urge to bundle "and also clean up X." Loop discipline keeps status entries readable and git history clean.

**4. "Append-only is a feature, not a bug."** The temptation to edit yesterday's status entry to "clarify" something is strong. Don't. Write a new entry that supersedes. Future agents reading top-to-bottom see the timeline.

**5. "Hard-enforce action tools."** When you build an action tool, give it a hard contract (exit 5 on partial failure). Soft "warn and proceed" modes turn into silent breakage three months later.

**6. "STALE is mechanical, not memory."** When a Bridge tool flips a DBL artifact to STALE, regenerate it in the same plan (or queue it in the next plan's §1 Inputs). Don't rely on remembering.

**7. "Cite the artifact, not the intent."** "Tests passed" is intent. `Tests run: 104, Failures: 0; report at build/reports/tests/test/index.html` is artifact. Verify against artifacts.

**8. "Reports are cheap, decisions are expensive."** A 500-token decision Report saves you from re-litigating the same choice six months later. Author Reports for any non-obvious decision.

### 13.2 Patterns that fail

**1. "Just a quick fix, no plan."** Multi-file changes without a plan ALWAYS produce missed ripple effects. The "quick" fix takes longer than the plan would have.

**2. "I'll update DBL after I commit."** You won't. DBL drift starts the moment you skip an update. Either UPDATE in the same task or MARK STALE — those are the only two options.

**3. "The build was green yesterday."** Daemon caches lie. JIT caches lie. Stale tsbuildinfo lies. **Always run a clean verify** when authoring `Verified:` lines.

**4. "Let's bundle Postgres + Spring Boot changes in one phase."** Bindings should be modified independently. A Phase 09 covering two bindings is two plans pretending to be one.

**5. "Skip the snapshot Report — it's a small phase."** Small phases close-of-phase Reports are easy to write and immensely valuable when reading the project six months later. Write them.

**6. "Tool name conflicts will sort themselves out."** They won't. The first cross-binding conflict (`migration_status` between SpringBoot and Postgres) was real. The conflict-resolution policy (`--binding <stack>`) is the answer — design for it from day one in new bindings.

**7. "Memory will remember this."** It won't, reliably. If a non-obvious decision matters, write a Report. If a recurring pattern matters, write it into CLAUDE.md (with a plan).

---

## 14. Troubleshooting

### "The agent isn't following the framework"

- Check that `CLAUDE.md` exists at the project root.
- Check that the agent read `AgentReports/StatusUpdate.md` first (HR#1).
- If the agent's first action skipped the boot protocol, restart the session and re-prompt.

### "The DBL artifact says STALE — how do I clear it?"

Either:
- Re-author the artifact body to match current state, then set `last_regenerated: <today> by <author>` in frontmatter.
- Run the relevant Bridge tool against the affected scope — modern tools regenerate the DBL artifact as part of their run (currently: future enhancement; this slice only flips to STALE).

### "Two bindings register the same tool name"

Use `--binding <stack>` to disambiguate:

```bash
./nissth-bridge migration_status --binding spring-boot
./nissth-bridge migration_status --binding postgres
```

Documented in `Tools/nissth-bridge/README.md` and `CLAUDE.md` §11.15.

### "The Bridge says my CLI isn't built"

```bash
cd Bindings/Postgres && npm run build
cd Bindings/Expo && npm run build
cd Bindings/SpringBoot && ./mvnw clean package
```

The dispatcher reads the binding's `cli_entry.path` and refuses to spawn if the artifact is missing.

### "`./nissth-bridge` says 'No bindings found'"

The dispatcher couldn't find any `Bindings/*/*.bridge.json`. Check that:

1. You're running from the repo root.
2. `Bindings/<stack>/<stack>.bridge.json` files exist.
3. Each manifest has a `cli_entry: {runtime, path}` object.

### "Tests passed but the build didn't actually rebuild"

The TypeScript incremental-build trap: `tsc --incremental` skips emit when `.tsbuildinfo` thinks the build is current, even if you deleted `dist/`. Fix:

```bash
npm run clean       # removes dist, .tsbuildinfo, node_modules/.cache
npm ci              # clean install
npm run build       # fresh tsc
```

For Spring Boot: `./gradlew --stop` before `clean build`. Daemon caches compiled classes.

### "Plan §1.3 Findings has a 'no' row — what do I do?"

STOP. Do not proceed to §3. Append a `Verified: FAIL` status entry citing which row failed. Either:
- Re-plan with the corrected starting state.
- Investigate why the predicted answer was wrong (often: stale DBL, missing migration, drift since last session).

### "The status entry I want to write is 50 lines long"

Spin off a Report. Status entries should be dense. Anything past ~10 lines in `**Report:**`, `**Issues:**`, or `**Verified:**` is a Report waiting to happen. Link the Report from the status entry.

### "My binding's action tool is refusing to run"

That's the hard-enforce contract working. Action tools refuse partial state. Check the error message — it cites the precondition that wasn't met. Fix the precondition or escalate to the user. **Do not bypass the contract.**

---

## 15. The 14 Hard Rules in one table

These are the framework's non-negotiables. Full text in `CLAUDE.md` §4.

| # | Rule | One-liner |
|:---|:---|:---|
| 1 | Read StatusUpdate.md first | Boot protocol; latest entry IS current state |
| 2 | No silent deviations | If about to break framework, STOP and tell the user |
| 3 | StatusUpdate.md strictly append-only | Whole file. New entries at bottom only. |
| 4 | Query structured layers before source | DBL + Bridge first, raw source last |
| 5 | Scope every read | Target file + line range; no full-tree dumps |
| 6 | Verify against the artifact | Build/test output is authoritative; plan is contract |
| 7 | One actionable Next | Single concrete step, not a list |
| 8 | Convert relative dates | "today" → ISO when writing to persistent files |
| 9 | Snapshot before destructive work | Rollback artifact under `AgentReports/Snapshots/` |
| 10 | Respect verifier freshness | State the freshness guarantee in Verify line |
| 11 | Document Sync Mandate | UPDATE or MARK STALE; log in `Doc sync:` line |
| 12 | Plan-before-execute | No source change without an Approved plan |
| 13 | Permission gate at project init | Explicit consent before first action on a new project |
| 14 | (reserved) | — |

(HR#14 not yet allocated; the framework reserves space for future rules without renumbering.)

---

## 16. Pointers

| Need | Read |
|:---|:---|
| Full framework spec | `CLAUDE.md` |
| Plan template | `ImplementationPlans/_TEMPLATE.md` |
| Current state of the project | `AgentReports/StatusUpdate.md` (latest entry) |
| Diagnostic Bridge contract | `CLAUDE.md` §11 + `Bindings/_schemas/bridge-command.schema.json` |
| Unified dispatcher | `Tools/nissth-bridge/README.md` |
| Spring Boot binding | `Bindings/SpringBoot/README.md` + `CLAUDE.md` §8.1 |
| Expo binding | `Bindings/Expo/README.md` + `CLAUDE.md` §8.2 |
| PostgreSQL binding | `Bindings/Postgres/README.md` + `CLAUDE.md` §8.3 |
| Phase 05 close (SpringBoot first slice) | `ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md` + snapshot Report |
| Phase 06 close (Expo first slice) | `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` + snapshot Report |
| Phase 07 close (Postgres first slice) | `ImplementationPlans/Phase_07_Bridge_Postgres_FirstSlice.md` + snapshot Report |
| Phase 08 close (unified dispatcher) | `ImplementationPlans/Phase_08_Unified_Bridge_Dispatcher.md` + snapshot Report |
| All Reports | `AgentReports/Reports/` |

---

## Appendix A: A complete worked example — adding a new endpoint

This appendix shows the full Loop for a single representative task. Use it as a template.

### A.1 The task

A Spring Boot project. User asks: "Add a new endpoint `GET /api/users/{id}/orders` that returns a user's order history."

### A.2 Boot

```
Read AgentReports/StatusUpdate.md, latest entry.
Latest entry's **Next:** = "Resume backlog item 2 (user orders endpoint)."
DBL refs include: DBL/APIIndex/users-api.md, DBL/Summaries/order-service.md.
Active plan: none. ← We need to author one.
```

### A.3 Pre-flight Bridge invocation

```bash
./nissth-bridge endpoint_lens --scope.package com.example.api.users
# Produces AgentReports/Bridge/endpoint_lens_2026-05-19T0930Z.md
```

The report confirms: `GET /api/users/{id}` exists; `GET /api/users/{id}/orders` does NOT. DBL/APIIndex/users-api.md matches the live state (no STALE flip).

### A.4 Author the plan

`ImplementationPlans/Phase_NN_AddUserOrdersEndpoint.md` per `_TEMPLATE.md`:

```markdown
## 0. Metadata
- Plan ID: Phase_NN_AddUserOrdersEndpoint
- Authored: 2026-05-19 by Claude
- Approved: pending
- Depends on: Phase_MM_<prior>
- Estimated scope: Add UserOrdersController + service method + DTO + integration test. No schema change.

## 1. Pre-Flight Diagnostic
### 1.1 Inputs
- DBL: DBL/APIIndex/users-api.md, DBL/Summaries/order-service.md
- Bridge reports: AgentReports/Bridge/endpoint_lens_2026-05-19T0930Z.md (freshness 2026-05-19T09:30Z; CLEAN)
- Source: src/main/java/com/example/api/users/UsersController.java:1-80
         src/main/java/com/example/service/OrderService.java:1-50

### 1.2 Diagnostic actions
| # | Action | Tool | Why |
|:---|:---|:---|:---|
| 1 | Confirm endpoint absent | endpoint_lens | Avoid duplicate route |
| 2 | Confirm OrderService.findByUserId exists | DBL Summary read | We don't want to add a service method we already have |
| 3 | Confirm tests pass on master | ./mvnw clean test | Regression baseline |

### 1.3 Findings
| Question | Expected | Actual | Match? |
|:---|:---|:---|:---|
| GET /api/users/{id}/orders absent? | yes | yes ✓ | ✅ |
| OrderService.findByUserId(Long) exists? | yes (signature `List<Order>`) | yes ✓ | ✅ |
| Master tests pass? | 104/104 | 104/104 ✓ | ✅ |

## 2. Expected State
### Before
| Target | Property | Value |
|:---|:---|:---|
| UsersController | endpoint count | 4 |
| UserOrdersDto | exists | no |

### After
| Target | Property | Value |
|:---|:---|:---|
| UsersController | endpoint count | 5 |
| UserOrdersDto | exists | yes (id, placedAt, amount, status) |
| Tests | count | 105 (1 added) |

## 3. Execution
- [ ] Step 1. Add UserOrdersDto under src/main/java/com/example/api/users/dto/. Fields: id, placedAt, amount, status.
- [ ] Step 2. Add UsersController.getUserOrders method (GET /api/users/{id}/orders). Returns List<UserOrdersDto>.
- [ ] Step 3. Add UsersControllerIT.getUserOrders_returnsOrders integration test. Use Testcontainers PG fixture.
- [ ] Step 4. Update DBL/APIIndex/users-api.md with the new endpoint row.

### 3.2 Forbidden
- No changes to OrderService (we use its existing method).
- No changes to Order entity or schema.
- No refactor of UsersController despite the temptation to extract a base class.

## 4. Post-Flight Verification
### 4.1 Freshness
- ./gradlew --stop; ./gradlew clean test
- Read build/reports/tests/test/index.html after test run

### 4.2 Checks
- [ ] Build CLEAN
- [ ] Tests 105/105 PASS (including new IT)
- [ ] Bridge re-query: endpoint_lens shows 5 endpoints under com.example.api.users
- [ ] DBL freshness: DBL/APIIndex/users-api.md regenerated in §3 Step 4

## 5. Cleanup
- [ ] No scratch files
- [ ] DBL/APIIndex/users-api.md updated
- [ ] Reports: none (small phase, no §10.4 trigger)
- [ ] Doc sync: [updated: DBL/APIIndex/users-api.md (+1 endpoint row); created: UserOrdersDto.java + UsersControllerIT.getUserOrders_*; marked stale: none]

## 6. Status Update Entry
(paste-ready)
```

### A.5 Get approval

User: "approved."

### A.6 Execute

Implement Steps 1-4. Tick each checkbox as done.

### A.7 Verify

```bash
cd /path/to/spring-app
./gradlew --stop && ./gradlew clean test
# 105 tests, 0 failures, build/reports/tests/test/index.html confirms

./nissth-bridge endpoint_lens --scope.package com.example.api.users
# Report shows 5 endpoints, no STALE flip
```

### A.8 Append status entry

```markdown
### 2026-05-19 11:42 — Phase NN: Add user orders endpoint — CLOSED

**State:**
- Phase: NN/N+1
- Build: CLEAN
- Tests: PASS (105/105)
- Active plan: ImplementationPlans/Phase_NN_AddUserOrdersEndpoint.md
- DBL refs: DBL/APIIndex/users-api.md
- Bridge reports: AgentReports/Bridge/endpoint_lens_2026-05-19T1140Z.md
- Blockers: none

**Report:** All §1 findings ✅. Added UserOrdersDto + UsersController.getUserOrders + integration test.

**Executed:** All 4 §3 steps.

**Verified:**
- Build CLEAN via clean test from a stopped daemon at 2026-05-19T11:40+03:00.
- Tests 105/105 PASS; report at build/reports/tests/test/index.html.
- endpoint_lens re-run shows 5 endpoints; DBL/APIIndex/users-api.md not STALE-flipped (DBL update preceded the Bridge query).
- Doc sync: [updated: DBL/APIIndex/users-api.md (+row for GET /api/users/{id}/orders); created: UserOrdersDto.java, UsersControllerIT.getUserOrders_returnsOrders; marked stale: none]
- Reports: none — small phase, no §10.4 trigger.

**Issues:** none.

**Next:** Backlog item 3 (configurable page size on /api/users) — author plan when ready.
```

### A.9 What happened

In ~15 minutes you authored an endpoint, an integration test, and a fully-auditable trail (plan + status entry). Six months from now, someone reading top-to-bottom can reconstruct exactly what happened and why.

That's Nissth.

---

*Document version: 1.0 — 2026-05-18. Update when CLAUDE.md changes substantially.*
