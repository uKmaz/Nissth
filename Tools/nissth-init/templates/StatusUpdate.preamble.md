# {{NAME}} Status Update — Append-Only Log

> **Strictly append-only.** Never edit, reorder, or delete any entry. New entries are appended at the bottom only. There is no editable "Current State" header.
>
> **Boot protocol.** Any agent resuming work reads the LATEST entry first (file tail). The `**Next:**` field of that entry is the agent's first instruction. The `**State:**` block is the current snapshot. Earlier entries are history — read only when explicitly cited.
>
> **Schema.** Every entry conforms to the schema in the comment block below. All fields required; if irrelevant, write "none" or "N/A".
>
> **If a past entry is wrong**, append a new entry that supersedes it. Do not mutate history.

<!--
ENTRY SCHEMA — copy this block when appending. Replace YYYY-MM-DD HH:MM with local time, fill all fields.

### YYYY-MM-DD HH:MM — [Task Name]

**State:**
- Phase: [n/N or descriptor]
- Build: CLEAN | HAS_ERRORS | NOT_RUN
- Tests: PASS | FAIL | NOT_RUN
- Active plan: [path or "none"]
- DBL refs: [files or "none"]
- Blockers: [list or "none"]

**Report:**
- [Pre-flight: what was checked, which DBL artifacts read, which diagnostics run]
- [Key findings that shaped the plan]

**Executed:**
- [Files created/modified with paths; cite line numbers for surgical edits]
- [What was added / changed / removed, one line per item]

**Verified:**
- [Build / test / runtime check + result]
- [Path to verification artifact, if any]
- [Freshness guarantee — how you know the verifier saw the latest changes]
- Doc sync: [updated: A, B; marked stale: C] | none — no source files modified  ← required from 2026-05-05 onward (Hard Rule #11)

**Issues:**
- [Anything unexpected — or "none"]

**Next:**
- [Single specific actionable step for the next agent or session]

---
-->

---
