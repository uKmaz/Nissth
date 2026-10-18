---
artifact_type: schema_index
name: users-fixture
last_regenerated: 2026-05-01 by hand
source_state: synthetic-pre-bridge
covers:
  - public
  - test
tables:
  - public.users
  - public.orders
  - public.legacy_signups
foreign_keys:
  - orders.user_id->users.id
stale_when:
  - any DDL applied to public schema
---

# Users / orders fixture schema (synthetic, intentionally stale)

This DBL artifact is part of the Phase 07 fixture. It documents a `public.legacy_signups`
table that does NOT exist in the live seeded fixture, so `schema_lens` will detect the
drift and STALE-flip this artifact.

## Tables (claimed)

- `public.users` (id, name, email, created_at)
- `public.orders` (id, user_id, amount, placed_at)
- `public.legacy_signups` (id, email, signed_up_at)  ← **drift seed: not in live schema**

## Foreign keys (claimed)

- `orders.user_id -> users.id`

Running `schema_lens` against the live fixture should rewrite this file's frontmatter
`last_regenerated` to `STALE — superseded by AgentReports/Bridge/schema_lens_<ts>.md`.
