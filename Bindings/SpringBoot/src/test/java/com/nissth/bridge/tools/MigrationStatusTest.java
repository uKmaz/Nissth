package com.nissth.bridge.tools;

import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.SubprocessRunner;
import com.nissth.bridge.core.SubprocessRunner.ProcessResult;
import com.nissth.bridge.core.ToolResult;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class MigrationStatusTest {

    static class StubRunner implements SubprocessRunner {
        private final List<ProcessResult> queued = new ArrayList<>();
        final List<List<String>> calls = new ArrayList<>();

        StubRunner enqueue(int exit, String stdout, String stderr) {
            queued.add(new ProcessResult(exit, stdout, stderr));
            return this;
        }

        @Override
        public ProcessResult run(Path workingDir, List<String> command, Duration timeout) {
            calls.add(command);
            if (queued.isEmpty()) {
                throw new AssertionError("Unexpected subprocess call: " + command);
            }
            return queued.remove(0);
        }
    }

    private static final String POM_WITH_PLUGIN = """
            <project>
              <build>
                <plugins>
                  <plugin>
                    <groupId>org.flywaydb</groupId>
                    <artifactId>flyway-maven-plugin</artifactId>
                    <version>10.20.1</version>
                  </plugin>
                </plugins>
              </build>
            </project>
            """;

    private static final String POM_WITHOUT_PLUGIN = "<project><build><plugins/></build></project>";

    private static final String GRADLE_KTS_WITH_PLUGIN = """
            plugins {
              id("org.flywaydb.flyway") version "10.20.1"
            }
            """;

    private static final String GRADLE_GROOVY_WITHOUT_PLUGIN = """
            plugins {
              id 'java'
            }
            """;

    private static final String FLYWAY_INFO_SAMPLE = """
            [INFO] Scanning for projects...
            [INFO]
            [INFO] +-----------+---------+-------------+------+---------------------+----------------+----------+
            [INFO] | Category  | Version | Description | Type | Installed On        | State          | Undoable |
            [INFO] +-----------+---------+-------------+------+---------------------+----------------+----------+
            [INFO] | Versioned | 1       | init        | SQL  | 2026-01-01 12:34:56 | Success        | No       |
            [INFO] | Versioned | 2       | add qty     | SQL  |                     | Pending        | No       |
            [INFO] | Versioned | 3       | broken      | SQL  |                     | Failed         | No       |
            [INFO] | Versioned | 4       | reordered   | SQL  |                     | Out of Order   | No       |
            [INFO] +-----------+---------+-------------+------+---------------------+----------------+----------+
            [INFO] BUILD SUCCESS
            """;

    private static final String FLYWAY_VALIDATE_CLEAN = "[INFO] Successfully validated 1 migration (execution time 00:00.024s)\n";

    private static final String FLYWAY_VALIDATE_DRIFT = """
            [INFO] Validating migrations
            [ERROR] Migration checksum mismatch for migration version 2
            [ERROR] -> Applied to database : 1234567890
            [ERROR] -> Resolved locally    : 9876543210
            """;

    private BridgeCommand command(Path target) {
        return new BridgeCommand("migration_status", null, null,
                new BridgeCommand.Scope(null, target.toString(), List.of(),
                        null, null, null, null, null, Map.of()),
                BridgeCommand.Output.defaults());
    }

    @Test
    void checkFlywayPlugin_maven_with_plugin_passes(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), POM_WITH_PLUGIN);
        MigrationStatus.checkFlywayPlugin(target, CompileVerify.BuildTool.MAVEN);
    }

    @Test
    void checkFlywayPlugin_maven_without_plugin_throws_missing_flyway_plugin(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), POM_WITHOUT_PLUGIN);
        assertThatThrownBy(() -> MigrationStatus.checkFlywayPlugin(target, CompileVerify.BuildTool.MAVEN))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    var err = ((BridgeException) e).error();
                    assertThat(err.errorCode()).isEqualTo("missing_flyway_plugin");
                    assertThat(err.exitCode()).isEqualTo(3);
                    assertThat(err.error())
                            .contains("flyway-maven-plugin")
                            .contains("<plugin>")
                            .contains("<artifactId>flyway-maven-plugin</artifactId>");
                });
    }

    @Test
    void checkFlywayPlugin_gradle_kts_with_plugin_passes(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("build.gradle.kts"), GRADLE_KTS_WITH_PLUGIN);
        MigrationStatus.checkFlywayPlugin(target, CompileVerify.BuildTool.GRADLE);
    }

    @Test
    void checkFlywayPlugin_gradle_groovy_without_plugin_throws(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("build.gradle"), GRADLE_GROOVY_WITHOUT_PLUGIN);
        assertThatThrownBy(() -> MigrationStatus.checkFlywayPlugin(target, CompileVerify.BuildTool.GRADLE))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    var err = ((BridgeException) e).error();
                    assertThat(err.errorCode()).isEqualTo("missing_flyway_plugin");
                    assertThat(err.error()).contains("org.flywaydb.flyway");
                });
    }

    @Test
    void parseFlywayInfo_classifies_all_four_states() {
        List<MigrationStatus.Migration> rows = MigrationStatus.parseFlywayInfo(FLYWAY_INFO_SAMPLE);
        assertThat(rows).hasSize(4);
        assertThat(rows).extracting(MigrationStatus.Migration::version)
                .containsExactly("1", "2", "3", "4");
        assertThat(rows).extracting(MigrationStatus.Migration::state).containsExactly(
                MigrationStatus.MigrationState.APPLIED,
                MigrationStatus.MigrationState.PENDING,
                MigrationStatus.MigrationState.FAILED,
                MigrationStatus.MigrationState.OUT_OF_ORDER);
    }

    @Test
    void parseFlywayInfo_empty_output_returns_no_rows() {
        assertThat(MigrationStatus.parseFlywayInfo("[INFO] Scanning for projects...\nBUILD SUCCESS\n"))
                .isEmpty();
    }

    @Test
    void classify_maps_known_flyway_states() {
        assertThat(MigrationStatus.classify("Success")).isEqualTo(MigrationStatus.MigrationState.APPLIED);
        assertThat(MigrationStatus.classify("Future")).isEqualTo(MigrationStatus.MigrationState.APPLIED);
        assertThat(MigrationStatus.classify("Pending")).isEqualTo(MigrationStatus.MigrationState.PENDING);
        assertThat(MigrationStatus.classify("Available")).isEqualTo(MigrationStatus.MigrationState.PENDING);
        assertThat(MigrationStatus.classify("Failed")).isEqualTo(MigrationStatus.MigrationState.FAILED);
        assertThat(MigrationStatus.classify("Missing Success")).isEqualTo(MigrationStatus.MigrationState.FAILED);
        assertThat(MigrationStatus.classify("Out of Order")).isEqualTo(MigrationStatus.MigrationState.OUT_OF_ORDER);
        assertThat(MigrationStatus.classify(null)).isEqualTo(MigrationStatus.MigrationState.PENDING);
    }

    @Test
    void parseValidationDrift_detects_checksum_mismatch() {
        List<String> drift = MigrationStatus.parseValidationDrift(FLYWAY_VALIDATE_DRIFT);
        assertThat(drift).isNotEmpty();
        assertThat(drift.get(0)).contains("checksum mismatch");
    }

    @Test
    void parseValidationDrift_returns_empty_on_clean_output() {
        assertThat(MigrationStatus.parseValidationDrift(FLYWAY_VALIDATE_CLEAN)).isEmpty();
    }

    @Test
    void maven_end_to_end_returns_Success_with_classified_rows(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), POM_WITH_PLUGIN);
        StubRunner runner = new StubRunner()
                .enqueue(0, FLYWAY_INFO_SAMPLE, "")
                .enqueue(0, FLYWAY_VALIDATE_CLEAN, "");
        ReportWriter writer = new ReportWriter(target);

        MigrationStatus tool = new MigrationStatus(runner, writer, Duration.ofSeconds(30));
        ToolResult result = tool.run(command(target));

        assertThat(result).isInstanceOf(ToolResult.Success.class);
        Path report = ((ToolResult.Success) result).reportPath();
        String body = Files.readString(report);
        assertThat(body)
                .contains("# migration_status report")
                .contains("Build tool:** maven")
                .contains("APPLIED").contains("PENDING").contains("FAILED").contains("OUT_OF_ORDER");
        assertThat(runner.calls).hasSize(2);
        assertThat(runner.calls.get(0)).containsExactly("mvn", "flyway:info", "-B");
        assertThat(runner.calls.get(1)).containsExactly("mvn", "flyway:validate", "-B");
    }

    @Test
    void maven_missing_plugin_short_circuits_with_snippet(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), POM_WITHOUT_PLUGIN);
        StubRunner runner = new StubRunner();
        ReportWriter writer = new ReportWriter(target);
        MigrationStatus tool = new MigrationStatus(runner, writer, Duration.ofSeconds(30));

        assertThatThrownBy(() -> tool.run(command(target)))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    var err = ((BridgeException) e).error();
                    assertThat(err.errorCode()).isEqualTo("missing_flyway_plugin");
                    assertThat(err.exitCode()).isEqualTo(3);
                });
        assertThat(runner.calls).isEmpty();
    }

    @Test
    void gradle_end_to_end_invokes_flywayInfo_then_flywayValidate(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("build.gradle.kts"), GRADLE_KTS_WITH_PLUGIN);
        StubRunner runner = new StubRunner()
                .enqueue(0, FLYWAY_INFO_SAMPLE.replace("[INFO] ", ""), "")
                .enqueue(0, "", "");
        ReportWriter writer = new ReportWriter(target);

        MigrationStatus tool = new MigrationStatus(runner, writer, Duration.ofSeconds(30));
        ToolResult result = tool.run(command(target));

        assertThat(result).isInstanceOf(ToolResult.Success.class);
        assertThat(runner.calls).hasSize(2);
        assertThat(runner.calls.get(0).get(runner.calls.get(0).size() - 1)).isEqualTo("flywayInfo");
        assertThat(runner.calls.get(1).get(runner.calls.get(1).size() - 1)).isEqualTo("flywayValidate");
    }
}
