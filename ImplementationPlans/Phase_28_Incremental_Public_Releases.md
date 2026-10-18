# Phase 28: Incremental public releases — a public branch people can pull, and tags they can pin — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_28_Incremental_Public_Releases
- **Authored:** 2026-10-18 by Claude (Opus 5.5)
- **Approved:** 2026-10-18 by user ("do it", answering the proposal to make public updates normal commits with version tags; standing pre-approval for framework-side phases, 2026-09-13)
- **Depends on:** Phase_23_Public_Recut, Phase_27_Shadowed_Launcher_Root
- **Estimated scope:** The user wants Nissth to be a tool other people use. Every public update is currently an orphan commit force-pushed over `master`, so a clone cannot `git pull` and the `Tools/Nissth` submodule install the README recommends loses its pinned commit on the next update. The README also says "pin to a tag", and no tag exists. This phase makes each cut a normal commit whose parent is the current public `master`, pushed without force, with an optional validated version tag. It also fixes a defect found while reading for this plan: `--push` hard-codes `origin` and ignores the resolved remote, so under today's topology it would push the scrubbed cut into the **private** repo. Touches `Tools/public-cut/` (plan-required), its README, the root README's update section, and the harvest Report's numbering. `CLAUDE.md` is not touched: its one line about the tool stays true, so no consumer drifts.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm that the public branch is rewritten on every cut, and that the push ignores the remote it resolves.

### 1.1 Inputs to read

- **DBL:** none in this repo.
- **Bridge reports:** none.
- **Source:** `Tools/public-cut/cut.mjs` (whole, 456 lines); `Tools/public-cut/test.mjs` (whole, 196 lines); `Tools/public-cut/README.md`; `README.md:550-560` (install options), `Tools/nissth-bridge/consumer-launcher/README.md` "Updating Nissth in your project".
- **StatusUpdate.md:** latest entry 2026-10-18 18:05 +03:00 (topology switch done), whose `Next:` names this phase.

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | How the cut is built | read `cut.mjs:280-290` | tool | orphan or parented? |
| 2 | Where `--push` goes | read `cut.mjs:438-449` | tool | is `publishRemote()` used? |
| 3 | Commit-count gate | read `cut.mjs:390-391` | tool | does a gate forbid history? |
| 4 | Remotes and public tip | `git remote -v`, `git ls-remote public` | repo | the target and base of the next release |
| 5 | Tags anywhere | `git tag`, `git ls-remote --tags public` | repo | is "pin to a tag" possible today? |
| 6 | Baseline | `node --test Tools/public-cut/test.mjs` | tool | count before change |

### 1.3 Findings

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| 1. Cut shape | orphan, previous branch deleted | `git branch -D` then `worktree add --orphan` (`:282-288`) | yes |
| 2. Push target | resolved remote | **`origin`, hard-coded** (`:441`); `remote` from `publishRemote()` (`:438`) is computed and never used; both hint messages also say `origin` | no — defect, folded in (Step 3) |
| 3. History gate | — | `commitCount !== 1` fails the cut (`:391`) | yes |
| 4. Remotes / tip | `origin` private, `public` public, `public/master` = `5380be3` | as expected | yes |
| 5. Tags | none | none locally or on `public` | yes |
| 6. Baseline | 16/16 | 16/16 | yes |

Row 2 is the same "computed and quietly unused" shape recorded on 2026-09-25. It turned the 18:05 ledger claim ("`--push` now reaches the public repo with no flag") false; the closing entry corrects it.

---

## 2. Expected State

### Before

| Target | Property | Value |
|:---|:---|:---|
| public `master` | history | one orphan commit, replaced by force on every cut |
| `--push` | target | `origin` regardless of remotes |
| tags | — | none; the README tells users to pin one |

### After

| Target | Property | Value |
|:---|:---|:---|
| a cut | parent | the current `<remote>/master`, fetched first; `--initial` (explicit) for a remote with no `master`, refused when one exists |
| a cut whose tree equals its base | result | no commit; "nothing to release", exit 0 |
| gates | history | HEAD has exactly one parent and it is the base (orphan only with `--initial`), replacing "exactly 1 commit" |
| `--push` | target / mode | the resolved remote (`public` when present); **no force**; `--atomic` with the tag |
| `--tag vX.Y.Z` | validation | semver; refused when it exists locally or on the remote, or is not above the highest existing `v*` tag |
| suites | public-cut | 16 + new, green |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** Parented cut. **File:** `Tools/public-cut/cut.mjs:244-359`. **Operation:** modify. Resolve the remote, `git fetch <remote> master` (skipped with `--base <ref>`, which the tests use). Base = `<remote>/master`. Build the worktree detached at the base, `git rm -r -q .`, then `git checkout <dev head> -- .`, then the existing delete / scrub / strip / ledger steps unchanged. Commit with the base as parent and point `nissth/public` at it. No staged change → no commit, `unchanged: true`. No base and no `--initial` → refuse (`no_base`); `--initial` with a base → refuse (`base_exists`).
- [x] **Step 2.** Gates. **File:** `cut.mjs:379-396`. Replace `commitCount !== 1` with "HEAD's parents = [base]" (or [] under `--initial`). **Acceptance:** Step 5 tests.
- [x] **Step 3.** Push. **File:** `cut.mjs:398-452`. `--push` sends `nissth/public:master` (and the tag) to the resolved remote with `--atomic`, no `--force`; a non-fast-forward rejection is reported as "the public branch moved since the fetch", exit 1. Hint text names the real remote. Exported `publish()` so tests can run it against bare repos.
- [x] **Step 4.** `--tag vX.Y.Z`. **File:** `cut.mjs`. Validate `^v\d+\.\d+\.\d+$`; refuse an existing tag (local or remote) and one not above the highest `v*` tag; create an annotated tag on the cut commit. The commit message names the tag and the base.
- [x] **Step 5.** Tests. **File:** `Tools/public-cut/test.mjs`. **Operation:** add. A synthetic dev repo (Axiom, LICENSE, CLAUDE.md/README.md trees with `public-cut/` and `Axiom/` rows, ledger) and bare remotes. Cases: the first cut needs `--initial`; the second cut's parent is the remote tip and its diff holds only the changed file; an unchanged tree → no commit; `--initial` over an existing base → refused; `publish()` pushes to `public` and not `origin` when both exist, without force, and rejects a moved remote; tag validation (format, duplicate, not-above). **Acceptance:** suite green; a mutation that restores the hard-coded `origin` fails a test.
- [x] **Step 6.** Docs. `Tools/public-cut/README.md` ("What it does", "Pushing", usage), root `README.md` install section (tags exist; `git pull` works), consumer-launcher README "Updating Nissth" (real tag example). **Operation:** modify.
- [x] **Step 7.** Harvest Report: proposed 28–32 → 29–33, revision line.
- [x] **Step 8.** Field run: `cut.mjs --tag v0.28.0` locally against `public/master` = `5380be3`; read the gates and `git diff --stat 5380be3 nissth/public`. Pushing and tagging the public repo are outward-facing: **only on the user's word**.

### 3.2 Forbidden in this phase

- Editing `CLAUDE.md` — the §5 line stays true; a body change would put every consumer into drift for nothing.
- Changing the scrub map, the deletion list, the strip, or the ledger reset — the scrubbed tree is unchanged by this phase, only its history.
- Rewriting the existing public commit `5380be3`; it becomes the root of the release history.
- GitHub Releases, changelog generation, CI — later, if wanted.
- Pushing without the user's word.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Suites run with `node --test` from the edited files on disk; the tool has no build step.
- The field run builds from the committed `dev` tip (the tool refuses a dirty tree) against a freshly fetched `public/master`.
- Development directory; no binding build input changed, so §8.x.6 does not apply.

### 4.2 Checks

- [x] **Tests:** `node --test Tools/public-cut/test.mjs` — all pass, above 16; mutation check on the push target.
- [x] **Field run:** the cut's parent = `5380be3`; gates pass; `Axiom/` intact in the primary directory; the working directory still on `dev`.
- [x] **Validators:** `doc-claims` exit 0; `plan-lint --plan` this file, no errors.
- [x] **Other suites unaffected:** dispatcher 41/41, nissth-init 39/39.

### 4.3 Pass criteria

- A cut is a child of the published tip, and the tests prove it on real git objects.
- `--push` cannot reach `origin` while `public` exists; proven by a test.
- An existing or non-increasing tag is refused.

### 4.4 Failure handling

If any check fails: STOP, append a `Verified: FAIL` entry with the artifact, do not retry silently.

---

## 5. Cleanup

- [x] Temp repos removed by the tests; no scratch worktree left (`git worktree list`).
- [x] Snapshot: ref only (`dev` before this phase); nothing under `AgentReports/Snapshots/`.
- [x] Reports: none — the plan and the tool README carry the design.
- [x] Document Sync (HR#11): `cut.mjs` is cited by its README, `CLAUDE.md` §5 (unchanged, still true) and the root README (Step 6); run `doc-claims`.

---

## 6. Status Update Entry

```
### YYYY-MM-DD HH:MM±hh:mm — Phase 28: Incremental public releases — CLOSED

**State:** Phase 28 closed · Tests: public-cut N/N, dispatcher 41/41, nissth-init 39/39 · Blockers: none
**Report:** [§1] · **Executed:** [§3] · **Verified:** [§4] · Doc sync: … · Reports: none
**Issues:** correction of the 18:05 `--push` claim; [others]
**Next:** Phase 29 (ledger and plan contract — harvest rows 3–7, 15).
```
