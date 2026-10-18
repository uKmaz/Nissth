package com.nissth.bridge.tools;

import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeError;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.ReportContext;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.SubprocessRunner;
import com.nissth.bridge.core.SubprocessRunner.ProcessResult;
import com.nissth.bridge.core.ToolHandler;
import com.nissth.bridge.core.ToolResult;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * migration_status — diagnostic, build-tool adaptive.
 *
 * <p>Reads Flyway state by invoking the target project's build tool. Pre-checks
 * that the Flyway plugin is configured; if not, returns a {@code missing_flyway_plugin}
 * error carrying a copy-pasteable plugin block per Phase_05 plan Step 11.
 */
public class MigrationStatus implements ToolHandler {

    public static final String NAME = "migration_status";

    private final SubprocessRunner runner;
    private final ReportWriter writer;
    private final Duration commandTimeout;

    public MigrationStatus(SubprocessRunner runner, ReportWriter writer) {
        this(runner, writer, Duration.ofMinutes(3));
    }

    public MigrationStatus(SubprocessRunner runner, ReportWriter writer, Duration commandTimeout) {
        this.runner = runner;
        this.writer = writer;
        this.commandTimeout = commandTimeout;
    }

    @Override
    public String name() {
        return NAME;
    }

    @Override
    public ToolResult run(BridgeCommand cmd) {
        Path target = CompileVerify.resolveTarget(cmd);
        CompileVerify.BuildTool tool = CompileVerify.detectBuildTool(target);
        checkFlywayPlugin(target, tool);

        OffsetDateTime startedAt = OffsetDateTime.now();
        ProcessResult info = runner.run(target, infoCommand(target, tool), commandTimeout);
        ProcessResult validate = runner.run(target, validateCommand(target, tool), commandTimeout);

        List<Migration> migrations = parseFlywayInfo(info.stdout() + "\n" + info.stderr());
        List<String> drift = parseValidationDrift(validate.stdout() + "\n" + validate.stderr());

        ReportContext ctx = ReportContext.builder()
                .tool(NAME)
                .mode(cmd.mode() == null ? "default" : cmd.mode())
                .scope(cmd.scope())
                .contextId(cmd.contextId())
                .generatedAt(OffsetDateTime.now())
                .freshness(
                        tool.name().toLowerCase(Locale.ROOT) + " flyway plugin in " + target,
                        "flyway:info exit=" + info.exitCode() + "; flyway:validate exit=" + validate.exitCode()
                                + "; run started " + startedAt,
                        "Live Flyway state read via build-tool plugin invocation; no cached info.")
                .body(renderBody(target, tool, migrations, drift, info, validate))
                .build();

        Path report = writer.write(ctx);
        return new ToolResult.Success(report);
    }

    // --- Plugin pre-check -----------------------------------------------------

    static void checkFlywayPlugin(Path target, CompileVerify.BuildTool tool) {
        switch (tool) {
            case MAVEN -> checkMavenFlywayPlugin(target);
            case GRADLE -> checkGradleFlywayPlugin(target);
        }
    }

    private static void checkMavenFlywayPlugin(Path target) {
        Path pom = target.resolve("pom.xml");
        String content = readOrThrow(pom);
        if (!content.contains("flyway-maven-plugin")) {
            throw new BridgeException(BridgeError.executeError(
                    NAME,
                    "Flyway plugin not configured in " + pom + ". "
                            + "migration_status reads Flyway state by invoking the build tool's Flyway goal; "
                            + "that requires the Flyway plugin to be configured in your target project.\n\n"
                            + "Add to <build><plugins> in your pom.xml:\n\n"
                            + MAVEN_PLUGIN_SNIPPET,
                    "missing_flyway_plugin",
                    3));
        }
    }

    private static void checkGradleFlywayPlugin(Path target) {
        Path groovy = target.resolve("build.gradle");
        Path kotlin = target.resolve("build.gradle.kts");
        Path build = Files.exists(groovy) ? groovy : kotlin;
        String content = readOrThrow(build);
        if (!content.contains("org.flywaydb.flyway")) {
            throw new BridgeException(BridgeError.executeError(
                    NAME,
                    "Flyway plugin not configured in " + build + ". "
                            + "migration_status reads Flyway state by invoking the build tool's Flyway goal; "
                            + "that requires the Flyway plugin to be configured in your target project.\n\n"
                            + "Add to plugins { } in your build file:\n\n"
                            + GRADLE_PLUGIN_SNIPPET,
                    "missing_flyway_plugin",
                    3));
        }
    }

    private static String readOrThrow(Path file) {
        try {
            return Files.readString(file);
        } catch (IOException e) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Cannot read " + file + ": " + e.getMessage()));
        }
    }

    static final String MAVEN_PLUGIN_SNIPPET = """
            <plugin>
              <groupId>org.flywaydb</groupId>
              <artifactId>flyway-maven-plugin</artifactId>
              <version>10.20.1</version>
              <configuration>
                <url>${flyway.url}</url>
                <user>${flyway.user}</user>
                <password>${flyway.password}</password>
                <locations>
                  <location>classpath:db/migration</location>
                </locations>
              </configuration>
            </plugin>
            """;

    static final String GRADLE_PLUGIN_SNIPPET = """
            plugins {
              id 'org.flywaydb.flyway' version '10.20.1'
            }
            flyway {
              url = project.findProperty('flyway.url')
              user = project.findProperty('flyway.user')
              password = project.findProperty('flyway.password')
              locations = ['classpath:db/migration']
            }
            """;

    // --- Subprocess command builders -----------------------------------------

    static List<String> infoCommand(Path target, CompileVerify.BuildTool tool) {
        return switch (tool) {
            case MAVEN -> CompileVerify.mavenCommand(target, "flyway:info", "-B");
            case GRADLE -> CompileVerify.gradleCommand(target, "flywayInfo");
        };
    }

    static List<String> validateCommand(Path target, CompileVerify.BuildTool tool) {
        return switch (tool) {
            case MAVEN -> CompileVerify.mavenCommand(target, "flyway:validate", "-B");
            case GRADLE -> CompileVerify.gradleCommand(target, "flywayValidate");
        };
    }

    // --- Output parsing -------------------------------------------------------

    /** Matches one Flyway info table row: at least 6 pipe-separated cells. */
    private static final Pattern INFO_ROW = Pattern.compile(
            "(?m)^(?:\\[INFO\\]\\s*)?\\|([^\\n]+)\\|\\s*$");

    static List<Migration> parseFlywayInfo(String output) {
        List<Migration> out = new ArrayList<>();
        Matcher m = INFO_ROW.matcher(output);
        while (m.find()) {
            String[] cells = m.group(1).split("\\|", -1);
            if (cells.length < 6) continue;
            String[] trimmed = new String[cells.length];
            for (int i = 0; i < cells.length; i++) trimmed[i] = cells[i].trim();

            // Header row has the literal column name "Version" in column 1 and "State" in column 5.
            if ("Version".equals(trimmed[1]) && "State".equals(trimmed[5])) continue;

            String version = trimmed[1];
            String description = trimmed[2];
            String state = trimmed[5];
            if (version.isEmpty() && description.isEmpty()) continue;

            out.add(new Migration(version, description, state, classify(state)));
        }
        return out;
    }

    static MigrationState classify(String flywayState) {
        if (flywayState == null) return MigrationState.PENDING;
        String s = flywayState.trim().toLowerCase(Locale.ROOT);
        if (s.contains("out of order")) return MigrationState.OUT_OF_ORDER;
        if (s.contains("fail")) return MigrationState.FAILED;
        if (s.contains("missing")) return MigrationState.FAILED;
        if (s.equals("success") || s.equals("future") || s.startsWith("baseline")) {
            return MigrationState.APPLIED;
        }
        return MigrationState.PENDING;
    }

    /** Matches Flyway validate output complaining about checksum drift. */
    private static final Pattern CHECKSUM_DRIFT = Pattern.compile(
            "(?im)^(?:\\[(?:WARNING|WARN|ERROR)\\]\\s*)?(?:.*Migration\\s+checksum\\s+mismatch.*|.*checksum.*mismatch.*)$");

    static List<String> parseValidationDrift(String output) {
        List<String> out = new ArrayList<>();
        Matcher m = CHECKSUM_DRIFT.matcher(output);
        while (m.find()) {
            out.add(m.group().trim());
        }
        return out;
    }

    // --- Report body ----------------------------------------------------------

    static String renderBody(Path target,
                             CompileVerify.BuildTool tool,
                             List<Migration> migrations,
                             List<String> drift,
                             ProcessResult info,
                             ProcessResult validate) {
        StringBuilder sb = new StringBuilder();
        sb.append("# migration_status report\n\n");
        sb.append("- **Target:** `").append(target).append("`\n");
        sb.append("- **Build tool:** ").append(tool.name().toLowerCase(Locale.ROOT)).append("\n");
        sb.append("- **flyway:info exit:** ").append(info.exitCode()).append("\n");
        sb.append("- **flyway:validate exit:** ").append(validate.exitCode()).append("\n");
        sb.append("- **Migrations:** ").append(migrations.size()).append("\n");
        sb.append("- **Checksum drift:** ").append(drift.isEmpty() ? "none" : drift.size() + " issue(s)")
                .append("\n\n");

        if (migrations.isEmpty()) {
            sb.append("_(no migrations parsed from flyway:info output)_\n\n");
        } else {
            sb.append("## Migrations\n\n");
            sb.append("| Version | Description | Flyway state | Classification |\n");
            sb.append("|:---|:---|:---|:---|\n");
            for (Migration mg : migrations) {
                sb.append("| ").append(esc(mg.version()))
                        .append(" | ").append(esc(mg.description()))
                        .append(" | ").append(esc(mg.flywayState()))
                        .append(" | ").append(mg.state().name())
                        .append(" |\n");
            }
            sb.append('\n');
        }

        if (!drift.isEmpty()) {
            sb.append("## Validation drift\n\n");
            for (String d : drift) {
                sb.append("- ").append(esc(d)).append('\n');
            }
        }
        return sb.toString();
    }

    private static String esc(String s) {
        if (s == null) return "";
        return s.replace("|", "\\|");
    }

    // --- Types ----------------------------------------------------------------

    public enum MigrationState {APPLIED, PENDING, FAILED, OUT_OF_ORDER}

    public record Migration(String version, String description, String flywayState, MigrationState state) {}
}
