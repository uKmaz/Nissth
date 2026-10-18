import { ToolDispatcher } from "../../src/core/ToolDispatcher";
import { BridgeError } from "../../src/core/BridgeError";
import type { BridgeCommand, ToolHandler, ToolResult } from "../../src/core/types";

function stubHandler(name: string, reportPath: string): ToolHandler {
  return {
    name,
    async invoke(_cmd: BridgeCommand): Promise<ToolResult> {
      return { reportPath };
    },
  };
}

describe("ToolDispatcher", () => {
  it("dispatches to a registered handler by name", async () => {
    const dispatcher = new ToolDispatcher();
    dispatcher.register(stubHandler("schema_lens", "/tmp/r.md"));
    const result = await dispatcher.dispatch({ tool: "schema_lens" });
    expect(result.reportPath).toBe("/tmp/r.md");
  });

  it("throws BridgeError(stage=execute, code=unknown_tool) for unregistered names", async () => {
    const dispatcher = new ToolDispatcher();
    try {
      await dispatcher.dispatch({ tool: "ghost_tool" });
      fail("expected throw");
    } catch (e: unknown) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("execute");
      expect(be.errorCode).toBe("unknown_tool");
      expect(be.exitCode()).toBe(4);
    }
  });

  it("listToolNames returns all registered names", () => {
    const dispatcher = new ToolDispatcher();
    dispatcher.register(stubHandler("a", "/r1"));
    dispatcher.register(stubHandler("b", "/r2"));
    expect(dispatcher.listToolNames().sort()).toEqual(["a", "b"]);
  });

  it("has() reflects registration state", () => {
    const dispatcher = new ToolDispatcher();
    expect(dispatcher.has("x")).toBe(false);
    dispatcher.register(stubHandler("x", "/r"));
    expect(dispatcher.has("x")).toBe(true);
  });
});
