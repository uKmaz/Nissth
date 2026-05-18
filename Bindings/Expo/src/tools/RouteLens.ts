import { existsSync, readdirSync, statSync } from "node:fs";
import { basename, join, relative } from "node:path";
import { Project, SyntaxKind } from "ts-morph";
import { ReportWriter } from "../core/ReportWriter";
import { StaleFlipper } from "../core/StaleFlipper";
import type {
  BridgeCommand,
  ReportContext,
  ToolHandler,
  ToolResult,
} from "../core/types";

export type RouteClassification =
  | "static"
  | "dynamic"
  | "catch-all"
  | "group"
  | "layout";

export interface RouteInfo {
  urlPath: string;
  filePath: string;
  componentName: string;
  paramsType?: string;
  layoutParent?: string;
  classification: RouteClassification;
}

export class RouteLens implements ToolHandler {
  readonly name = "route_lens";

  constructor(
    private readonly reportWriter: ReportWriter,
    private readonly staleFlipper: StaleFlipper,
    private readonly defaultRoot: string
  ) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const rootPath = cmd.scope?.root_path
      ? cmd.scope.root_path
      : this.defaultRoot;
    const subPath = cmd.scope?.package ?? "";
    const appDir = join(rootPath, "app");
    const maxDepth = cmd.scope?.max_depth ?? -1;

    let routes: RouteInfo[] = [];
    if (existsSync(appDir) && statSync(appDir).isDirectory()) {
      const scanRoot = subPath ? join(appDir, subPath) : appDir;
      const files = this.walkTsx(scanRoot, maxDepth);
      for (const f of files) {
        const info = this.classifyRoute(f, appDir);
        routes.push(this.parseRoute(f, info));
      }
    }

    const ctx: ReportContext = {
      tool: "route_lens",
      freshness: {
        source: `filesystem walk + ts-morph AST parse under ${appDir}`,
        source_state: `walk at ${new Date().toISOString()}`,
        guarantee:
          "AST built fresh this call from disk; no persistent cache; no Metro involvement",
      },
      body: this.renderBody(routes, appDir),
    };
    if (cmd.mode !== undefined) ctx.mode = cmd.mode;
    if (cmd.scope !== undefined) {
      ctx.scope = cmd.scope as unknown as Record<string, unknown>;
    }

    const reportPath = this.reportWriter.write(ctx);

    // STALE-flip DBL/APIIndex/*.md whose `covers` overlaps the app/ scope.
    const liveRouteUrls = new Set(
      routes
        .filter((r) => r.classification !== "layout")
        .map((r) => r.urlPath)
    );
    const reportFileName = basename(reportPath);
    this.staleFlipper.flipIfStale({
      dblSubdir: "APIIndex",
      scopePath: "app/",
      driftCheck: (_fm, body) => RouteLens.detectDrift(body, liveRouteUrls),
      reportFileName,
    });

    return { reportPath };
  }

  static detectDrift(dblBody: string, liveRouteUrls: Set<string>): boolean {
    const documented = new Set<string>();
    for (const line of dblBody.split(/\r?\n/)) {
      const m = line.match(/^\|\s*`?(\/[^\s|`]*)`?\s*\|/);
      if (m) documented.add(m[1]);
    }
    if (documented.size === 0 && liveRouteUrls.size === 0) return false;
    if (documented.size === 0) return true; // DBL has no routes, live has some — drift
    for (const r of liveRouteUrls) if (!documented.has(r)) return true;
    for (const d of documented) if (!liveRouteUrls.has(d)) return true;
    return false;
  }

  private walkTsx(dir: string, maxDepth: number, currentDepth = 0): string[] {
    const out: string[] = [];
    if (maxDepth >= 0 && currentDepth > maxDepth) return out;
    let entries: string[];
    try {
      entries = readdirSync(dir);
    } catch {
      return out;
    }
    for (const e of entries) {
      const p = join(dir, e);
      let st;
      try {
        st = statSync(p);
      } catch {
        continue;
      }
      if (st.isDirectory()) {
        out.push(...this.walkTsx(p, maxDepth, currentDepth + 1));
      } else if (e.endsWith(".tsx")) {
        out.push(p);
      }
    }
    return out;
  }

  private classifyRoute(filePath: string, appDir: string): RouteInfo {
    const rel = relative(appDir, filePath).replace(/\\/g, "/");
    const base = rel.split("/").pop()!;
    let classification: RouteClassification = "static";
    if (base === "_layout.tsx") {
      classification = "layout";
    } else if (/^\[\.\.\..+\]\.tsx$/.test(base)) {
      classification = "catch-all";
    } else if (/^\[.+\]\.tsx$/.test(base)) {
      classification = "dynamic";
    } else if (rel.split("/").some((seg) => /^\(.+\)$/.test(seg))) {
      classification = "group";
    }

    // URL path: strip .tsx, strip /index, strip (group) segments
    let urlPath = "/" + rel.replace(/\.tsx$/, "");
    urlPath = urlPath.replace(/\/index$/, "/");
    urlPath = urlPath.replace(/\/\(.+?\)/g, "");
    if (urlPath === "" || urlPath === "/") urlPath = "/";
    if (classification === "layout") {
      urlPath = "(layout)";
    }

    // Parent layout: nearest _layout.tsx in ancestor dir
    let layoutParent: string | undefined;
    const parts = rel.split("/");
    parts.pop();
    while (parts.length > 0) {
      const candidate = join(appDir, ...parts, "_layout.tsx");
      try {
        if (statSync(candidate).isFile()) {
          layoutParent = relative(appDir, candidate).replace(/\\/g, "/");
          break;
        }
      } catch {
        // fall through
      }
      parts.pop();
    }
    if (!layoutParent && rel !== "_layout.tsx") {
      const rootLayout = join(appDir, "_layout.tsx");
      try {
        if (statSync(rootLayout).isFile()) {
          layoutParent = "_layout.tsx";
        }
      } catch {
        // ignore
      }
    }

    return {
      urlPath,
      filePath: rel,
      componentName: "",
      ...(layoutParent !== undefined ? { layoutParent } : {}),
      classification,
    };
  }

  private parseRoute(filePath: string, info: RouteInfo): RouteInfo {
    let componentName = this.inferDefaultName(filePath);
    let paramsType: string | undefined;
    try {
      const project = new Project({
        useInMemoryFileSystem: false,
        skipAddingFilesFromTsConfig: true,
        compilerOptions: { allowJs: false },
      });
      const sf = project.addSourceFileAtPath(filePath);
      // Try to find default-exported function name
      const defaultExport = sf
        .getFunctions()
        .find((fn) => fn.isDefaultExport());
      if (defaultExport) {
        componentName = defaultExport.getName() ?? componentName;
      } else {
        const assignment = sf.getExportAssignment((a) => !a.isExportEquals());
        if (assignment) {
          const expr = assignment.getExpression().getText();
          if (/^[A-Za-z_$][\w$]*$/.test(expr)) componentName = expr;
        }
      }
      // Find useLocalSearchParams<T>() type arg
      sf.forEachDescendant((node) => {
        if (paramsType !== undefined) return;
        if (node.getKind() === SyntaxKind.CallExpression) {
          const txt = node.getText();
          const m = txt.match(/useLocalSearchParams\s*<([^>]+)>/);
          if (m) paramsType = m[1].trim();
        }
      });
    } catch {
      // best-effort; keep inferred name
    }
    return {
      ...info,
      componentName,
      ...(paramsType !== undefined ? { paramsType } : {}),
    };
  }

  private inferDefaultName(filePath: string): string {
    const b = filePath.split(/[/\\]/).pop()!.replace(/\.tsx$/, "");
    return b.replace(/[^A-Za-z0-9]/g, "_") || "Default";
  }

  private renderBody(routes: RouteInfo[], appDir: string): string {
    const lines: string[] = [];
    lines.push(`# Expo Router Route Lens`);
    lines.push(``);
    lines.push(`**Scope:** \`${appDir}\``);
    lines.push(`**Routes found:** ${routes.length}`);
    lines.push(``);
    if (routes.length === 0) {
      lines.push(
        `_No routes discovered. Either no \`app/\` directory exists or it contains no \`.tsx\` files._`
      );
      return lines.join("\n");
    }
    lines.push(`## Routes`);
    lines.push(``);
    lines.push(
      `| URL path | File | Component | Params | Layout parent | Classification |`
    );
    lines.push(`|:---|:---|:---|:---|:---|:---|`);
    for (const r of routes) {
      lines.push(
        `| \`${r.urlPath}\` | \`${r.filePath}\` | \`${r.componentName}\` | \`${r.paramsType ?? "—"}\` | \`${r.layoutParent ?? "—"}\` | ${r.classification} |`
      );
    }
    return lines.join("\n");
  }
}
