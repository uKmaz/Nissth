package com.nissth.bridge.core;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class StaleFlipperTest {

    private static final String FRESH_ARTIFACT = """
            ---
            artifact_type: schema_index
            name: items
            last_regenerated: 2026-05-10 by Claude
            source_state: abc123
            covers:
              - src/main/java/com/example/Item.java
            stale_when:
              - column added
            ---

            ## Table: items

            | Column | Type |
            |:---|:---|
            | id | BIGINT |
            | name | VARCHAR(255) |
            """;

    private static final String ALREADY_STALE_ARTIFACT = """
            ---
            artifact_type: schema_index
            name: items
            last_regenerated: STALE — superseded by some-old-report.md
            ---

            body
            """;

    @Test
    void flips_artifact_when_checker_reports_drift(@TempDir Path repoRoot) throws IOException {
        Path schemaIndexDir = repoRoot.resolve("DBL").resolve("SchemaIndex");
        Files.createDirectories(schemaIndexDir);
        Path artifact = schemaIndexDir.resolve("items.md");
        Files.writeString(artifact, FRESH_ARTIFACT);

        Path bridgeReport = repoRoot.resolve("AgentReports").resolve("Bridge")
                .resolve("entity_lens_2026-05-15T143000Z.md");

        StaleFlipper flipper = new StaleFlipper(repoRoot);
        List<Path> flipped = flipper.flipIfDrift(Path.of("SchemaIndex"), bridgeReport, (p, fm) -> true);

        assertThat(flipped).containsExactly(artifact);
        String updated = Files.readString(artifact);
        assertThat(updated).contains(
                "last_regenerated: STALE — superseded by AgentReports/Bridge/entity_lens_2026-05-15T143000Z.md");
        // body should be preserved
        assertThat(updated).contains("| name | VARCHAR(255) |");
    }

    @Test
    void no_op_when_checker_reports_no_drift(@TempDir Path repoRoot) throws IOException {
        Path schemaIndexDir = repoRoot.resolve("DBL").resolve("SchemaIndex");
        Files.createDirectories(schemaIndexDir);
        Path artifact = schemaIndexDir.resolve("items.md");
        Files.writeString(artifact, FRESH_ARTIFACT);
        String original = Files.readString(artifact);

        StaleFlipper flipper = new StaleFlipper(repoRoot);
        List<Path> flipped = flipper.flipIfDrift(Path.of("SchemaIndex"),
                Path.of("ignored.md"), (p, fm) -> false);

        assertThat(flipped).isEmpty();
        assertThat(Files.readString(artifact)).isEqualTo(original);
    }

    @Test
    void skips_artifacts_already_marked_stale(@TempDir Path repoRoot) throws IOException {
        Path schemaIndexDir = repoRoot.resolve("DBL").resolve("SchemaIndex");
        Files.createDirectories(schemaIndexDir);
        Path artifact = schemaIndexDir.resolve("items.md");
        Files.writeString(artifact, ALREADY_STALE_ARTIFACT);
        String original = Files.readString(artifact);

        StaleFlipper flipper = new StaleFlipper(repoRoot);
        // Even with a checker that always reports drift, an already-STALE artifact stays untouched
        List<Path> flipped = flipper.flipIfDrift(Path.of("SchemaIndex"),
                Path.of("new-report.md"), (p, fm) -> true);

        assertThat(flipped).isEmpty();
        assertThat(Files.readString(artifact)).isEqualTo(original);
    }

    @Test
    void silent_no_op_when_DBL_subdir_does_not_exist(@TempDir Path repoRoot) {
        StaleFlipper flipper = new StaleFlipper(repoRoot);
        // No DBL/ directory at all
        List<Path> flipped = flipper.flipIfDrift(Path.of("SchemaIndex"),
                Path.of("ignored.md"), (p, fm) -> true);
        assertThat(flipped).isEmpty();
    }
}
