# public-cut — rebuild the public branch from the development tip

Rebuilds `nissth/public` — the single-commit, scrubbed snapshot that `origin/master`
serves to anyone who lands on the repository — from the current development tip.
Zero runtime dependencies, Node 20+, no network except the optional push.

```sh
node Tools/public-cut/cut.mjs --dry-run      # report, write nothing
node Tools/public-cut/cut.mjs                # cut and commit the branch, do not push
node Tools/public-cut/cut.mjs --push         # cut, then force-push nissth/public:master
```

Exit **0** done · **1** a verification gate failed (nothing is pushed) · **2** a
precondition refusal.

## Why this exists

Phase 10 authored the cut as prose in a plan. Phases 12, 13 and 14 each re-derived
its `Axiom/`-row strip **by hand**, because the project tree had changed shape
underneath the pattern each time; Phase 13 predicted the next break and Phase 14 hit
it. The ledger has carried *"script the public re-cut so its `Axiom/` strip stops
being re-derived"* as an open item since 2026-08-24. A procedure performed by hand
four times, identically, is a defect by §2's own standard.

The scrub surface was also written for **one** consumer project and there are now
three — which is exactly the kind of drift nobody notices until a consumer's name is
published.

## The hard gate

`Axiom/` is the user's live reference material, not a build input. It is removed from
the **cut**, never from disk:

- The orphan branch is built in a scratchpad worktree. The primary working directory
  is never checked out to it — an in-place `git checkout --orphan` would empty
  `Axiom/` from disk between the deletion and the verification (Phase 10 §3.2).
- `Axiom/`'s tracked file count and git status in the primary directory are asserted
  immediately after the deletion step **and** after the commit. Either check failing
  aborts the cut.
- The script refuses to run at all in a tree where `Axiom/` is absent, so the gate can
  never pass vacuously.

## What it does

1. **Preconditions** — the tree must be committed (a dirty tree would publish a state
   nobody reviewed) and `Axiom/` must be present.
2. **Worktree** — `git worktree add --orphan`, populated from the development tip.
3. **Delete** — the paths and globs in `scrub-map.json`: `Axiom/`, root PDFs,
   `.claude/settings.local.json`, and one consumer-specific Report.
4. **Scrub** — every `find` in `scrub-map.json` across the committed text files.
5. **Strip** — the `Axiom/` row out of the project trees in `CLAUDE.md` and
   `README.md`, repairing the promoted row's connector and its children's spine.
   Written as an algorithm, and it **raises** (`STRIP PATTERN MISMATCH`) when the
   shape is not what it expects, rather than silently publishing a row that points at
   a directory which is not there.
6. **Reset the ledger** to its preamble plus `seed-status.md` — the public branch
   starts a fresh append-only log rather than carrying the development history.
7. **Commit** one orphan commit, then run the gates: no `Axiom/` path, no PDF, no
   `settings.local.json`, `LICENSE` present, exactly one commit, and **zero scrub
   residue** — every pattern is re-grepped against the committed tree.

The worktree is removed either way; the branch survives.

## `scrub-map.json`

Each replacement carries `find` (a regex), `replace`, and a one-line `reason`, so
adding one is a deliberate act recorded in a file. Order matters: longer, more
specific patterns run before the bare names they contain.

Because the gates re-grep every pattern **after** the commit, an entry that stops
matching what it used to match becomes a loud failure rather than a quiet leak.

## Pushing

`--push` force-pushes `nissth/public:master` from the branch ref, without checking the
orphan out in the primary working directory. Force is required: the orphan shares no
ancestry with `origin/master`.

A force-push rewrites a public default branch — anyone who has cloned needs
`git fetch && git reset --hard origin/master` — and it does **not** unpublish what was
published before. Both were settled with the user on 2026-08-24 and are restated here
because the next person to run this should not have to rediscover them.

## Tests

```sh
node --test Tools/public-cut/test.mjs
```

The strip is tested against the **real** `CLAUDE.md` and `README.md`, not fixtures:
fixtures are what let the pattern break three times.
