package com.nissth.bridge.it;

import com.networknt.schema.ValidationMessage;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.SubprocessRunner;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.MigrationStatus;
import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.junit.jupiter.Testcontainers;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Integration test for {@code migration_status} per Phase_05 §3 Step 17.
 *
 * <p>Uses Testcontainers to spin a real PostgreSQL container, then invokes
 * {@code migration_status} via {@code mvn flyway:info -B} against the fixture
 * (whose pom reads JDBC URL/user/password from environment variables). One
 * test programmatically migrates first and asserts {@code APPLIED}; another
 * starts a fresh container, skips migration, and asserts {@code PENDING}.
 */
@Testcontainers
class MigrationStatusIT {

    private static final String PG_IMAGE = "postgres:15-alpine";

    @Test
    void reports_APPLIED_after_flyway_migrate(@TempDir Path reportRoot) throws IOException {
        try (PostgreSQLContainer<?> pg = new PostgreSQLContainer<>(PG_IMAGE)) {
            pg.start();
            Path fixture = ItSupport.fixtureRoot();
            Flyway.configure()
                    .dataSource(pg.getJdbcUrl(), pg.getUsername(), pg.getPassword())
                    .locations("filesystem:" + fixture.resolve("src/main/resources/db/migration"))
                    .load()
                    .migrate();

            ToolResult result = runStatus(fixture, reportRoot, pg);
            assertThat(result).isInstanceOf(ToolResult.Success.class);

            Path report = ((ToolResult.Success) result).reportPath();
            String body = Files.readString(report);
            assertThat(body)
                    .as("V1__init.sql should be APPLIED after migrate")
                    .contains("| 1 | init |")
                    .contains("APPLIED");

            Set<ValidationMessage> errors = ItSupport.validateFrontmatter(report);
            assertThat(errors).as("migration_status report frontmatter schema errors").isEmpty();
        }
    }

    @Test
    void reports_PENDING_before_flyway_migrate(@TempDir Path reportRoot) throws IOException {
        try (PostgreSQLContainer<?> pg = new PostgreSQLContainer<>(PG_IMAGE)) {
            pg.start();
            Path fixture = ItSupport.fixtureRoot();

            ToolResult result = runStatus(fixture, reportRoot, pg);
            assertThat(result).isInstanceOf(ToolResult.Success.class);

            Path report = ((ToolResult.Success) result).reportPath();
            String body = Files.readString(report);
            assertThat(body)
                    .as("V1__init.sql should be PENDING against a fresh database")
                    .contains("| 1 | init |")
                    .contains("PENDING");
        }
    }

    private static ToolResult runStatus(Path fixture, Path reportRoot, PostgreSQLContainer<?> pg) {
        Map<String, String> env = new HashMap<>();
        env.put("SPRING_DATASOURCE_URL", pg.getJdbcUrl());
        env.put("SPRING_DATASOURCE_USERNAME", pg.getUsername());
        env.put("SPRING_DATASOURCE_PASSWORD", pg.getPassword());

        ReportWriter writer = new ReportWriter(reportRoot);
        MigrationStatus tool = new MigrationStatus(new EnvSubprocessRunner(env), writer);

        BridgeCommand cmd = new BridgeCommand(
                "migration_status", null, null,
                new BridgeCommand.Scope(null, fixture.toString(), List.of(),
                        null, null, null, null, null, Map.of()),
                BridgeCommand.Output.defaults());
        return tool.run(cmd);
    }

    /**
     * SubprocessRunner that overlays the given env vars on top of the inherited
     * environment. Mirrors DefaultSubprocessRunner's drain/timeout discipline.
     */
    private static final class EnvSubprocessRunner implements SubprocessRunner {

        private final Map<String, String> overlay;

        EnvSubprocessRunner(Map<String, String> overlay) {
            this.overlay = overlay;
        }

        @Override
        public ProcessResult run(Path workingDir, List<String> command, Duration timeout) {
            ProcessBuilder pb = new ProcessBuilder(command);
            pb.directory(workingDir.toFile());
            pb.environment().putAll(overlay);

            Process p;
            try {
                p = pb.start();
            } catch (IOException e) {
                throw new BridgeException(com.nissth.bridge.core.BridgeError.executeError(
                        "<subprocess>", "Could not start subprocess " + command + ": " + e.getMessage()));
            }

            ByteArrayOutputStream outBuf = new ByteArrayOutputStream();
            ByteArrayOutputStream errBuf = new ByteArrayOutputStream();
            Thread t1 = new Thread(() -> drain(p.getInputStream(), outBuf));
            Thread t2 = new Thread(() -> drain(p.getErrorStream(), errBuf));
            t1.setDaemon(true);
            t2.setDaemon(true);
            t1.start();
            t2.start();

            try {
                if (!p.waitFor(timeout.toMillis(), TimeUnit.MILLISECONDS)) {
                    p.destroyForcibly();
                    throw new BridgeException(com.nissth.bridge.core.BridgeError.executeError(
                            "<subprocess>", "Subprocess timed out after " + timeout + ": " + command));
                }
                t1.join(2000);
                t2.join(2000);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new BridgeException(com.nissth.bridge.core.BridgeError.executeError(
                        "<subprocess>", "Subprocess interrupted: " + command));
            }
            return new ProcessResult(
                    p.exitValue(),
                    outBuf.toString(StandardCharsets.UTF_8),
                    errBuf.toString(StandardCharsets.UTF_8));
        }

        private static void drain(InputStream in, ByteArrayOutputStream out) {
            try {
                in.transferTo(out);
            } catch (IOException e) {
                // Partial output acceptable.
            }
        }
    }
}
