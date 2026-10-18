---
artifact_type: schema_index
name: [SchemaName]
last_regenerated: YYYY-MM-DD by [agent name | user]
source_state: <git commit hash | "uncommitted state at YYYY-MM-DD HH:MM">
covers:
  - migrations/**
  - schema.sql | prisma.schema | etc.
stale_when:
  - any migration file added
  - any schema definition file modified
---

# [SchemaName] — Schema Index

> Token budget: 200–800 per database/schema. If schema is large, split per logical group (e.g., `<schema>-auth.md`, `<schema>-billing.md`).

## Tables

### `users`
| Column | Type | Nullable | Default | Notes |
|:---|:---|:---|:---|:---|
| id | uuid | no | `gen_random_uuid()` | PK |
| email | text | no | — | unique |
| password_hash | text | no | — | argon2id |
| created_at | timestamptz | no | `now()` | |
| deleted_at | timestamptz | yes | null | soft-delete sentinel |

**Indexes:** `users_email_idx` on `(email)` unique
**Foreign keys:** none
**Constraints:** `email` matches RFC 5321 (CHECK constraint)

### `sessions`
| Column | Type | Nullable | Default | Notes |
|:---|:---|:---|:---|:---|
| id | uuid | no | `gen_random_uuid()` | PK |
| user_id | uuid | no | — | FK → users.id |
| expires_at | timestamptz | no | — | |

**Indexes:** `sessions_user_id_idx` on `(user_id)`
**Foreign keys:** `user_id` → `users.id` ON DELETE CASCADE

## Relationships
```
users (1) ──< (N) sessions
```

## Migrations baseline
- Latest applied migration: `[migration_id_or_filename]`
- Schema initialized: `YYYY-MM-DD`

## Out of scope
- [Other schemas in the same database, replication-only tables, etc.]
