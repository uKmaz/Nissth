package com.nissth.bridge.core;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.io.InputStream;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * Reads the binding's manifest (spring-boot.bridge.json), wires tool name → handler,
 * and dispatches incoming commands. Step 7 of Phase 05.
 */
public class ToolDispatcher {

    private static final Logger LOG = LoggerFactory.getLogger(ToolDispatcher.class);
    private static final String MANIFEST_RESOURCE = "/spring-boot.bridge.json";

    private final BindingManifest manifest;
    private final Map<String, ToolHandler> handlers;

    public ToolDispatcher(List<ToolHandler> handlers) {
        this(loadManifest(), handlers);
    }

    /** Package-private overload for tests that want to inject a manifest. */
    ToolDispatcher(BindingManifest manifest, List<ToolHandler> handlers) {
        this.manifest = manifest;
        this.handlers = new HashMap<>();
        for (ToolHandler h : handlers) {
            this.handlers.put(h.name(), h);
        }
        for (BindingManifest.ToolDescriptor td : manifest.tools()) {
            if (!this.handlers.containsKey(td.name())) {
                LOG.warn("Manifest registers tool '{}' but no ToolHandler is wired; "
                        + "--list-tools will show it; runtime calls will return unknown_tool.", td.name());
            }
        }
    }

    private static BindingManifest loadManifest() {
        try (InputStream in = ToolDispatcher.class.getResourceAsStream(MANIFEST_RESOURCE)) {
            if (in == null) {
                throw new IllegalStateException("spring-boot.bridge.json not found on classpath; "
                        + "ensure the binding's pom.xml <resources> wires it from the project basedir.");
            }
            return new ObjectMapper().readValue(in, BindingManifest.class);
        } catch (IOException e) {
            throw new IllegalStateException("Failed to load manifest", e);
        }
    }

    public BindingManifest manifest() {
        return manifest;
    }

    public Optional<BindingManifest.ToolDescriptor> describe(String toolName) {
        return manifest.tools().stream()
                .filter(t -> t.name().equals(toolName))
                .findFirst();
    }

    /**
     * Dispatch a parsed command to its handler. Returns ToolResult.Failure
     * with exit code 4 if the tool name is unknown.
     */
    public ToolResult dispatch(BridgeCommand cmd) {
        ToolHandler h = handlers.get(cmd.tool());
        if (h == null) {
            return new ToolResult.Failure(BridgeError.unknownTool(cmd.tool()));
        }
        try {
            return h.run(cmd);
        } catch (BridgeException e) {
            return new ToolResult.Failure(e.error());
        } catch (Exception e) {
            LOG.error("Tool {} threw unexpected exception", cmd.tool(), e);
            return new ToolResult.Failure(BridgeError.executeError(
                    cmd.tool(), "Unexpected: " + e.getClass().getSimpleName() + ": " + e.getMessage()));
        }
    }
}
