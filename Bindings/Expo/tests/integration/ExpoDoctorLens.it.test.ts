import { existsSync } from "node:fs";
import {
  buildDispatcher,
  copyFixtureToTmp,
  readReportFrontmatter,
} from "./_support";
import { ExpoDoctorLens } from "../../src/tools/ExpoDoctorLens";
import type {
  SubprocessResult,
  SubprocessRunner,
} from "../../src/core/SubprocessRunner";
import { BridgeError } from "../../src/core/BridgeError";

class StubRunner implements SubprocessRunner {
  constructor(private readonly result: SubprocessResult | Error) {}
  async run(): Promise<SubprocessResult> {
    if (this.result instanceof Error) throw this.result;
    return this.result;
  }
}

describe("ExpoDoctorLens integration", () => {
  let tmpRoot: string;
  let cleanup: () => void;

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-edl-it-"));
  });

  afterEach(() => {
    cleanup();
  });

  it("parses stub expo-doctor output into a findings table (PASS/WARN/FAIL)", async () => {
    const stubStdout = [
      "✔ Check package.json for common issues",
      "✔ Check Expo config for common issues",
      "⚠ Validate packages: react-native-screens not satisfied",
      "✖ Native module mismatch: expo-foo: details about the failure",
      "✔ Check dependency versions are compatible with installed Expo SDK",
    ].join("\n");
    const stubRunner = new StubRunner({
      exitCode: 1,
      stdout: stubStdout,
      stderr: "",
    });

    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new ExpoDoctorLens(writer, tmpRoot, stubRunner),
    ]);
    const result = await dispatcher.dispatch({
      tool: "expo_doctor_lens",
      scope: { root_path: tmpRoot },
    });
    expect(existsSync(result.reportPath)).toBe(true);
    const parsed = readReportFrontmatter(result.reportPath);
    expect(parsed).not.toBeNull();
    const { frontmatter, body } = parsed!;
    expect(frontmatter.tool).toBe("expo_doctor_lens");
    expect(body).toContain("PASS");
    expect(body).toContain("WARN");
    expect(body).toContain("FAIL");
    expect(body).toContain("Check package.json for common issues");
    expect(body).toContain("**Overall:** FAIL");
    expect(body).toContain("**Exit code:** 1");
  });

  it("returns expo_doctor_unavailable when subprocess throws", async () => {
    const failingRunner = new StubRunner(new Error("ENOENT: npx not found"));
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new ExpoDoctorLens(writer, tmpRoot, failingRunner),
    ]);
    try {
      await dispatcher.dispatch({
        tool: "expo_doctor_lens",
        scope: { root_path: tmpRoot },
      });
      throw new Error("expected throw");
    } catch (e) {
      expect(e).toBeInstanceOf(BridgeError);
      const be = e as BridgeError;
      expect(be.stage).toBe("execute");
      expect(be.errorCode).toBe("expo_doctor_unavailable");
    }
  });

  it("records the stdout hash in freshness.source_state (no caching)", async () => {
    const stubRunner = new StubRunner({
      exitCode: 0,
      stdout: "✔ One thing\n✔ Another thing\n",
      stderr: "",
    });
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new ExpoDoctorLens(writer, tmpRoot, stubRunner),
    ]);
    const result = await dispatcher.dispatch({
      tool: "expo_doctor_lens",
      scope: { root_path: tmpRoot },
    });
    const fm = readReportFrontmatter(result.reportPath)!.frontmatter;
    const freshness = fm.freshness as Record<string, string>;
    expect(freshness.source_state).toMatch(/sha256 prefix [0-9a-f]{8}/);
    expect(freshness.guarantee).toContain("no cached output");
  });
});
