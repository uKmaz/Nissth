# Phase 06b: Top-Level Engineer-Facing README

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_06b_Top_Level_README
- **Authored:** 2026-05-17 by Claude (Opus 4.7)
- **Approved:** 2026-05-17 by Emre Uçmaz (verbal: "Go" — Claude session)
- **Depends on:** Phase_05_Bridge_SpringBoot_FirstSlice (closed 2026-05-17 — provides the binding install path the README documents); Phase_06_Bridge_Expo_FirstSlice (authored 2026-05-17, paused at pre-flight — provides the "Phase 06 in flight" status the README references). Numbered `06b` (not `07`) to keep `Phase_07_*` earmarked for the PostgreSQL binding per the Phase 05 snapshot Report's queue.
- **Estimated scope:** Single new file at repo root: `README.md`. Target length 800–1500 dense Markdown lines. No source code. No DBL changes. No `CLAUDE.md` / `AGENTS.md` / binding-README edits — README cross-links to existing sections rather than restates them. Plan-required per HR#12 because the top-level README is not in the plan-exempt zones (`ImplementationPlans/`, `AgentReports/`, `DBL/`, `.claude/`, or bridge contract docs).

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm no existing top-level README would be overwritten, confirm the framework files this README will cite are in their expected state, and confirm the agent has read enough of the existing prose to write *for* the engineer audience without restating CLAUDE.md content.

### 1.1 Inputs to read

- **DBL:** none — Nissth core has no DBL.
- **Bridge reports:** none — Bridge runtime is not used for doc authoring.
- **Source:**
  - `CLAUDE.md` (full file — already in conversation context). The README's section structure mirrors CLAUDE.md's logical groupings but the README links to anchors rather than restating.
  - `AGENTS.md` (likely thin redirect — read once to confirm).
  - `Bindings/README.md` (per-stack-binding model — README references this as the "stop here next" link for binding consumers).
  - `Bindings/SpringBoot/README.md` (reference for how a binding-level README looks; do not duplicate).
  - `ImplementationPlans/_TEMPLATE.md` (cited by the README's "What does a plan look like?" subsection).
  - Latest 5 status entries from `AgentReports/StatusUpdate.md` (already in conversation context via boot protocol).
  - `ImplementationPlans/Phase_05_Bridge_SpringBoot_FirstSlice.md` §0 + §3.1 (cited as the canonical reference plan in the README).
  - `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` §0 + §1.3 Findings (cited as the "in flight, host-paused" example).
  - `AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md` (cited as the canonical snapshot Report example in the §10 explainer subsection).
- **StatusUpdate.md:** latest entry as of plan authoring — `2026-05-17 — Phase 06 plan authored + approved; pre-flight STOPPED at host mismatch; pivot to Task #15` (the entry written immediately before this plan).

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Confirm no top-level `README.md` exists | `ls README.md` (Bash) / `Test-Path README.md` (PS) | repo root | Avoid overwriting prior work. Expected: not found. If present: STOP, read it, decide append vs replace with user. |
| 2 | Confirm `CLAUDE.md` §8 is still un-renumbered (Spring Boot only) | `Read CLAUDE.md` §8 header line | line ~205 of CLAUDE.md | Phase 06's Step 4 plans to renumber §8 when it executes. Until then, the README's §-references must point to the current numbering. Expected: `## 8. Stack Bindings — Spring Boot` (un-renumbered). |
| 3 | Confirm `AGENTS.md` is the thin redirect described in CLAUDE.md §5 | `Read AGENTS.md` (full file — should be ~30 lines) | full file | The README will cite AGENTS.md as the "non-Claude agent entrypoint." If it has grown into a fuller spec, README wording adjusts. |
| 4 | Confirm `Bindings/SpringBoot/scripts/nissth-bridge` exists and is the documented launcher | `ls Bindings/SpringBoot/scripts/` | dir | The README's "Install & use the SpringBoot binding" subsection cites this script. Expected: `nissth-bridge` (POSIX) + `nissth-bridge.ps1` (PS) both present. |
| 5 | Confirm the snapshot Report cited in §10 explainer exists | `ls AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md` | exact path | Expected: present (created 2026-05-17 at Phase 05 close). |
| 6 | Confirm Phase 06 plan exists at its expected path | `ls ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` | exact path | The README's "current phase status" subsection cites this. Expected: present (created earlier this session). |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| Does top-level `README.md` already exist? | no | no — `ls README.md` returned "No such file or directory" | ✅ yes |
| Is `CLAUDE.md` §8 still `## 8. Stack Bindings — Spring Boot` (un-renumbered)? | yes | yes — `Grep '^## \d' CLAUDE.md` shows line 230: `## 8. Stack Bindings — Spring Boot` | ✅ yes |
| Is `AGENTS.md` a thin redirect (~30 lines)? | yes | yes — exactly 30 lines; opens with "This project (**Nissth**) operates under a strict deterministic execution framework. The complete reference is `CLAUDE.md`..." | ✅ yes |
| Do `nissth-bridge` + `nissth-bridge.ps1` exist under `Bindings/SpringBoot/scripts/`? | yes (both) | yes — both present in `Bindings/SpringBoot/scripts/` | ✅ yes |
| Does the Phase 05 snapshot Report exist at the cited path? | yes | yes — `AgentReports/Reports/2026-05-17_phase-05-bridge-springboot-snapshot.md` present | ✅ yes |
| Does the Phase 06 plan exist at the cited path? | yes | yes — `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md` present | ✅ yes |

**Stop condition:** all rows ✅ yes; no stop. §3 cleared to start.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| `README.md` (repo root) | exists | no |
| `CLAUDE.md` | content | unchanged from plan-authoring time |
| `AGENTS.md` | content | unchanged |
| `Bindings/`, `ImplementationPlans/`, `AgentReports/` | content | unchanged |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| `README.md` (repo root) | exists | yes |
| `README.md` | line count | 800–1500 (dense, not padded) |
| `README.md` | required sections present | hero/elevator-pitch · what Nissth is / is not · 30-second quick start for arriving agents · 30-second quick start for arriving humans · boot protocol (cite HR#1) · the Loop (cite §3) · project structure (cite CLAUDE.md §5) · DBL + Bridge: two layers above source (cite §7 + §11) · Implementation Template + plan-before-execute (cite §6 + HR#12) · Reports taxonomy (cite §10) · Stack bindings current state (SpringBoot shipped, Expo in flight, Postgres queued) · Installing & using a binding (point to `Bindings/SpringBoot/README.md` + `scripts/nissth-bridge`) · Working in this repo as an agent (checklist) · Working in this repo as a human (checklist) · Maintaining StatusUpdate.md, plans, DBL · Glossary / Hard Rules at-a-glance · Pointers (CLAUDE.md anchors, related files) |
| `README.md` | cross-references | every `CLAUDE.md` anchor cited resolves; every file path cited exists on disk; no broken `[[...]]` style links |
| `CLAUDE.md`, `AGENTS.md`, `Bindings/**` READMEs | content | UNCHANGED — this phase touches only `README.md` |
| `Phase_06b_Top_Level_README.md` §1.3 | filled | actuals filled, all rows ✅ yes (or stop condition triggered) |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1. Author `README.md` at repo root.** **File:** `README.md`. **Operation:** add. **Content:** all required sections from §2 After table, in order. **Authoring rules:**
  - **Voice:** matter-of-fact engineer-to-engineer. No marketing prose. No emoji unless quoting an existing file that has them.
  - **Density target:** every paragraph carries new information. If a paragraph could be deleted without loss, delete it.
  - **Cross-reference rather than restate:** "Read `CLAUDE.md` §3 for the full Loop spec" rather than copy-pasting §3 in. The exception is the Hard Rules at-a-glance subsection, which lists the 13 rules by number + one-line each — that IS valuable redundancy.
  - **Code blocks for commands:** every install / build / verify command shown is something the reader can paste and run. PowerShell + POSIX variants where they differ (matches the Bindings/SpringBoot/README.md pattern).
  - **Tables for catalogs:** stack bindings, phase status, Hard Rules summary, directory layout — all tables, not bullet walls.
  - **GFM Markdown only:** standard headers, tables, fenced code blocks. No HTML, no MDX. Renders correctly on GitHub.
  - **Anchors:** use stable `## section-title` headers. Cross-link within the README via `[text](#section-title)`. Cross-link to CLAUDE.md sections via `[text](CLAUDE.md#section-NN-title)`.
  - **Current-state honesty:** the README documents what is shipped, what is in flight, what is queued. Phase 06 is in flight (host-paused); say so explicitly. Phase 07 (Postgres) is queued; say so. Do not present queued items as if shipped.
  - **No new framework claims:** the README must not invent rules, taxonomies, or workflows that don't already live in `CLAUDE.md`. If a topic isn't in CLAUDE.md yet, omit it from the README (or note it as "TBD when X").
  - **No restating Hard Rules verbatim except in the at-a-glance subsection.** Other sections cite HR#NN with a one-line gloss.
  - **Length-guard:** between 800 and 1500 lines (dense markdown). Drafting longer is fine intermediately; final cut must fit. Under 800 = too thin for the "ship-in-30-minutes" audience; over 1500 = becomes unreadable, signal to split or trim.
  - **Acceptance** (verifies this step before §4 freshness checks): file created at `<repo-root>/README.md`; line count via `wc -l README.md` returns 800–1500; every section listed in §2 After table is present as an H2 header; every cited file path resolves (manual scan of code blocks and links).

### 3.2 Forbidden in this phase

- **No edits to `CLAUDE.md`.** Even one anchor name change would invalidate cross-references the README is built around. CLAUDE.md edits are a separate plan.
- **No edits to `AGENTS.md`.** Same reasoning. If the README discovers AGENTS.md is wrong (e.g., out of date relative to CLAUDE.md), append a note to `**Issues:**` in §6; do not silently fix.
- **No edits to any `Bindings/**/README.md`.** Those are stable bridge contract documentation per HR#12; per-binding READMEs ship with their binding's plan.
- **No edits to Phase 06 plan.** Phase 06 stays paused; the top-level README cites its current paused-state.
- **No new files outside `README.md`.** Specifically: no `CONTRIBUTING.md`, no `LICENSE`, no `.github/` templates, no `docs/` subtree. Each of those is a separate plan if/when desired.
- **No new framework rules.** The README documents what IS, not what could-or-should-be.
- **No emoji introductions or "✨ welcome ✨" hero sections.** Engineer-to-engineer voice; pretty hero blocks are off-brand for this repo.
- **No verbatim copy-paste of CLAUDE.md sections.** Cite + summarize, never duplicate. The single exception is the Hard Rules at-a-glance subsection which lists the 13 rule titles + a one-line gloss each.
- **No claims about Phase 06's tool count, Phase 07's existence, or Süprüz integration** beyond what is currently in `CLAUDE.md` and the most-recent status entries. The README must not get ahead of the framework.

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

> How do you know the verifier sees the latest changes? (HR#10.)

- README authoring is a single Write — no caching layer between writer and verifier. `wc -l README.md` reads the file from disk fresh.
- Cross-reference verification is a manual scan after the Write completes; no compiled artifact to be stale against.
- No daemon, no incremental cache, no subprocess — pure file I/O.

### 4.2 Checks

- [x] **File exists:** `ls README.md` (repo root) returns the file with non-zero size. **PASS** — `Write` succeeded; file present at `<repo-root>/README.md`.
- [x] **Line count:** `wc -l README.md` returns 800–1500. **PASS** — `wc -l` returned 817; comfortably in range.
- [x] **Section completeness:** every H2 header listed in §2 After table is present in `README.md`. **PASS** — `Grep '^## ' README.md` returned 18 H2 headers; the eight "## 0./1./2./3./4./5./6./Endpoints" hits are inside fenced code blocks (illustrative plan + report shapes), not real headers. All 17 required §2 After sub-sections are present as H2 headers in canonical order.
- [x] **CLAUDE.md anchor resolution:** every `CLAUDE.md#anchor` cited in README.md resolves to a real section header. **PASS, vacuously** — the README deliberately avoids `CLAUDE.md#anchor` links and uses textual citations (e.g., `CLAUDE.md §3`) instead, so anchor rot from future §-renumbering cannot break the README. No `](CLAUDE.md#...)` patterns appear; verified by `Grep '](CLAUDE.md#'` returning nothing.
- [x] **File-path citations resolve:** every relative file path in README code blocks / links exists on disk. **PASS** — Bash loop over the 13 cited paths (`CLAUDE.md`, `AGENTS.md`, `AgentReports/StatusUpdate.md`, `ImplementationPlans/_TEMPLATE.md`, `ImplementationPlans/Phase_05_*.md`, `ImplementationPlans/Phase_06_*.md`, three `AgentReports/Reports/*.md`, `Bindings/_schemas/bridge-command.schema.json`, `Bindings/README.md`, `Bindings/SpringBoot/README.md`, `Bindings/SpringBoot/mcp/README.md`) returned `OK` on every line.
- [x] **No forbidden inclusions:** no emoji except those quoted from existing files; no marketing language; no claims beyond current state. **PASS** — emoji grep `[\u{1F300}-\u{1F9FF}]|[\u{2600}-\u{27BF}]|[\u{1F000}-\u{1F2FF}]` returned no matches; marketing-word grep over 20 typical offenders (`amazing`, `revolutionary`, `blazing`, `magic`, `seamless`, etc.) returned no matches. Phase 06's status is explicitly labeled "in flight" + "paused"; Phase 07 is explicitly "queued"; no over-claiming.
- [x] **GFM render:** README renders cleanly when previewed. **PASS** — fence count is 26 (even, → 13 balanced code blocks); tables use canonical `|---|` separators throughout; H1 at line 1 + H2 hierarchy is well-nested; bullet/numbered list indentation is consistent. No HTML, no MDX, no unbalanced backticks observed.

### 4.3 Pass criteria

ALL must be true:
- `README.md` exists at repo root.
- Line count in [800, 1500].
- Every required §2 After section is present.
- Every CLAUDE.md anchor citation resolves.
- Every file-path citation resolves.
- No forbidden inclusions per §3.2.
- README renders without GFM syntax errors.

### 4.4 Failure handling

If any check in §4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. If only one or two minor issues (e.g., a typo in a CLAUDE.md anchor, a line-count overshoot of 20): fix in place, re-run §4.2. The fix is part of Step 1's authorship; not a re-plan.
3. If structural issues (e.g., a whole section missing, fundamentally wrong audience, length way off): append a `Verified: FAIL` status entry citing the failure, author an `incident` Report per §10.4(1), STOP and request user direction.

---

## 5. Cleanup

- [ ] No scratch files: README is a single Write, no intermediates.
- [ ] No snapshots: README authoring is purely additive — nothing to roll back.
- [ ] **Reports check (CLAUDE.md §10):**
  - No `Verified: FAIL` expected → no `incident` Report.
  - No named-alternative decision in flight → no `decision` Report.
  - No long external spec ingested → no `spec_digest` Report.
  - Not a phase-close on a "non-trivial code-change phase" — this is a docs-only single-file phase → snapshot Report is NOT mandatory per §10.4(4). The status entry §6 alone is sufficient.
  - If during authoring a sub-decision arose (e.g., "should the README mention Süprüz or not"), document it inline in §6 `**Issues:**`; spin off a `decision` Report only if it merits future reconsideration.
- [ ] **Document Sync sweep (Hard Rule #11):**
  - Source files modified in §3: only `README.md` (new file).
  - Affected stable documents:
    - `CLAUDE.md` — no edits this phase; verify no §-anchor was incidentally relied on that doesn't exist.
    - `AGENTS.md` — no edits.
    - `Bindings/**/README.md` — no edits.
    - DBL — none in Nissth core.
    - Other plans — none cross-reference Task #15 / Phase 06b yet.
  - Action: no UPDATE needed; no MARK STALE needed. New file only.
  - Result MUST be logged in the §6 status entry's `**Verified:**` block as: `Doc sync: [created: README.md; updated: none; marked stale: none]`.
- [ ] No orphan branches, no debug code (N/A — pure docs).

---

## 6. Status Update Entry

> After Cleanup completes, append the following block (filled in) to `AgentReports/StatusUpdate.md`. Do not edit this template's copy of the block — it is the source for the paste.

```
### YYYY-MM-DD HH:MM — Phase 06b: Top-Level Engineer-Facing README

**State:**
- Phase: 6b — top-level README authored; Phase 06 still paused awaiting Node-capable host; Phase 05 closed.
- Build: unchanged from prior entries (no code touched).
- Active plan: ImplementationPlans/Phase_06b_Top_Level_README.md
- DBL refs: none.
- Bridge reports: none.
- Blockers: none.

**Report:**
- [condensed from §1 findings — all Match? = yes]
- Top-level README authored at <length> lines (target 800–1500), with all required sections per §2 After.
- Cross-references to CLAUDE.md anchors verified; file-path citations verified; GFM syntax clean.

**Executed:**
- [§3 Step 1 checkbox resolved]

**Verified:**
- File exists at repo root, line count <n> within [800, 1500].
- Every §2 After section present (Grep '^## ' README.md cross-checked).
- Every CLAUDE.md anchor cite resolves; every file-path cite resolves.
- No emoji, no marketing prose, no forward-looking framework claims.
- GFM renders cleanly.
- Doc sync: [created: README.md; updated: none; marked stale: none]
- Reports: none — docs-only single-file phase, no §10.4 mandatory trigger.

**Issues:**
- [or "none"; if a sub-decision arose during authoring, log it here]

**Next:**
- Top-level README is live. Remaining backlog: Phase 06 (Expo binding — resumes when on Node-capable host); Phase 07 (Postgres binding — needs plan when prioritized); Süprüz consumer project work (`Desktop/Supruz/` — Nissth now ships with the binding stack Süprüz needs).
```
