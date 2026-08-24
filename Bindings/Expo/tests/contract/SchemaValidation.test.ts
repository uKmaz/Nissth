import { readFileSync } from "node:fs";
import { join } from "node:path";
import { parse as yamlParse } from "yaml";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import {
  REPO_ROOT,
  buildDispatcher,
  copyFixtureToTmp,
} from "../integration/_support";
import { RouteLens } from "../../src/tools/RouteLens";
import { ComponentLens } from "../../src/tools/ComponentLens";
import { DependencyAudit } from "../../src/tools/DependencyAudit";
import { ExpoDoctorLens } from "../../src/tools/ExpoDoctorLens";
import { RouteScaffold } from "../../src/tools/RouteScaffold";
import type {
  SubprocessResult,
  SubprocessRunner,
} from "../../src/core/SubprocessRunner";

class StubDoctor implements SubprocessRunner {
  async run(): Promise<SubprocessResult> {
    return { exitCode: 0, stdout: "✔ stub check\n", stderr: "" };
  }
}

function loadValidator() {
  const schemaPath = join(
    REPO_ROOT,
    "Bindings",
    "_schemas",
    "bridge-command.schema.json"
  );
  const schema = JSON.parse(readFileSync(schemaPath, "utf8")) as {
    $defs: { reportFrontmatter: object };
  };
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const AjvCtor = (Ajv2020 as any).default ?? Ajv2020;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const addFormatsFn = (addFormats as any).default ?? addFormats;
  const ajv = new AjvCtor({ allErrors: true, strict: false });
  addFormatsFn(ajv);
  return ajv.compile(schema.$defs.reportFrontmatter);
}

function readFm(reportPath: string): Record<string, unknown> {
  const text = readFileSync(reportPath, "utf8");
  const m = text.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
  if (!m) throw new Error(`No frontmatter in ${reportPath}`);
  return yamlParse(m[1]) as Record<string, unknown>;
}

describe("Schema validation harness — every tool's report frontmatter validates", () => {
  let tmpRoot: string;
  let cleanup: () => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let validate: any;

  beforeAll(() => {
    validate = loadValidator();
  });

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-sv-ct-"));
  });

  afterEach(() => {
    cleanup();
  });

  it("route_lens report validates", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer, flipper) => [
      new RouteLens(writer, flipper, tmpRoot),
    ]);
    const r = await dispatcher.dispatch({
      tool: "route_lens",
      scope: { root_path: tmpRoot },
    });
    const fm = readFm(r.reportPath);
    expect(validate(fm)).toBe(true);
  });

  it("component_lens report validates", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer, flipper) => [
      new ComponentLens(writer, flipper, tmpRoot),
    ]);
    const r = await dispatcher.dispatch({
      tool: "component_lens",
      scope: { root_path: tmpRoot },
    });
    const fm = readFm(r.reportPath);
    expect(validate(fm)).toBe(true);
  });

  it("dependency_audit report validates", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new DependencyAudit(writer, tmpRoot),
    ]);
    const r = await dispatcher.dispatch({
      tool: "dependency_audit",
      scope: { root_path: tmpRoot },
    });
    const fm = readFm(r.reportPath);
    expect(validate(fm)).toBe(true);
  });

  it("expo_doctor_lens report validates (stub subprocess)", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new ExpoDoctorLens(writer, tmpRoot, new StubDoctor()),
    ]);
    const r = await dispatcher.dispatch({
      tool: "expo_doctor_lens",
      scope: { root_path: tmpRoot },
    });
    const fm = readFm(r.reportPath);
    expect(validate(fm)).toBe(true);
  });

  it("route_scaffold report validates", async () => {
    const { dispatcher } = buildDispatcher(tmpRoot, (writer) => [
      new RouteScaffold(writer, tmpRoot),
    ]);
    const r = await dispatcher.dispatch({
      tool: "route_scaffold",
      scope: {
        root_path: tmpRoot,
        extra: {
          route_path: "schema/check",
          component_name: "SchemaCheck",
        },
      },
    });
    const fm = readFm(r.reportPath);
    expect(validate(fm)).toBe(true);
  });
});
