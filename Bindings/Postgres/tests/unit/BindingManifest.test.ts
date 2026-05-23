import { BindingManifest } from "../../src/core/BindingManifest";
import { join } from "node:path";

describe("BindingManifest", () => {
  const manifestPath = join(__dirname, "..", "..", "postgres.bridge.json");
  const manifest = BindingManifest.load(manifestPath);

  it("loads the postgres binding manifest", () => {
    expect(manifest.binding).toBe("postgres");
    expect(manifest.bindingVersion).toBe("0.1.1");
    expect(manifest.contractVersion).toBe(1);
  });

  it("lists exactly five tools", () => {
    expect(manifest.toolNames().sort()).toEqual(
      ["index_audit", "lock_audit", "migration_status", "query_plan", "schema_lens"]
    );
  });

  it("each tool has scope_extra_keys including connection_string", () => {
    for (const tool of manifest.tools) {
      expect(tool.scope_extra_keys).toContain("connection_string");
    }
  });

  it("every tool is kind=diagnostic in this slice (no action tools)", () => {
    for (const tool of manifest.tools) {
      expect(tool.kind).toBe("diagnostic");
    }
  });

  it("getTool returns the matching entry by name", () => {
    const q = manifest.getTool("query_plan");
    expect(q?.modes).toContain("analyze");
    expect(q?.scope_extra_keys).toContain("sql");
  });

  it("getTool returns undefined for unknown name", () => {
    expect(manifest.getTool("not_a_tool")).toBeUndefined();
  });
});
