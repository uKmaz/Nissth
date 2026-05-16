package com.nissth.bridge.core;

import com.fasterxml.jackson.annotation.JsonCreator;
import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.annotation.JsonProperty;
import com.fasterxml.jackson.annotation.JsonValue;

/**
 * Error response shape per CLAUDE.md §11.2 and
 * Bindings/_schemas/bridge-command.schema.json $defs.errorResponse.
 * Carries a CLI exit code per §11.5.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record BridgeError(
        String error,
        String tool,
        Stage stage,
        @JsonProperty("context_id") String contextId,
        @JsonProperty("error_code") String errorCode,
        @JsonProperty("exit_code") int exitCode
) {

    public enum Stage {
        PARSE("parse"),
        VALIDATE("validate"),
        EXECUTE("execute"),
        FORMAT("format");

        private final String jsonValue;

        Stage(String jsonValue) {
            this.jsonValue = jsonValue;
        }

        @JsonValue
        public String jsonValue() {
            return jsonValue;
        }

        @JsonCreator
        public static Stage fromJson(String s) {
            for (Stage st : values()) {
                if (st.jsonValue.equals(s)) return st;
            }
            throw new IllegalArgumentException("Unknown stage: " + s);
        }
    }

    public static BridgeError parseError(String tool, String msg) {
        return new BridgeError(msg, tool, Stage.PARSE, null, null, 2);
    }

    public static BridgeError validateError(String tool, String msg) {
        return new BridgeError(msg, tool, Stage.VALIDATE, null, null, 2);
    }

    public static BridgeError executeError(String tool, String msg) {
        return new BridgeError(msg, tool, Stage.EXECUTE, null, null, 3);
    }

    /**
     * For tool-specific execute failures that need an error_code and a non-default exit code
     * (e.g., compile_verify's exit 5 on missed daemon-stop; entity_field_add's exit 5 on hard-enforce
     * rollback; migration_status's missing_flyway_plugin).
     */
    public static BridgeError executeError(String tool, String msg, String errorCode, int exitCode) {
        return new BridgeError(msg, tool, Stage.EXECUTE, null, errorCode, exitCode);
    }

    public static BridgeError formatError(String tool, String msg) {
        return new BridgeError(msg, tool, Stage.FORMAT, null, null, 3);
    }

    /**
     * For "no binding registered for tool" — CLI exit code 4 per §11.5.
     */
    public static BridgeError unknownTool(String tool) {
        return new BridgeError(
                "No handler registered for tool: " + tool,
                tool,
                Stage.VALIDATE,
                null,
                "unknown_tool",
                4
        );
    }
}
