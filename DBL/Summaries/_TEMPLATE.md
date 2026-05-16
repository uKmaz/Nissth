---
artifact_type: summary
name: [ModuleName]
last_regenerated: YYYY-MM-DD by [agent name | user]
source_state: <git commit hash | "uncommitted state at YYYY-MM-DD HH:MM">
covers:
  - path/to/module/**
stale_when:
  - any file in `covers` is added, removed, or has its public surface changed
  - the module's external dependencies change
---

# [ModuleName] — Summary

> Token budget: 200–800. If you exceed 1500, split into `<name>-overview.md` + `<name>-details.md`.

## Purpose
[1–2 sentences. What this module exists to do. Not how. Not history. Not roadmap.]

## Public API
| Symbol | Kind | One-line description |
|:---|:---|:---|
| `funcName(args)` | function | what it does, in 8 words or fewer |
| `ClassName` | class | role of the class |
| `CONSTANT_NAME` | const | what it represents |

## Key dependencies
- **[other-module-name]** — [why this module imports it; one line]
- **[external-library]** — [what role]

## Gotchas / Non-obvious behavior
- [A specific footgun, undocumented constraint, or surprising behavior. Include the file:line if there's a witness.]
- [Or write `none documented`.]

## Files in this module
- `path/to/file1.ext` — [role in the module]
- `path/to/file2.ext` — [role in the module]

## Out of scope (what this Summary deliberately does NOT cover)
- [If you split this artifact, name the sibling file. Or write `none — single file covers the module`.]
