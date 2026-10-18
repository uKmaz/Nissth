# Phase 29: Ledger rotation — the first §5.1 rotation of this repo's own ledger, and a public cut that does not publish it — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_29_Ledger_Rotation
- **Authored:** 2026-10-18 by Claude (Opus 5.5)
- **Approved:** 2026-10-18 by user ("go on", answering "say the word and I'll start the rotation"; standing pre-approval for framework-side phases, 2026-09-13)
- **Depends on:** Phase_28_Incremental_Public_Releases
- **Estimated scope:** `AgentReports/StatusUpdate.md` is 530 KB, more than five times §5.1's threshold. The rotation itself is plan-exempt (`AgentReports/` only), but it has one consequence in product code: `Tools/public-cut` resets the published ledger to its preamble plus a seed entry. It does not know about archive slices, so the next cut would publish the full unreviewed development history as `AgentReports/Archive/StatusUpdate_*.md`, and would keep the live file's archive pointer in the published preamble, pointing at nothing. This phase rotates the ledger and teaches the cut to drop both, with a gate. The harvest proposals renumber 29–33 → 30–34. `CLAUDE.md` is not touched.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the size, a clean boundary, and what the cut would do with an archive slice.

### 1.1 Inputs to read

- **DBL:** none in this repo.
- **Bridge reports:** none.
- **Source:** `CLAUDE.md` §5.1; `Tools/nissth-init/templates/Archive.README.md`; `Tools/public-cut/cut.mjs:296-306` (deletions), `:397-403` (ledger reset), `:445-449` (`preambleOf`), `:464-486` (`verifyCut`); `Tools/public-cut/test.mjs:210-266` (release fixture); `README.md:766` (archive description).
- **StatusUpdate.md:** latest entry 2026-10-18 18:58 +03:00 (Phase 28 closed), whose `Next:` names this rotation.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Ledger size and entry count | `wc -c`, `grep -c '^### '` | ledger | over threshold? |
| 2 | Boundary | `grep -n '^### '` tail | ledger | where Phase 28 begins |
| 3 | Does `Archive/` exist here | `ls AgentReports/Archive` | repo | §5 names it; init ships its README |
| 4 | What the cut does with `Archive/` | read `cut.mjs` deletions, reset, gates | tool | would a slice be published? |
| 5 | Baseline | `node --test Tools/public-cut/test.mjs` | tool | count before change |

### 1.3 Findings

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| 1. Size | > 100 KB | 530 019 bytes, 101 entries + the schema's example heading | yes |
| 2. Boundary | the first Phase 28 entry | line 4015, `2026-10-18 18:19 +03:00 — Phase 28: … built`; everything above it closes Phase 27 and the topology switch, so no phase is split | yes |
| 3. `Archive/` | absent | absent — this repo never rotated | yes |
| 4. Cut | publishes slices | nothing in `scrub-map.json` `delete` or the reset touches `AgentReports/Archive/`; `preambleOf` keeps everything above the first entry heading, so a pointer placed there is published | yes — the defect this phase fixes |
| 5. Baseline | 20/20 | 20/20 (Phase 28 close) | yes |

---

## 2. Expected State

### Before

| Target | Property | Value |
|:---|:---|:---|
| `AgentReports/StatusUpdate.md` | size | 530 KB, all history since 2026-05-05 |
| `AgentReports/Archive/` | — | absent |
| a public cut | archive slices / pointer | would be published |

### After

| Target | Property | Value |
|:---|:---|:---|
| `AgentReports/Archive/StatusUpdate_2026-05-05_2026-10-18_phases-00-27.md` | content | a one-line header, then lines 49–4014 of the old ledger, byte-identical |
| `AgentReports/Archive/README.md` | content | the `nissth-init` template, verbatim |
| `AgentReports/StatusUpdate.md` | content | the preamble, one `> **Earlier entries** …` pointer line, then the Phase 28 entries onward, byte-identical; well under 100 KB |
| a public cut | archive | `AgentReports/Archive/StatusUpdate_*.md` removed, the pointer line removed from the published preamble, both gated; `Archive/README.md` kept |
| suites | public-cut | 20 + new, green |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** Cut changes. **File:** `Tools/public-cut/cut.mjs`. **Operation:** modify. In the ledger reset (step 4), delete every `AgentReports/Archive/StatusUpdate_*.md` in the worktree and drop the archive pointer line (`ARCHIVE_POINTER`, exported) from the preamble before writing the seed. In `verifyCut`, fail on any `AgentReports/Archive/StatusUpdate_` path and on a published ledger that still carries the pointer.
- [x] **Step 2.** Tests. **File:** `Tools/public-cut/test.mjs`. **Operation:** modify. The release fixture gains an archive slice, `Archive/README.md` and a pointer line; the first-release test asserts the slice and the pointer are absent from the published tree and the README is present. **Acceptance:** suite green; a mutation that skips the slice deletion fails a test, and so does one that keeps the pointer.
- [x] **Step 3.** Rotate (plan-exempt, recorded here because Step 1 depends on its exact shape). Write the archive file and its header; rewrite the live file as preamble + pointer + entries from line 4015. Copy `Archive.README.md` to `AgentReports/Archive/README.md`. **Acceptance:** archive body + live entries = the old file's lines 49–end, byte for byte (`cmp`).
- [x] **Step 4.** Docs. `README.md:766` names the §5.1 file pattern; harvest Report proposals 29–33 → 30–34 with a revision line.
- [x] **Step 5.** Field check: `cut.mjs --dry-run`, then a local cut with no tag and no push; read the gates and `git ls-tree` for `AgentReports/Archive/`. Delete the local `nissth/public` result afterwards only if it differs from `public/master` — no publishing in this phase.

### 3.2 Forbidden in this phase

- Editing, reordering or dropping any ledger entry — the rotation moves bytes; `cmp` proves it.
- Editing `CLAUDE.md` — §5.1 already describes this; a body change drifts every consumer.
- Changing the scrub replacements or the seed entry.
- The `ledger-check` tool and the timestamp rule — harvest row 6, now Phase 30.
- Pushing or tagging — only on the user's word.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Suites run with `node --test` from the edited files on disk; the tool has no build step.
- The field check builds from the committed `dev` tip (the tool refuses a dirty tree).
- Development directory; no binding build input changed, so §8.x.6 does not apply.

### 4.2 Checks

- [x] **Byte identity:** `cmp` of (archive body ++ live entries) against the old file's entries.
- [x] **Tests:** public-cut all pass, above 20; both mutations fail.
- [x] **Field check:** gates pass; no `AgentReports/Archive/StatusUpdate_` path and no pointer in the cut; `Axiom/` intact; still on `dev`.
- [x] **Validators:** `doc-claims` exit 0; `plan-lint --plan` this file, no errors.
- [x] **Other suites unaffected:** dispatcher 41/41, nissth-init 39/39.

### 4.3 Pass criteria

- The live ledger is under 100 KB and its tail is unchanged.
- No archive slice or pointer can reach the public repository; a test proves it.

### 4.4 Failure handling

If any check fails: STOP, append a `Verified: FAIL` entry with the artifact, do not retry silently.

---

## 5. Cleanup

- [x] Temp repos removed by the tests; no scratch worktree left (`git worktree list`).
- [x] Snapshot: ref only — `dev` @ `48967e1` holds the unrotated ledger; nothing under `AgentReports/Snapshots/`.
- [x] Reports: none.
- [x] Document Sync (HR#11): `cut.mjs` is cited by its README (step 6 "Reset the ledger") and the root README; run `doc-claims`.

---

## 6. Status Update Entry

```
### YYYY-MM-DD HH:MM±hh:mm — Phase 29: Ledger rotation — CLOSED

**State:** Phase 29 closed · Tests: public-cut N/N, dispatcher 41/41, nissth-init 39/39 · Blockers: none
**Report:** [§1] · **Executed:** [§3] · **Verified:** [§4] · Doc sync: … · Reports: none
**Issues:** [none]
**Next:** Phase 30 (ledger and plan contract — harvest rows 3–7, 15).
```
