---
report_type: audit
title: Harvest from two sibling consumers — a Spring Boot backend and an Expo app, 2026-05-23 → 2026-10-05
authored: 2026-10-06 by Claude (Opus 5.5)
last_updated: 2026-10-06 by Claude (Opus 5.5)
related_status_entries:
  - 2026-10-06 23:14 — Lessons harvested from two sibling consumers
related_plans:
  - none
covers:
  - consumer drift and Bridge reachability
  - status-ledger schema (runtime, deploy, timestamps)
  - plan contract (micro plans, approval scope, reserved numbers)
  - cross-repo hand-offs
  - Spring Boot binding and §8.1
  - Expo §8.2 (Android, non-Expo-Router apps, i18n)
supersedes:
  - none
---

# Harvest from two sibling consumers

Two consumers of one product, adopted brownfield on 2026-05-23 with a per-repo
install, worked hardest between 2026-09-01 and 2026-10-05. Called here **the
backend** (Spring Boot + Maven + Flyway + PostgreSQL, 12 phases) and **the app**
(Expo / React Native with React Navigation, 29 phases). There is also a third
sibling — a React + Vite web client with **no** Nissth scaffold — which the app's
sessions edited.

Both were read end-to-end, read-only: ledgers (220 KB and 178 KB), Reports, plans,
git history, CI workflow. The load-bearing claims below were re-checked by hand
against the files before this Report was written.

## The one-line finding

**Ten days and 30+ phases of consumer work ran on the framework as it stood on
2026-05-23, and nothing told either repo.** Neither `init --check` nor the Bridge
was ever run against them after the move to a new machine: both CLAUDE.md bodies
are 1 192 lines behind, and every failure that phases 10–25 built a check for —
CRLF DBL, stale artifacts, plans missing sections, unrotated ledgers — is present
in both. The framework improved, and its consumers never found out. Most of what
follows is new; but the biggest lever is making drift **visible from the
consumer's side at boot**, because nobody opens the framework repo to look.

## A. Lessons, ranked

| # | Lesson | Seen in | Already fixed? | Proposed change |
|:-:|:---|:---|:---|:---|
| 1 | **The Bridge died silently on a machine move and stayed dead for 5 weeks.** The launcher bakes `NISSTH_FRAMEWORK_ROOT` to the old machine's path; from 2026-09-01 every entry says "Bridge reports: none". Twelve backend phases were verified with raw `mvn`/grep/curl; the Postgres binding was never usable for the pre-deploy SQL checks, which the user ran by hand — one was skipped. | backend (both) | Partly: the consumer launcher now says what it tried. Nothing raises it at boot. | §1 boot step: `nissth-bridge --list-bindings`; failure is a **Blocker** in State, never "Bridge reports: none". `init --check` reports an unreachable `DEFAULT_ROOT` as drift. HR#4: an unreachable Bridge is a defect to fix or escalate, not a licence to fall back forever. |
| 2 | **Consumer copy drifts with no signal from the consumer side.** | both | `init --check` exists but only runs from the framework checkout. | Same boot probe prints framework-body drift when the framework root is reachable. |
| 3 | **Runtime checks pile up and never close.** App: almost every phase 06–28 closes "runtime check pending"; the open list grows in each Next:, Phase 15's still open 13 phases later; regressions (layout, frozen tutorial, missing key) were found by the user on device, phases later. Backend: authenticated smokes "DEFERRED per standing decision" from Phase 03 on; Phase 01 never formally closed. | both | §8.2.6 item 7 `Runtime: NOT_RUN` — Expo native-config phases only. | Stack-agnostic State line `Runtime: PASS \| NOT_RUN(<n> open) \| FAIL` plus an **open-checks register** (id · phase · check) restated until confirmed. `ACCEPTED-RISK — <who, date, scope, expiry/trigger>` lets a phase close without lying. A ledger check warns when > N phases close with checks open. |
| 4 | **Deploy has no place in the contract.** Push to `main` = production. Every backend phase closes in two entries ("code complete, deploy pending" → "deploy verified"). A hand-off push deployed a phase before its two pre-deploy data checks ran; another entry said "not pushed" when another machine had pushed it. | backend | No. | `_TEMPLATE.md` §4.5 **Release gate**: deploy trigger, pre-deploy checks each with a recorded result, post-deploy evidence. State field `Deploy: n/a \| pending \| live-verified @<sha>`. plan-lint warns on a migration plan with no release gate. |
| 5 | **No test harness, for 12 and 28 phases.** Backend `src/test` absent through Phase 09; CI deploys with `-DskipTests`. App: "Tests: N/A" throughout; "PASS — ad-hoc Node case tables" never committed; "Jest harness is the highest-ROI item" in Next: for weeks, never planned. | both | §8.2.9b says *how* to write tests; §8.2.8 ripple only fires for Expo Router, which the app does not use. | **Test-harness debt rule:** `Tests: N/A` for 3 consecutive phases ⇒ the next plan is the harness plan, or a recorded user waiver. A case table cited as PASS is committed under `Tests/`. §8.1.5: CI deploy with `-DskipTests` while a suite exists is forbidden. Brownfield Phase 00 records "no tests" as a named blocker. |
| 6 | **Timestamps unreliable.** Five headers admitted "estimated, not read from the clock"; a cloud session wrote UTC beside local +03:00; entries out of order; four without a time. | app | No — HR#8 covers relative dates only. | Header `YYYY-MM-DD HH:MM±hh:mm`, read from `date`. A `ledger-check` (or `dbl-check` sibling) flags missing time/offset, non-monotonic order, oversize (§5.1). |
| 7 | **Snapshots are file copies, committed, and outlive their phase.** App: 89 tracked files, 3 MB (full locale files nearly every phase). Backend: Phase 01/10 sets still on `main`. Meanwhile a Report says the SDK-55 line is "preserved on branch `sdk55`" — no branch or tag holds that commit now; it is dangling and `git gc` will take it. | both | No — HR#9 never says what a snapshot is. | HR#9: in a git repo with a clean tree the snapshot **is a ref** (tag); file copies only for uncommitted state, gitignored, pruned at close. A "preserved on X" claim names a tag; a check can `git rev-parse` it. |
| 8 | **Small fixes eroded the template; blanket approvals preceded plans.** App phases 09–16 drop §2/§5/§6 (plan-lint: 42 errors), one is 24 lines; phases 06–11 ran on a single "I approve all of them" given before 07–11 were written. Backend Phase 08 "approved" the same way. | both | plan-lint catches missing sections; nothing serves the legitimate need for a light plan, or approval scope. | Official **micro plan** (`Plan type: micro`: §0, one-row §1, §3 + Forbidden, §4, 3-line §6), eligible only for ≤ 2 files and no config/native/schema/dep change; plan-lint enforces eligibility and shape. `Approved:` quotes the user verbatim and covers only plan files that **exist** when it is given. |
| 9 | **Cross-repo work was invented ad hoc — and it worked.** The app session authored backend phases inside the backend repo; a frontend-changes ledger (rows A1…G3) and a backend to-do (B1…B13, gated "DO NOT START until D1–D5") coordinated three repos; the web client (no Nissth) took five feature/privacy commits recorded only in the app's ledger, and one privacy commit went live before the backend change it described. Naming drifted ("Phase 07" = "B11"). | both | §10.5b covers consumer → framework only. | §10.5c **Cross-repo hand-off ledger**: lives in the producer repo, stable row ids, a status column per client, gate rows; plans cite row ids and gated steps name their row. The banner declares satellite repos; any entry touching one cites its commits; a session writing another repo's plan records itself as author. Legal/privacy text tied to a backend release gets a release-gate row. |
| 10 | **§8.1.8 baseline check verified with the wrong executor.** `V1__baseline.sql` from `pg_dump` carries `\restrict`/`\unrestrict` psql meta-commands; it was "verified idempotent" with psql, but Flyway cannot run it. Production works only because `baselineOnMigrate` never executes V1. | backend | No. | §8.1.8: strip psql meta-commands; verify with **Flyway itself** on an empty DB, `baselineOnMigrate=false`. |
| 11 | **Spring Boot binding gaps.** `compile_verify` returned HAS_ERRORS with an empty table on a wrong JDK — its patterns match only `File.java:[l,c]`. `endpoint_lens` printed Auth "—" for 91 endpoints because auth lives in `SecurityFilterChain.requestMatchers`. A plan cited a `--mode` the binding does not have. | backend | No. | `compile_verify` falls back to the first `[ERROR]` / `BUILD FAILURE` lines; `endpoint_lens` flags "auth not annotation-based" (a filter-chain mode later); plan-lint checks cited `--mode` values against manifests. |
| 12 | **Expo: Android pitfalls and wrong-platform verification.** `expo-camera` adds CAMERA without `uses-feature required=false` ⇒ Play hides the listing on many devices; `expo prebuild` aborts without `google-services.json` and rewrites `package.json` scripts; secret file via EAS file env; Expo Go throws on `expo-notifications` import on Android. Bundle checks ran `--platform web` on an app that never ships web; `npx --yes expo-doctor` floated to a newer doctor (PASS → FAIL 4/18); an npm-11 lockfile was rejected by EAS's npm 10. | app | §8.2.9c is iOS-only; 5b/5c partly cover pinning. | §8.2.9d Android pitfalls. Bundle check targets shipped platforms only; freshness line records the expo-doctor version; `npx npm@<EAS major> ci --dry-run` before an EAS build. Runtime check cites build id **and** source commit (the user once tested a build older than the fix). |
| 13 | **SDK churn with no map.** 55 → rolled back to 54 (store binary was 54) → 57 (Expo Go dropped 54), with three plan-less hotfixes on the way. | app | §8.2.1 asks the SRS for a workflow, nothing more. | Required table in `_config.md`: branch → SDK → store binary → how it gets runtime-tested. An SDK-upgrade plan answers "how will the user run this build?" in §1. |
| 14 | **DBL became a changelog.** One artifact with nine "Phase N delta" sections, four over budget (max 2 194 words); `routes.md` unregenerated across 25 covered changes because each Doc sync read only its own `stale_when` (§15.1's exact failure, reproduced independently). | app | dbl-check / dbl-regen catch most of it now. | dbl-check warns on > 2 "Phase N" sections (§7.5: history belongs in the ledger). |
| 15 | **Backlogs retyped forward rot; id-tracked ones didn't.** The backend's "Phase 13 backlog" still lists two items finished phases earlier — copied entry to entry for three weeks — while the id-tracked to-do Report stayed exact. | backend | No. | Backlog lives in one Report with ids and status; entries cite ids, never retype the list. Same mechanism as #9, inside one repo. |
| 16 | **Negative claims went unproved.** A delegated audit called event endpoints owner-only; Pre-Flight found any logged-in user could create and delete events. Two plan "Before" tables asserted "no X exists" wrongly. | backend | No. | `_TEMPLATE.md` §1/§2: a negative claim cites the grep or line range that proves it; security conclusions from sub-agents are spot-checked in source before entering a Report. |
| 17 | **Distributable documents ahead of deploys.** A security report and two user-facing PDFs describe system behaviour; "do not distribute until deployed" appears five times; after the latest phase they are stale; their sources are not in the repo, only the PDFs. | backend | No. | Report frontmatter `claims_depend_on: <phase/deploy>`; a derived binary commits its source beside it. |
| 18 | **Minor.** Deploy topology lived in machine-local agent memory and was rediscovered on the new machine (→ `DBL/Summaries/_deploy.md`). Reserved plan numbers exist only in prose (→ `Approved: reserved` stub or `Superseded by:`). Shell heredocs collapsed `\\` in regex content twice (→ write such content with the file tool). `mvnw.cmd` broke on a path with a space. Agent-started Metro servers leaked across ports 8081–8084 (→ stop what you started; record it). Hand-checked tr/en locale parity every phase (→ an `i18n_lens` candidate). | both | — | as noted |

## B. Worked — codify it

- **Migration rehearsal** (backend, five phases): fresh `postgres:17` container
  seeded from V1, `java -jar` exactly as production runs, read the Flyway version
  chain and the EntityManagerFactory line (proves `validate`), `information_schema`
  shape check, a **backfill proof** and a **constraint proof**; the live DB never
  touched. → §8.1.6 standard for any migration phase.
- **Migration file before entity field**, so no intermediate commit fails to boot
  under `ddl-auto=validate`.
- **Maven freshness by artifact mtime** (`target/` deleted, JAR newer than the newest
  source edit) — simpler and more honest than the Gradle-daemon wording for Maven.
- **Mutation check:** restore the pre-phase code, see exactly the expected failures.
- **Deploy evidence without CI-log access:** poll the health endpoint across the
  deploy (a 502 gap then stable 200s), or a behaviour flip that only the new build has.
- **"Stated limit:" in freshness lines** — each Verify says what it cannot prove.
- **Verbatim approval quotes** in §0.
- **Gated steps inside an approved plan**, released by a hand-off row later.
- **STOP at Pre-Flight on divergence → decision Report → amended plan** — the loop
  working as designed, several times, in both repos.
- **Prebuild into a throwaway tree and read the generated AndroidManifest** to
  verify a config-plugin change.
- **Superseding correction entries** — HR#3 held throughout both ledgers.

## C. Not framework material

Mail-server and SMTP-port outages, the VPS deploy script, the managed-Postgres host,
privacy-law wording, product UX choices, Firebase token-caching semantics (its
generalizable part — "a hand-off states client caching implications" — is in #9).

## Proposed phase grouping

| Phase | Scope | Lessons |
|:---|:---|:---|
| 26 | **Consumer health at boot** — boot probe (Bridge reachability + body drift), `init --check` reports unreachable launcher roots; HR#4 wording | 1, 2 |
| 27 | **Ledger and plan contract** — `Runtime:` / `Deploy:` State lines, open-checks register, accepted risk, release gate §4.5, test-harness debt rule, timestamp rule + `ledger-check`, HR#9 snapshot-as-ref | 3–7, 15 |
| 28 | **Plan contract II** — micro plan, approval scope, reserved/superseded numbers, negative-claim citations, plan-lint support | 8, 16, 18 |
| 29 | **Cross-repo hand-off ledger** §10.5c + satellite repos in the banner | 9, 17 |
| 30 | **Spring Boot slice** — `compile_verify` fallback, `endpoint_lens` auth flag, §8.1.8 Flyway-verified baseline, rehearsal + Maven freshness codified, `-DskipTests` rule | 10, 11, B |
| 31 | **Expo slice** — §8.2.9d, shipped-platform bundle check, version pinning, router-agnostic render-test ripple, SDK map, DBL changelog warning; `i18n_lens` scoped separately | 12–14 |

## Revision history

- 2026-10-06 — authored.
