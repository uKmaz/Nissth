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

## Repository topology

A force-push cleans only the branch it lands on. Everything else in the repository
stays exactly as public as the repository is — so on a single public repo carrying
both `dev` and the cut, scrubbing `master` only changes what a visitor sees **first**.
Anyone can switch branches.

The topology this tool is built for:

| Remote | Repository | Holds |
|:---|:---|:---|
| `origin` | **private** | `dev` and any working branches — unscrubbed, consumer names and local paths included |
| `public` | public | `master` only: the cut, and nothing else |

`--remote` follows that: with no flag it pushes to a remote named **`public`** when one
exists and falls back to `origin` otherwise, so a private `origin` cannot receive the cut
because someone forgot a flag.

### Switching a single public repo to that shape

```sh
# 1. Create the private repository first — nothing is deleted until it exists.
#    gh repo create <owner>/Nissth-dev --private     (or the web UI)

# 2. Point origin at it and keep the public one under its own name.
git remote rename origin public
git remote add origin <private-remote-url>
git push -u origin dev
git push origin 'refs/heads/nissth/*'          # any working branches

# 3. Only once step 2 is verified, remove the unscrubbed branches from the public repo.
git push public --delete dev
git push public --delete nissth/phase-09-5-binding-framework-root
git push public --delete nissth/phase-09-7-postgres-coerce-ssl

# 4. From then on the cut goes where it should with no flag at all.
node Tools/public-cut/cut.mjs --push
```

**Deleting a branch does not unpublish it.** Forks, existing clones, anything that
fetched it and every cache or index that saw it keep their copy. The switch stops future
exposure; it does not retract past exposure.

## Pushing

`--push` force-pushes `nissth/public:master` from the branch ref, without checking the
orphan out in the primary working directory. Force is required: the orphan shares no
ancestry with the published branch. It goes to the remote `--remote` resolves (above).

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
