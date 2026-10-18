---
artifact_type: schema_index
name: items-schema
last_regenerated: 2026-01-01 by hand
source_state: hand-authored fixture (intentionally stale for stale-flip test)
covers:
  - src/main/java
  - src/main/resources/db/migration
stale_when:
  - Item.java changes structurally
  - new migration adds a column
---

# items schema (intentionally stale)

This artifact is the test target for `entity_lens` STALE-flip behavior (CLAUDE.md §11.4). The column list below is INTENTIONALLY WRONG — `entity_lens` should detect the drift and rewrite `last_regenerated:` to `STALE — superseded by AgentReports/Bridge/entity_lens_<ts>.md`.

## Table: items

| Column      | Type         | Nullable | Notes                           |
|:------------|:-------------|:---------|:--------------------------------|
| id          | BIGINT       | NO       | PK                              |
| name        | VARCHAR(255) | NO       |                                 |
| description | TEXT         | YES      | INTENTIONALLY WRONG — not in entity |

The real `Item` entity has `name` and `qty` (Integer, NOT NULL) — no `description` field. Step 17's `EntityLensIT` copies this artifact into `target/test-classes/DBL/SchemaIndex/items.md` (per the plan's risk-4 mitigation), runs `entity_lens` against the fixture's source, and asserts the copy's frontmatter contains `last_regenerated: STALE`.
