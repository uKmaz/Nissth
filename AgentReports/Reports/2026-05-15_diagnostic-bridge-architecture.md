---
report_type: decision
title: Diagnostic Bridge architecture — hybrid DBL + live bridge, per-stack bindings
authored: 2026-05-15 by Claude (Opus 4.7)
last_updated: 2026-05-15 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-15 — Framework Hardening: §11 Diagnostic Bridge contract added
related_plans:
  - none yet — Phase_05_Bridge_SpringBoot_FirstSlice.md is the first plan that will consume this decision
covers:
  - Nissth Diagnostic Bridge — runtime layer above raw source
  - Per-stack binding model
  - CLI + MCP invocation surface
  - Action-tool strictness
supersedes:
  - none
---

## Context

The user asked: "make Nissth a software development version of Axiom for backend, frontend, and databases." Axiom (open-sourced as `github.com/umutbrkt/Axiom`) is a Unity 6 diagnostic bridge — a single JSON-command gateway inside the Unity Editor that executes 22 diagnostic + 16 action tools and writes structured Markdown reports to `AgentReports/`. The agent reads reports instead of grepping scene YAML.

Nissth before this decision already encoded Axiom's **rules** (Report→Execute→Verify loop, append-only status log, no silent deviations, freshness guarantees). What it lacked was Axiom's **runtime** — a live executable layer that produces those reports on demand. DBL artifacts under `DBL/` were hand-maintained Markdown; staleness was a discipline rule (Hard Rule #11) rather than a mechanical one. Agents still grepped source for endpoints, entities, migration state.

The decision: how should Nissth acquire Axiom's runtime, given that the substrate is a long-running Unity Editor process and Nissth targets backend/frontend/database stacks with no equivalent single host?

## Options considered

| Option | Shape | Pros | Cons |
|:---|:---|:---|:---|
| **A — Static generators only** | AST-parse source via `javaparser` / `ts-morph` / `pg_dump`; regenerate DBL Markdown into existing `DBL/` layout. No live runtime. | Smallest engineering effort; reuses existing DBL model; cross-platform trivially. | Reports are point-in-time; staleness remains a manual fight; misses Axiom's command-grammar advantage; no way to query "what is the live profile doing?" |
| **B — Live bridge only (replace DBL)** | Per-stack runtime (Spring Boot Actuator + custom controllers, Node helper for Expo, pooled psql client for Postgres) behind a single `nissth-bridge` CLI. DBL is deleted. | Faithful Axiom port; always fresh; structured queries land everywhere. | Loses the durable architectural-intent layer DBL provides (module summaries, decision rationale, "what's allowed where"); breaks every existing Nissth artifact that references DBL; runtime dependency for every query, even ones that don't need live state. |
| **C — Hybrid (recommended)** | DBL stays as the **stable** layer (architectural intent, durable). Add a **live Diagnostic Bridge** for runtime state (live bean graph, migration status, type errors, EXPLAIN plans). Bridge reports auto-flip DBL artifacts to `STALE` on drift. | Keeps DBL's strengths; gets Axiom's runtime advantages; converts HR#11 from discipline to mechanical enforcement; bindings layer scales to new stacks without rewriting the contract. | Two layers to maintain; agent has to know which to query when (mitigated by HR#4 routing table in §11.1); requires per-stack bindings in real languages. |

## Decision

**Option C — Hybrid.**

Architecture:
- **Contract** (stack-agnostic) lives in Nissth core: `CLAUDE.md` §11 + `Bindings/_schemas/bridge-command.schema.json`. Same `{tool, mode, scope, output}` grammar as Axiom.
- **Bindings** (per stack) live at `Bindings/<stack>/` as real subprojects in this repo: `Bindings/SpringBoot/` (Java), `Bindings/Expo/` (TypeScript), `Bindings/Postgres/` (Go/Python — TBD). Each implements the contract; none modify it.
- **Project-local tools** stay in each consuming project's `Tools/` directory for project-specific custom diagnostics.

Invocation: single CLI binary `nissth-bridge` + thin MCP wrapper (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status` — direct port of Axiom's five).

Action tools are **hard-enforce**: they refuse to proceed unless their full enforcement contract is satisfied (e.g., `entity_field_add` writes both the `@Column` edit and the matching Flyway migration; `compile_verify` refuses to return `CLEAN` without a daemon-stop preamble). No warn-and-proceed mode. This is the structural enforcement Nissth has wanted: rules that were soft (§8.6 prose) become hard (exit code 5).

Drift detection runs through the Bridge: when a live tool's output contradicts a DBL artifact whose `covers` overlap the scope, the Bridge writes `last_regenerated: STALE — superseded by <report>` into the DBL artifact's frontmatter. The agent never silently reads a STALE artifact.

First slice (Spring Boot, end-to-end): five tools — `compile_verify`, `endpoint_lens`, `entity_lens`, `migration_status`, `entity_field_add` — to be delivered under `Phase_05_Bridge_SpringBoot_FirstSlice.md` (pending authoring + user approval per HR#12).

## Consequences

**Positive:**
- HR#11 (Document Sync Mandate) is now backed by runtime stale-flipping, not just agent discipline.
- Action-tool strictness collapses several soft prose rules (§8.6 freshness sequence, §8.9 entity/migration ripple) into mechanically-enforced contracts.
- Adding a new stack means adding `Bindings/<NewStack>/` with no changes to the contract — the design scales beyond Spring Boot/Expo/Postgres.
- Example inherits the bridge for free when its bootstrap resumes.

**Negative / risks:**
- Two structured layers (DBL + Bridge) means more routing decisions for the agent. Mitigated by the §11.1 table and the broadened HR#4.
- Bridges need a working stack runtime (compiled project, running app, populated DB). When the precondition is absent, the tool errors — agents must treat that as signal, not as a missing tool.
- Cross-platform shell quirks (Windows vs *nix) will surface in the CLI. Each binding handles its own platform conventions; the contract stays neutral.

**Carry-overs:**
- Example bootstrap (paused since 2026-05-07) stays paused until the Spring Boot binding's first slice is usable. The bootstrap sequence (PDF relocation, SRS/SDD authoring, PostGIS in §8.1, `Phase_00_DBL_Bootstrap.md`) is unaffected and will resume after Phase 05 closes.
- The five-tool first slice is chosen for highest payoff: `compile_verify` and `entity_field_add` each automate a previously-soft Nissth rule, so their landing immediately tightens the framework even before frontend/database bindings arrive.

## Revision history

- 2026-05-15 — Initial decision authored. Architecture chosen (C), first slice scope locked, no further open questions before plan authoring begins.
