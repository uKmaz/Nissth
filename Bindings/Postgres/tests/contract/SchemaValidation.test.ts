// Contract test: every frontmatter our ReportWriter builds for every tool
// validates against $defs.reportFrontmatter in the cross-stack schema.
// This runs WITHOUT a live Postgres — uses ReportWriter directly with
// representative ReportContexts per tool.

import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { parse as yamlParse } from "yaml";
import { ReportWriter } from "../../src/core/ReportWriter";
import type { ReportContext } from "../../src/core/types";

function loadFrontmatterValidator() {
  const schemaPath = join(__dirname, "..", "..", "..", "_schemas", "bridge-command.schema.json");
  const schema = JSON.parse(readFileSync(schemaPath, "utf8")) as { $defs?: { reportFrontmatter?: object } };
  const fmSchema = schema.$defs?.reportFrontmatter;
  if (!fmSchema) throw new Error("Could not find $defs.reportFrontmatter in schema");
  const ajv = new (Ajv2020 as unknown as new (opts: unknown) => Ajv2020)({ allErrors: true, strict: false });
  addFormats(ajv as unknown as Parameters<typeof addFormats>[0]);
  return ajv.compile(fmSchema);
}

const FRONTMATTER_REGEX = /^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/;

describe("SchemaValidation (frontmatter contract)", () => {
  const validate = loadFrontmatterValidator();
  let tmpRoot: string;

  beforeAll(() => {
    tmpRoot = mkdtempSync(join(tmpdir(), "contract-pg-"));
  });
  afterAll(() => {
    rmSync(tmpRoot, { recursive: true, force: true });
  });

  function buildContext(tool: string, mode: string): ReportContext {
    return {
      tool,
      mode,
      scope: { package: "public", extra: { connection_string: "***REDACTED***" } },
      freshness: {
        source: "postgresql://user@host:5432/db",
        source_state: "redo_lsn=0/12345",
        guarantee: "Fresh PG connection per call; statement_timeout=30000ms.",
      },
      body: "## Body\n- ok\n",
    };
  }

  it.each([
    ["schema_lens", "full"],
    ["query_plan", "explain"],
    ["index_audit", "usage"],
    ["lock_audit", "current"],
    ["migration_status", "auto"],
  ])("frontmatter for %s/%s validates", (tool, mode) => {
    const writer = new ReportWriter({
      binding: "postgres",
      bindingVersion: "0.1.0",
      repoRoot: tmpRoot,
    });
    const path = writer.write(buildContext(tool, mode));
    const text = readFileSync(path, "utf8");
    const match = text.match(FRONTMATTER_REGEX);
    expect(match).toBeTruthy();
    const fm = yamlParse(match![1]);
    const ok = validate(fm);
    if (!ok) {
      const errors = (validate.errors ?? []).map((e) => `${e.instancePath} ${e.message}`).join("; ");
      throw new Error(`Frontmatter validation failed for ${tool}/${mode}: ${errors}`);
    }
    expect(ok).toBe(true);
  });

  it("frontmatter has the required binding fields", () => {
    const writer = new ReportWriter({
      binding: "postgres",
      bindingVersion: "0.1.0",
      repoRoot: tmpRoot,
    });
    const path = writer.write(buildContext("schema_lens", "full"));
    const text = readFileSync(path, "utf8");
    expect(text).toContain("binding: postgres");
    expect(text).toContain("binding_version: 0.1.0");
    expect(text).toContain("contract_version: 1");
  });
});
