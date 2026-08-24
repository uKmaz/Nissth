import { mkdtempSync, readFileSync, rmSync, mkdirSync, copyFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { parse as yamlParse } from "yaml";
import { ReportWriter } from "../../src/core/ReportWriter";
import { BridgeError } from "../../src/core/BridgeError";

describe("ReportWriter", () => {
  let tmpRoot: string;

  beforeEach(() => {
    tmpRoot = mkdtempSync(join(tmpdir(), "nissth-rw-"));
    // Provide a CLAUDE.md marker + Bindings/_schemas/bridge-command.schema.json
    // so loadFrontmatterValidator() works against the tmp root.
    const realRepoRoot = findRealRepoRoot(__dirname);
    require("node:fs").writeFileSync(join(tmpRoot, "CLAUDE.md"), "# stub\n");
    mkdirSync(join(tmpRoot, "Bindings", "_schemas"), { recursive: true });
    copyFileSync(
      join(realRepoRoot, "Bindings", "_schemas", "bridge-command.schema.json"),
      join(tmpRoot, "Bindings", "_schemas", "bridge-command.schema.json")
    );
  });

  afterEach(() => {
    rmSync(tmpRoot, { recursive: true, force: true });
  });

  it("writes a report with valid frontmatter and Markdown body", () => {
    const writer = new ReportWriter({
      binding: "expo",
      bindingVersion: "0.1.0",
      repoRoot: tmpRoot,
      // Force fresh load from the tmp-root copy of the schema.
      // (constructor loadFrontmatterValidator() walks findRepoRoot(); we want tmpRoot)
      validateFrontmatter: makeValidator(tmpRoot),
    });

    const reportPath = writer.write({
      tool: "route_lens",
      scope: { root_path: "app" },
      freshness: {
        source: "filesystem scan of app/",
        source_state: "mtime 2026-05-18T01:00:00Z",
        guarantee: "AST built fresh this call",
      },
      body: "## Routes\n\n_(none)_",
    });

    expect(reportPath).toMatch(/AgentReports[/\\]Bridge[/\\]route_lens_.*\.md$/);
    const content = readFileSync(reportPath, "utf8");
    expect(content).toMatch(/^---\r?\n/);
    const match = content.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/);
    expect(match).not.toBeNull();
    const fm = yamlParse(match![1]);
    expect(fm.tool).toBe("route_lens");
    expect(fm.binding).toBe("expo");
    expect(fm.binding_version).toBe("0.1.0");
    expect(fm.contract_version).toBe(1);
    expect(fm.freshness.source).toBeTruthy();
    expect(fm.freshness.source_state).toBeTruthy();
    expect(fm.freshness.guarantee).toBeTruthy();
    expect(match![2]).toContain("## Routes");
  });

  it("uses custom file_name when provided", () => {
    const writer = new ReportWriter({
      binding: "expo",
      bindingVersion: "0.1.0",
      repoRoot: tmpRoot,
      validateFrontmatter: makeValidator(tmpRoot),
    });
    const reportPath = writer.write({
      tool: "endpoint_lens",
      freshness: { source: "x", source_state: "y", guarantee: "z" },
      body: "body",
      fileName: "custom_report.md",
    });
    expect(reportPath.endsWith("custom_report.md")).toBe(true);
  });

  it("throws BridgeError(stage=format) when frontmatter is invalid", () => {
    const writer = new ReportWriter({
      binding: "expo",
      bindingVersion: "0.1.0",
      repoRoot: tmpRoot,
      validateFrontmatter: makeValidator(tmpRoot),
    });
    expect(() =>
      writer.write({
        tool: "route_lens",
        freshness: { source: "", source_state: "", guarantee: "" } as unknown as {
          source: string;
          source_state: string;
          guarantee: string;
        },
        body: "body",
      })
    ).not.toThrow();
    // Empty strings are still valid per schema (only require presence as strings).
    // Test that supplying a non-string would fail.
    expect(() =>
      writer.write({
        tool: "route_lens",
        freshness: {
          source: undefined as unknown as string,
          source_state: "x",
          guarantee: "y",
        },
        body: "body",
      })
    ).toThrow(BridgeError);
  });

  it("compactIso renders the expected format", () => {
    const d = new Date("2026-05-18T01:23:45.123Z");
    expect(ReportWriter.compactIso(d)).toBe("2026-05-18T012345Z");
  });
});

function findRealRepoRoot(startDir: string): string {
  let dir = startDir;
  for (let i = 0; i < 16; i++) {
    try {
      require("node:fs").statSync(join(dir, "CLAUDE.md"));
      return dir;
    } catch {
      // ignore
    }
    const parent = require("node:path").dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error("could not find real repo root");
}

function makeValidator(repoRoot: string) {
  // Construct a frontmatter validator bound to the tmp-root schema.
  const Ajv2020 = require("ajv/dist/2020.js");
  const addFormats = require("ajv-formats");
  const schemaPath = join(repoRoot, "Bindings", "_schemas", "bridge-command.schema.json");
  const schema = JSON.parse(readFileSync(schemaPath, "utf8"));
  const fmSchema = schema.$defs.reportFrontmatter;
  const ajv = new Ajv2020.default({ allErrors: true, strict: false });
  addFormats.default(ajv);
  return ajv.compile(fmSchema);
}
