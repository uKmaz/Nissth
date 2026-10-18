---
report_type: decision
title: Phase 05 — pivot Spring Boot binding self-build from Gradle to Maven
authored: 2026-05-15 by Claude (Opus 4.7)
last_updated: 2026-05-15 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-15 — Phase 05 Pre-flight + Maven Pivot
related_plans:
  - Phase_05_Bridge_SpringBoot_FirstSlice
covers:
  - Bindings/SpringBoot self-build tool choice
  - compile_verify build-tool adaptive behavior
  - migration_status build-tool adaptive behavior
supersedes:
  - none
---

## Context

`Phase_05_Bridge_SpringBoot_FirstSlice.md` was authored against the assumption that Gradle (the preferred build tool per `CLAUDE.md` §8.1) would be available on the host. Pre-flight §1.2 row 4 returned `Match=no`:

- `gradle --version` → "command not found"
- No `gradlew` exists yet (this is a fresh subproject)
- Java 21 + Maven 3.9.9 + Node 22 are all on PATH

`CLAUDE.md` §8.1 explicitly lists Maven as a supported alternative: *"Gradle with Kotlin DSL (preferred) — or Maven"*. The pivot is in-scope per existing stack binding documentation.

## Options considered

| Option | Pros | Cons |
|:---|:---|:---|
| **A — Install Gradle, resume original plan** | Original plan executes unchanged; matches the §8.1 "preferred" framing. | Permanent software install on the user's machine for a one-shot decision; ~5 min of user time. |
| **B — Pivot binding self-build to Maven** | No software install; Maven already present + works with the same JDK 21; `CLAUDE.md` §8.1 lists Maven as a first-class alternative; tool catalog stays identical. | Plan revision touches ~10 sections; binding tools must be build-tool-adaptive (Gradle support not dropped — just no longer the binding's own build). |
| **C — Bootstrap Gradle wrapper from a downloaded `gradle-wrapper.jar`** | Original plan resumes unchanged after a one-time setup. | Network download for a 63 KB jar; hand-written wrapper scripts; "silent workaround" feel — the plan's stated `Match` criteria are technically not met. Higher cognitive cost than option B. |

## Decision

**Option B — pivot binding self-build to Maven.**

Concrete changes (all in `Phase_05_Bridge_SpringBoot_FirstSlice.md`):
- §0 Metadata: added `Revised:` line citing this report; `Estimated scope` mentions Maven.
- §1.2 row 4: "Confirm Gradle availability" → "Confirm Maven availability."
- §1.3: Findings filled with actual values; Gradle row marked `❌ no — resolved by pivot to Maven`; an extra `POST-PIVOT` row records Maven 3.9.9 + Java 21 = ✅; `Resolution` paragraph explains the pivot.
- §2 Expected State: `build.gradle.kts` → `pom.xml`; `build/libs/nissth-bridge.jar` → `target/nissth-bridge-0.1.0.jar`.
- §3 Step 1: scaffold becomes Maven (`pom.xml`, no `settings.gradle.kts`).
- §3 Step 8 (`compile_verify`): added build-tool auto-detection. Maven path runs `mvn clean compile test-compile -U -B`. Gradle path preserved (`--stop` + `clean compileJava compileTestJava --no-daemon`, exit 5 if `--stop` skipped). Auto-detect resolves by file presence in target dir.
- §3 Step 11 (`migration_status`): same auto-detection. Maven path runs `mvn flyway:info -B`; Gradle path runs `./gradlew flywayInfo`.
- §3 Step 14: launcher scripts reference `target/nissth-bridge-0.1.0.jar`.
- §3 Step 16: fixture is a Maven project (`pom.xml` with flyway-maven-plugin).
- §3 Step 17: integration tests use the Failsafe `*IT.java` suffix convention; run via `mvn verify -B`.
- §3 Step 20: `mvn clean verify -U -B`.
- §4.1 freshness: Maven self-build (no daemon to stop, `-U` and `-B` are the freshness contract); both build-tool paths documented for `compile_verify`.
- §4.2 checks: commands and report paths updated to Maven.
- §5 cleanup: `target/` instead of `build/`.
- §6 status entry template: build command updated to Maven.

## Consequences

**Positive:**
- No software install on the user's machine; immediate path forward.
- Binding tools become **build-tool-agnostic** for target projects — consumer projects (Example and future Nissth-bound projects) can use either Gradle or Maven. This is a strict superset of the original plan's coverage.
- Maven's "no persistent daemon" property eliminates one source of false-CLEAN failures for the binding's own build. (The Gradle daemon trap stays relevant for consumer projects that use Gradle, hence the Gradle path in `compile_verify`.)

**Negative / risks:**
- The plan's expected verification paths (`target/surefire-reports/`, `target/failsafe-reports/`) replace Gradle's `build/reports/tests/test/`. Anyone reviewing prior plans that cited the Gradle paths must remap mentally — but no prior plan does (this is the first binding plan).
- The `compile_verify` Gradle path can no longer be exercised against a Maven-only binding self-build; it's instead tested via a stubbed-subprocess unit test plus the §4.2 check that the Gradle exit-5 contract holds. This is slightly weaker than running against a real Gradle target. Acceptable: Gradle path will be exercised end-to-end the first time a Gradle-built consumer project (or a future Gradle fixture) is added.

**Carry-overs:**
- `CLAUDE.md` §8.1 still names Gradle as preferred — that ranking is unchanged. The binding's choice of Maven is a local implementation detail driven by tool availability, not a framework-wide repudiation of Gradle.
- If the user ever installs Gradle and prefers a Gradle binding self-build, swapping is a small revision (drop Maven plugins, add Gradle build files, change Step 20's command). The Java source code is build-tool-agnostic.

## Revision history

- 2026-05-15 — Initial decision authored. Pivot adopted; plan revised in same turn. No prior decision superseded.
