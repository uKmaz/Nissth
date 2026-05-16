package com.nissth.bridge.core;

/**
 * Unchecked exception carrying a BridgeError. Tool handlers throw this on failure;
 * ToolDispatcher catches and converts to ToolResult.Failure.
 */
public class BridgeException extends RuntimeException {

    private final BridgeError error;

    public BridgeException(BridgeError error) {
        super(error.error());
        this.error = error;
    }

    public BridgeError error() {
        return error;
    }
}
