import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import {
  buildDispatcher,
  copyFixtureToTmp,
  readReportFrontmatter,
} from "./_support";
import { RouteScaffold } from "../../src/tools/RouteScaffold";
import { BridgeError } from "../../src/core/BridgeError";

describe("RouteScaffold integration", () => {
  let tmpRoot: string;
  let cleanup: () => void;

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-rs-it-"));
  });

  afterEach(() => {
    cleanup();
  });

  it("scaffolds a route + matching test atomically (success path)", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "route_scaffold",
      scope: {
        root_path: tmpRoot,
        extra: {
          route_path: "settings/account",
          component_name: "AccountScreen",
        },
      },
    });
    expect(existsSync(result.reportPath)).toBe(true);
    const routeFile = join(tmpRoot, "app", "settings", "account.tsx");
    const testFile = join(tmpRoot, "__tests__", "settings", "account.test.tsx");
    expect(existsSync(routeFile)).toBe(true);
    expect(existsSync(testFile)).toBe(true);
    const routeContent = readFileSync(routeFile, "utf8");
    const testContent = readFileSync(testFile, "utf8");
    expect(routeContent).toContain("export default function AccountScreen");
    expect(routeContent).toContain('import { View, Text } from "react-native"');
    expect(testContent).toContain("AccountScreen");
    expect(testContent).toContain("renders without crashing");

    const parsed = readReportFrontmatter(result.reportPath);
    expect(parsed).not.toBeNull();
    expect(parsed!.body).toContain("settings/account");
    expect(parsed!.body).toContain("AccountScreen");
  });

  it("scaffolds a dynamic route with typed params (has_params=true)", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    await dispatcher.dispatch({
      tool: "route_scaffold",
      scope: {
        root_path: tmpRoot,
        extra: {
          route_path: "profile/[id]",
          component_name: "ProfileScreen",
          has_params: true,
          params_type: "{ id: string }",
        },
      },
    });
    const routeContent = readFileSync(
      join(tmpRoot, "app", "profile", "[id].tsx"),
      "utf8"
    );
    expect(routeContent).toContain("useLocalSearchParams<{ id: string }>");
    expect(routeContent).toContain('import { useLocalSearchParams } from "expo-router"');
  });

  it("refuses when route_path is missing (stage=validate)", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    try {
      await dispatcher.dispatch({
        tool: "route_scaffold",
        scope: {
          root_path: tmpRoot,
          extra: { component_name: "Foo" },
        },
      });
      throw new Error("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("validate");
      expect(be.errorCode).toBe("missing_route_path");
    }
  });
});
