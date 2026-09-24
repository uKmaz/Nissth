# dbl-regen — what needs regenerating, and the worksheet to do it

Lists the `DBL/**` artifacts that have a reason to be rewritten, prints a worksheet
for each, and stamps the frontmatter once a human has rewritten the body. Zero
runtime dependencies, Node 20+, no network.

```sh
node Tools/dbl-regen/regen.mjs --root .                            # what is due, and why
node Tools/dbl-regen/regen.mjs --root . --artifact DBL/APIIndex/routes.md
node Tools/dbl-regen/regen.mjs --root . --all
node Tools/dbl-regen/regen.mjs --root . --stamp DBL/APIIndex/routes.md --by "Claude (Opus 5)"
```

Exit **0** nothing due, or a worksheet was printed · **1** artifacts are due (so a
close-out sweep can gate on it) · **2** usage or config error.

## What it will not do

**It does not write an artifact's body.** A Summary's gotchas, a DependencyMap's
reasons, an APIIndex's auth column — those are judgment, and a tool that generated
them would be producing confident text nobody verified. That is the failure DBL exists
to prevent, so automating it would be the tool's first lie.

It does the mechanical half, which is most of the cost:

| The worksheet gives you | Because |
|:---|:---|
| Every file that changed under `covers` since `source_state`, A/M/D | This is the question "what do I have to reflect?", and it is a `git diff` nobody should retype |
| The full inventory under `covers` as it is now | Artifacts whose body is a list (routes, tables, components) are half-rewritten by this |
| The artifact's own `stale_when` as a checklist | It already says what invalidates it; answering those questions *is* the regeneration |
| The stack lens that answers the surface question | `route_lens`, `entity_lens`, `schema_lens` — the Bridge already knows how to enumerate a surface per stack |
| The two frontmatter lines to write when you are done | With today's date and the current HEAD, ready to paste — or `--stamp` writes them |

Often the worksheet ends the job in one look: if six covered files were *modified* and
the artifact's `stale_when` only fires on a file being **added, renamed or removed**,
there is nothing to rewrite and the artifact just needs its stamp.

## `--stamp` and its one precondition

`--stamp` rewrites exactly two lines — `last_regenerated` and `source_state` — in place,
preserving every other byte. It does not re-serialise the YAML block: that is the Phase 19
lesson, where a whole-block rewrite folded long lines and handed `dbl-check` a
`bad-frontmatter` on a file the tooling had just written.

**It refuses while the artifact is unmodified in the working tree.** Stamping records that
a human rewrote the body; if nothing was rewritten there is nothing to record, and a fresh
stamp on a stale body is worse than the stale stamp it replaces — it converts "I know this
is old" into "I have checked this", which is the strongest claim in the frontmatter.

## Why this exists

The 2026-09-13 two-consumer assessment called DBL regeneration *"the leak that matters
most"*: every phase close in every consumer paid a hand-rolled `git diff` plus a manual
re-listing, and `CLAUDE.md`'s own status banner listed the tooling as unbuilt for four
months.

Building it turned up something worse than the cost. `dbl-check`'s freshness check only
fired when `source_state` was a **bare** hex ref, and one consumer had written
`git c5e6a34 (Phase 06 M5 feature commit — reports)` on all 17 of its artifacts. Not one
had ever been freshness-checked; four were stale, one by 74 covered files, while
`dbl-check --strict` reported `0 error, 0 warn, 0 info`. See
`AgentReports/Reports/2026-09-25_dbl-freshness-blind-spot.md`.

## Tests

```sh
node --test Tools/dbl-regen/test.mjs
```

Every git path runs against a real `git init` repository rather than a mock, and the stamp
case asserts byte-preservation by diffing the file before and after — including a
deliberately long `name:` line, the kind a re-serialiser would fold.
