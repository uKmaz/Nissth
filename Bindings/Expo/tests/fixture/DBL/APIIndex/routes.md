---
artifact_type: api_index
name: routes
last_regenerated: 2026-05-10 by stub-author
source_state: synthetic-fixture-baseline
covers:
  - app/
stale_when:
  - "any change under app/ that adds, removes, or renames a route file"
  - "any change to Expo Router conventions"
---

# Routes (synthetic, intentionally stale)

This artifact is intentionally pre-populated with routes that do NOT match
the live fixture's actual `app/` contents. It exists to exercise the
StaleFlipper path: when `route_lens` runs against the fixture, it should
detect drift between this documented set and the live filesystem, then
rewrite `last_regenerated` to `STALE — superseded by AgentReports/Bridge/<report>`.

## Routes

| URL path | File | Component | Params | Layout parent | Classification |
|:---|:---|:---|:---|:---|:---|
| `/profile` | `app/profile.tsx` | `ProfileScreen` | `—` | `_layout.tsx` | static |
| `/settings/[id]` | `app/settings/[id].tsx` | `SettingsDetail` | `{ id: string }` | `settings/_layout.tsx` | dynamic |
| `/login` | `app/login.tsx` | `LoginScreen` | `—` | `_layout.tsx` | static |
