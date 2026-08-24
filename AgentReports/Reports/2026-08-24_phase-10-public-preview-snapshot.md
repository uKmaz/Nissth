---
report_type: snapshot
title: Nissth public preview branch — architectural state at Phase 10 close
authored: 2026-08-24 by Claude (Opus 5)
last_updated: 2026-08-24 by Claude (Opus 5)
related_status_entries:
  - 2026-08-21 — Phase 10 authored + approved; pre-commit record
  - 2026-08-21 — Phase 10 VERIFY FAIL — fresh-clone build defects
  - 2026-08-24 08:40 — Phase 11: Fresh-Clone Hardening — VERIFIED PASS
  - 2026-08-24 09:05 — Phase 10: Public Preview Branch — VERIFIED PASS (re-cut)
related_plans:
  - Phase_10_Public_Preview_Branch
  - Phase_11_Fresh_Clone_Hardening
covers:
  - distribution — the nissth/public branch
  - repo topology — private master vs generalized public cut
supersedes:
  - none
---

# Nissth public preview — state at Phase 10 close

## Summary

`nissth/public` exists locally at `fd1a6b7`: a single orphan commit, 230 files, no
`Axiom/` anywhere in its history, and — for the first time — a branch that passes its
own test suite from a fresh clone. Nothing is pushed.

The repo now carries two intentionally different faces of the same framework.

| | `master` | `nissth/public` |
|:---|:---|:---|
| Tip | `2f434db` | `fd1a6b7` (orphan, 1 commit) |
| Purpose | the author's working copy — training data for developing the framework | the shareable cut |
| Status log | 54 entries, full development history | 1 seed entry, 78 lines |
| `Axiom/` | 148 files, present | absent from tip **and** history |
| Spec PDFs | 2 at repo root | absent |
| Consumer references | Süprüz, UniHub, local paths, vendor names | 0 |
| Files | 384 tracked | 230 tracked |

## What "generalized" means concretely

172 replacements across 23 files, from the Phase 10 §3.0 scrub map:

| Class | Became |
|:---|:---|
| `com.supruz.reservation` | `com.example.reservation` (RFC 2606 reserved namespace) |
| Consumer project names (Süprüz, UniHub-Backend, UniHub-Frontend) | `Example`, `Example-Backend`, `Example-Frontend` |
| Windows-absolute paths (`C:\Users\admin\Desktop\Nissth`, `/c/Users/...`) | `<repo-root>`, `<workspace>`, `<user-home>`, `<scratchpad>` |
| Vendor choice (`iyzico`) | `a payment provider` |
| Local account name | `<user>` |

Deliberately **retained**, and the reasoning matters for anyone auditing the branch:

- **`github.com/umutbrkt/Axiom`** (2 sites) — attribution to the already-public Unity 6
  predecessor whose diagnostic-bridge design Nissth generalizes. Credit, not leakage.
- **`Emre Uçmaz` on the `Approved:` lines of 8 phase plans** — the framework author's own
  name on his own approval records. Removing it would misrepresent who authored the work.
  This was carried as an open decision through Phase 10 and resolved by keeping it.

Deliberately **omitted** beyond the five non-shareable paths: `Phase_10_Public_Preview_Branch.md`
and its snapshot manifest. Both are self-referential — the manifest is a table of the user's
private filenames, and scrubbing the plan's own find/replace table yields `Example → Example`.
So `ImplementationPlans/` ships 10 files: 9 worked phase plans plus `_TEMPLATE.md`.

## Verification

All four suites run from the public worktree, per the fresh-clone clause that Phase 11 added
to `CLAUDE.md` §8.1.6 / §8.2.6 / §8.3.6:

| Suite | Result | Previous cut (`c0240f9`) |
|:---|:---|:---|
| Dispatcher | 32/32 | 32/32 |
| Spring Boot | 104/104 BUILD SUCCESS | 104/104 |
| PostgreSQL | 107 pass / 18 skip / 125 total | **106 pass / 1 fail** |
| Expo | 58/58, 13 suites | **57 pass / 1 fail** |

`Bindings/Postgres/dist/` emits 16 `.js` files. Checkout bytes verified directly: the Expo
fixture materializes LF, `mvnw.cmd` CRLF, the POSIX launchers LF. `./nissth-bridge
--list-bindings` resolves expo, postgres, spring-boot.

The two red cells are the whole reason Phase 11 existed. They were never Phase 10's doing —
both defects lived on `master` too, and the first cut simply became the first thing ever
verified from a clean tree.

## Consequences

- **Publishing is still a manual step.** Nothing was pushed; no remote for the public branch
  has been chosen. Pushing an orphan branch to the same remote as `master` puts both in one
  repo — a separate repo is the more usual shape for a public cut.
- **Real gaps remain for a public audience**, all deliberately out of Phase 10's scope: no
  LICENSE, no CONTRIBUTING, and a README written for the author rather than a newcomer.
  A LICENSE is the one that actually blocks reuse — without it the default is all-rights-reserved.
- **The two faces will drift.** Every future change on `master` needs a re-cut to reach the
  public branch. The scrub is scripted and the plan is reusable, so a re-cut is cheap, but it
  is not automatic.

## Revision history

- 2026-08-24 — authored at Phase 10 close, after the branch was re-cut from the Phase 11-fixed
  `master` and passed §4.2 from its own worktree (Claude, Opus 5).
