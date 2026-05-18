import { ToolDispatcher } from "../../src/core/ToolDispatcher";
import { BridgeError } from "../../src/core/BridgeError";
import type { BridgeCommand, ToolHandler, ToolResult } from "../../src/core/types";

class StubHandler implements ToolHandler {
  constructor(public readonly name: string, private readonly outPath: string) {}
  async invoke(_cmd: BridgeCommand): Promise<ToolResult> {
    return { reportPath: this.outPath };
  }
}

describe("ToolDispatcher", () => {
  it("registers and dispatches by tool name", async () => {
    const d = new ToolDispatcher();
    d.register(new StubHandler("route_lens", "/tmp/route_lens.md"));
    const result = await d.dispatch({ tool: "route_lens" });
    expect(result.reportPath).toBe("/tmp/route_lens.md");
  });

  it("lists registered tool names", () => {
    const d = new ToolDispatcher();
    d.register(new StubHandler("a", "/x"));
    d.register(new StubHandler("b", "/y"));
    expect(d.listToolNames().sort()).toEqual(["a", "b"]);
  });

  it("has() reflects registration", () => {
    const d = new ToolDispatcher();
    d.register(new StubHandler("route_lens", "/x"));
    expect(d.has("route_lens")).toBe(true);
    expect(d.has("never_registered")).toBe(false);
  });

  it("throws BridgeError(stage=execute, error_code=unknown_tool) on unknown tool", async () => {
    const d = new ToolDispatcher();
    try {
      await d.dispatch({ tool: "never_registered" });
      fail("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("execute");
      expect(be.errorCode).toBe("unknown_tool");
      expect(be.exitCode()).toBe(4);
    }
  });

  it("re-throws errors from handlers without wrapping", async () => {
    const d = new ToolDispatcher();
    const failing: ToolHandler = {
      name: "boom",
      invoke: async () => {
        throw new BridgeError({
          stage: "execute",
          tool: "boom",
          message: "inner failure",
          errorCode: "test_failure",
        });
      },
    };
    d.register(failing);
    try {
      await d.dispatch({ tool: "boom" });
      fail("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.message).toBe("inner failure");
      expect(be.errorCode).toBe("test_failure");
    }
  });
});
