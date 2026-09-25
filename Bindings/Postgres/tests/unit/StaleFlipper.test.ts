import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { StaleFlipper } from "../../src/core/StaleFlipper";

describe("StaleFlipper.scopeOverlaps", () => {
  it("matches identical paths", () => {
    expect(StaleFlipper.scopeOverlaps("DBL/SchemaIndex/users", "DBL/SchemaIndex/users")).toBe(true);
  });

  it("matches when scope is descendant of cover", () => {
    expect(StaleFlipper.scopeOverlaps("DBL/SchemaIndex", "DBL/SchemaIndex/users")).toBe(true);
  });

  it("matches glob patterns with *", () => {
    expect(StaleFlipper.scopeOverlaps("DBL/SchemaIndex/*.md", "DBL/SchemaIndex/users.md")).toBe(true);
  });

  it("rejects disjoint paths", () => {
    expect(StaleFlipper.scopeOverlaps("DBL/Summaries", "DBL/SchemaIndex/users")).toBe(false);
  });
});

describe("StaleFlipper.flipIfStale", () => {
  let tmpRoot: string;

  beforeEach(() => {
    tmpRoot = mkdtempSync(join(tmpdir(), "staleflip-pg-"));
    mkdirSync(join(tmpRoot, "DBL", "SchemaIndex"), { recursive: true });
  });

  afterEach(() => {
    rmSync(tmpRoot, { recursive: true, force: true });
  });

  function writeArtifact(name: string, frontmatter: Record<string, unknown>, body: string): string {
    const path = join(tmpRoot, "DBL", "SchemaIndex", name);
    const fm = Object.entries(frontmatter)
      .map(([k, v]) => `${k}: ${Array.isArray(v) ? `\n  - ${(v as string[]).join("\n  - ")}` : v}`)
      .join("\n");
    writeFileSync(path, `---\n${fm}\n---\n${body}`);
    return path;
  }

  it("flips an artifact when driftCheck returns true", () => {
    const path = writeArtifact("users.md", {
      last_regenerated: "2026-05-01",
      covers: ["public.users"],
    }, "Body content here.");
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "SchemaIndex",
      scopePath: "public.users",
      driftCheck: () => true,
      reportFileName: "schema_lens_2026-05-18T1234Z.md",
    });
    expect(flipped).toHaveLength(1);
    const after = readFileSync(path, "utf8");
    expect(after).toContain("STALE — superseded by AgentReports/Bridge/schema_lens_2026-05-18T1234Z.md");
  });

  it("does not flip when driftCheck returns false", () => {
    writeArtifact("users.md", {
      last_regenerated: "2026-05-01",
      covers: ["public.users"],
    }, "Body");
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "SchemaIndex",
      scopePath: "public.users",
      driftCheck: () => false,
      reportFileName: "x.md",
    });
    expect(flipped).toHaveLength(0);
  });

  it("is idempotent — does not re-flip artifacts already STALE", () => {
    writeArtifact("users.md", {
      last_regenerated: "STALE — superseded by AgentReports/Bridge/old.md",
      covers: ["public.users"],
    }, "Body");
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "SchemaIndex",
      scopePath: "public.users",
      driftCheck: () => true,
      reportFileName: "new.md",
    });
    expect(flipped).toHaveLength(0);
  });

  it("silently no-ops when DBL subdir does not exist", () => {
    rmSync(join(tmpRoot, "DBL"), { recursive: true, force: true });
    const flipper = new StaleFlipper(tmpRoot);
    const flipped = flipper.flipIfStale({
      dblSubdir: "SchemaIndex",
      scopePath: "x",
      driftCheck: () => true,
      reportFileName: "x.md",
    });
    expect(flipped).toHaveLength(0);
  });
});
