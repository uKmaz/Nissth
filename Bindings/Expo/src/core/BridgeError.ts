import type { BridgeErrorPayload, ErrorStage } from "./types";

export class BridgeError extends Error {
  public readonly stage: ErrorStage;
  public readonly tool: string;
  public readonly errorCode?: string;
  public readonly contextId?: string;

  constructor(opts: {
    stage: ErrorStage;
    tool: string;
    message: string;
    errorCode?: string;
    contextId?: string;
  }) {
    super(opts.message);
    this.name = "BridgeError";
    this.stage = opts.stage;
    this.tool = opts.tool;
    this.errorCode = opts.errorCode;
    this.contextId = opts.contextId;
    Object.setPrototypeOf(this, BridgeError.prototype);
  }

  toPayload(): BridgeErrorPayload {
    const payload: BridgeErrorPayload = {
      error: this.message,
      tool: this.tool,
      stage: this.stage,
    };
    if (this.errorCode !== undefined) {
      payload.error_code = this.errorCode;
    }
    if (this.contextId !== undefined) {
      payload.context_id = this.contextId;
    }
    return payload;
  }

  exitCode(): number {
    switch (this.stage) {
      case "parse":
      case "validate":
        return 2;
      case "execute":
        return this.errorCode === "unknown_tool"
          ? 4
          : this.errorCode?.startsWith("hard-enforce")
            ? 5
            : 3;
      case "format":
        return 3;
      default:
        return 1;
    }
  }
}
