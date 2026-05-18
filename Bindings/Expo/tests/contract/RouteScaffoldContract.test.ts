import {
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import {
  buildDispatcher,
  copyFixtureToTmp,
} from "../integration/_support";
import { RouteScaffold } from "../../src/tools/RouteScaffold";
import { BridgeError } from "../../src/core/BridgeError";

describe("RouteScaffold hard-enforce contract", () => {
  let tmpRoot: string;
  let cleanup: () => void;

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-rsc-ct-"));
  });

  afterEach(() => {
    cleanup();
  });

  it("refuses when route file already exists (route_already_exists, stage=validate)", async () => {
    const routeFile = join(tmpRoot, "app", "duplicate.tsx");
    writeFileSync(routeFile, "// pre-existing\n", "utf8");

    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    try {
      await dispatcher.dispatch({
        tool: "route_scaffold",
        scope: {
          root_path: tmpRoot,
          extra: {
            route_path: "duplicate",
            component_name: "DuplicateScreen",
          },
        },
      });
      throw new Error("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("validate");
      expect(be.errorCode).toBe("route_already_exists");
    }
    // Pre-existing file is untouched
    expect(readFileSync(routeFile, "utf8")).toBe("// pre-existing\n");
  });

  it("rejects invalid route_path (validate)", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    try {
      await dispatcher.dispatch({
        tool: "route_scaffold",
        scope: {
          root_path: tmpRoot,
          extra: {
            route_path: "../escape",
            component_name: "EscapeScreen",
          },
        },
      });
      throw new Error("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("validate");
      expect(be.errorCode).toBe("invalid_route_path");
    }
    expect(existsSync(join(tmpRoot, "app", "..", "escape.tsx"))).toBe(false);
  });

  it("rejects non-PascalCase component name (validate)", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    try {
      await dispatcher.dispatch({
        tool: "route_scaffold",
        scope: {
          root_path: tmpRoot,
          extra: {
            route_path: "lowercase",
            component_name: "lowercaseScreen",
          },
        },
      });
      throw new Error("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("validate");
      expect(be.errorCode).toBe("invalid_component_name");
    }
  });

  it("rejects has_params=true without params_type (validate)", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    try {
      await dispatcher.dispatch({
        tool: "route_scaffold",
        scope: {
          root_path: tmpRoot,
          extra: {
            route_path: "needs/params",
            component_name: "NeedsParams",
            has_params: true,
          },
        },
      });
      throw new Error("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("validate");
      expect(be.errorCode).toBe("missing_params_type");
    }
  });

  it("rolls back atomically when one of the writes fails (hard-enforce, exit code 5)", async () => {
    // Pre-create __tests__/rollback as a FILE (not a directory). When RouteScaffold
    // attempts mkdirSync(dirname(testFile), {recursive:true}), it fails with EEXIST
    // because the path collides with a file. The route file may have been
    // written first; the catch block must roll it back and exit 5.
    const testsDir = join(tmpRoot, "__tests__");
    mkdirSync(testsDir, { recursive: true });
    writeFileSync(join(testsDir, "rollback"), "// not a directory\n", "utf8");

    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);

    const routeFile = join(tmpRoot, "app", "rollback", "demo.tsx");

    try {
      await dispatcher.dispatch({
        tool: "route_scaffold",
        scope: {
          root_path: tmpRoot,
          extra: {
            route_path: "rollback/demo",
            component_name: "DemoScreen",
          },
        },
      });
      throw new Error("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("execute");
      expect(be.errorCode).toBe("hard-enforce_route_pair_atomicity");
      expect(be.exitCode()).toBe(5);
    }

    // Route file must NOT exist (rollback worked)
    expect(existsSync(routeFile)).toBe(false);
  });
});
