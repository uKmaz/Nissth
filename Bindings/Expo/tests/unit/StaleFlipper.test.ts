import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { parse as yamlParse } from "yaml";
import { StaleFlipper } from "../../src/core/StaleFlipper";

describe("StaleFlipper", () => {
  let tmpRoot: string;

  beforeEach(() => {
    tmpRoot = mkdtempSync(join(tmpdir(), "nissth-sf-"));
  });

  afterEach(() => {
    rmSync(tmpRoot, { recursive: true, force: true });
  });

  function writeDBL(subdir: string, name: string, frontmatter: object, body: string): string {
    const dir = join(tmpRoot, "DBL", subdir);
    mkdirSync(dir, { recursive: true });
    const filePath = join(dir, name);
    const yaml = require("yaml").stringify(frontmatter);
    writeFileSync(filePath, `---\n${yaml}---\n${body}`, "utf8");
    return filePath;
  }

  it("STALE-flips a DBL artifact when drift is detected", () => {
    const artifactPath = writeDBL(
      "APIIndex",
      "routes.md",
      {
        artifact_type: "api_index",
        name: "routes",
        last_regenerated: "2026-05-01 by user",
        source_state: "abc123",
        covers: ["app/"],
      },
      "# routes\n"
    );

    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "APIIndex",
      scopePath: "app/",
      driftCheck: () => true,
      reportFileName: "route_lens_2026-05-18T012345Z.md",
    });

    expect(flipped).toContain(artifactPath);
    const updated = readFileSync(artifactPath, "utf8");
    const match = updated.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
    expect(match).not.toBeNull();
    const fm = yamlParse(match![1]);
    expect(String(fm.last_regenerated)).toMatch(
      /^STALE — superseded by AgentReports\/Bridge\/route_lens_/
    );
  });

  it("does NOT flip when drift-check returns false", () => {
    const artifactPath = writeDBL(
      "APIIndex",
      "routes.md",
      {
        artifact_type: "api_index",
        last_regenerated: "2026-05-01 by user",
        covers: ["app/"],
      },
      "# routes\n"
    );
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "APIIndex",
      scopePath: "app/",
      driftCheck: () => false,
      reportFileName: "route_lens_x.md",
    });
    expect(flipped).toHaveLength(0);
    const fm = yamlParse(readFileSync(artifactPath, "utf8").match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/)![1]);
    expect(fm.last_regenerated).toBe("2026-05-01 by user");
  });

  it("skips artifacts not covered by the scope path", () => {
    writeDBL(
      "APIIndex",
      "other.md",
      {
        artifact_type: "api_index",
        last_regenerated: "2026-05-01 by user",
        covers: ["src/other/"],
      },
      "# other\n"
    );
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "APIIndex",
      scopePath: "app/",
      driftCheck: () => true,
      reportFileName: "route_lens_x.md",
    });
    expect(flipped).toHaveLength(0);
  });

  it("is idempotent: skips already-STALE artifacts", () => {
    const artifactPath = writeDBL(
      "APIIndex",
      "routes.md",
      {
        artifact_type: "api_index",
        last_regenerated: "STALE — superseded by AgentReports/Bridge/old.md",
        covers: ["app/"],
      },
      "# routes\n"
    );
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "APIIndex",
      scopePath: "app/",
      driftCheck: () => true,
      reportFileName: "route_lens_new.md",
    });
    expect(flipped).toHaveLength(0);
    const fm = yamlParse(readFileSync(artifactPath, "utf8").match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/)![1]);
    // unchanged STALE marker
    expect(String(fm.last_regenerated)).toContain("old.md");
  });

  it("silent no-op when DBL directory is missing", () => {
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "Nonexistent",
      scopePath: "app/",
      driftCheck: () => true,
      reportFileName: "x.md",
    });
    expect(flipped).toHaveLength(0);
  });

  it("scopeOverlaps handles substring and glob patterns", () => {
    expect(StaleFlipper.scopeOverlaps("app/", "app/")).toBe(true);
    expect(StaleFlipper.scopeOverlaps("app/", "app/settings/account.tsx")).toBe(true);
    expect(StaleFlipper.scopeOverlaps("app/settings/", "app/")).toBe(true);
    expect(StaleFlipper.scopeOverlaps("components/", "app/")).toBe(false);
    expect(StaleFlipper.scopeOverlaps("app/**/*.tsx", "app/foo/bar.tsx")).toBe(true);
  });
});
