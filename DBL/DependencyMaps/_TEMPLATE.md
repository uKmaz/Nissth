---
artifact_type: dependency_map
name: [ScopeName]
last_regenerated: YYYY-MM-DD by [agent name | user]
source_state: <git commit hash | "uncommitted state at YYYY-MM-DD HH:MM">
covers:
  - path/glob/**
stale_when:
  - any new import is added or removed under `covers`
  - module boundaries are restructured
---

# [ScopeName] — Dependency Map

> Token budget: 200–800. The goal is "which way do dependencies flow?" not "every import line."

## Boundary rules (forbidden imports)
> List MUST-NOT-IMPORT pairs explicitly. These are the architectural invariants — violations should be caught here before they reach review.

- `[ModuleA]` MUST NOT import from `[ModuleB]` — reason: [layering / circular / leak / etc.]
- [or write `no enforced boundaries yet`]

## Module-to-module relationships
| From | To | Direction | Notes |
|:---|:---|:---|:---|
| auth | db | uses | reads `sessions` table |
| api | auth | uses | calls `verify()` on every request |
| db | — | leaf | no outbound module deps |

## Cycle audit
- [List any known cycles, or write `no cycles detected at last regeneration`.]

## ASCII diagram (optional)
```
api ──► auth ──► db
 │       ▲
 └───────┘ (forbidden — see boundary rules)
```

## Out of scope
- [What this map deliberately does NOT cover — e.g., third-party deps, tests-only deps.]
