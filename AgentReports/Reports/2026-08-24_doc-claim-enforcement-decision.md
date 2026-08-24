---
report_type: decision
title: Enforcing doc-claim accuracy with a mechanism rather than another rule
authored: 2026-08-24 by Claude (Opus 5)
last_updated: 2026-08-24 by Claude (Opus 5)
related_status_entries:
  - 2026-08-24 10:10 — Completion audit; Phase 12 authored (Approved: pending)
  - 2026-08-24 11:20 — Phase 12: Documentation Status Sync — VERIFIED PASS
  - 2026-08-24 12:30 — Phase 13: Doc-Claim Validator
related_plans:
  - Phase_12_Doc_Status_Sync
  - Phase_13_Doc_Claim_Validator
covers:
  - documentation accuracy enforcement
  - Hard Rule #11 coverage gap for repo-root prose
supersedes:
  - none
---

# Doc-claim accuracy: mechanism over rule

## Context

Phase 12 found that `README.md` had described the PostgreSQL binding as "queued — plan not
yet authored" for roughly three months after it shipped, and had promised a tool named
`index_drift` that has never existed in any manifest. `CLAUDE.md`'s status header still read
"Phase 4 of 4 — Spring Boot stack bound" seven phases later.

The uncomfortable part is not that this happened. It is that **Hard Rule #11 already
required a Document Sync sweep at every phase close, and seven closes ran that sweep in good
faith without catching any of it.**

The reason is structural, not a discipline failure. HR#11 tells the agent to find affected
documents by looking at `DBL/` artifacts' `covers` globs and at plan cross-references. Both
are pointers that exist *in the artifact being changed*. Repo-root prose has neither: nothing
about shipping `Bindings/Postgres/` mechanically points at line 518 of `README.md`. The sweep
asks "which documents does this change affect?" and the honest answer, from inside the
change, is "none that declare themselves."

A rule that is followed and still misses the defect is not a rule that needs restating.

## Options considered

| Option | How it works | Why not / why |
|:---|:---|:---|
| **A. Add Hard Rule #14** — "check the README when a binding ships" | Another instruction in `CLAUDE.md` | Rejected. HR#11 already says this in general form and was obeyed seven times without effect. A more specific restatement of an ineffective rule is not more effective; it is longer. It also grows the always-loaded context for every session, which cuts against the framework's stated purpose (§2, token-lean) |
| **B. Give repo-root docs `covers`-style frontmatter** | Add YAML frontmatter to `README.md` / `CLAUDE.md` declaring what they describe, so HR#11's existing sweep reaches them | Rejected. Puts machine metadata at the top of the two documents a human reader opens first, and still depends on an agent running the sweep and reading the pointer correctly. Solves the pointing problem, not the remembering problem |
| **C. A validator tool** | `Tools/doc-claims/` parses the binding manifests as ground truth and checks the prose against them; exits non-zero on findings | **Chosen.** Turns "remember to check" into "run a command that answers". No context cost — it lives on disk, not in the prompt. Its ground truth is the manifests, which are already the thing that changes when a binding ships, so it cannot drift from reality the way prose can |
| **D. Wire it into a hook or CI** | Same tool, run automatically on edit or on push | Deferred, deliberately. Automatic enforcement has its own failure modes — a false positive that blocks an unrelated edit teaches people to bypass the hook, and a bypassed hook is worse than a manual command. Ship the tool, use it, then decide |
| **E. Do nothing** | Fix the docs, move on | Rejected. Phase 12 fixed the symptoms. Nothing about that fix prevents recurrence, and the repo is now public, so the audience for a stale claim is strangers rather than the author |

## Decision

Build **`Tools/doc-claims/`** (Option C). Three checks, chosen for a near-zero false-positive
rate on the current tree rather than for coverage:

| Check | Catches |
|:---|:---|
| `stale-binding-status` | A shipped binding described as queued / not-yet-authored / not-on-disk / in-flight |
| `fictional-tool` | A tool name presented as shipped that exists in no manifest |
| `tool-count-drift` | A `README.md` table count disagreeing with the manifest |

Explicitly **not** done: Hard Rule #14, and hook/CI wiring.

### The design constraint that mattered most

A validator that cries wolf is worse than no validator, because the reader learns to skip it
and the next real finding scrolls past unread. Two false positives surfaced during
implementation and both were fixed by narrowing the *tool*, never by editing the prose:

1. `CLAUDE.md` §11.12 contains "`Phase_05_Bridge_SpringBoot_FirstSlice.md` (not yet authored
   at the time §11 is written)" — accurate historical prose about a *plan*, which a naive
   substring match read as "the Spring Boot binding does not exist". Fixed by stripping plan
   filenames before matching binding names.
2. §12.1 of `CLAUDE.md` — the section documenting this very tool — quotes the historical
   defect verbatim, and tripped its own checker. Fixed by adding an inline waiver
   (`<!-- doc-claims:allow <check> - reason -->`) rather than by rewording the explanation
   into something vaguer.

Both waivers and allowlist entries require a written reason and appear in the diff. That is
the same philosophy as the framework's action tools (§11.7): make the exception a deliberate,
recorded act rather than a silent one.

## Consequences

- **The defect class is now detectable in one command.** Both Phase 12 defects are caught by
  fixtures reproducing their verbatim original text, so the tool demonstrably catches what it
  was built for rather than merely claiming to.
- **The allowlist is a maintenance surface.** ~60 entries today, mostly PostgreSQL catalog
  objects and frontmatter keys. If it grows without anyone reading the reasons, it becomes the
  silencer it was designed not to be. Worth an occasional audit.
- **Coverage is narrow on purpose.** The tool does not check test counts, phase numbering,
  dates, or link validity. Each would need its own ground truth, and each carries its own
  false-positive risk. Narrow and trusted beats broad and ignored.
- **It is not automatic.** Nothing runs it unless someone does. That is a real gap and a
  deliberate one — Option D remains open once the tool has a track record.
- **HR#11 is unchanged.** The rule still governs; this tool is one instrument the sweep can
  now use for a class of document the sweep could not previously reach.

## Revision history

- 2026-08-24 — authored at Phase 13 close, after the validator passed on the real tree and
  its 23-test suite went green (Claude, Opus 5).
