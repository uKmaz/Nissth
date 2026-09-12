---
report_type: snapshot
title: Phase 15 — Consumer Init Tooling (`Tools/nissth-init`) — closing snapshot
authored: 2026-09-13 by Claude (Opus 5)
last_updated: 2026-09-13 by Claude (Opus 5)
related_status_entries:
  - 2026-09-13 02:20 — Phase 15: Consumer Init Tooling
related_plans:
  - Phase_15_Consumer_Init_Tooling
covers:
  - Tools/nissth-init/** · Tools/nissth-bridge/consumer-launcher/** · CLAUDE.md §5, §9.1, §11.15 · README.md consumer section
supersedes:
  - none (closes audit F1–F5 of 2026-09-13_finans-consumer-init-friction-audit.md)
---

> **Snapshot Report** per CLAUDE.md §10.4(4). Phase 15 turned §9.1 step 2 from a ten-item manual copy into one refusing, subprocess-free command, and fixed the consumer launcher template that had ignored `NISSTH_FRAMEWORK_ROOT` since 2026-05-23.

## What shipped

| Component | Path | Size |
|:---|:---|:---|
| CLI + library (`plan()`, `apply()`, `parseArgs()`, `frameworkBody()`) | `Tools/nissth-init/init.mjs` | ≈ 290 lines, zero deps |
| Templates | `Tools/nissth-init/templates/` | `CLAUDE.banner.md`, `StatusUpdate.preamble.md`, `Bootstrap.entry.md`, `gitattributes`, `gitignore.{none,expo,spring-boot,postgres}`, `settings.json` + `settings.{none,expo,spring-boot,postgres}.json` (13 files) |
| Tests | `Tools/nissth-init/test.mjs` | 20 cases via `node --test`, temp dirs cleaned in `after` |
| Launcher templates (rewritten) | `Tools/nissth-bridge/consumer-launcher/{nissth-bridge,nissth-bridge.ps1}` | resolution chain env → `Tools/Nissth/` → `DEFAULT_ROOT`; exit 3 lists every path tried |
| Docs | `Tools/nissth-init/README.md`, `consumer-launcher/README.md`, `CLAUDE.md` §5/§9.1/§11.15, `README.md` tree + consumer section | — |

## CLI surface

```
node Tools/nissth-init/init.mjs --target <dir> --name <ProjectName> --stack <expo|spring-boot|postgres|none>
                               [--wiring local|submodule] [--framework-root <abs>] [--dry-run] [--json]
```

Creates 18 files. Exit 0 done · 2 usage/refusal (nothing written) · 3 write failure.

## Refusal table (the contract)

| `error_code` | Trigger | Written |
|:---|:---|:---|
| `already_initialized` | `<target>/AgentReports/StatusUpdate.md` exists | nothing |
| `file_exists` | any planned path exists — all collisions listed | nothing |
| `target_is_framework` | target == framework checkout | nothing |
| `invalid_framework_root` | no `Bindings/` / `CLAUDE.md` / `AGENTS.md` / plan template / dispatcher | nothing |
| `framework_claude_shape` · `framework_agents_shape` · `framework_launcher_shape` | framework files drifted from what the templates rely on | nothing |
| `usage` | bad flags | nothing |

No `--force`. The `apply()` step re-checks with `wx` open flags, so a race between plan and write also aborts.

## Design decisions

| Decision | Alternative rejected | Why |
|:---|:---|:---|
| Copy the framework `CLAUDE.md` **body** verbatim below a project banner, split at the first `---` rule | maintain a separate consumer `CLAUDE.md` template | One source of truth; the banner is the only project-owned text (F3). A framework head without `**Status:**` before the rule is a refusal, so the split cannot silently pick the wrong line. |
| Bake `DEFAULT_ROOT` into the launchers for `--wiring local` | keep launchers generic, require the env var | Matches the UniHub/Finans precedent; the launcher still honours the env var first and exports it so the dispatcher's tier-1 resolution agrees (F1). |
| No subprocesses | offer `--git-init`, `--npm-ci` | Keeps the tool inside §9.1 step 2. `git init` and installs are agent decisions recorded in the ledger. |
| Bootstrap entry lists SRS/SDD presence but not approval | parse the `Approved` rows | Approval wording is project-specific; a false "approved" would be worse than "present". |
| `settings.json` is a narrow allow-list with per-stack additions | copy Nissth's own `settings.local.json` | User feedback: never widen posture on the user's behalf; test asserts no `bypassPermissions`/`defaultMode`. |
| LF normalisation on every write | rely on `.gitattributes` | F5: the *working copy* had CRLF; git attributes fix the index, not what `cp` reads. Test injects a CRLF fake framework root. |

## What remains manual (deliberately)

`git init` · SRS/SDD authoring and approval · `Phase_00_DBL_Bootstrap.md` · consumer framework bumps (diff and copy; the banner survives because it is above the split point).

## Verification

20/20 (init) · 32/32 (dispatcher) · 23/23 (doc-claims) · doc-claims exit 0 · field test: generated project's both launchers list 3 bindings / 5 Expo tools with the env var unset. Fresh-worktree confirmation is recorded in the follow-up status entry.

## Revision history

- 2026-09-13 by Claude (Opus 5) — initial snapshot at Phase 15 close.
