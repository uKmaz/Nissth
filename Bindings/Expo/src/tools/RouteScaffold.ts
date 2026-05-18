import {
  existsSync,
  mkdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { dirname, join } from "node:path";
import { ReportWriter } from "../core/ReportWriter";
import type {
  BridgeCommand,
  ReportContext,
  ToolHandler,
  ToolResult,
} from "../core/types";
import { BridgeError } from "../core/BridgeError";

export interface RouteScaffoldInputs {
  routePath: string;
  componentName: string;
  hasParams: boolean;
  paramsType?: string;
  forceCreateLayout: boolean;
}

export class RouteScaffold implements ToolHandler {
  readonly name = "route_scaffold";

  constructor(
    private readonly reportWriter: ReportWriter,
    private readonly defaultRoot: string
  ) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const rootPath = cmd.scope?.root_path ?? this.defaultRoot;
    const inputs = RouteScaffold.parseInputs(cmd, this.name);

    const routeFile = join(
      rootPath,
      "app",
      ...inputs.routePath.split("/")
    ) + ".tsx";
    const testFile = join(
      rootPath,
      "__tests__",
      ...inputs.routePath.split("/")
    ) + ".test.tsx";

    // Validate routePath
    if (
      inputs.routePath.includes("..") ||
      inputs.routePath.startsWith("/") ||
      inputs.routePath.startsWith("\\")
    ) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: `route_path must be relative to app/ without '..' or leading separators; got '${inputs.routePath}'`,
        errorCode: "invalid_route_path",
      });
    }

    // Refuse if either target already exists
    if (existsSync(routeFile) || existsSync(testFile)) {
      throw new BridgeError({
        stage: "validate",
        tool: this.name,
        message: `Target file already exists. Refusing to overwrite. route=${routeFile}; test=${testFile}`,
        errorCode: "route_already_exists",
      });
    }

    // Find parent _layout.tsx
    const parentDir = dirname(routeFile);
    const layoutFile = join(parentDir, "_layout.tsx");
    const layoutMissing = !existsSync(layoutFile);
    if (layoutMissing && !inputs.forceCreateLayout) {
      const appRootLayout = join(rootPath, "app", "_layout.tsx");
      if (!existsSync(appRootLayout)) {
        throw new BridgeError({
          stage: "validate",
          tool: this.name,
          message: `Parent layout missing (${layoutFile}) AND root app/_layout.tsx is absent. Pass scope.extra.force_create_layout=true to scaffold a layout alongside the route.`,
          errorCode: "layout_missing",
        });
      }
      // Root layout exists → scaffolding the route works without per-subdir layout
    }
    const willScaffoldLayout =
      layoutMissing &&
      inputs.forceCreateLayout &&
      parentDir !== join(rootPath, "app");

    // Atomic writes with rollback tracking
    const writtenPaths: string[] = [];
    try {
      mkdirSync(parentDir, { recursive: true });
      mkdirSync(dirname(testFile), { recursive: true });

      if (willScaffoldLayout) {
        writeFileSync(layoutFile, RouteScaffold.renderLayout(), "utf8");
        writtenPaths.push(layoutFile);
      }
      writeFileSync(routeFile, RouteScaffold.renderRoute(inputs), "utf8");
      writtenPaths.push(routeFile);
      writeFileSync(testFile, RouteScaffold.renderTest(inputs), "utf8");
      writtenPaths.push(testFile);
    } catch (e: unknown) {
      // Rollback: remove anything we wrote this call
      for (const p of writtenPaths) {
        try {
          rmSync(p, { force: true });
        } catch {
          /* ignore — best-effort rollback */
        }
      }
      const msg = e instanceof Error ? e.message : String(e);
      throw new BridgeError({
        stage: "execute",
        tool: this.name,
        message: `Atomic route+test write failed; both files rolled back. Underlying error: ${msg}`,
        errorCode: "hard-enforce_route_pair_atomicity",
      });
    }

    const ctx: ReportContext = {
      tool: "route_scaffold",
      freshness: {
        source: `direct file writes to ${rootPath}`,
        source_state: `scaffold at ${new Date().toISOString()}`,
        guarantee:
          "Both files (and optional layout) written atomically within a single try-block; partial failure rolls back all writes",
      },
      body: this.renderBody(
        rootPath,
        routeFile,
        testFile,
        willScaffoldLayout ? layoutFile : null,
        inputs
      ),
    };
    if (cmd.mode !== undefined) ctx.mode = cmd.mode;
    if (cmd.scope !== undefined) {
      ctx.scope = cmd.scope as unknown as Record<string, unknown>;
    }
    const reportPath = this.reportWriter.write(ctx);
    return { reportPath };
  }

  static parseInputs(cmd: BridgeCommand, toolName: string): RouteScaffoldInputs {
    const extra = cmd.scope?.extra ?? {};
    const routePath = extra.route_path;
    const componentName = extra.component_name;
    if (typeof routePath !== "string" || routePath.length === 0) {
      throw new BridgeError({
        stage: "validate",
        tool: toolName,
        message: "scope.extra.route_path is required (e.g., 'settings/account')",
        errorCode: "missing_route_path",
      });
    }
    if (typeof componentName !== "string" || componentName.length === 0) {
      throw new BridgeError({
        stage: "validate",
        tool: toolName,
        message: "scope.extra.component_name is required (e.g., 'AccountScreen')",
        errorCode: "missing_component_name",
      });
    }
    if (!/^[A-Z][A-Za-z0-9]*$/.test(componentName)) {
      throw new BridgeError({
        stage: "validate",
        tool: toolName,
        message: `scope.extra.component_name must be PascalCase; got '${componentName}'`,
        errorCode: "invalid_component_name",
      });
    }
    const hasParams = extra.has_params === true;
    const paramsType =
      typeof extra.params_type === "string" ? extra.params_type : undefined;
    if (hasParams && !paramsType) {
      throw new BridgeError({
        stage: "validate",
        tool: toolName,
        message:
          "scope.extra.params_type is required when has_params=true (e.g., '{ id: string }')",
        errorCode: "missing_params_type",
      });
    }
    return {
      routePath,
      componentName,
      hasParams,
      ...(paramsType !== undefined ? { paramsType } : {}),
      forceCreateLayout: extra.force_create_layout === true,
    };
  }

  static renderRoute(inputs: RouteScaffoldInputs): string {
    const lines: string[] = [];
    lines.push(`import { View, Text } from "react-native";`);
    if (inputs.hasParams) {
      lines.push(`import { useLocalSearchParams } from "expo-router";`);
    }
    lines.push(``);
    lines.push(`export default function ${inputs.componentName}() {`);
    if (inputs.hasParams) {
      lines.push(`  const params = useLocalSearchParams<${inputs.paramsType}>();`);
      lines.push(`  void params;`);
    }
    lines.push(`  return (`);
    lines.push(`    <View>`);
    lines.push(`      <Text>${inputs.componentName}</Text>`);
    lines.push(`    </View>`);
    lines.push(`  );`);
    lines.push(`}`);
    return lines.join("\n") + "\n";
  }

  static renderTest(inputs: RouteScaffoldInputs): string {
    // Build the relative import path from __tests__/<routePath>.test.tsx to app/<routePath>.tsx.
    const segments = inputs.routePath.split("/");
    const upHops = segments.length;
    const upPrefix = "../".repeat(upHops);
    const importPath = `${upPrefix}app/${segments.join("/")}`;
    const lines: string[] = [];
    lines.push(`import { render } from "@testing-library/react-native";`);
    lines.push(`import ${inputs.componentName} from "${importPath}";`);
    lines.push(``);
    lines.push(`describe("${inputs.componentName}", () => {`);
    lines.push(`  it("renders without crashing", () => {`);
    lines.push(`    const { getByText } = render(<${inputs.componentName} />);`);
    lines.push(`    expect(getByText("${inputs.componentName}")).toBeTruthy();`);
    lines.push(`  });`);
    lines.push(`});`);
    return lines.join("\n") + "\n";
  }

  static renderLayout(): string {
    return [
      `import { Stack } from "expo-router";`,
      ``,
      `export default function Layout() {`,
      `  return <Stack />;`,
      `}`,
      ``,
    ].join("\n");
  }

  private renderBody(
    rootPath: string,
    routeFile: string,
    testFile: string,
    layoutFile: string | null,
    inputs: RouteScaffoldInputs
  ): string {
    const lines: string[] = [];
    lines.push(`# Route Scaffolded`);
    lines.push(``);
    lines.push(`**Project root:** \`${rootPath}\``);
    lines.push(`**Route path:** \`${inputs.routePath}\``);
    lines.push(`**Component:** \`${inputs.componentName}\``);
    lines.push(`**Params:** ${inputs.hasParams ? `\`${inputs.paramsType}\`` : "—"}`);
    lines.push(``);
    lines.push(`## Files written (atomic)`);
    lines.push(``);
    lines.push(`- Route component: \`${routeFile}\``);
    lines.push(`- Test smoke: \`${testFile}\``);
    if (layoutFile) {
      lines.push(`- Parent layout: \`${layoutFile}\``);
    }
    lines.push(``);
    lines.push(
      `Both files (and optional layout) were written within a single try-block. On partial failure all writes would have been rolled back and the tool would have exited 5.`
    );
    return lines.join("\n");
  }
}
