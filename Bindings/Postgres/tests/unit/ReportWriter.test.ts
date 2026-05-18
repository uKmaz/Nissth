import { mkdtempSync, readFileSync, rmSync, writeFileSync, mkdirSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ReportWriter } from "../../src/core/ReportWriter";
import type { ReportContext } from "../../src/core/types";

describe("ReportWriter", () => {
  let tmpRoot: string;

  beforeEach(() => {
    tmpRoot = mkdtempSync(join(tmpdir(), "reportwriter-pg-"));
    // Place a minimal CLAUDE.md so findRepoRoot resolves to the real Nissth root via prod path.
    // We bypass that path by passing repoRoot explicitly.
  });

  afterEach(() => {
    rmSync(tmpRoot, { recursive: true, force: true });
  });

  it("writes a report under AgentReports/Bridge/ with frontmatter + body", () => {
    const writer = new ReportWriter({
      binding: "postgres",
      bindingVersion: "0.1.0",
      repoRoot: tmpRoot,
    });
    const ctx: ReportContext = {
      tool: "schema_lens",
      mode: "full",
      scope: { package: "public" },
      freshness: {
        source: "postgresql://user@host:5432/db",
        source_state: "redo_lsn=0/12345",
        guarantee: "fresh connection per call; statement_timeout=30000ms",
      },
      body: "## Tables\n- users\n- orders\n",
    };
    const path = writer.write(ctx);
    expect(path).toContain("AgentReports");
    expect(path.endsWith(".md")).toBe(true);
    expect(statSync(path).isFile()).toBe(true);

    const content = readFileSync(path, "utf8");
    expect(content.startsWith("---\n")).toBe(true);
    expect(content).toContain("tool: schema_lens");
    expect(content).toContain("binding: postgres");
    expect(content).toContain("binding_version: 0.1.0");
    expect(content).toContain("contract_version: 1");
    expect(content).toContain("source: postgresql://user@host:5432/db");
    expect(content).toContain("## Tables");
  });

  it("uses custom file_name when supplied", () => {
    const writer = new ReportWriter({
      binding: "postgres",
      bindingVersion: "0.1.0",
      repoRoot: tmpRoot,
    });
    const ctx: ReportContext = {
      tool: "schema_lens",
      freshness: { source: "x", source_state: "y", guarantee: "z" },
      body: "...",
      fileName: "custom_name.md",
    };
    const path = writer.write(ctx);
    expect(path.endsWith("custom_name.md")).toBe(true);
  });
});
