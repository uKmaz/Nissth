package com.nissth.bridge.core;

import java.nio.file.Path;

/**
 * Result of a tool invocation. Success carries the report file path (and optionally
 * the body string for destination=return); Failure carries a BridgeError with an
 * exit code per §11.5.
 */
public sealed interface ToolResult permits ToolResult.Success, ToolResult.Failure {

    record Success(Path reportPath, String returnedBody) implements ToolResult {
        public Success(Path reportPath) {
            this(reportPath, null);
        }
    }

    record Failure(BridgeError error) implements ToolResult {}
}
