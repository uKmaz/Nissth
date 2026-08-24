package com.nissth.bridge.cli;

import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeError;
import com.nissth.bridge.core.JsonCommandParser;
import com.nissth.bridge.core.ToolDispatcher;
import com.nissth.bridge.core.ToolHandler;
import com.nissth.bridge.core.ToolResult;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class NissthBridgeCliTest {

    /** Stub ToolHandler — records the BridgeCommand and returns a canned result. */
    static class RecordingHandler implements ToolHandler {
        private final String name;
        private final ToolResult result;
        BridgeCommand received;

        RecordingHandler(String name, ToolResult result) {
            this.name = name;
            this.result = result;
        }

        @Override
        public String name() {
            return name;
        }

        @Override
        public ToolResult run(BridgeCommand cmd) {
            this.received = cmd;
            return result;
        }
    }

    private static InputStream emptyIn() {
        return new ByteArrayInputStream(new byte[0]);
    }

    private static NissthBridgeCli cliWith(List<ToolHandler> handlers) {
        return new NissthBridgeCli(new ToolDispatcher(handlers), new JsonCommandParser());
    }

    // --- Discovery flags -----------------------------------------------------

    @Test
    void empty_args_prints_usage_and_exits_2() {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(new String[]{}, emptyIn(),
                new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(err, true, StandardCharsets.UTF_8));
        assertThat(exit).isEqualTo(2);
        assertThat(err.toString(StandardCharsets.UTF_8)).contains("usage:");
    }

    @Test
    void help_prints_usage_to_stdout_and_exits_0() {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(new String[]{"--help"}, emptyIn(),
                new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(err, true, StandardCharsets.UTF_8));
        assertThat(exit).isZero();
        assertThat(out.toString(StandardCharsets.UTF_8)).contains("usage:").contains("nissth-bridge");
    }

    @Test
    void list_bindings_prints_manifest_header_as_json() {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(new String[]{"--list-bindings"}, emptyIn(),
                new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));
        assertThat(exit).isZero();
        String s = out.toString(StandardCharsets.UTF_8);
        assertThat(s)
                .contains("\"binding\" : \"spring-boot\"")
                .contains("\"binding_version\" : \"0.1.0\"")
                .contains("\"tool_count\" : 5");
    }

    @Test
    void list_tools_prints_all_five_tools_one_per_line() {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(new String[]{"--list-tools"}, emptyIn(),
                new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));
        assertThat(exit).isZero();
        String[] lines = out.toString(StandardCharsets.UTF_8).strip().split("\\R");
        assertThat(lines).hasSize(5);
        assertThat(lines[0]).startsWith("compile_verify\tdiagnostic\t");
        assertThat(lines[4]).startsWith("entity_field_add\taction\t");
    }

    @Test
    void list_tools_with_unknown_binding_returns_empty() {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(
                new String[]{"--list-tools", "--binding", "expo"}, emptyIn(),
                new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));
        assertThat(exit).isZero();
        assertThat(out.toString(StandardCharsets.UTF_8)).isEmpty();
    }

    @Test
    void describe_known_tool_prints_descriptor_with_enforces() {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(
                new String[]{"--describe", "entity_field_add"}, emptyIn(),
                new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));
        assertThat(exit).isZero();
        String s = out.toString(StandardCharsets.UTF_8);
        assertThat(s)
                .contains("\"name\" : \"entity_field_add\"")
                .contains("\"kind\" : \"action\"")
                .contains("CLAUDE.md §8.9")
                .contains("entity_fqn");
    }

    @Test
    void describe_unknown_tool_exits_4() {
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(
                new String[]{"--describe", "nonexistent"}, emptyIn(),
                new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8), new PrintStream(err, true, StandardCharsets.UTF_8));
        assertThat(exit).isEqualTo(4);
        assertThat(err.toString(StandardCharsets.UTF_8)).contains("unknown tool");
    }

    @Test
    void describe_without_tool_name_exits_2() {
        int exit = cliWith(List.of()).run(new String[]{"--describe"}, emptyIn(),
                new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8),
                new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));
        assertThat(exit).isEqualTo(2);
    }

    @Test
    void unknown_flag_at_position_0_exits_2() {
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(new String[]{"--frobulator"}, emptyIn(),
                new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8), new PrintStream(err, true, StandardCharsets.UTF_8));
        assertThat(exit).isEqualTo(2);
        assertThat(err.toString(StandardCharsets.UTF_8)).contains("unknown flag");
    }

    // --- Flag-form parsing ---------------------------------------------------

    @Test
    void parseFlagged_maps_top_level_scope_keys() {
        BridgeCommand cmd = NissthBridgeCli.parseFlagged(new String[]{
                "compile_verify",
                "--scope.root-path", "/some/path",
                "--scope.package", "com.example",
                "--scope.max-depth", "3",
                "--mode", "default"
        });
        assertThat(cmd.tool()).isEqualTo("compile_verify");
        assertThat(cmd.mode()).isEqualTo("default");
        assertThat(cmd.scope().rootPath()).isEqualTo("/some/path");
        assertThat(cmd.scope().packageName()).isEqualTo("com.example");
        assertThat(cmd.scope().maxDepth()).isEqualTo(3);
    }

    @Test
    void parseFlagged_maps_scope_extra_with_boolean_and_int_coercion() {
        BridgeCommand cmd = NissthBridgeCli.parseFlagged(new String[]{
                "entity_field_add",
                "--scope.extra.entity_fqn", "com.example.fixture.Item",
                "--scope.extra.field_name", "qty",
                "--scope.extra.field_type", "Integer",
                "--scope.extra.nullable", "false",
                "--scope.extra.count_hint", "42"
        });
        assertThat(cmd.scope().extra()).containsEntry("entity_fqn", "com.example.fixture.Item");
        assertThat(cmd.scope().extra()).containsEntry("field_name", "qty");
        assertThat(cmd.scope().extra()).containsEntry("nullable", Boolean.FALSE);
        assertThat(cmd.scope().extra()).containsEntry("count_hint", 42);
    }

    @Test
    void parseFlagged_maps_scope_names_csv() {
        BridgeCommand cmd = NissthBridgeCli.parseFlagged(new String[]{
                "endpoint_lens",
                "--scope.names", "ItemController,UserController"
        });
        assertThat(cmd.scope().names()).containsExactly("ItemController", "UserController");
    }

    @Test
    void parseFlagged_maps_output_keys() {
        BridgeCommand cmd = NissthBridgeCli.parseFlagged(new String[]{
                "compile_verify",
                "--output.format", "json",
                "--output.destination", "return",
                "--output.file-name", "my-report"
        });
        assertThat(cmd.output().format()).isEqualTo(BridgeCommand.Format.JSON);
        assertThat(cmd.output().destination()).isEqualTo(BridgeCommand.Destination.RETURN);
        assertThat(cmd.output().fileName()).isEqualTo("my-report");
    }

    @Test
    void coerce_handles_booleans_ints_and_strings() {
        assertThat(NissthBridgeCli.coerce("true")).isEqualTo(Boolean.TRUE);
        assertThat(NissthBridgeCli.coerce("false")).isEqualTo(Boolean.FALSE);
        assertThat(NissthBridgeCli.coerce("42")).isEqualTo(42);
        assertThat(NissthBridgeCli.coerce("-7")).isEqualTo(-7);
        assertThat(NissthBridgeCli.coerce("hello")).isEqualTo("hello");
    }

    // --- Dispatch ------------------------------------------------------------

    @Test
    void successful_dispatch_prints_report_path_and_exits_0(@TempDir Path tmp) throws IOException {
        Path fakeReport = tmp.resolve("compile_verify_2026.md");
        Files.writeString(fakeReport, "# fake report body\n");

        RecordingHandler stub = new RecordingHandler("compile_verify",
                new ToolResult.Success(fakeReport));
        ByteArrayOutputStream out = new ByteArrayOutputStream();

        int exit = cliWith(List.of(stub)).run(new String[]{
                "compile_verify",
                "--scope.root-path", "/x"
        }, emptyIn(), new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));

        assertThat(exit).isZero();
        assertThat(out.toString(StandardCharsets.UTF_8).strip())
                .isEqualTo(fakeReport.toAbsolutePath().toString());
        assertThat(stub.received.scope().rootPath()).isEqualTo("/x");
    }

    @Test
    void successful_dispatch_with_destination_return_prints_body(@TempDir Path tmp) throws IOException {
        Path fakeReport = tmp.resolve("rep.md");
        Files.writeString(fakeReport, "# body\n\nbody-line\n");

        RecordingHandler stub = new RecordingHandler("compile_verify",
                new ToolResult.Success(fakeReport));
        ByteArrayOutputStream out = new ByteArrayOutputStream();

        int exit = cliWith(List.of(stub)).run(new String[]{
                "compile_verify",
                "--output.destination", "return"
        }, emptyIn(), new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));

        assertThat(exit).isZero();
        assertThat(out.toString(StandardCharsets.UTF_8)).contains("body-line");
    }

    @Test
    void unknown_tool_exits_4_and_prints_error_json() {
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(new String[]{"ghost_tool"}, emptyIn(),
                new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8), new PrintStream(err, true, StandardCharsets.UTF_8));
        assertThat(exit).isEqualTo(4);
        assertThat(err.toString(StandardCharsets.UTF_8))
                .contains("\"error_code\" : \"unknown_tool\"")
                .contains("\"exit_code\" : 4");
    }

    @Test
    void execute_failure_exits_with_bridge_error_exit_code() {
        BridgeError missed = BridgeError.executeError("compile_verify",
                "Gradle --stop returned exit 1; freshness contract violated.",
                "missed_stop", 5);
        RecordingHandler stub = new RecordingHandler("compile_verify",
                new ToolResult.Failure(missed));

        ByteArrayOutputStream err = new ByteArrayOutputStream();
        int exit = cliWith(List.of(stub)).run(new String[]{"compile_verify"}, emptyIn(),
                new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8), new PrintStream(err, true, StandardCharsets.UTF_8));

        assertThat(exit).isEqualTo(5);
        assertThat(err.toString(StandardCharsets.UTF_8))
                .contains("\"error_code\" : \"missed_stop\"")
                .contains("\"exit_code\" : 5");
    }

    @Test
    void flag_requiring_value_at_end_of_args_exits_2() {
        ByteArrayOutputStream err = new ByteArrayOutputStream();
        int exit = cliWith(List.of()).run(new String[]{
                "compile_verify",
                "--scope.root-path"   // no value
        }, emptyIn(), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8), new PrintStream(err, true, StandardCharsets.UTF_8));
        assertThat(exit).isEqualTo(2);
        assertThat(err.toString(StandardCharsets.UTF_8)).contains("requires a value");
    }

    // --- JSON stdin ----------------------------------------------------------

    @Test
    void json_stdin_parses_and_dispatches(@TempDir Path tmp) throws IOException {
        Path fakeReport = tmp.resolve("r.md");
        Files.writeString(fakeReport, "# r\n");
        RecordingHandler stub = new RecordingHandler("compile_verify",
                new ToolResult.Success(fakeReport));

        String json = """
                {"tool":"compile_verify","scope":{"root_path":"/json/path"}}
                """;
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        int exit = cliWith(List.of(stub)).run(new String[]{"--json-stdin"},
                new ByteArrayInputStream(json.getBytes(StandardCharsets.UTF_8)),
                new PrintStream(out, true, StandardCharsets.UTF_8), new PrintStream(new ByteArrayOutputStream(), true, StandardCharsets.UTF_8));

        assertThat(exit).isZero();
        assertThat(stub.received.scope().rootPath()).isEqualTo("/json/path");
    }
}
