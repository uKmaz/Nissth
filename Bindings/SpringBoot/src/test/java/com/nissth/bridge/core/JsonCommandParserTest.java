package com.nissth.bridge.core;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class JsonCommandParserTest {

    private JsonCommandParser parser;

    @BeforeEach
    void setUp() {
        parser = new JsonCommandParser();
    }

    @Test
    void parses_minimal_valid_command() {
        BridgeCommand cmd = parser.parse("{\"tool\":\"compile_verify\"}");

        assertThat(cmd.tool()).isEqualTo("compile_verify");
        assertThat(cmd.mode()).isNull();
        assertThat(cmd.scope()).isNotNull();
        assertThat(cmd.output().format()).isEqualTo(BridgeCommand.Format.MARKDOWN);
        assertThat(cmd.output().destination()).isEqualTo(BridgeCommand.Destination.FILE);
    }

    @Test
    void parses_full_command_with_scope_and_output() {
        String json = """
                {
                  "tool": "endpoint_lens",
                  "mode": "with_dto",
                  "context_id": "abc-123",
                  "scope": {
                    "package": "com.example.reservation",
                    "max_depth": 2,
                    "extra": {"include_deprecated": true}
                  },
                  "output": {
                    "format": "json",
                    "destination": "return"
                  }
                }
                """;
        BridgeCommand cmd = parser.parse(json);

        assertThat(cmd.tool()).isEqualTo("endpoint_lens");
        assertThat(cmd.mode()).isEqualTo("with_dto");
        assertThat(cmd.contextId()).isEqualTo("abc-123");
        assertThat(cmd.scope().packageName()).isEqualTo("com.example.reservation");
        assertThat(cmd.scope().maxDepth()).isEqualTo(2);
        assertThat(cmd.scope().extra()).containsEntry("include_deprecated", true);
        assertThat(cmd.output().format()).isEqualTo(BridgeCommand.Format.JSON);
        assertThat(cmd.output().destination()).isEqualTo(BridgeCommand.Destination.RETURN);
    }

    @Test
    void parse_error_on_malformed_json() {
        assertThatThrownBy(() -> parser.parse("{not json"))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    BridgeError err = ((BridgeException) e).error();
                    assertThat(err.stage()).isEqualTo(BridgeError.Stage.PARSE);
                    assertThat(err.exitCode()).isEqualTo(2);
                });
    }

    @Test
    void validate_error_on_missing_tool() {
        assertThatThrownBy(() -> parser.parse("{\"mode\":\"default\"}"))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    BridgeError err = ((BridgeException) e).error();
                    assertThat(err.stage()).isEqualTo(BridgeError.Stage.VALIDATE);
                    assertThat(err.exitCode()).isEqualTo(2);
                });
    }

    @Test
    void validate_error_on_unknown_top_level_scope_key() {
        // additionalProperties:false on scope rejects unknown top-level keys
        // (stack-specific keys must go in scope.extra)
        String json = "{\"tool\":\"x\",\"scope\":{\"frobulator\":\"yes\"}}";
        assertThatThrownBy(() -> parser.parse(json))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> assertThat(((BridgeException) e).error().stage())
                        .isEqualTo(BridgeError.Stage.VALIDATE));
    }

    @Test
    void scope_extra_round_trips_with_unknown_keys() {
        // scope.extra is an open object — anything is allowed
        String json = "{\"tool\":\"entity_field_add\",\"scope\":{\"extra\":{\"entity_fqn\":\"com.example.Item\",\"field_name\":\"x\"}}}";
        BridgeCommand cmd = parser.parse(json);
        assertThat(cmd.scope().extra())
                .containsEntry("entity_fqn", "com.example.Item")
                .containsEntry("field_name", "x");
    }
}
