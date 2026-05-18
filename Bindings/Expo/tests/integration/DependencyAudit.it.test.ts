import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  buildDispatcher,
  copyFixtureToTmp,
  readReportFrontmatter,
} from "./_support";
import { DependencyAudit } from "../../src/tools/DependencyAudit";

describe("DependencyAudit integration", () => {
  let tmpRoot: string;
  let cleanup: () => void;

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-da-it-"));
  });

  afterEach(() => {
    cleanup();
  });

  it("classifies fixture deps and emits a findings table", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new DependencyAudit(writer, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "dependency_audit",
      scope: { root_path: tmpRoot },
    });
    expect(existsSync(result.reportPath)).toBe(true);
    const parsed = readReportFrontmatter(result.reportPath);
    expect(parsed).not.toBeNull();
    const { frontmatter, body } = parsed!;
    expect(frontmatter.tool).toBe("dependency_audit");
    expect(body).toContain("expo");
    expect(body).toContain("expo-router");
    expect(body).toContain("react");
    expect(body).toContain("react-native");
    expect(body).toContain("| Package | Declared in |");
  });

  it("flags an injected unused dep", async () => {
    const pkgPath = join(tmpRoot, "package.json");
    const pkg = JSON.parse(readFileSync(pkgPath, "utf8"));
    pkg.dependencies.lodash = "^4.17.21";
    writeFileSync(pkgPath, JSON.stringify(pkg, null, 2), "utf8");

    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new DependencyAudit(writer, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "dependency_audit",
      scope: { root_path: tmpRoot },
    });
    const parsed = readReportFrontmatter(result.reportPath);
    expect(parsed).not.toBeNull();
    expect(parsed!.body).toMatch(/\|\s*`lodash`\s*\|.*\|\s*unused\s*\|/);
  });

  it("reports the lockfile choice in freshness.source", async () => {
    // Fixture has no lockfile by default; create a fake one to verify detection.
    writeFileSync(join(tmpRoot, "package-lock.json"), "{}", "utf8");
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new DependencyAudit(writer, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "dependency_audit",
      scope: { root_path: tmpRoot },
    });
    const parsed = readReportFrontmatter(result.reportPath);
    const freshness = parsed!.frontmatter.freshness as Record<string, string>;
    expect(freshness.source).toContain("package-lock.json");
  });
});
