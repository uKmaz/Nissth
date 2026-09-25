package com.nissth.bridge.core;

import org.junit.jupiter.api.Test;

import java.nio.file.Path;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class ToolDispatcherTest {

    private static class StubHandler implements ToolHandler {
        private final String name;
        private final ToolResult result;

        StubHandler(String name, ToolResult result) {
            this.name = name;
            this.result = result;
        }

        @Override
        public String name() {
            return name;
        }

        @Override
        public ToolResult run(BridgeCommand cmd) {
            return result;
        }
    }

    @Test
    void loads_manifest_from_classpath_and_lists_five_tools() {
        ToolDispatcher dispatcher = new ToolDispatcher(List.of());
        BindingManifest m = dispatcher.manifest();

        assertThat(m.binding()).isEqualTo("spring-boot");
        assertThat(m.bindingVersion()).isEqualTo("0.1.0");
        assertThat(m.tools()).extracting(BindingManifest.ToolDescriptor::name)
                .containsExactly("compile_verify", "endpoint_lens", "entity_lens",
                        "migration_status", "entity_field_add");
        // entity_field_add is the only action tool
        assertThat(m.tools()).filteredOn(BindingManifest.ToolDescriptor::isAction)
                .extracting(BindingManifest.ToolDescriptor::name)
                .containsExactly("entity_field_add");
    }

    @Test
    void describe_returns_the_named_tool() {
        ToolDispatcher dispatcher = new ToolDispatcher(List.of());
        var td = dispatcher.describe("entity_field_add").orElseThrow();
        assertThat(td.isAction()).isTrue();
        assertThat(td.enforces()).anyMatch(s -> s.contains("§8.9"));
    }

    @Test
    void dispatch_invokes_named_handler() {
        Path reportPath = Path.of("/tmp/report.md");
        ToolHandler h = new StubHandler("compile_verify", new ToolResult.Success(reportPath));
        ToolDispatcher dispatcher = new ToolDispatcher(List.of(h));

        BridgeCommand cmd = new BridgeCommand(
                "compile_verify", null, null,
                BridgeCommand.Scope.empty(), BridgeCommand.Output.defaults());

        ToolResult result = dispatcher.dispatch(cmd);
        assertThat(result).isInstanceOf(ToolResult.Success.class);
        assertThat(((ToolResult.Success) result).reportPath()).isEqualTo(reportPath);
    }

    @Test
    void unknown_tool_returns_failure_with_exit_4() {
        ToolDispatcher dispatcher = new ToolDispatcher(List.of());

        BridgeCommand cmd = new BridgeCommand(
                "no_such_tool", null, null,
                BridgeCommand.Scope.empty(), BridgeCommand.Output.defaults());

        ToolResult result = dispatcher.dispatch(cmd);
        assertThat(result).isInstanceOf(ToolResult.Failure.class);
        BridgeError err = ((ToolResult.Failure) result).error();
        assertThat(err.exitCode()).isEqualTo(4);
        assertThat(err.errorCode()).isEqualTo("unknown_tool");
        assertThat(err.tool()).isEqualTo("no_such_tool");
    }

    @Test
    void handler_throwing_BridgeException_becomes_failure() {
        ToolHandler thrower = new ToolHandler() {
            @Override public String name() { return "compile_verify"; }
            @Override public ToolResult run(BridgeCommand cmd) {
                throw new BridgeException(BridgeError.executeError(
                        "compile_verify", "boom", "missed_stop", 5));
            }
        };
        ToolDispatcher dispatcher = new ToolDispatcher(List.of(thrower));

        BridgeCommand cmd = new BridgeCommand(
                "compile_verify", null, null,
                BridgeCommand.Scope.empty(), BridgeCommand.Output.defaults());

        ToolResult result = dispatcher.dispatch(cmd);
        assertThat(result).isInstanceOf(ToolResult.Failure.class);
        BridgeError err = ((ToolResult.Failure) result).error();
        assertThat(err.exitCode()).isEqualTo(5);
        assertThat(err.errorCode()).isEqualTo("missed_stop");
    }

    @Test
    void handler_throwing_unchecked_becomes_generic_execute_failure() {
        ToolHandler thrower = new ToolHandler() {
            @Override public String name() { return "compile_verify"; }
            @Override public ToolResult run(BridgeCommand cmd) {
                throw new RuntimeException("kaboom");
            }
        };
        ToolDispatcher dispatcher = new ToolDispatcher(List.of(thrower));

        BridgeCommand cmd = new BridgeCommand(
                "compile_verify", null, null,
                BridgeCommand.Scope.empty(), BridgeCommand.Output.defaults());

        ToolResult result = dispatcher.dispatch(cmd);
        assertThat(result).isInstanceOf(ToolResult.Failure.class);
        BridgeError err = ((ToolResult.Failure) result).error();
        assertThat(err.stage()).isEqualTo(BridgeError.Stage.EXECUTE);
        assertThat(err.error()).contains("kaboom");
    }
}
