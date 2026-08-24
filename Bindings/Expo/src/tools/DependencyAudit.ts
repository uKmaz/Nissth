import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { Project, SyntaxKind } from "ts-morph";
import { ReportWriter } from "../core/ReportWriter";
import type {
  BridgeCommand,
  ReportContext,
  ToolHandler,
  ToolResult,
} from "../core/types";
import { BridgeError } from "../core/BridgeError";

export type DepStatus = "used" | "unused" | "dev_in_prod";
export type ImportStatus = "declared" | "missing" | "transitive";

export interface DepFinding {
  name: string;
  declaredIn: "dependencies" | "devDependencies" | "peerDependencies" | "none";
  importStatus: ImportStatus;
  depStatus: DepStatus;
}

export class DependencyAudit implements ToolHandler {
  readonly name = "dependency_audit";

  constructor(
    private readonly reportWriter: ReportWriter,
    private readonly defaultRoot: string
  ) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const rootPath = cmd.scope?.root_path ?? this.defaultRoot;
    const pkgJsonPath = join(rootPath, "package.json");
    if (!existsSync(pkgJsonPath)) {
      throw new BridgeError({
        stage: "execute",
        tool: this.name,
        message: `No package.json found at ${pkgJsonPath}`,
        errorCode: "no_package_json",
      });
    }
    const pkg = JSON.parse(readFileSync(pkgJsonPath, "utf8")) as {
      dependencies?: Record<string, string>;
      devDependencies?: Record<string, string>;
      peerDependencies?: Record<string, string>;
    };

    const dependencies = pkg.dependencies ?? {};
    const devDependencies = pkg.devDependencies ?? {};
    const peerDependencies = pkg.peerDependencies ?? {};

    const lockfile = DependencyAudit.detectLockfile(rootPath);
    const imports = this.scanImports(rootPath);

    // Build the unified findings list
    const findings: DepFinding[] = [];
    const knownDeps = new Set([
      ...Object.keys(dependencies),
      ...Object.keys(devDependencies),
      ...Object.keys(peerDependencies),
    ]);

    for (const dep of knownDeps) {
      const declaredIn: DepFinding["declaredIn"] = dependencies[dep]
        ? "dependencies"
        : devDependencies[dep]
          ? "devDependencies"
          : peerDependencies[dep]
            ? "peerDependencies"
            : "none";
      const isImported = imports.has(dep);
      const usedInProd = DependencyAudit.usedInProd(imports, dep);
      let depStatus: DepStatus;
      if (!isImported) {
        depStatus = "unused";
      } else if (declaredIn === "devDependencies" && usedInProd) {
        depStatus = "dev_in_prod";
      } else {
        depStatus = "used";
      }
      findings.push({
        name: dep,
        declaredIn,
        importStatus: "declared",
        depStatus,
      });
    }
    // Imports that aren't declared anywhere
    for (const imp of imports.keys()) {
      if (knownDeps.has(imp)) continue;
      if (DependencyAudit.isNodeBuiltin(imp)) continue;
      if (imp.startsWith(".") || imp.startsWith("/")) continue;
      findings.push({
        name: imp,
        declaredIn: "none",
        importStatus: "missing",
        depStatus: "used",
      });
    }

    const ctx: ReportContext = {
      tool: "dependency_audit",
      freshness: {
        source: `package.json + ${lockfile ?? "(no lockfile)"} + ts-morph import scan under ${rootPath}`,
        source_state: `audit at ${new Date().toISOString()}`,
        guarantee:
          "package.json read fresh from disk; import scan walks the source tree; no caching",
      },
      body: this.renderBody(findings, rootPath, lockfile, knownDeps.size),
    };
    if (cmd.mode !== undefined) ctx.mode = cmd.mode;
    if (cmd.scope !== undefined) {
      ctx.scope = cmd.scope as unknown as Record<string, unknown>;
    }
    const reportPath = this.reportWriter.write(ctx);
    return { reportPath };
  }

  static detectLockfile(rootPath: string): string | null {
    const candidates = ["package-lock.json", "yarn.lock", "pnpm-lock.yaml"];
    for (const c of candidates) {
      if (existsSync(join(rootPath, c))) return c;
    }
    return null;
  }

  static usedInProd(imports: Map<string, Set<string>>, dep: string): boolean {
    const files = imports.get(dep);
    if (!files) return false;
    for (const f of files) {
      // Used in prod if it appears outside __tests__/ or *.test.*
      if (
        !f.includes("__tests__") &&
        !/\.test\.(t|j)sx?$/.test(f) &&
        !/\.spec\.(t|j)sx?$/.test(f)
      ) {
        return true;
      }
    }
    return false;
  }

  static isNodeBuiltin(name: string): boolean {
    return (
      name.startsWith("node:") ||
      [
        "fs",
        "path",
        "os",
        "crypto",
        "child_process",
        "url",
        "util",
        "buffer",
        "stream",
        "events",
        "http",
        "https",
        "net",
        "tls",
        "zlib",
        "assert",
      ].includes(name)
    );
  }

  /** Returns Map<package-name, Set<file-paths-relative-to-root>>. */
  private scanImports(rootPath: string): Map<string, Set<string>> {
    const result = new Map<string, Set<string>>();
    const files = this.collectSourceFiles(rootPath);
    let project: Project;
    try {
      project = new Project({
        useInMemoryFileSystem: false,
        skipAddingFilesFromTsConfig: true,
        compilerOptions: { allowJs: true, jsx: 2 },
      });
    } catch {
      return result;
    }

    for (const f of files) {
      let sf;
      try {
        sf = project.addSourceFileAtPath(f);
      } catch {
        continue;
      }
      const rel = relative(rootPath, f).replace(/\\/g, "/");
      for (const imp of sf.getImportDeclarations()) {
        const m = imp.getModuleSpecifierValue();
        const pkg = DependencyAudit.toPackageName(m);
        if (!pkg) continue;
        if (!result.has(pkg)) result.set(pkg, new Set());
        result.get(pkg)!.add(rel);
      }
      // dynamic import() and require()
      sf.forEachDescendant((node) => {
        if (node.getKind() === SyntaxKind.CallExpression) {
          const txt = node.getText();
          const dyn = txt.match(/^(?:import|require)\(["']([^"']+)["']\)/);
          if (dyn) {
            const pkg = DependencyAudit.toPackageName(dyn[1]);
            if (pkg) {
              if (!result.has(pkg)) result.set(pkg, new Set());
              result.get(pkg)!.add(rel);
            }
          }
        }
      });
    }
    return result;
  }

  static toPackageName(specifier: string): string | null {
    if (!specifier) return null;
    if (specifier.startsWith(".") || specifier.startsWith("/")) return null;
    if (specifier.startsWith("@")) {
      const parts = specifier.split("/");
      if (parts.length < 2) return null;
      return parts.slice(0, 2).join("/");
    }
    return specifier.split("/")[0];
  }

  private collectSourceFiles(rootPath: string): string[] {
    const out: string[] = [];
    const skipDirs = new Set([
      "node_modules",
      "dist",
      "build",
      ".expo",
      ".next",
      "coverage",
      ".git",
    ]);
    const walk = (dir: string): void => {
      let entries: string[];
      try {
        entries = readdirSync(dir);
      } catch {
        return;
      }
      for (const e of entries) {
        if (skipDirs.has(e)) continue;
        const p = join(dir, e);
        let st;
        try {
          st = statSync(p);
        } catch {
          continue;
        }
        if (st.isDirectory()) walk(p);
        else if (/\.(tsx?|jsx?)$/.test(e)) out.push(p);
      }
    };
    walk(rootPath);
    return out;
  }

  private renderBody(
    findings: DepFinding[],
    rootPath: string,
    lockfile: string | null,
    declaredCount: number
  ): string {
    const lines: string[] = [];
    lines.push(`# Dependency Audit`);
    lines.push(``);
    lines.push(`**Project:** \`${rootPath}\``);
    lines.push(`**Lockfile:** \`${lockfile ?? "(none)"}\``);
    lines.push(
      `**Declared:** ${declaredCount} · **Findings:** ${findings.length}`
    );
    lines.push(``);

    const unused = findings.filter((f) => f.depStatus === "unused");
    const devInProd = findings.filter((f) => f.depStatus === "dev_in_prod");
    const missing = findings.filter((f) => f.importStatus === "missing");

    lines.push(`## Summary`);
    lines.push(``);
    lines.push(`- **Unused (declared but never imported):** ${unused.length}`);
    lines.push(`- **dev_in_prod (imported in prod, listed only as devDep):** ${devInProd.length}`);
    lines.push(`- **Missing (imported but not declared):** ${missing.length}`);
    lines.push(``);

    lines.push(`## Findings`);
    lines.push(``);
    lines.push(`| Package | Declared in | Import status | Dep status |`);
    lines.push(`|:---|:---|:---|:---|`);
    for (const f of findings.sort((a, b) => a.name.localeCompare(b.name))) {
      lines.push(
        `| \`${f.name}\` | ${f.declaredIn} | ${f.importStatus} | ${f.depStatus} |`
      );
    }
    return lines.join("\n");
  }
}
