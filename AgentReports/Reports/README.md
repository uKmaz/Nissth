# AgentReports/Reports/

Long-form companions to `StatusUpdate.md` entries. Anything that **details the project** beyond what fits in a status block.

Authoritative spec: `CLAUDE.md` §10 (Reports Taxonomy).

## Quick reference

- **Filename:** `YYYY-MM-DD_kebab-case-slug.md` — ISO authoring date + slug.
- **Frontmatter:** required, per `CLAUDE.md` §10.3.
- **Kinds:** `decision` · `incident` · `design_review` · `audit` · `spec_digest` · `snapshot` · `verification` · `other`.
- **Linkage:** every Report is referenced from the closing status entry's `Reports:` line; every Report's frontmatter back-references the status entry via `related_status_entries`.

## Mandatory cases (CLAUDE.md §10.4)

A Report is required (not optional) when:

1. A status entry has `Verified: FAIL` → kind `incident`.
2. An architecture decision picks among named alternatives → kind `decision`.
3. A long external spec (PDF, RFC, vendor doc) is ingested → kind `spec_digest`.
4. A non-trivial `Phase_NN_*.md` plan closes → kind `snapshot`.
5. A cross-phase pivot invalidates an earlier plan's premise → kind `decision`.

Outside these cases, authoring a Report is a judgment call — when in doubt, write it.

## What does NOT belong here

Status entries (→ `StatusUpdate.md`) · Plans (→ `ImplementationPlans/`) · DBL artifacts (→ `DBL/`) · User or agent memory · volatile working notes.
