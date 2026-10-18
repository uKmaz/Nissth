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

  // --- Phase 21: the two false-positive classes the ExampleFinanceApp consumer
  // hit on every run — six `@/…` aliases reported as missing packages, and three
  // devDependencies imported only from root config files reported as dev_in_prod.

  function withTsconfigPaths(root: string): void {
    const p = join(root, "tsconfig.json");
    const cfg = JSON.parse(readFileSync(p, "utf8"));
    cfg.compilerOptions = { ...cfg.compilerOptions, baseUrl: ".", paths: { "@/*": ["./*"] } };
    writeFileSync(p, JSON.stringify(cfg, null, 2), "utf8");
  }

  it("a tsconfig path alias is not a package", async () => {
    withTsconfigPaths(tmpRoot);
    writeFileSync(
      join(tmpRoot, "components", "AliasUser.tsx"),
      [
        'import { db } from "@/db/schema";',
        'import { fmt } from "@/domain/money";',
        'import { View } from "react-native";',
        "export function AliasUser() { return <View>{fmt(db)}</View>; }",
      ].join("\n"),
      "utf8"
    );
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new DependencyAudit(writer, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "dependency_audit",
      scope: { root_path: tmpRoot },
    });
    const { body } = readReportFrontmatter(result.reportPath)!;
    expect(body).not.toContain("`@/db`");
    expect(body).not.toContain("`@/domain`");
    expect(body).toContain("**Path aliases (not packages):** `@/*`");
    // The real package in the same file is still seen.
    expect(body).toMatch(/\|\s*`react-native`\s*\|/);
  });

  it("without a paths entry an @-scoped specifier is still a package", async () => {
    writeFileSync(
      join(tmpRoot, "components", "ScopedUser.tsx"),
      'import { thing } from "@acme/widgets";\nexport const x = thing;\n',
      "utf8"
    );
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new DependencyAudit(writer, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "dependency_audit",
      scope: { root_path: tmpRoot },
    });
    const { body } = readReportFrontmatter(result.reportPath)!;
    expect(body).toMatch(/\|\s*`@acme\/widgets`\s*\|\s*none\s*\|\s*missing\s*\|/);
  });

  it("a devDependency imported only from a root config file is not dev_in_prod", async () => {
    const pkgPath = join(tmpRoot, "package.json");
    const pkg = JSON.parse(readFileSync(pkgPath, "utf8"));
    pkg.devDependencies["drizzle-kit"] = "^0.31.0";
    pkg.devDependencies["some-bundler"] = "^1.0.0";
    writeFileSync(pkgPath, JSON.stringify(pkg, null, 2), "utf8");
    writeFileSync(
      join(tmpRoot, "drizzle.config.ts"),
      'import { defineConfig } from "drizzle-kit";\nexport default defineConfig({});\n',
      "utf8"
    );
    // Control: the same shape of import, but from a file that does ship.
    writeFileSync(
      join(tmpRoot, "components", "Bundled.tsx"),
      'import { go } from "some-bundler";\nexport const y = go;\n',
      "utf8"
    );

    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new DependencyAudit(writer, tmpRoot),
    ]);
    const result = await dispatcher.dispatch({
      tool: "dependency_audit",
      scope: { root_path: tmpRoot },
    });
    const { body } = readReportFrontmatter(result.reportPath)!;
    expect(body).toMatch(/\|\s*`drizzle-kit`\s*\|\s*devDependencies\s*\|\s*declared\s*\|\s*used\s*\|/);
    expect(body).toMatch(/\|\s*`some-bundler`\s*\|\s*devDependencies\s*\|\s*declared\s*\|\s*dev_in_prod\s*\|/);
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
