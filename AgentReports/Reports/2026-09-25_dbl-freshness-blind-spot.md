---
report_type: incident
title: The DBL freshness check could not fail — one anchored regex, 17 artifacts, four of them stale
authored: 2026-09-25 by Claude (Opus 5)
last_updated: 2026-09-25 by Claude (Opus 5)
related_status_entries:
  - 2026-09-25 — Phase 25 close
  - 2026-09-13 — Phase 16 (dbl-check shipped)
related_plans:
  - Phase_25_DBL_Regeneration
  - Phase_16_DBL_Check
covers:
  - Tools/dbl-check — source_state ref extraction
  - CLAUDE.md §7.2, §7.3 — the freshness contract
  - the FinansYonetimApp consumer's DBL
supersedes:
  - none
---

# The DBL freshness check could not fail

## Summary

`Tools/dbl-check`'s `covers-changed-since` check — the mechanical form of §7.3, and the
only automated answer to "is this artifact still true?" — matched `source_state` against
an **anchored** `^[0-9a-f]{7,40}$`. It therefore ran only when the value was a bare hash
and nothing else.

One consumer wrote every artifact's `source_state` as `git c5e6a34 (Phase 06 M5 feature
commit — reports + English UI)`: a ref with a `git ` prefix and a human note. **All 17 of
its artifacts** were therefore never freshness-checked, from Phase 16 (2026-09-13) until
2026-09-25. Four of them were stale at the moment of discovery, one by 74 covered files,
while `dbl-check --strict` reported `0 error, 0 warn, 0 info`.

No data was lost and no product defect followed from it. The cost is subtler and worse:
for twelve days a green check told two projects their architectural knowledge layer was
current, and agents were instructed (§7.3, Hard Rule #4) to trust it.

## Timeline

| When | What |
|:---|:---|
| 2026-09-13 | Phase 16 ships `dbl-check` with `HEX_REF = /^[0-9a-f]{7,40}$/`. Its fixtures use bare hashes, so every test passes and the anchor is never exercised against another form. |
| 2026-09-13 → 09-22 | The FinansYonetimApp consumer runs `dbl-check --strict` at each phase close, part of its §5 sweep. It reports clean every time. The consumer writes annotated refs throughout, in good faith — §7.2 says "`<git commit hash, OR …>`" and does not forbid a note. |
| 2026-09-24 | Phase 24 re-runs `dbl-check` over both consumers while verifying something else. Still clean. Nothing is suspected. |
| 2026-09-25 | Phase 25's pre-flight asks a question the tool never had to answer: *what `source_state` forms exist in the wild?* Four shapes turn up; the regex accepts one. |
| 2026-09-25 | A throwaway script extracts the ref by hand from the 17 rejected values and runs the same `git diff` the tool would have run: **4 artifacts stale** — `DependencyMaps/layers.md` (35 covered files changed), `Summaries/_layout.md` (74), `APIIndex/routes.md` (6), `Summaries/_state.md` (1). |
| 2026-09-25 | Fixed, regression-tested against all four real forms, and verified against the live consumer: the fixed check names exactly those four artifacts with exactly those counts. |

## Root cause

**One anchor, and a fixture set that shared the author's assumption.** `^…$` encodes
"`source_state` *is* a hash". The contract in §7.2 is looser than that and the field is
looser still, but every fixture was written by the person who wrote the regex, so the
suite agreed with the code about the one shape both had in mind.

The deeper cause is the failure mode this repository has now recorded three times:

- **Phase 23** — the public cut's residue gate shelled out to `git grep -E`, which rejected
  a `(?:…)` group; a non-matching grep also exits non-zero, so "could not check" was read
  as "clean". It printed `fatal:` and reported success in the same breath.
- **Phase 21** — `expo_doctor_lens` parsed zero checks and reported PASS, because
  expo-doctor names checks only under `--verbose`.
- **Here** — a check that silently skipped the input it did not recognise.

In all three, the tool could not distinguish *"I checked and found nothing"* from *"I could
not check"*, and reported the first. **A verification step that cannot fail is worse than no
verification step, because it is believed.** That sentence is the finding; the regex is just
this instance of it.

## Remediation

1. **`sourceRef()` extracts the first hex token** from `source_state`, wherever it sits,
   ignoring a `git ` prefix and any parenthetical. A 7+ hex run with no digit is treated as
   prose, not a hash.
2. **Silence is no longer an outcome.** A `source_state` with no ref that is not one of the
   two ref-less forms (`design-only — …`, `uncommitted state at …`) now raises
   `unrecognised-source-state`. The check either runs, or says why it did not.
3. **Regression tests over all four real forms**, including one asserting that an annotated
   ref is *checked* rather than skipped — the case whose absence caused this.
4. **`Tools/dbl-regen`** (§15) turns the now-firing signal into a worksheet, so that a
   check which suddenly reports four stale artifacts does not simply become noise the
   reader learns to skip.
5. **A framework test stopped asserting a consumer's cleanliness.** `dbl-check`'s suite had
   a case asserting a live consumer validates clean; the fix made it go red, correctly,
   for a fact about someone else's repository. It now asserts only that the consumer's
   artifacts satisfy the *contract* (no `error` findings) — warnings are that project's
   state, not this one's.

## Follow-ups

- The consumer's four stale artifacts are **not** regenerated from here; the bodies are
  that project's judgment. Its ledger carries the list and the worksheet command.
- Worth a look when someone has an hour: the other three validators each have a
  "recognised input" boundary (`doc-claims`'s tool-enumeration heuristic, `plan-lint`'s
  step-target extractor, `nissth-init --check`'s banner split). Each should be audited for
  the same question — *what does it do with input it does not recognise?* — and the honest
  answer must never be "nothing, quietly".

## Revision history

- 2026-09-25 — authored at Phase 25 close, per §10.4 item 1 (a defect class discovered
  during a phase requires an incident Report).
