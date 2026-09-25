package com.nissth.bridge.core;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.networknt.schema.JsonSchema;
import com.networknt.schema.JsonSchemaFactory;
import com.networknt.schema.SpecVersion;
import com.networknt.schema.ValidationMessage;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.Set;
import java.util.stream.Collectors;

/**
 * Parses incoming JSON commands and validates them against the cross-stack
 * bridge-command.schema.json. Step 4 of Phase 05.
 */
public class JsonCommandParser {

    private static final String SCHEMA_RESOURCE = "/bridge-command.schema.json";

    private final ObjectMapper mapper;
    private final JsonSchema schema;

    public JsonCommandParser() {
        this(new ObjectMapper());
    }

    public JsonCommandParser(ObjectMapper mapper) {
        this.mapper = mapper;
        this.schema = loadSchema();
    }

    private static JsonSchema loadSchema() {
        try (InputStream in = JsonCommandParser.class.getResourceAsStream(SCHEMA_RESOURCE)) {
            if (in == null) {
                throw new IllegalStateException("bridge-command.schema.json not found on classpath; "
                        + "ensure Bindings/_schemas/ is wired into the binding's pom.xml <resources> block.");
            }
            JsonSchemaFactory factory = JsonSchemaFactory.getInstance(SpecVersion.VersionFlag.V202012);
            return factory.getSchema(in);
        } catch (IOException e) {
            throw new IllegalStateException("Failed to load bridge-command.schema.json", e);
        }
    }

    /**
     * Parse + validate. Throws BridgeException(parse) on malformed JSON,
     * BridgeException(validate) on schema violation.
     */
    public BridgeCommand parse(String json) {
        JsonNode node;
        try {
            node = mapper.readTree(json);
        } catch (IOException e) {
            throw new BridgeException(BridgeError.parseError("<unknown>",
                    "Malformed JSON: " + e.getMessage()));
        }

        Set<ValidationMessage> errors = schema.validate(node);
        String tool = node.has("tool") ? node.get("tool").asText() : "<unknown>";
        if (!errors.isEmpty()) {
            String summary = errors.stream()
                    .map(ValidationMessage::getMessage)
                    .collect(Collectors.joining("; "));
            throw new BridgeException(BridgeError.validateError(tool, summary));
        }

        try {
            return mapper.treeToValue(node, BridgeCommand.class);
        } catch (IOException e) {
            throw new BridgeException(BridgeError.validateError(tool,
                    "Could not bind to BridgeCommand: " + e.getMessage()));
        }
    }

    /** Read the entire stdin as a JSON command. */
    public BridgeCommand parseStdin(InputStream in) {
        try {
            String json = new String(in.readAllBytes(), StandardCharsets.UTF_8);
            return parse(json);
        } catch (IOException e) {
            throw new BridgeException(BridgeError.parseError("<unknown>",
                    "Could not read stdin: " + e.getMessage()));
        }
    }
}
