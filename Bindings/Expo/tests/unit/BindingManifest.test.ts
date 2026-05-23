import { BindingManifest } from "../../src/core/BindingManifest";

describe("BindingManifest", () => {
  it("loads the in-repo expo.bridge.json by default", () => {
    const m = BindingManifest.load();
    expect(m.binding).toBe("expo");
    expect(m.bindingVersion).toBe("0.1.1");
    expect(m.contractVersion).toBe(1);
  });

  it("registers the 5 expected tools", () => {
    const m = BindingManifest.load();
    const names = m.toolNames().sort();
    expect(names).toEqual(
      [
        "component_lens",
        "dependency_audit",
        "expo_doctor_lens",
        "route_lens",
        "route_scaffold",
      ].sort()
    );
  });

  it("each tool has the required manifest fields", () => {
    const m = BindingManifest.load();
    for (const tool of m.tools) {
      expect(tool.name).toBeTruthy();
      expect(["diagnostic", "action"]).toContain(tool.kind);
      expect(Array.isArray(tool.modes)).toBe(true);
      expect(Array.isArray(tool.scope_keys)).toBe(true);
      expect(Array.isArray(tool.scope_extra_keys)).toBe(true);
      expect(tool.description).toBeTruthy();
    }
  });

  it("identifies route_scaffold as the action tool with hard-enforce", () => {
    const m = BindingManifest.load();
    const t = m.getTool("route_scaffold");
    expect(t).toBeDefined();
    expect(t!.kind).toBe("action");
    expect(t!.scope_extra_keys).toEqual(
      expect.arrayContaining([
        "route_path",
        "component_name",
        "has_params",
      ])
    );
    expect(t!.enforces).toBeTruthy();
  });

  it("getTool returns undefined for unknown tool name", () => {
    const m = BindingManifest.load();
    expect(m.getTool("nonexistent_tool")).toBeUndefined();
  });
});
