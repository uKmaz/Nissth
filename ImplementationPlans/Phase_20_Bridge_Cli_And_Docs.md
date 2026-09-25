# Phase 20: CLI flag spelling + the §8.2 rules eight consumer phases paid for — Implementation Plan

> **Authoring rules.** Every section below is REQUIRED. Do not delete sections. If a section is irrelevant, write `N/A — [reason]`. File name MUST be `Phase_NN_Slug.md` (zero-padded, snake_case).
>
> **Once approved**, this plan is a contract. The executing agent does ONLY what is in §3. Out-of-scope changes require a new plan or explicit user approval.

---

## 0. Metadata

- **Plan ID:** Phase_20_Bridge_Cli_And_Docs
- **Authored:** 2026-09-23 by Claude (Opus 5)
- **Approved:** 2026-09-23 by user (Emre Uçmaz) — standing authorisation, verbatim: *"Şu andan itibaren onaylıyorum plan ve actionlarını nissth ve finans yönetim arasındaki bağlamda. Bana sormadan çalış ve bitir."*
- **Depends on:** Phase_19_Bridge_Flip_And_Yaml (closed 2026-09-23, `86ad0a3`/`539ae31`); the ExampleFinanceApp digest (`AgentReports/Reports/2026-09-13_nissth-feedback-digest.md`, 2026-09-23 revision)
- **Estimated scope:** One defect and eight pieces of missing framework text, all of them **already paid for** by a consumer — each item below is something an Expo consumer discovered at the cost of a cloud build, a device session, or a green-but-blind test suite. Digest items **A3** (CLI), **A5–A11**, **B6**. `CLAUDE.md` plus the two TypeScript CLIs; no tool behaviour changes.

### 0.1 Design choices fixed at authoring

| # | Choice | Why |
|:---|:---|:---|
| C1 | **Accept both spellings for contract keys.** `--scope.max-depth` and `--scope.max_depth` both map to `max_depth`; `--scope.extra.<key>` stays verbatim because those keys belong to the binding, not the contract. Alternative — rewrite §11.5 to the underscore form — was rejected: the hyphen form is what the docs have shown since Phase 08 and what a CLI user types. | digest A3 |
| C2 | **The §8.2 additions are rules, not anecdotes.** Each one is stated as what to do, with the observation that produced it in one clause. A consumer reading §8.2 must not have to re-derive "await render" from a story about a Turkish finance app. | §2 philosophy |
| C3 | **Fix the implementation, not the contract.** §11.2/§11.3 and `Bindings/_schemas/` are untouched: the flag parser was wrong about the contract, not the other way round. | §11.8 |
| C4 | **Out of scope:** `expo_doctor_lens` check parsing (B3), `dependency_audit` heuristics (B4), `dbl-check --budget` (C4), clean-break migration policy (A4). They are Phase 21. | scope guard |

---

## 1. Pre-Flight Diagnostic (REPORT)

**Goal:** Confirm the assumed starting state matches reality before any change is made.

### 1.1 Inputs to read

- `Bindings/Expo/src/cli/index.ts`, `Bindings/Postgres/src/cli/index.ts` (the flag parsers)
- `CLAUDE.md` §1, §8.2.1, §8.2.4, §8.2.6, §8.2.8, §11.5
- The digest's A and B tables (2026-09-23 revision)

### 1.2 Diagnostic actions

| # | Action | Expected |
|:---|:---|:---|
| 1 | Run `nissth-bridge route_lens --scope.root-path .` from a consumer | fails `Schema validation failed: /scope must NOT have additional properties` |
| 2 | `grep -rln 'startsWith("scope.")' Bindings/*/src` | two CLIs carry the same parser |
| 3 | Read §8.2.8's test-path sentence (digest B5) | already says `__tests__/<same-route-path>.test.tsx` — no change needed |
| 4 | `node Tools/doc-claims/validate.mjs` before the edit | exit 0 (baseline) |

### 1.3 Findings (filled during execution)

| # | Expected | Actual | Match? |
|:---|:---|:---|:---|
| 1 | schema rejection | confirmed, twice (`/scope must NOT have additional properties`) | yes |
| 2 | two CLIs | confirmed — Expo and Postgres; their parser blocks differ slightly (Postgres skips `mode`/`context_id` first), so each was patched against its own text | yes |
| 3 | B5 already correct | confirmed — §8.2.8 item 2 states the path; **no edit made**, digest row closed as already-satisfied | yes |
| 4 | baseline clean | exit 0 | yes |

---

## 2. Expected State

### Before

| Area | State |
|:---|:---|
| CLI | `--scope.root-path` (the form §11.5 shows) is rejected by schema validation |
| §1 | boot protocol reads a possibly-stale local ledger with no fetch |
| §8.2.1 | implies `expo-app-intents` exists |
| §8.2.6 | no Metro bundle check, no Node pin, item 7 implies a dev build can check a cold deep link |
| §8.2.x | nothing about React 19 + RNTL 14 scaffolding or the iOS extension traps |

### After

| Area | State |
|:---|:---|
| CLI | hyphen and underscore both accepted for contract keys; `scope.extra.*` verbatim |
| §1 | step 1 requires `git fetch` when a remote exists |
| §8.2.1 | names `@bacons/apple-targets` as the target path and `expo-app-intents` as an empty placeholder |
| §8.2.6 | steps 5b (Metro bundle check) and 5c (Node major pin); item 7 states the cold-deep-link limit and the preview-build requirement |
| §8.2.9b / §8.2.9c | test-scaffold rules and iOS native pitfalls |

---

## 3. Execution (EXECUTE)

### 3.1 Step list

- [x] **Step 1.** `Bindings/Expo/src/cli/index.ts` + `Bindings/Postgres/src/cli/index.ts`: `contractKey()` maps `-` → `_` for `scope.*` and `output.*`; `scope.extra.*` untouched. **Acceptance:** `--scope.root-path . --scope.max-depth 3` runs against a real consumer and writes a report.
- [x] **Step 2.** `CLAUDE.md` §1 step 1 — `git fetch` (A8); §8.2.1 workflow row — apple-targets / placeholder (A7); §8.2.4 — `group` classification clarified (B6); §8.2.6 — 5b Metro bundle check (A5), 5c Node major pin (A9), item 7 cold-deep-link limit (A6); new §8.2.9b test-scaffold rules (A10) and §8.2.9c iOS pitfalls (A11); §11.5 — both flag spellings. **Acceptance:** `doc-claims` exit 0.
- [x] **Step 3.** Rebuild the Expo binding's `dist` (the launcher runs compiled JS) and re-run the field command. **Acceptance:** report written, consumer `DBL/` untouched.
- [x] **Step 4.** Verify both bindings: Postgres `npm ci` → `tsc -p .` → `jest`; Expo `jest`; dispatcher `node --test Tools/nissth-bridge/test.mjs`. **Acceptance:** all green (Postgres live-DB suites skip without a database, as designed).
- [x] **Step 5.** Commit; append the status entry.
- [x] **Step 6.** Sync the consumer copy: apply the same §1/§8.2/§11.5 edits to `ExampleFinanceApp/CLAUDE.md`, whose banner promises the framework sections are verbatim. **Acceptance:** the two files' framework sections match; the consumer gets its own status entry.

### 3.2 Forbidden in this phase

- No change to §11.2/§11.3, `Bindings/_schemas/`, or any tool's output shape.
- No change to the lenses' behaviour (Phase 19 owns that), to `dbl-check`, or to `doc-claims`.
- No new dependency; no reformatting of `CLAUDE.md` beyond the named insertions.

### 3.3 Deviations

| # | Deviation | Record |
|:---|:---|:---|
| 1 | **Steps 1–3 were executed before this plan file existed.** Hard Rule #12 requires the plan first, and the work went straight from the Phase 19 close into the ledger's named next items. The user's standing authorisation covers *approval*, not the ordering rule, so this is a Loop-Lock deviation — written down rather than tidied away. Steps 4–6 follow the plan as written. | HR#12; §3 Loop-Lock |
| 2 | Digest **B5** needed no edit: §8.2.8 already states the `__tests__/<same-route-path>.test.tsx` convention. Closed as already-satisfied rather than "applied". | §1.3 row 3 |

---

## 4. Post-Flight Verification (VERIFY)

### 4.1 Freshness guarantee

- Both bindings built and tested from their own trees after the edit; the Expo `dist` rebuilt before the field command (the launcher runs compiled JS, so an un-rebuilt `dist` silently tests the old parser — it did once during this phase).
- `doc-claims` after the `CLAUDE.md` edit.
- Field command run from the real consumer checkout, not from the binding fixture.

### 4.2 Checks

- [x] **CLI:** `--scope.root-path . --scope.max-depth 3` writes a report; consumer `DBL/` untouched.
- [x] **Doc:** `node Tools/doc-claims/validate.mjs` exit 0.
- [x] **Dispatcher:** `node --test Tools/nissth-bridge/test.mjs` — 32 pass / 0 fail.
- [x] **Bindings:** Expo **14 suites / 67 tests**; Postgres **11 of 16 suites, 107 pass / 18 skip of 125** (`npm ci` 0, `tsc -p .` 0) — the five skipped suites are the live-database ones (§8.3.1), reported as skips, not passes.
- [x] **Consumer sync:** `diff` of everything from `## 1. Boot Protocol` onward is **empty** — and it caught two Phase 18 hunks (`Tests/README.md`) that had never been carried across, now synced.

### 4.3 Pass criteria

CLI accepts both spellings; every listed §8.2 rule present; both binding suites green; `doc-claims` clean; the consumer's copy re-synced.

### 4.4 Failure handling

On any failure: STOP, append `Verified: FAIL` with the artifact, author the incident Report. Known risk: the Postgres binding has no `node_modules` in this checkout, so its suite needs an `npm ci` first and its live-database suites will skip — a skip is not a pass and is reported as a skip.

---

## 5. Cleanup

- [x] No temp artifacts (no worktree needed — no build inputs changed for the fresh-clone clause; the binding's own suite covers the parser).
- [x] **Reports check (§10):** none — small defect + documentation; the digest and the two ledgers carry the record.
- [x] **Document Sync sweep (HR#11):** `CLAUDE.md` (edited), `Bindings/Expo/README.md` (no claim about flag spelling — checked), the consumer's `CLAUDE.md` copy (step 6), the consumer's digest (rows A3, A5–A11, B6 → applied).
- [x] No orphan branches, no leftover debug code.

---

## 6. Status Update Entry

```
### 2026-09-23 09:15 — Phase 20: CLI flag spelling + the §8.2 rules eight consumer phases paid for — CLOSED
```
