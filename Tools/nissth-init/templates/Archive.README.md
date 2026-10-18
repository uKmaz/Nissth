# AgentReports/Archive/

Rotated slices of `AgentReports/StatusUpdate.md` (`CLAUDE.md` §5).

The ledger is append-only and never shrinks, so on a long-running project the
boot protocol's "read the latest entry" eventually means opening a file of
hundreds of kilobytes to read its last forty lines. **Past roughly 100 KB,
rotate.** Rotation is not an edit of past entries — the entries are moved
verbatim into a file here, and the live ledger keeps a pointer to them.

## How to rotate

1. Pick the cut **at a natural boundary** — a release, a milestone, the close of
   a phase wave — never mid-phase.
2. Move every entry up to that boundary, verbatim, into
   `AgentReports/Archive/StatusUpdate_<first-date>_<last-date>_<slug>.md`
   (e.g. `StatusUpdate_2026-09-13_2026-09-19_phases-00-09.md`). Give the file a
   one-line header saying what it holds and which live entry follows it.
3. Leave the live `StatusUpdate.md` with its schema preamble, a one-line pointer
   to the archive file, and every entry from the boundary onward.
4. Say so in the next status entry, and update the `CLAUDE.md` project banner if
   it describes where history lives.

## What this does not change

- **Hard Rule #3 still holds.** No entry is edited, reordered, or deleted; text
  moves between files and stays byte-identical. A correction is still a new
  entry that supersedes the old one, never a rewrite.
- **The latest entry is still the current state** (§1). Rotation only changes
  where the *old* entries live.
- Archived files are read on demand — when someone asks what happened back then.
  A resuming agent reads the live ledger's tail and nothing here.
