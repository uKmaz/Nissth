# AGENTS.md

This project (**Nissth**) operates under a strict deterministic execution framework. The complete reference is `CLAUDE.md` at the project root.

## Boot Protocol — any agent

Before any other action — before reading code, running tools, browsing directories, or proposing work:

1. Read `AgentReports/StatusUpdate.md`. The **latest entry** (bottom of file) is the current project state.
2. The `**Next:**` field of that entry is your first instruction this session.
3. The `**State:**` block tells you: phase, build/test status, active plan, DBL refs, blockers.
4. If `Active plan` is set, read that plan file next.
5. Read DBL artifacts listed in `DBL refs` — and only those. Do not browse `DBL/` opportunistically.
6. Read `CLAUDE.md` end-to-end — it is the canonical rule set. Other agent-specific instruction files do not exist.

If `StatusUpdate.md` does not exist, this directory is uninitialized; tell the user and stop.

## Core Invariants

- `AgentReports/StatusUpdate.md` is **strictly append-only**. Never edit, reorder, or delete any past entry. New entries are appended at the bottom only.
- Every unit of work follows the loop: **REPORT → EXECUTE → VERIFY → UPDATE STATUS**. Do not execute without a report. Do not verify without an artifact. Do not end without appending a status entry.
- **No Silent Deviations.** If an action isn't covered by the framework — using an unauthorized tool, breaking the loop, inventing a file location — STOP and ask the user. Do not improvise.
- **Plan-before-execute.** No source-modifying execution begins without an approved `ImplementationPlans/Phase_NN_*.md`. Bootstrap, DBL/plan/report authoring, and bridge contract documentation (`Bindings/_schemas/`, binding READMEs) are plan-exempt; product code, configs, schemas, migrations, and binding implementation source under `Bindings/<stack>/src/` are not. (Hard Rule #12.)
- **Permission gate at project initialization.** Before any initialization action on a new Nissth-bound project (inputs, bootstrap, file creation, commands), the agent MUST ask the user for full permission and wait for unambiguous consent. Fires once per project; session resumes use §1 boot protocol instead. (Hard Rule #13 + `CLAUDE.md` §9.1 Step 0.)
- **Document Sync Mandate.** Every task closure runs a sweep over stable docs (DBL, plans, CLAUDE.md examples) and logs the result on the status entry's `Doc sync:` line. (Hard Rule #11.)
- **Reports for anything that details the project.** Long-form decision records, incident reports, design reviews, audits, spec digests, and phase snapshots live under `AgentReports/Reports/YYYY-MM-DD_<slug>.md`. Mandatory in five cases: `Verified: FAIL`, named-alternative architecture decisions, long external spec ingestion, non-trivial phase close, cross-phase pivots. (See `CLAUDE.md` §10.)
- **Diagnostic Bridge before raw source.** Two structured layers sit above raw source: **DBL** for stable architectural intent and the **Diagnostic Bridge** for live runtime state. When a Bridge tool (`nissth-bridge <tool>`) or DBL artifact covers the question, use it — re-reading raw files is a token leak. (Hard Rule #4 + `CLAUDE.md` §11.)
- **Bridge action tools are hard-enforce.** Action tools (e.g., `entity_field_add`) refuse to proceed unless their full enforcement contract is satisfied (e.g., migration emitted atomically with the entity edit). No warn-and-proceed mode exists. (`CLAUDE.md` §11.7.)

For full rules (13 Hard Rules), the Implementation Template, project structure, Reports taxonomy, Diagnostic Bridge contract, and workflow details: see `CLAUDE.md`.
