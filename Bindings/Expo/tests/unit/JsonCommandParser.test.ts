import { JsonCommandParser } from "../../src/core/JsonCommandParser";
import { BridgeError } from "../../src/core/BridgeError";

describe("JsonCommandParser", () => {
  let parser: JsonCommandParser;

  beforeAll(() => {
    parser = new JsonCommandParser();
  });

  it("parses a valid command (object input)", () => {
    const cmd = parser.parse({ tool: "route_lens" });
    expect(cmd.tool).toBe("route_lens");
  });

  it("parses a valid command (string input)", () => {
    const cmd = parser.parse('{"tool":"endpoint_lens"}');
    expect(cmd.tool).toBe("endpoint_lens");
  });

  it("parses a command with scope.extra (binding-specific)", () => {
    const cmd = parser.parse({
      tool: "route_scaffold",
      scope: {
        root_path: "/tmp/app",
        extra: {
          route_path: "settings/account",
          component_name: "AccountScreen",
        },
      },
    });
    expect(cmd.scope?.extra?.route_path).toBe("settings/account");
  });

  it("throws BridgeError(stage=parse) on malformed JSON", () => {
    expect(() => parser.parse("{not valid json"))
      .toThrow(BridgeError);
    try {
      parser.parse("{not valid json");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      expect((e as BridgeError).stage).toBe("parse");
    }
  });

  it("throws BridgeError(stage=validate) on missing tool", () => {
    try {
      parser.parse({});
      fail("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      expect((e as BridgeError).stage).toBe("validate");
    }
  });

  it("throws BridgeError(stage=validate) on unknown top-level scope key", () => {
    try {
      parser.parse({
        tool: "route_lens",
        scope: { not_a_real_key: "foo" } as unknown as Record<string, unknown>,
      });
      fail("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      expect((e as BridgeError).stage).toBe("validate");
    }
  });

  it("accepts arbitrary keys under scope.extra (binding-specific filters)", () => {
    const cmd = parser.parse({
      tool: "route_scaffold",
      scope: {
        extra: { weird_binding_specific_key: 42 },
      },
    });
    expect(cmd.scope?.extra?.weird_binding_specific_key).toBe(42);
  });
});
