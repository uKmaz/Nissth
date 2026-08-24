import { existsSync, readdirSync, statSync } from "node:fs";
import { basename, join, relative } from "node:path";
import { Project, SyntaxKind, type SourceFile } from "ts-morph";
import { ReportWriter } from "../core/ReportWriter";
import { StaleFlipper } from "../core/StaleFlipper";
import type {
  BridgeCommand,
  ReportContext,
  ToolHandler,
  ToolResult,
} from "../core/types";

export interface ComponentInfo {
  name: string;
  filePath: string;
  propsType?: string;
  exported: "default" | "named";
  hooks: string[];
}

export class ComponentLens implements ToolHandler {
  readonly name = "component_lens";

  constructor(
    private readonly reportWriter: ReportWriter,
    private readonly staleFlipper: StaleFlipper,
    private readonly defaultRoot: string
  ) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const rootPath = cmd.scope?.root_path ?? this.defaultRoot;
    const subPath = cmd.scope?.package ?? "components";
    const scanDir = join(rootPath, subPath);
    const maxDepth = cmd.scope?.max_depth ?? -1;

    let components: ComponentInfo[] = [];
    if (existsSync(scanDir) && statSync(scanDir).isDirectory()) {
      const files = this.walkSourceFiles(scanDir, maxDepth);
      for (const f of files) {
        components.push(...this.parseComponents(f, scanDir));
      }
    }

    const ctx: ReportContext = {
      tool: "component_lens",
      freshness: {
        source: `ts-morph AST scan under ${scanDir}`,
        source_state: `scan at ${new Date().toISOString()}`,
        guarantee: "AST built fresh this call; no cached output",
      },
      body: this.renderBody(components, scanDir),
    };
    if (cmd.mode !== undefined) ctx.mode = cmd.mode;
    if (cmd.scope !== undefined) {
      ctx.scope = cmd.scope as unknown as Record<string, unknown>;
    }

    const reportPath = this.reportWriter.write(ctx);

    const liveComponentNames = new Set(components.map((c) => c.name));
    const reportFileName = basename(reportPath);
    this.staleFlipper.flipIfStale({
      dblSubdir: "Summaries",
      scopePath: subPath,
      driftCheck: (_fm, body) =>
        ComponentLens.detectDrift(body, liveComponentNames),
      reportFileName,
    });

    return { reportPath };
  }

  static detectDrift(dblBody: string, liveNames: Set<string>): boolean {
    const documented = new Set<string>();
    for (const line of dblBody.split(/\r?\n/)) {
      // Lenient: pick PascalCase identifiers off table cells or backticks
      const m = line.match(/\b([A-Z][A-Za-z0-9]+)\b/g);
      if (!m) continue;
      for (const id of m) documented.add(id);
    }
    if (documented.size === 0 && liveNames.size === 0) return false;
    if (documented.size === 0) return true;
    // Conservative: drift if any live name is missing from documented set
    for (const name of liveNames) if (!documented.has(name)) return true;
    return false;
  }

  private walkSourceFiles(
    dir: string,
    maxDepth: number,
    currentDepth = 0
  ): string[] {
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
        if (e === "node_modules" || e === "__tests__" || e === ".expo") continue;
        out.push(...this.walkSourceFiles(p, maxDepth, currentDepth + 1));
      } else if (/\.(tsx?|jsx?)$/.test(e) && !/\.test\.(tsx?|jsx?)$/.test(e)) {
        out.push(p);
      }
    }
    return out;
  }

  private parseComponents(filePath: string, scanRoot: string): ComponentInfo[] {
    const found: ComponentInfo[] = [];
    let sf: SourceFile;
    try {
      const project = new Project({
        useInMemoryFileSystem: false,
        skipAddingFilesFromTsConfig: true,
        compilerOptions: { allowJs: true, jsx: 2 /* React */ },
      });
      sf = project.addSourceFileAtPath(filePath);
    } catch {
      return found;
    }

    const rel = relative(scanRoot, filePath).replace(/\\/g, "/");
    const hooksInFile = this.collectHooks(sf);

    for (const fn of sf.getFunctions()) {
      const name = fn.getName();
      if (!name || !/^[A-Z]/.test(name)) continue;
      if (!ComponentLens.returnsJsx(fn.getBodyText() ?? "")) continue;
      const exported: "default" | "named" = fn.isDefaultExport()
        ? "default"
        : fn.isExported()
          ? "named"
          : "named";
      const props = fn.getParameters()[0];
      const propsType = props?.getTypeNode()?.getText();
      found.push({
        name,
        filePath: rel,
        propsType,
        exported,
        hooks: [...hooksInFile],
      });
    }

    for (const vs of sf.getVariableStatements()) {
      for (const decl of vs.getDeclarations()) {
        const name = decl.getName();
        if (!/^[A-Z]/.test(name)) continue;
        const initText = decl.getInitializer()?.getText() ?? "";
        if (!ComponentLens.returnsJsx(initText)) continue;
        const exported: "default" | "named" = vs.hasDefaultKeyword()
          ? "default"
          : vs.isExported()
            ? "named"
            : "named";
        const typeNode = decl.getTypeNode();
        const propsType = typeNode ? typeNode.getText() : undefined;
        found.push({
          name,
          filePath: rel,
          propsType,
          exported,
          hooks: [...hooksInFile],
        });
      }
    }

    return found;
  }

  private collectHooks(sf: SourceFile): Set<string> {
    const hooks = new Set<string>();
    sf.forEachDescendant((node) => {
      if (node.getKind() === SyntaxKind.CallExpression) {
        const text = node.getText();
        const m = text.match(/^(use[A-Z]\w*)/);
        if (m) hooks.add(m[1]);
      }
    });
    return hooks;
  }

  static returnsJsx(text: string): boolean {
    // Heuristic: source text contains a JSX element opener or React.createElement
    return (
      /<[A-Za-z]/.test(text) ||
      /React\.createElement\(/.test(text) ||
      /createElement\(/.test(text)
    );
  }

  private renderBody(components: ComponentInfo[], scanDir: string): string {
    const lines: string[] = [];
    lines.push(`# Expo Component Lens`);
    lines.push(``);
    lines.push(`**Scope:** \`${scanDir}\``);
    lines.push(`**Components found:** ${components.length}`);
    lines.push(``);
    if (components.length === 0) {
      lines.push(
        `_No React components discovered under the scoped directory._`
      );
      return lines.join("\n");
    }
    lines.push(`## Components`);
    lines.push(``);
    lines.push(
      `| Component | File | Props | Exported | Hooks used |`
    );
    lines.push(`|:---|:---|:---|:---|:---|`);
    for (const c of components) {
      const hooks = c.hooks.length > 0 ? c.hooks.join(", ") : "—";
      lines.push(
        `| \`${c.name}\` | \`${c.filePath}\` | \`${c.propsType ?? "—"}\` | ${c.exported} | ${hooks} |`
      );
    }
    return lines.join("\n");
  }
}
