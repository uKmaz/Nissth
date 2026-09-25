# Phase 17: Greenfield Phase 00 + Expo Local-Persistence / Dev-Client Rules — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_17_Greenfield_And_Expo_Local_Rules
- **Authored:** 2026-09-13 by Claude (Opus 5)
- **Approved:** 2026-09-13 by user (Emre Uçmaz) — blanket: "You have all the permission to move on as Nissth framework does step by step"
- **Depends on:** Phase_15_Consumer_Init_Tooling (so §9.1 can name `nissth-init`), Phase_16_DBL_Check (so §7.6 can name the `design-only-source-exists` check). If executed before either closes, the corresponding sentence is written in the future tense and flagged in `Issues:`.
- **Estimated scope:** Documentation only, but `CLAUDE.md` edits are plan-required (HR#12). Sites: §7.6 (greenfield mode), §8.2.1 (dev-client row), §8.2.3 (commands table +3 rows), §8.2.4 (SchemaIndex row rewritten), §8.2.5 (+2 forbidden patterns), §8.2.6 (dev-client verification sentence), new §8.2.10 (local-schema ripple), §8.2.9 (step 3 rewritten), §9.1 (greenfield note); `Ultimate_Guide.md` §4.1 (init tool), §6.6 (local SQLite note), §7.3 (dbl-check) — the two deferrals from Phases 15/16; `README.md` status line. ≈ 120 changed lines total. Audit Report F7 (text half), F8, F9.

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- **DBL:** none.
- **Bridge reports:** none.
- **Source:** `CLAUDE.md` lines 219–229 (§7.6), 376–414 (§8.2.1–8.2.2), 416–438 (§8.2.3–8.2.4), 439–452 (§8.2.5), 453–475 (§8.2.6), 481–494 (§8.2.8), 495–506 (§8.2.9), 626–640 (§9.1); `Ultimate_Guide.md` §4.1, §6.6, §7.3; `README.md:5` (status line). Reference: `<path>/ExampleFinanceApp\ImplementationPlans\Phase_00_DBL_Bootstrap.md` §0 and `SDD.md` §12 (the improvised greenfield/local-SQLite rules to be promoted).
- **StatusUpdate.md:** latest entry at execution time (Phase 15/16 closes expected).

### 1.2 Diagnostic actions

| # | Action | Tool/command | Scope | Why |
|:---|:---|:---|:---|:---|
| 1 | Line anchors still valid | `Grep -n '^### 7.6\|^#### 8.2.[0-9]*\|^### 9.1' CLAUDE.md` | 1 file | Phases 15/16 append/edit CLAUDE.md; re-anchor before editing |
| 2 | Phase 15/16 closed? | tail of `StatusUpdate.md` | ledger | Decides tense of the two cross-references |
| 3 | doc-claims baseline | `node Tools/doc-claims/validate.mjs` → exit 0 | repo | Must stay 0 after edits |
| 4 | Tree clean | `git status --short` | repo | Baseline |

### 1.3 Findings (filled during execution)

| Question | Expected answer | Actual answer | Match? |
|:---|:---|:---|:---|
| anchors resolve | 10 headings found | 12 (§7.6, §8.2.1–8.2.9, §8.3, §9.1) — all +4 lines vs. the plan's numbers after Phases 15/16 | yes |
| Phases 15, 16 closed | yes / yes | yes (`48beaa7`+`4aed23f`) / yes (`ab0fe79`+`3e46b39`) — cross-references written in the present tense | yes |
| doc-claims | exit 0 | exit 0 | yes |
| `git status` | empty | empty | yes |

**Stop condition:** If any row's `Match? = no`, STOP — the plan was authored against stale state. Append a `Verified: FAIL` status entry and request a re-plan.

---

## 2. Expected State

### Before (current state, per Pre-Flight)

| Target | Property | Expected value |
|:---|:---|:---|
| §7.6 | greenfield guidance | none — assumes source exists |
| §8.2.3 | dev-client rows | none; "Dev server … QR code for Expo Go" |
| §8.2.4 SchemaIndex row | text | "**No SchemaIndex by default** … borrow §8.1.4 on a per-project basis" |
| §8.2.9 step 3 | text | "No baseline-migration step … borrow the §8.1.8 Flyway-baseline pattern" |
| §8.2.10 | existence | absent |
| `Ultimate_Guide.md` §4.1 step 2 | text | manual copy tree |

### After (post-execution target)

| Target | Property | Expected value |
|:---|:---|:---|
| §7.6 | new paragraph "Greenfield projects" | When no source exists, Phase 00 derives every artifact from the approved SDD with `source_state: design-only — SDD.md §<n> approved YYYY-MM-DD; no source yet` and a first `stale_when` entry `any file is created under a covers path — Phase 01 regenerates from source`. `Tools/dbl-check` reports `design-only-source-exists` the moment that happens; Phase 01's §5 must clear all of them. Design-only artifacts are citable in §1 Inputs as *intent*, never as *evidence* (HR#6). |
| §8.2.1 | new row | `Workflow` — `Expo Go` (default) **or** `development build` (required when any native module / config plugin / widget / App Intent / App Group is used; SRS states which). Development builds on a host without Xcode go through EAS Build. |
| §8.2.3 | +3 rows | `Dev server (dev client)` `npx expo start --dev-client` · `Development build (device)` `eas build --profile development --platform ios` · `Migration generation (Drizzle)` `npx drizzle-kit generate` (when §8.2.10 applies) |
| §8.2.4 SchemaIndex row | rewritten | `SchemaIndex — only when the app owns a local database (expo-sqlite, op-sqlite, WatermelonDB …)`: one `DBL/SchemaIndex/<db>.md` per database file; source signal = the ORM schema module (`src/db/schema.ts` for Drizzle) + migrations dir; extract tables/columns/CHECKs/indexes/FKs/seeds + migration baseline. Apps that delegate persistence to a backend still have none. |
| §8.2.5 | +2 patterns | 12. **No two writers on a local SQLite file.** Native extensions (widgets, intents, share targets) run in a separate process; they communicate via files/UserDefaults in the App Group, never by opening the DB. 13. **No float arithmetic on money.** Integer minor units in the domain; formatting only at the display edge; enforce with a lint rule *and* a test — a lint config edit must not be able to silently disable it. |
| §8.2.6 | +1 clause | Dev-client projects: the on-device check (`eas build --profile development` + install + launch) is part of any phase that changes native config (`app.json` plugins/entitlements, `targets/`); if the device or EAS account is unavailable, the phase closes with `Runtime: NOT_RUN` and a blocker — never as a pass. |
| §8.2.10 (new) | "Local-schema ripple (Hard Rule #11 specialization for apps that own a local DB)" | Any change to the ORM schema module ⇒ (1) a new versioned migration (`drizzle-kit generate` output committed) **and** (2) `DBL/SchemaIndex/<db>.md` refresh; both in the closing `Doc sync:` line, mirroring §8.1.9. |
| §8.2.9 step 3 | rewritten | points at §8.2.4's SchemaIndex row and §8.2.10; greenfield note "if no source exists yet, see §7.6 Greenfield" |
| §9.1 step 2 | +1 sentence (if Phase 15 closed) | already names `nissth-init`; add "greenfield projects: Phase 00 uses §7.6 Greenfield mode" |
| `Ultimate_Guide.md` §4.1 step 2 | text | replaced by the `nissth-init` command + "creates:" tree; §6.6 gains "if the app owns a local SQLite DB, see CLAUDE.md §8.2.10"; §7.3 gains one sentence on `dbl-check` |
| `README.md:5` | status line | mentions `nissth-init` and `dbl-check` under Tools (counts untouched) |
| doc-claims | exit | 0 |

---

## 3. Execution (EXECUTE)

> Each step MUST be atomic and verifiable. Do not bundle "and also fix X."

### 3.1 Step list

- [x] **Step 1.** `CLAUDE.md` §7.6 — append the "Greenfield projects" paragraph. **Operation:** modify. **Acceptance:** `Grep 'design-only' CLAUDE.md` ≥ 2 hits.
- [x] **Step 2.** §8.2.1 — add the `Workflow` row. **Acceptance:** table renders (pipe count matches header).
- [x] **Step 3.** §8.2.3 — add the three rows; reword the "Dev server" row to "(Expo Go workflow)". **Acceptance:** 10 rows.
- [x] **Step 4.** §8.2.4 — rewrite the SchemaIndex row. **Acceptance:** row no longer says "No SchemaIndex by default".
- [x] **Step 5.** §8.2.5 — append patterns 12 and 13. **Acceptance:** list ends at 13.
- [x] **Step 6.** §8.2.6 — add the dev-client clause after item 6. **Acceptance:** `Grep 'NOT_RUN' CLAUDE.md` includes §8.2.6.
- [x] **Step 7.** Insert §8.2.10 after §8.2.9 (before §8.3). **Acceptance:** heading present; §8.3 unchanged.
- [x] **Step 8.** §8.2.9 step 3 rewrite + §9.1 greenfield sentence. **Acceptance:** §8.2.9 no longer says "borrow the §8.1.8 Flyway-baseline pattern".
- [x] **Step 9.** `Ultimate_Guide.md` §4.1 / §6.6 / §7.3 edits. **Acceptance:** `Grep 'nissth-init\|dbl-check' Ultimate_Guide.md` ≥ 3 hits.
- [x] **Step 10.** `README.md:5` status line. **Acceptance:** doc-claims exit 0.
- [x] **Step 11.** Re-read the ExampleFinanceApp `Phase_00` §0 improvisation against the new §7.6 text — they must agree; if the consumer did something the new rule forbids, record it in `Issues:` for that repo (do not edit it).

### 3.2 Forbidden in this phase

- Any code change (`Tools/**`, `Bindings/**`).
- Editing §8.1 / §8.3 or §11 (contract).
- Re-numbering existing §8.2.5 patterns or any §11.x.
- Editing the ExampleFinanceApp repo.
- Re-cutting the public branch (separate decision; its `Axiom/` strip is a known open item).

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Documentation only; the verifier is `doc-claims` + greps run after the last edit in the same session. No fresh-worktree run needed (no build inputs changed) — state this explicitly in the entry.

### 4.2 Checks

- [x] **Build:** N/A.
- [x] **Tests:** N/A for the docs; run `node Tools/doc-claims/validate.mjs` → exit 0. RESULT exit 0, no findings.
- [x] **Structure:** RESULT §8.2.10 at line 522 (1 hit); 10 `#### 8.2.` headings; §8.3 heading intact at line 531. `Grep -n '^#### 8.2.10' CLAUDE.md` → 1; `Grep -c '^#### 8.2.' CLAUDE.md` → 10; §8.3 heading line unchanged relative to its content.
- [x] **Cross-refs:** RESULT `§8.2.10` ×3 CLAUDE.md + ×1 Ultimate_Guide; `design-only` ×4; §8.2.5 numbered 1–13 in order; §8.2.6 item 7 precedes the freshness statement. every `§8.2.10`, `§7.6`, `nissth-init`, `dbl-check` mention resolves to an existing heading/dir.
- [x] **Bridge re-query / DBL freshness:** N/A.

### 4.3 Pass criteria

ALL of the following must be true:
- doc-claims exit 0.
- Structure and cross-ref greps as above.
- `git diff --stat` touches only `CLAUDE.md`, `Ultimate_Guide.md`, `README.md`, this plan, the ledger.

### 4.4 Failure handling

If any check in 4.2 fails:
1. STOP. Do not proceed to Cleanup.
2. Append a status entry to `AgentReports/StatusUpdate.md` with `Verified: FAIL`, citing which check failed and the artifact location.
3. Do not retry silently. The user decides: re-plan, fix forward, or rollback.

---

## 5. Cleanup

- [x] Nothing temporary created.
- [x] **Reports check:** no §10.4 trigger (documentation phase; the audit Report already records the rationale) → `Reports: none`. Update the audit Report's `## Revision history` with "F7/F8/F9 text delivered by Phase 17".
- [x] **Document Sync sweep:** this phase *is* the sweep for Phases 15/16's deferrals. Log: `Doc sync: [updated: CLAUDE.md §7.6/§8.2.1/§8.2.3/§8.2.4/§8.2.5/§8.2.6/§8.2.9/§8.2.10/§9.1, Ultimate_Guide.md §4.1/§6.6/§7.3, README.md status line, audit Report revision line]`.
- [x] Commit `docs(phase-17): greenfield Phase 00 mode; Expo local-DB and dev-client rules`.

---

## 6. Status Update Entry

```
### YYYY-MM-DD HH:MM — Phase 17: Greenfield + Expo Local Rules

**State:**
- Phase: 17 closed
- Build: NOT_RUN — docs only
- Tests: NOT_RUN — docs only (doc-claims exit 0)
- Active plan: none
- DBL refs: none
- Bridge reports: none
- Blockers: none

**Report:**
- Pre-flight §1.3: [rows]

**Executed:**
- [sites list]

**Verified:**
- doc-claims exit 0; structure/cross-ref greps; no fresh-worktree run (no build inputs changed)
- Doc sync: [as §5]
- Reports: none — documentation phase; audit Report revision line added

**Issues:**
- [or none]

**Next:**
- Tell the ExampleFinanceApp session that §7.6 Greenfield, §8.2.10 and `dbl-check` now govern its Phase 01; then no pending Nissth work unless Phase 01 surfaces Expo-binding defects (expected: Phase 18 candidate).
```
