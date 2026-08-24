# doc-claims — repo-root prose validator

Checks what the documentation *claims* against what the binding manifests
actually *say*. Zero runtime dependencies, Node 20+.

## Why this exists

`CLAUDE.md` Hard Rule #11 (Document Sync Mandate) requires a sync sweep at every
phase close. It keys off `DBL/` artifacts' `covers` globs and plan
cross-references — and repo-root prose has neither. Nothing mechanically points
at `README.md` when a binding ships.

The result, found in Phase 12: `README.md` told readers the PostgreSQL binding
was "queued — plan not yet authored" for three months after it shipped, and
promised a tool named `index_drift` that has never existed in any manifest.
Both survived seven phase closes, each of which ran the HR#11 sweep.

This tool is the mechanism that rule could not be. It does not edit anything.

## Run it

```sh
node Tools/doc-claims/validate.mjs          # from the repo root
npm run check                               # from Tools/doc-claims/
node Tools/doc-claims/validate.mjs --json   # machine-readable
```

Exit codes: **0** clean · **1** findings · **2** usage or config error.

## What it checks

| Check | Fires when | Kept narrow by |
|:---|:---|:---|
| `stale-binding-status` | A line calls a binding `queued` / `not yet authored` / `not on disk` / `in flight` while `Bindings/<X>/` **and** its manifest exist | Only shipped bindings can trigger it. Stack names inside plan filenames (`Phase_05_Bridge_SpringBoot_FirstSlice.md`) are stripped before matching — a sentence about a *plan* is not a claim about a *binding* |
| `fictional-tool` | Inside a tool-enumeration line — one naming a real binding, containing a shipping verb, and listing ≥2 backticked identifiers — an identifier appears that is in no manifest and not allowlisted | Restricting to enumeration lines keeps PostgreSQL catalog names, DBL frontmatter keys, and §11.7's illustrative tools out of scope |
| `tool-count-drift` | A `README.md` stack-table row links `Bindings/<X>/` and its trailing count column disagrees with the manifest | Pure numeric comparison |

The checks are deliberately conservative. A validator that cries wolf gets
ignored, which is worse than no validator: the reader learns to skip it, and the
next real finding scrolls past unread.

## The allowlist

`known-non-tools.json` holds identifiers that look like tool names but are
deliberately not shipped tools — hypothetical tools in illustrative tables,
PostgreSQL catalog objects, frontmatter keys, Bridge command fields. Each entry
carries a `reason`.

Adding an entry is meant to be deliberate: an agent writing a hypothetical tool
into the docs has to say so, in a file, in writing.

It is **not** a silencer. If a name is genuinely fictional and the prose presents
it as shipped, the honest fix is to correct the prose. `index_drift` — the name
that motivated this tool — belongs in the docs' history, not in this file.

## Tests

```sh
node --test Tools/doc-claims/test.mjs
```

Fixtures under `_fixtures/` are self-contained fake repo trees, so editing the
real documentation cannot turn a unit test red. Two of them reproduce the
verbatim pre-Phase-12 text, so the suite proves the tool catches the defects it
was built for rather than merely asserting it.

One test does read the real repository — `the real repository passes`. That one
is intentional: it fails if the docs regress *or* if a future check turns noisy.

## Inline suppression

Documentation legitimately quotes past defects — `CLAUDE.md` §12.1 quotes the very
"queued — plan not yet authored" line this tool exists to catch. Rewording prose around a
checker makes the prose worse, so a single line can be waived with a marker on the line
before it:

```html
<!-- doc-claims:allow stale-binding-status - quoting a historical defect -->
```

The check name is required; there is no blanket wildcard, and a waiver naming one check will
not silence another. Everything after the check name is a free-text reason. Like the
allowlist, a waiver is deliberate, appears in the diff, and says what is being waived and why.
