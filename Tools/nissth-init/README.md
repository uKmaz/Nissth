# nissth-init — consumer-project bootstrap

Mechanical implementation of `CLAUDE.md` §9.1 **step 2**. Creates the Nissth
control-file skeleton in a target directory from templates. Zero runtime
dependencies, Node 20+.

## Why this exists

§9.1 step 2 was a ten-item copy list executed by hand. The second real consumer
install (FinansYönetimApp, 2026-09-13) reproduced every paper cut of the first
(UniHub, 2026-05-23): a launcher edited by hand because the template ignored
`NISSTH_FRAMEWORK_ROOT`, a `StatusUpdate.md` preamble scraped from Nissth's own
ledger with `awk`, a `CLAUDE.md` banner rewritten with `tail -n +6`, and CRLF
templates leaking from the framework working copy. See
`AgentReports/Reports/2026-09-13_finans-consumer-init-friction-audit.md` F1–F5.

A step that is deterministic and done by hand every time is, by Nissth's own
philosophy (§2), a defect. This is the mechanism.

## Run it

```sh
node Tools/nissth-init/init.mjs --target ../my-app --name "My App" --stack expo
node Tools/nissth-init/init.mjs --target ../my-app --name "My App" --stack spring-boot --wiring submodule
node Tools/nissth-init/init.mjs --target ../my-app --name "My App" --stack none --dry-run --json
```

| Flag | Values | Default | Meaning |
|:---|:---|:---|:---|
| `--target` | dir | required | Consumer project root (created if absent) |
| `--name` | text | required | Project name for the `CLAUDE.md` banner, `AGENTS.md`, ledger title |
| `--stack` | `expo` · `spring-boot` · `postgres` · `none` | required | Selects `.gitignore` rules, the banner's stack sentence, and `.claude/settings.json` additions |
| `--wiring` | `local` · `submodule` | `local` | `local` bakes this checkout's path into the launchers' third fallback; `submodule` leaves it empty and the launcher expects `Tools/Nissth/` |
| `--framework-root` | abs path | this checkout | Where to copy the framework files from; must contain `Bindings/` |
| `--dry-run` | — | off | Print the file list, write nothing |
| `--json` | — | off | Machine-readable output |

Exit codes: **0** done · **2** usage error or refusal (nothing written) · **3** write failure.

## What it creates (18 files)

`CLAUDE.md` (project banner + the framework body copied verbatim from the first
`---` rule) · `AGENTS.md` · `ImplementationPlans/_TEMPLATE.md` ·
`DBL/{Summaries,DependencyMaps,APIIndex,SchemaIndex}/_TEMPLATE.md` ·
`AgentReports/StatusUpdate.md` (schema preamble + a filled **Bootstrap** entry
that lists every created file and whether SRS/SDD were present) ·
`AgentReports/{Reports,Bridge,Snapshots}/.gitkeep` · `Tests/README.md` (states the
`Tests/` rule: test sources live here, never a `tests/` sibling) ·
`Tools/.gitkeep` · `.claude/settings.json` (narrow allow-list, never a bypass
key) · `.gitignore` (stack-specific; always ignores `AgentReports/Bridge/`) ·
`.gitattributes` (LF baseline) · `nissth-bridge` + `nissth-bridge.ps1`.

Every text file is written LF regardless of the template's line endings.

## What it refuses

| `error_code` | When |
|:---|:---|
| `already_initialized` | `<target>/AgentReports/StatusUpdate.md` exists — resume via the boot protocol, do not re-init |
| `file_exists` | any planned path already exists; the list is printed, **nothing** is written |
| `target_is_framework` | `--target` is the framework checkout itself |
| `invalid_framework_root` | `--framework-root` lacks `Bindings/`, `CLAUDE.md`, `AGENTS.md`, the plan template, or the dispatcher |
| `framework_*_shape` | the framework files no longer have the shape the templates rely on (banner without `**Status:**`, `AGENTS.md` without the project line, launchers without the `DEFAULT_ROOT` placeholder) — fix the framework, not the consumer |
| `usage` | bad or missing flags |

There is no `--force`. Refuse-and-list is the contract.

## What it does not do

- Run any subprocess — no `git init`, no `npm`, no `npx create-*`.
- Author `SRS.md` / `SDD.md` or `Phase_00_DBL_Bootstrap.md`. Those are the agent's, after the HR#13 permission gate, which init cannot verify and does not pretend to.
- Update a consumer later. Framework bumps are a manual diff (see `Tools/nissth-bridge/consumer-launcher/README.md`).

## Tests

```sh
npm --prefix Tools/nissth-init test     # node --test; 20 cases, temp dirs cleaned
```

The two launcher tests spawn `sh` and `pwsh`/`powershell` and skip with a note
when the shell is not on PATH.
