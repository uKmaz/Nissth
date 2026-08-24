package com.nissth.bridge.core;

/**
 * Each diagnostic and action tool implements this. The ToolDispatcher maps
 * BridgeCommand.tool() to a ToolHandler by name and invokes run().
 */
public interface ToolHandler {

    /** Snake-case tool name as it appears in the manifest. */
    String name();

    /**
     * Execute the tool. Implementations may throw BridgeException; the dispatcher
     * converts it to ToolResult.Failure. Unexpected exceptions become a generic
     * execute-stage failure.
     */
    ToolResult run(BridgeCommand cmd);
}
