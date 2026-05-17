---
report_type: decision
title: Phase 05 — Maven Wrapper adoption (resume-protocol freshness-check discovered no system mvn)
authored: 2026-05-16 by Claude (Opus 4.7)
last_updated: 2026-05-16 by Claude (Opus 4.7)
related_status_entries:
  - 2026-05-16 — Phase 05 Step 11 Complete + Maven Wrapper added (52/52 tests)
related_plans:
  - Phase_05_Bridge_SpringBoot_FirstSlice
covers:
  - Bindings/SpringBoot/ binding self-build toolchain
supersedes:
  - none
---

## Context

Resuming Phase 05 after the prior session closed at Step 10 (40/40 tests). Resume protocol requires `mvn test` to confirm the 40/40 baseline before any new code is written (freshness anchor per Hard Rule #10).

`mvn` is not on this machine — not in `PATH`, not in `User PATH`/`System PATH`, not in `JAVA_HOME`/`M2_HOME`/`MAVEN_HOME`, not in any standard install location (`C:\Program Files\Maven*`, `~/scoop/apps/maven`, `~/.jdks/*`, IntelliJ bundled). The prior session's status entry claimed `mvn test` succeeded — whatever system Maven existed then is gone. Java IS available (Eclipse Temurin 17.0.19 under `~/.jdks/temurin-17.0.19`).

Without a working Maven the freshness anchor cannot be established and Hard Rule #2 (No Silent Deviations) blocks proceeding to Step 11.

## Options considered

| Option | Effort | Persistence | Cost to consumer projects | Notes |
|:---|:---|:---|:---|:---|
| **A. Add Maven Wrapper** (`mvnw`) | one-time, ~5 min | per-project; survives PATH changes | none — `./mvnw` works without system install | Bundles `mvnw`, `mvnw.cmd`, `.mvn/wrapper/maven-wrapper.properties`. Pinned to Maven 3.9.9. Self-bootstrapping. |
| B. Install Maven system-wide | one-time | host-level | requires every dev machine to install | Chocolatey / scoop / manual tarball; PATH + `JAVA_HOME` env edits. One-host scope. |
| C. Use IntelliJ's bundled Maven | one-time | host-level | path-fragile (varies by IDE version) | Found `C:\Program Files\JetBrains\IntelliJ IDEA 2026.1.1\plugins\maven\lib\maven3\bin\mvn.cmd`. Breaks on IDE upgrade or non-IDE users. |
| D. Skip freshness check | zero | n/a | n/a | Violates HR#10. Not acceptable. |

## Decision

**Option A — Maven Wrapper.** Authored 2026-05-16. User chose this option from the four presented.

Concretely:
- `Bindings/SpringBoot/mvnw` (POSIX shell, executable)
- `Bindings/SpringBoot/mvnw.cmd` (Windows CMD)
- `Bindings/SpringBoot/.mvn/wrapper/maven-wrapper.properties` — `distributionType=only-script`, `distributionUrl=https://repo.maven.apache.org/maven2/org/apache/maven/apache-maven/3.9.9/apache-maven-3.9.9-bin.zip`, `wrapperVersion=3.3.2`.

Files were fetched verbatim from `https://repo.maven.apache.org/maven2/org/apache/maven/wrapper/maven-wrapper-distribution/3.3.2/maven-wrapper-distribution-3.3.2-only-script.zip`. The only-script variant has no jar dependency — both scripts contain inline distribution downloaders.

First run (`./mvnw -v`) downloaded Apache Maven 3.9.9 into `~/.m2/wrapper/dists/apache-maven-3.9.9/3477a4f1/` and emitted the standard Maven version banner against Temurin 17.0.19.

## Consequences

**Positive:**
- The 40/40 baseline was re-confirmed via `./mvnw -q test` immediately after wrapper install; freshness anchor restored. Step 11 implementation followed in the same turn.
- Consumer projects pulling this binding (Süprüz, future stacks adopting the Spring Boot binding) no longer need to install Maven separately — `./mvnw` works out of the box.
- `binding_version: "0.1.0"` is unchanged; the wrapper is build-tooling, not API.

**Neutral:**
- The plan's §3 step list cited `mvn …` commands; those still work on hosts that DO have system Maven, and `./mvnw …` works elsewhere. `Phase_05` §0 has a new `Revised: 2026-05-16` line documenting the addition.
- The pre-flight (§1.3 row 7) result that "Maven 3.9.9 is on PATH" was true at plan-authoring time and is no longer true on this machine. The plan does NOT need re-running of pre-flight — the wrapper supersedes the row.

**Negative:**
- Adds ~18KB of scripts under `Bindings/SpringBoot/`. Worth it; wrappers are conventional for Maven-based projects (Spring Initializr ships them by default).
- First `./mvnw` invocation does a ~10MB Maven distribution download. One-time per host per Maven version.

**Forbidden-pattern check:** The plan's §3.2 lists no prohibition on adding build-toolchain files. This is not a "new tool" (§3.2 caps tools at five) — it's bootstrapping for the existing build. In-scope per the same authorization that approved the 2026-05-15 Gradle→Maven pivot.

## Revision history

- **2026-05-16** — authored. Decision made + executed in one turn under blanket consent (Scope A). Subsequently Step 11 (MigrationStatus) landed with 12 new tests; 52/52 PASS overall.
