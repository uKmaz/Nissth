# dbl-check — DBL frontmatter + freshness validator

Checks every `DBL/**/*.md` artifact against the frontmatter contract in
`CLAUDE.md` §7.2 and the freshness signals §7.3 asks the agent to look for.
Zero runtime dependencies, Node 20+. Reports and exits; **never edits**.

## Why this exists

§7.2 says every DBL artifact carries six frontmatter keys — "no exceptions" —
and §7.3 says the agent must compare `source_state` to the tree before citing
an artifact. Both were discipline rules with no mechanism behind them. The
first greenfield consumer (FinansYönetimApp, Phase 00, 2026-09-13) wrote eleven
artifacts and verified them with a throwaway `node -e` script — the same
"rule followed, defect can still survive" shape that produced `doc-claims`
(`CLAUDE.md` §12.1). See
`AgentReports/Reports/2026-09-13_finans-consumer-init-friction-audit.md` F6/F7.

## Run it

```sh
node Tools/dbl-check/check.mjs                      # current directory must hold DBL/
node Tools/dbl-check/check.mjs --root ../my-app     # a consumer project
node Tools/dbl-check/check.mjs --json               # machine-readable
node Tools/dbl-check/check.mjs --strict             # any finding fails
node Tools/dbl-check/check.mjs --budget draft.md    # word-count one file before writing it
```

Exit codes: **0** clean (info/warn only) · **1** error-severity findings (any finding with `--strict`) · **2** usage or config error (no `DBL/`, bad flag).

`_TEMPLATE.md` files are ignored at any depth. A root whose `DBL/` holds only templates exits 0 with a one-line notice.

### `--budget <file>` — ask before you write

`over-budget` only fires inside a full `DBL/` scan, so the §7.4 split threshold
was discoverable only *after* the artifact was committed: the consumer's Phase 08
paid for four artifacts twice, writing each one and then splitting and moving it.
`--budget` word-counts any file — a draft in a scratchpad, an artifact not yet
saved — against the same threshold and the same word count the scan uses.

```sh
node Tools/dbl-check/check.mjs --budget draft.md --budget other.md
draft.md: 1340 words / 1100 — OVER by 240
other.md: 612 words / 1100 — ok, 488 to spare
```

Repeatable, honours `--json`, exits **1** when any file is over, **2** when a path
does not exist. It reads files and writes nothing, like the rest of the tool.

## What it checks

| Check | Severity | Fires when |
|:---|:---|:---|
| `missing-frontmatter` | error | file does not start with a `---` block |
| `bad-frontmatter` | error | a line inside the block is not `key: value`, `key:` + `- item`, blank, or a comment; or the block never closes |
| `missing-key` | error | any of `artifact_type`, `name`, `last_regenerated`, `source_state`, `covers`, `stale_when` is absent, or `covers` / `stale_when` is an empty list |
| `type-dir-mismatch` | error | `artifact_type` disagrees with the directory (`Summaries→summary`, `DependencyMaps→dependency_map`, `APIIndex→api_index`, `SchemaIndex→schema_index`) |
| `unknown-dir` | warn | an artifact lives in a `DBL/` subdirectory that is none of the four |
| `bad-regenerated-format` | error | `last_regenerated` is neither `YYYY-MM-DD by <who>` nor `STALE — <reason>` |
| `stale-marked` | info | `last_regenerated` starts with `STALE` — the Bridge's stale-flip (§11.4) did its job; the artifact must be regenerated before it is cited |
| `design-only-source-exists` | error | `source_state` starts with `design-only` and a file exists under one of the `covers` globs — the greenfield Phase 00 artifact has been overtaken by real source and Phase 01's §5 must regenerate it (§7.6) |
| `covers-changed-since` | warn | `source_state` is a git ref and `git diff --name-only <ref> -- <covers>` is non-empty; skipped with a note (not failed) when git or the ref is unavailable |
| `over-budget` | warn | more than 1 100 words (≈ 1 500 tokens) — split per §7.4 |
| `crlf` | error | the file contains CR — binding parsers anchor on `\n` (Phase 11) |

`covers` globs understand `**`, `*`, `?`, plain paths, and a trailing `/`.
The design-only walk skips `.git`, `node_modules`, build output, and `DBL/` itself.

## What it is not

- Not an auto-fixer. There is no `--fix`; a finding is corrected by regenerating the artifact.
- Not a YAML parser. The subset above is deliberate; frontmatter the subset cannot read is a `bad-frontmatter` finding, not a reason to add a dependency.
- Not a Bridge tool: it writes no report under `AgentReports/Bridge/` and has no enforcement contract to satisfy.
- Not wired into hooks or CI — the same deferral as `doc-claims` §12.4.

## Tests

```sh
npm --prefix Tools/dbl-check test     # node --test; 21 cases
```

Static fixtures under `_fixtures/` cover clean, stale, and design-only trees; broken
files (CRLF included — `.gitattributes` would normalise a committed one) and the
git-ref case are built in temp directories at test time. One test runs against the
FinansYönetimApp checkout when present and skips otherwise.
