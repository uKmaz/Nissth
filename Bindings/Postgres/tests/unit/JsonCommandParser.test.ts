import { JsonCommandParser } from "../../src/core/JsonCommandParser";
import { BridgeError } from "../../src/core/BridgeError";

describe("JsonCommandParser", () => {
  const parser = new JsonCommandParser();

  it("parses a valid command", () => {
    const cmd = parser.parse({ tool: "schema_lens", mode: "full", scope: { package: "public" } });
    expect(cmd.tool).toBe("schema_lens");
    expect(cmd.mode).toBe("full");
    expect(cmd.scope?.package).toBe("public");
  });

  it("accepts scope.extra (free-form per binding)", () => {
    const cmd = parser.parse({
      tool: "schema_lens",
      scope: { extra: { connection_string: "postgresql://u@h/d", statement_timeout_ms: 5000 } },
    });
    expect(cmd.scope?.extra).toBeDefined();
  });

  it("rejects malformed JSON with stage=parse", () => {
    try {
      parser.parse("{ not json");
      fail("expected throw");
    } catch (e: unknown) {
      expect(e).toBeInstanceOf(BridgeError);
      expect((e as BridgeError).stage).toBe("parse");
    }
  });

  it("rejects missing tool with stage=validate", () => {
    try {
      parser.parse({ mode: "full" });
      fail("expected throw");
    } catch (e: unknown) {
      expect(e).toBeInstanceOf(BridgeError);
      expect((e as BridgeError).stage).toBe("validate");
    }
  });

  it("rejects unknown top-level scope key (additionalProperties: false)", () => {
    try {
      parser.parse({ tool: "schema_lens", scope: { not_a_real_key: "x" } });
      fail("expected throw");
    } catch (e: unknown) {
      expect(e).toBeInstanceOf(BridgeError);
      expect((e as BridgeError).stage).toBe("validate");
    }
  });
});
