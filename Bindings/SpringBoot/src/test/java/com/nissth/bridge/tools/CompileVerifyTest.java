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

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class CompileVerifyTest {

    /** Recording stub: captures every invocation and returns canned results in order. */
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

    @Test
    void detectBuildTool_returns_MAVEN_for_pom_only(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), "<project/>");
        assertThat(CompileVerify.detectBuildTool(target)).isEqualTo(CompileVerify.BuildTool.MAVEN);
    }

    @Test
    void detectBuildTool_returns_GRADLE_for_build_gradle_kts(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("build.gradle.kts"), "// empty");
        assertThat(CompileVerify.detectBuildTool(target)).isEqualTo(CompileVerify.BuildTool.GRADLE);
    }

    @Test
    void detectBuildTool_errors_on_both_build_files(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), "<project/>");
        Files.writeString(target.resolve("build.gradle"), "// empty");
        assertThatThrownBy(() -> CompileVerify.detectBuildTool(target))
                .isInstanceOf(BridgeException.class)
                .hasMessageContaining("Ambiguous build tool");
    }

    @Test
    void detectBuildTool_errors_on_neither(@TempDir Path target) {
        assertThatThrownBy(() -> CompileVerify.detectBuildTool(target))
                .isInstanceOf(BridgeException.class)
                .hasMessageContaining("No pom.xml or build.gradle");
    }

    @Test
    void parseMavenErrors_extracts_file_line_column_message() {
        String output = """
                [INFO] Compiling 12 source files
                [ERROR] /home/u/Item.java:[15,21] cannot find symbol
                [ERROR]   symbol: class Foo
                [ERROR] /home/u/Other.java:[3,0] package x does not exist
                [INFO] BUILD FAILURE
                """;
        List<CompileVerify.CompileError> errors = CompileVerify.parseMavenErrors(output);
        assertThat(errors).hasSize(2);
        assertThat(errors.get(0)).isEqualTo(new CompileVerify.CompileError(
                "/home/u/Item.java", 15, 21, "cannot find symbol"));
        assertThat(errors.get(1).file()).isEqualTo("/home/u/Other.java");
        assertThat(errors.get(1).line()).isEqualTo(3);
    }

    @Test
    void parseGradleErrors_extracts_javac_format() {
        String output = """
                > Task :compileJava FAILED
                /home/u/src/main/java/Item.java:18: error: ';' expected
                    private String name
                                       ^
                /home/u/src/main/java/Item.java:25: error: cannot find symbol
                """;
        List<CompileVerify.CompileError> errors = CompileVerify.parseGradleErrors(output);
        assertThat(errors).hasSize(2);
        assertThat(errors.get(0).line()).isEqualTo(18);
        assertThat(errors.get(0).message()).isEqualTo("';' expected");
        assertThat(errors.get(0).column()).isZero();
    }

    @Test
    void maven_path_returns_CLEAN_on_zero_exit(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), "<project/>");
        StubRunner runner = new StubRunner().enqueue(0, "[INFO] BUILD SUCCESS\n", "");
        ReportWriter writer = new ReportWriter(target);

        CompileVerify tool = new CompileVerify(runner, writer, Duration.ofSeconds(30));
        BridgeCommand cmd = new BridgeCommand("compile_verify", null, null,
                new BridgeCommand.Scope(null, target.toString(), List.of(),
                        null, null, null, null, null, java.util.Map.of()),
                BridgeCommand.Output.defaults());

        ToolResult result = tool.run(cmd);
        assertThat(result).isInstanceOf(ToolResult.Success.class);
        assertThat(runner.calls).hasSize(1);
        assertThat(runner.calls.get(0)).startsWith("mvn", "clean", "compile");
    }

    @Test
    void mavenCommand_prefers_mvnw_wrapper_when_present(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("pom.xml"), "<project/>");
        Files.writeString(target.resolve("mvnw"), "#!/bin/sh\n");
        Files.writeString(target.resolve("mvnw.cmd"), "@echo off\r\n");

        List<String> cmd = CompileVerify.mavenCommand(target, "flyway:info", "-B");
        boolean windows = System.getProperty("os.name", "").toLowerCase().contains("win");
        String expectedExe = windows
                ? target.resolve("mvnw.cmd").toString()
                : target.resolve("mvnw").toString();
        assertThat(cmd).containsExactly(expectedExe, "flyway:info", "-B");
    }

    @Test
    void mavenCommand_falls_back_to_mvn_when_no_wrapper(@TempDir Path target) {
        List<String> cmd = CompileVerify.mavenCommand(target, "clean", "compile");
        assertThat(cmd).containsExactly("mvn", "clean", "compile");
    }

    @Test
    void gradle_path_exits_5_when_stop_fails(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("build.gradle.kts"), "// empty");
        // First call (--stop) returns non-zero → tool must abort with missed_stop
        StubRunner runner = new StubRunner().enqueue(1, "", "daemon stop failed");
        ReportWriter writer = new ReportWriter(target);

        CompileVerify tool = new CompileVerify(runner, writer, Duration.ofSeconds(30));
        BridgeCommand cmd = new BridgeCommand("compile_verify", null, null,
                new BridgeCommand.Scope(null, target.toString(), List.of(),
                        null, null, null, null, null, java.util.Map.of()),
                BridgeCommand.Output.defaults());

        assertThatThrownBy(() -> tool.run(cmd))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    var err = ((BridgeException) e).error();
                    assertThat(err.exitCode()).isEqualTo(5);
                    assertThat(err.errorCode()).isEqualTo("missed_stop");
                    assertThat(err.error()).contains("freshness contract violated");
                });
        // Only one call was made (--stop); compile was NOT invoked
        assertThat(runner.calls).hasSize(1);
    }

    @Test
    void gradle_path_invokes_stop_then_compile(@TempDir Path target) throws IOException {
        Files.writeString(target.resolve("build.gradle"), "// empty");
        StubRunner runner = new StubRunner()
                .enqueue(0, "", "")  // --stop
                .enqueue(0, "BUILD SUCCESSFUL\n", "");  // compile
        ReportWriter writer = new ReportWriter(target);

        CompileVerify tool = new CompileVerify(runner, writer, Duration.ofSeconds(30));
        BridgeCommand cmd = new BridgeCommand("compile_verify", null, null,
                new BridgeCommand.Scope(null, target.toString(), List.of(),
                        null, null, null, null, null, java.util.Map.of()),
                BridgeCommand.Output.defaults());

        ToolResult result = tool.run(cmd);
        assertThat(result).isInstanceOf(ToolResult.Success.class);
        assertThat(runner.calls).hasSize(2);
        assertThat(runner.calls.get(0)).contains("--stop");
        assertThat(runner.calls.get(1)).contains("clean", "compileJava");
    }
}
