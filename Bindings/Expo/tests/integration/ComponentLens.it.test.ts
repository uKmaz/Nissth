import { existsSync } from "node:fs";
import {
  buildDispatcher,
  copyFixtureToTmp,
  readReportFrontmatter,
} from "./_support";
import { ComponentLens } from "../../src/tools/ComponentLens";

describe("ComponentLens integration", () => {
  let tmpRoot: string;
  let cleanup: () => void;

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-cl-it-"));
  });

  afterEach(() => {
    cleanup();
  });

  it("discovers Greeting under components/ and reports its props + hooks", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer, flipper) => [
      new ComponentLens(writer, flipper, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "component_lens",
      scope: { root_path: tmpRoot },
    });
    expect(existsSync(result.reportPath)).toBe(true);
    const parsed = readReportFrontmatter(result.reportPath);
    expect(parsed).not.toBeNull();
    const { frontmatter, body } = parsed!;
    expect(frontmatter.tool).toBe("component_lens");
    expect(body).toContain("Greeting");
    expect(body).toContain("GreetingProps");
    expect(body).toContain("useMemo");
    expect(body).toContain("**Components found:** 1");
  });

  it("writes an empty-but-valid report when components/ is missing", async () => {
    const { tmpRoot: emptyRoot, cleanup: emptyCleanup } = copyFixtureToTmp(
      "nissth-cl-empty-"
    );
    require("node:fs").rmSync(require("node:path").join(emptyRoot, "components"), {
      recursive: true,
      force: true,
    });
    try {
      const { dispatcher } = buildDispatcher(emptyRoot, (writer, flipper) => [
        new ComponentLens(writer, flipper, emptyRoot),
      ]);
      const result = await dispatcher.dispatch({
        tool: "component_lens",
        scope: { root_path: emptyRoot },
      });
      const parsed = readReportFrontmatter(result.reportPath);
      expect(parsed).not.toBeNull();
      expect(parsed!.body).toContain("**Components found:** 0");
    } finally {
      emptyCleanup();
    }
  });
});
