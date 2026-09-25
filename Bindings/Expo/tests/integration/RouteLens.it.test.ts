import { join } from "node:path";
import {
  buildDispatcher,
  copyFixtureToTmp,
  readDBLFrontmatter,
  readReportFrontmatter,
} from "./_support";
import { RouteLens } from "../../src/tools/RouteLens";
import { existsSync } from "node:fs";

describe("RouteLens integration", () => {
  let tmpRoot: string;
  let cleanup: () => void;

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-rl-it-"));
  });

  afterEach(() => {
    cleanup();
  });

  it("scans fixture app/ and produces a valid report listing both layout and index", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer, flipper) => [
      new RouteLens(writer, flipper, tmpRoot),
    ]);

    const result = await dispatcher.dispatch({
      tool: "route_lens",
      scope: { root_path: tmpRoot },
    });

    expect(existsSync(result.reportPath)).toBe(true);
    const parsed = readReportFrontmatter(result.reportPath);
    expect(parsed).not.toBeNull();
    const { frontmatter, body } = parsed!;
    expect(frontmatter.tool).toBe("route_lens");
    expect(frontmatter.binding).toBe("expo");
    expect(frontmatter.contract_version).toBe(1);
    expect(body).toContain("**Routes found:** 2");
    expect(body).toContain("_layout.tsx");
    expect(body).toContain("index.tsx");
    expect(body).toContain("IndexScreen");
    expect(body).toContain("RootLayout");
  });

  it("STALE-flips DBL/APIIndex/routes.md when documented routes differ from live", async () => {
    const dblPath = join(tmpRoot, "DBL", "APIIndex", "routes.md");
    expect(existsSync(dblPath)).toBe(true);

    const beforeFm = readDBLFrontmatter(dblPath)!;
    expect(String(beforeFm.last_regenerated)).not.toMatch(/^STALE/);

    const { dispatcher } = buildDispatcher(tmpRoot, (writer, flipper) => [
      new RouteLens(writer, flipper, tmpRoot),
    ]);
    await dispatcher.dispatch({
      tool: "route_lens",
      scope: { root_path: tmpRoot },
    });

    const afterFm = readDBLFrontmatter(dblPath)!;
    expect(String(afterFm.last_regenerated)).toMatch(
      /^STALE — superseded by AgentReports\/Bridge\/route_lens_/
    );
  });

  it("writes an empty-but-valid report when no app/ directory exists", async () => {
    const { tmpRoot: emptyRoot, cleanup: emptyCleanup } = copyFixtureToTmp(
      "nissth-rl-empty-"
    );
    // Remove the app/ directory
    require("node:fs").rmSync(join(emptyRoot, "app"), {
      recursive: true,
      force: true,
    });

    try {
      const { dispatcher } = buildDispatcher(emptyRoot, (writer, flipper) => [
        new RouteLens(writer, flipper, emptyRoot),
      ]);
      const result = await dispatcher.dispatch({
        tool: "route_lens",
        scope: { root_path: emptyRoot },
      });
      const parsed = readReportFrontmatter(result.reportPath);
      expect(parsed).not.toBeNull();
      expect(parsed!.body).toContain("**Routes found:** 0");
    } finally {
      emptyCleanup();
    }
  });
});
