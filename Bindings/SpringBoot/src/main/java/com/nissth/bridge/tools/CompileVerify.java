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

import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * compile_verify — diagnostic, build-tool adaptive.
 *
 * <p>Detects the target project's build tool (Maven vs Gradle). Maven path:
 * {@code mvn clean compile test-compile -U -B}. Gradle path:
 * {@code ./gradlew --stop} (refuse to proceed if this fails — exit 5)
 * then {@code ./gradlew clean compileJava compileTestJava --no-daemon}.
 *
 * <p>Enforces {@code CLAUDE.md} §8.6 false-CLEAN protocol via the §11.7
 * hard-enforce contract.
 */
public class CompileVerify implements ToolHandler {

    public static final String NAME = "compile_verify";

    private static final Pattern MAVEN_ERR =
            Pattern.compile("^\\[ERROR\\]\\s+(?<file>[^:\\s]+\\.java):\\[(?<line>\\d+),(?<col>\\d+)\\]\\s+(?<msg>.+)$",
                    Pattern.MULTILINE);
    private static final Pattern GRADLE_ERR =
            Pattern.compile("^(?<file>[^:\\s]+\\.java):(?<line>\\d+):\\s+error:\\s+(?<msg>.+)$",
                    Pattern.MULTILINE);

    private final SubprocessRunner runner;
    private final ReportWriter writer;
    private final Duration commandTimeout;

    public CompileVerify(SubprocessRunner runner, ReportWriter writer) {
        this(runner, writer, Duration.ofMinutes(5));
    }

    public CompileVerify(SubprocessRunner runner, ReportWriter writer, Duration commandTimeout) {
        this.runner = runner;
        this.writer = writer;
        this.commandTimeout = commandTimeout;
    }

    @Override
    public String name() {
        return NAME;
    }

    public enum BuildTool {MAVEN, GRADLE}

    @Override
    public ToolResult run(BridgeCommand cmd) {
        Path target = resolveTarget(cmd);
        BuildTool tool = detectBuildTool(target);

        OffsetDateTime startedAt = OffsetDateTime.now();
        Result r = switch (tool) {
            case MAVEN -> runMaven(target);
            case GRADLE -> runGradle(target);
        };

        ReportContext ctx = ReportContext.builder()
                .tool(NAME)
                .mode(cmd.mode() == null ? "default" : cmd.mode())
                .scope(cmd.scope())
                .generatedAt(OffsetDateTime.now())
                .contextId(cmd.contextId())
                .freshness(
                        tool.name().toLowerCase() + " subprocess in " + target,
                        "clean rebuild started " + startedAt + "; exit=" + r.exitCode,
                        tool == BuildTool.MAVEN
                                ? "Maven clean+compile in batch mode with -U; no persistent daemon"
                                : "Gradle daemon stopped before clean rebuild; classpath fresh")
                .body(renderBody(tool, target, r))
                .build();

        Path report = writer.write(ctx);
        return new ToolResult.Success(report);
    }

    static Path resolveTarget(BridgeCommand cmd) {
        String rp = cmd.scope() == null ? null : cmd.scope().rootPath();
        if (rp == null || rp.isBlank()) {
            return Path.of("").toAbsolutePath();
        }
        return Path.of(rp).toAbsolutePath();
    }

    static BuildTool detectBuildTool(Path dir) {
        if (!Files.isDirectory(dir)) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Target directory does not exist: " + dir));
        }
        boolean hasMaven = Files.exists(dir.resolve("pom.xml"));
        boolean hasGradle = Files.exists(dir.resolve("build.gradle"))
                || Files.exists(dir.resolve("build.gradle.kts"));
        if (hasMaven && hasGradle) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Ambiguous build tool — both pom.xml and build.gradle(.kts) found in " + dir));
        }
        if (hasMaven) return BuildTool.MAVEN;
        if (hasGradle) return BuildTool.GRADLE;
        throw new BridgeException(BridgeError.validateError(NAME,
                "No pom.xml or build.gradle(.kts) found in " + dir));
    }

    private Result runMaven(Path target) {
        ProcessResult pr = runner.run(target,
                List.of("mvn", "clean", "compile", "test-compile", "-U", "-B"),
                commandTimeout);
        List<CompileError> errors = parseMavenErrors(pr.stdout() + "\n" + pr.stderr());
        return new Result(pr.exitCode(), errors, pr.stdout(), pr.stderr());
    }

    private Result runGradle(Path target) {
        // Hard-enforce freshness sequence: --stop before clean compile.
        ProcessResult stop = runner.run(target, gradleCommand(target, "--stop"), commandTimeout);
        if (!stop.ok()) {
            throw new BridgeException(BridgeError.executeError(
                    NAME,
                    "Gradle --stop returned exit " + stop.exitCode() + "; freshness contract violated. stderr: " + stop.stderr(),
                    "missed_stop",
                    5));
        }
        ProcessResult compile = runner.run(target,
                gradleCommand(target, "clean", "compileJava", "compileTestJava", "--no-daemon"),
                commandTimeout);
        List<CompileError> errors = parseGradleErrors(compile.stdout() + "\n" + compile.stderr());
        return new Result(compile.exitCode(), errors, compile.stdout(), compile.stderr());
    }

    static List<String> gradleCommand(Path target, String... args) {
        boolean windows = System.getProperty("os.name", "").toLowerCase().contains("win");
        String wrapper;
        if (windows && Files.exists(target.resolve("gradlew.bat"))) {
            wrapper = target.resolve("gradlew.bat").toString();
        } else if (Files.exists(target.resolve("gradlew"))) {
            wrapper = target.resolve("gradlew").toString();
        } else {
            wrapper = "gradle";
        }
        List<String> cmd = new ArrayList<>();
        cmd.add(wrapper);
        for (String a : args) cmd.add(a);
        return cmd;
    }

    static List<CompileError> parseMavenErrors(String output) {
        return parse(output, MAVEN_ERR, true);
    }

    static List<CompileError> parseGradleErrors(String output) {
        return parse(output, GRADLE_ERR, false);
    }

    private static List<CompileError> parse(String output, Pattern p, boolean hasColumn) {
        List<CompileError> out = new ArrayList<>();
        Matcher m = p.matcher(output);
        while (m.find()) {
            int line = Integer.parseInt(m.group("line"));
            int col = hasColumn ? Integer.parseInt(m.group("col")) : 0;
            out.add(new CompileError(m.group("file"), line, col, m.group("msg").trim()));
        }
        return out;
    }

    static String renderBody(BuildTool tool, Path target, Result r) {
        StringBuilder sb = new StringBuilder();
        sb.append("# compile_verify report\n\n");
        sb.append("- **Target:** `").append(target).append("`\n");
        sb.append("- **Build tool:** ").append(tool.name().toLowerCase()).append("\n");
        sb.append("- **Exit code:** ").append(r.exitCode).append("\n");
        sb.append("- **Status:** ").append(r.exitCode == 0 ? "CLEAN" : "HAS_ERRORS").append("\n\n");

        if (r.errors.isEmpty() && r.exitCode == 0) {
            sb.append("No compilation errors.\n");
            return sb.toString();
        }

        sb.append("## Errors\n\n");
        if (r.errors.isEmpty()) {
            sb.append("_(exit was non-zero but no `file:line` errors matched; full stderr follows.)_\n\n");
            sb.append("```\n").append(truncate(r.stderr, 4000)).append("\n```\n");
            return sb.toString();
        }
        sb.append("| File | Line | Col | Message |\n");
        sb.append("|:---|---:|---:|:---|\n");
        for (CompileError e : r.errors) {
            sb.append("| `").append(e.file()).append("` | ")
                    .append(e.line()).append(" | ")
                    .append(e.column()).append(" | ")
                    .append(escape(e.message())).append(" |\n");
        }
        return sb.toString();
    }

    private static String escape(String s) {
        return s.replace("|", "\\|");
    }

    private static String truncate(String s, int max) {
        if (s == null) return "";
        return s.length() <= max ? s : s.substring(0, max) + "\n…(truncated)";
    }

    /** Pure-data result of one build-tool invocation. */
    record Result(int exitCode, List<CompileError> errors, String stdout, String stderr) {}

    public record CompileError(String file, int line, int column, String message) {}
}
