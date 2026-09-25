import { ExpoDoctorLens } from "../../src/tools/ExpoDoctorLens";

/**
 * Both fixtures are verbatim captures from `npx --yes expo-doctor` (v1.20.4)
 * run against a real Expo SDK 57 project on 2026-09-24 — the consumer whose
 * Bridge report read "Checks parsed: 0" for nine phases. A parser test written
 * against invented output proves only that the parser matches its author's
 * imagination, which is how this defect survived Phase 06.
 */
const DEFAULT_OUTPUT = [
  "Running 21 checks on your project...",
  "20/21 checks passed. 1 checks failed. Possible issues detected:",
  "Use the --verbose flag to see more details about passed checks.",
  "",
  "✖ Check that packages match versions required by installed Expo SDK",
  "",
  "🔧 Patch version mismatches",
  "package       expected  found    ",
  "@expo/ui      ~57.0.20  57.0.19  ",
  "expo          ~57.0.25  57.0.24  ",
  "",
  "Advice:",
  "Use 'npx expo install --check' to review and upgrade your dependencies.",
  "",
  "1 check failed, indicating possible issues with the project.",
].join("\n");

const VERBOSE_OUTPUT = [
  "expo-doctor: v1.20.4",
  "Running 21 checks on your project...",
  "✔ Check that local environment files are not committed",
  "✔ Check package.json for common issues",
  "✖ Check that packages match versions required by installed Expo SDK",
  "✔ Check Expo config (app.json/ app.config.js) schema",
  "",
  "20/21 checks passed. 1 checks failed. Possible issues detected:",
  "",
  "✖ Check that packages match versions required by installed Expo SDK",
  "",
  "🔧 Patch version mismatches",
  "expo          ~57.0.25  57.0.24  ",
  "",
  "1 check failed, indicating possible issues with the project.",
].join("\n");

const ALL_PASS_OUTPUT = [
  "Running 21 checks on your project...",
  "21/21 checks passed. No issues detected!",
].join("\n");

describe("ExpoDoctorLens.parseOutput — expo-doctor 1.x", () => {
  it("reads the counts from the summary line when no check is named", () => {
    const p = ExpoDoctorLens.parseOutput(ALL_PASS_OUTPUT);
    expect(p.total).toBe(21);
    expect(p.passed).toBe(21);
    expect(p.failed).toBe(0);
    expect(p.findings).toHaveLength(0);
    expect(ExpoDoctorLens.overall(p, 0)).toBe("PASS");
  });

  it("default output: counts plus the one failing check, with its detail block", () => {
    const p = ExpoDoctorLens.parseOutput(DEFAULT_OUTPUT);
    expect({ total: p.total, passed: p.passed, failed: p.failed }).toEqual({
      total: 21,
      passed: 20,
      failed: 1,
    });
    expect(p.findings).toHaveLength(1);
    expect(p.findings[0].status).toBe("FAIL");
    expect(p.findings[0].check).toBe(
      "Check that packages match versions required by installed Expo SDK"
    );
    expect(p.findings[0].message).toContain("Patch version mismatches");
    expect(p.findings[0].message).toContain("~57.0.25");
    // The trailing "1 check failed, …" line is a summary, not detail.
    expect(p.findings[0].message).not.toContain("indicating possible issues");
    expect(ExpoDoctorLens.overall(p, 1)).toBe("FAIL");
  });

  it("verbose output: every check named once, the repeated failure not doubled", () => {
    const p = ExpoDoctorLens.parseOutput(VERBOSE_OUTPUT);
    expect(p.total).toBe(21);
    expect(p.passed).toBe(20);
    expect(p.findings).toHaveLength(4);
    const names = p.findings.map((f) => f.check);
    expect(new Set(names).size).toBe(4);
    const failed = p.findings.filter((f) => f.status === "FAIL");
    expect(failed).toHaveLength(1);
    // The detail block enriched the check the list line had already recorded.
    expect(failed[0].message).toContain("Patch version mismatches");
    expect(p.findings[0]).toEqual({
      check: "Check that local environment files are not committed",
      status: "PASS",
      message: "(no detail)",
    });
  });

  it("check names keep their colons and slashes", () => {
    const p = ExpoDoctorLens.parseOutput(VERBOSE_OUTPUT);
    expect(p.findings.map((f) => f.check)).toContain(
      "Check Expo config (app.json/ app.config.js) schema"
    );
  });

  it("still parses the literal PASS/FAIL shape", () => {
    const p = ExpoDoctorLens.parseOutput(
      ["PASS - Check one", "FAIL: Check two: it broke"].join("\n")
    );
    expect(p.findings).toEqual([
      { check: "Check one", status: "PASS", message: "(no detail)" },
      { check: "Check two", status: "FAIL", message: "it broke" },
    ]);
    expect(p.total).toBeNull();
    expect(p.failed).toBe(1);
  });

  it("a non-zero exit is FAIL even when the output shape defeats the parser", () => {
    const p = ExpoDoctorLens.parseOutput("something entirely unexpected");
    expect(p.findings).toHaveLength(0);
    expect(ExpoDoctorLens.overall(p, 1)).toBe("FAIL");
    expect(ExpoDoctorLens.overall(p, 0)).toBe("PASS");
  });

  it("parseFindings still answers for pre-Phase-21 callers", () => {
    expect(ExpoDoctorLens.parseFindings(DEFAULT_OUTPUT)).toHaveLength(1);
  });
});
