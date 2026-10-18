import { BridgeError } from "./BridgeError";
import type { BridgeCommand, ToolHandler, ToolResult } from "./types";

export class ToolDispatcher {
  private readonly handlers = new Map<string, ToolHandler>();

  register(handler: ToolHandler): void {
    this.handlers.set(handler.name, handler);
  }

  has(name: string): boolean {
    return this.handlers.has(name);
  }

  listToolNames(): string[] {
    return [...this.handlers.keys()];
  }

  async dispatch(cmd: BridgeCommand): Promise<ToolResult> {
    const handler = this.handlers.get(cmd.tool);
    if (!handler) {
      throw new BridgeError({
        stage: "execute",
        tool: cmd.tool,
        message: `Unknown tool: ${cmd.tool}. Use --list-tools to see registered tools.`,
        errorCode: "unknown_tool",
      });
    }
    return handler.invoke(cmd);
  }
}
