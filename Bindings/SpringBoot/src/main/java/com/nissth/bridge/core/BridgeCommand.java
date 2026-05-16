package com.nissth.bridge.core;

import com.fasterxml.jackson.annotation.JsonCreator;
import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.annotation.JsonProperty;
import com.fasterxml.jackson.annotation.JsonValue;

import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Parsed Bridge command. Mirrors the JSON shape defined in
 * Bindings/_schemas/bridge-command.schema.json and CLAUDE.md §11.2.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record BridgeCommand(
        String tool,
        String mode,
        @JsonProperty("context_id") String contextId,
        Scope scope,
        Output output
) {

    public BridgeCommand {
        Objects.requireNonNull(tool, "tool");
        if (scope == null) scope = Scope.empty();
        if (output == null) output = Output.defaults();
    }

    /**
     * Stack-agnostic query scope. Stack-specific filters live in {@link #extra}.
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record Scope(
            @JsonProperty("package") String packageName,
            @JsonProperty("root_path") String rootPath,
            List<String> names,
            @JsonProperty("file_extension") String fileExtension,
            @JsonProperty("tag_filter") String tagFilter,
            @JsonProperty("type_filter") String typeFilter,
            @JsonProperty("max_depth") Integer maxDepth,
            String profile,
            Map<String, Object> extra
    ) {
        public Scope {
            if (names == null) names = List.of();
            if (extra == null) extra = Map.of();
        }

        public static Scope empty() {
            return new Scope(null, null, List.of(), null, null, null, null, null, Map.of());
        }
    }

    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record Output(
            Format format,
            Destination destination,
            @JsonProperty("file_name") String fileName
    ) {
        public Output {
            if (format == null) format = Format.MARKDOWN;
            if (destination == null) destination = Destination.FILE;
        }

        public static Output defaults() {
            return new Output(Format.MARKDOWN, Destination.FILE, null);
        }
    }

    public enum Format {
        MARKDOWN("markdown"),
        JSON("json"),
        FLAT_TEXT("flat_text");

        private final String jsonValue;

        Format(String jsonValue) {
            this.jsonValue = jsonValue;
        }

        @JsonValue
        public String jsonValue() {
            return jsonValue;
        }

        @JsonCreator
        public static Format fromJson(String s) {
            if (s == null) return MARKDOWN;
            for (Format f : values()) {
                if (f.jsonValue.equals(s)) return f;
            }
            throw new IllegalArgumentException("Unknown format: " + s);
        }
    }

    public enum Destination {
        FILE("file"),
        RETURN("return"),
        CONSOLE("console");

        private final String jsonValue;

        Destination(String jsonValue) {
            this.jsonValue = jsonValue;
        }

        @JsonValue
        public String jsonValue() {
            return jsonValue;
        }

        @JsonCreator
        public static Destination fromJson(String s) {
            if (s == null) return FILE;
            for (Destination d : values()) {
                if (d.jsonValue.equals(s)) return d;
            }
            throw new IllegalArgumentException("Unknown destination: " + s);
        }
    }
}
