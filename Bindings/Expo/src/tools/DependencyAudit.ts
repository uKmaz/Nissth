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
    const aliases = DependencyAudit.readPathAliases(rootPath);
    const imports = this.scanImports(rootPath, aliases);

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
      body: this.renderBody(
        findings,
        rootPath,
        lockfile,
        knownDeps.size,
        aliases
      ),
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
      if (!DependencyAudit.isProdFile(f)) continue;
      return true;
    }
    return false;
  }

  /**
   * A file that ships in the bundle. Tests do not, and neither do build-time
   * config files (`drizzle.config.ts`, `eslint.config.js`, `metro.config.js`, …):
   * a devDependency imported only from one of those is correctly a devDependency,
   * and reporting it as `dev_in_prod` is noise the reader learns to ignore.
   */
  static isProdFile(relPath: string): boolean {
    const p = relPath.replace(/\\/g, "/");
    if (p.includes("__tests__") || p.includes("__mocks__")) return false;
    if (/\.(test|spec)\.(t|j)sx?$/.test(p)) return false;
    if (DependencyAudit.isConfigFile(p)) return false;
    return true;
  }

  static isConfigFile(relPath: string): boolean {
    const p = relPath.replace(/\\/g, "/");
    const base = p.slice(p.lastIndexOf("/") + 1);
    // `<name>.config.<ext>` (babel, metro, jest, drizzle, eslint, tailwind, …)
    if (/\.config\.(m|c)?(t|j)sx?$/.test(base)) return true;
    // Jest/RNTL setup files, which are loaded by the runner, never bundled.
    if (/^jest\.setup\.(m|c)?(t|j)sx?$/.test(base)) return true;
    return false;
  }

  /**
   * TypeScript path aliases (`{"@/*": ["./*"]}`) look exactly like scoped-package
   * specifiers to an import scan, so without this every `@/db` import is reported
   * as a missing package. Follows relative `extends` chains; a bare `extends`
   * (`expo/tsconfig.base`) is left alone — those do not define project aliases.
   */
  static readPathAliases(rootPath: string, depth = 0): string[] {
    const file = join(rootPath, "tsconfig.json");
    return DependencyAudit.readAliasesFromFile(file, depth);
  }

  private static readAliasesFromFile(file: string, depth: number): string[] {
    if (depth > 5 || !existsSync(file)) return [];
    let cfg: {
      extends?: string;
      compilerOptions?: { paths?: Record<string, unknown> };
    };
    try {
      cfg = JSON.parse(DependencyAudit.stripJsonComments(readFileSync(file, "utf8")));
    } catch {
      return [];
    }
    const out: string[] = [];
    if (typeof cfg.extends === "string" && cfg.extends.startsWith(".")) {
      const parent = join(file, "..", cfg.extends);
      const withExt = /\.json$/.test(parent) ? parent : `${parent}.json`;
      out.push(...DependencyAudit.readAliasesFromFile(withExt, depth + 1));
    }
    const paths = cfg.compilerOptions?.paths;
    if (paths && typeof paths === "object") out.push(...Object.keys(paths));
    return out;
  }

  /** tsconfig.json is JSONC in practice; drop line and block comments so JSON.parse survives. */
  static stripJsonComments(text: string): string {
    let out = "";
    let inString = false;
    let inLine = false;
    let inBlock = false;
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      const next = text[i + 1];
      if (inLine) {
        if (c === "\n") {
          inLine = false;
          out += c;
        }
        continue;
      }
      if (inBlock) {
        if (c === "*" && next === "/") {
          inBlock = false;
          i++;
        }
        continue;
      }
      if (inString) {
        out += c;
        if (c === "\\") {
          out += next ?? "";
          i++;
        } else if (c === '"') inString = false;
        continue;
      }
      if (c === '"') {
        inString = true;
        out += c;
        continue;
      }
      if (c === "/" && next === "/") {
        inLine = true;
        i++;
        continue;
      }
      if (c === "/" && next === "*") {
        inBlock = true;
        i++;
        continue;
      }
      out += c;
    }
    // Trailing commas are legal in tsconfig and not in JSON.
    return out.replace(/,(\s*[}\]])/g, "$1");
  }

  /** True when the specifier resolves through a tsconfig `paths` entry. */
  static matchesAlias(specifier: string, aliases: string[]): boolean {
    for (const pattern of aliases) {
      const star = pattern.indexOf("*");
      if (star === -1) {
        if (specifier === pattern) return true;
        continue;
      }
      const head = pattern.slice(0, star);
      const tail = pattern.slice(star + 1);
      if (
        specifier.length >= head.length + tail.length &&
        specifier.startsWith(head) &&
        specifier.endsWith(tail)
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
  private scanImports(
    rootPath: string,
    aliases: string[] = []
  ): Map<string, Set<string>> {
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
        if (DependencyAudit.matchesAlias(m, aliases)) continue;
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
          if (dyn && !DependencyAudit.matchesAlias(dyn[1], aliases)) {
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
    declaredCount: number,
    aliases: string[] = []
  ): string {
    const lines: string[] = [];
    lines.push(`# Dependency Audit`);
    lines.push(``);
    lines.push(`**Project:** \`${rootPath}\``);
    lines.push(`**Lockfile:** \`${lockfile ?? "(none)"}\``);
    lines.push(
      `**Declared:** ${declaredCount} · **Findings:** ${findings.length}`
    );
    lines.push(
      `**Path aliases (not packages):** ${
        aliases.length
          ? aliases.map((a) => `\`${a}\``).join(", ")
          : "none in tsconfig.json"
      }`
    );
    lines.push(
      `**Prod usage excludes:** tests, \`__mocks__\`, and build-time config files (\`*.config.*\`, \`jest.setup.*\`)`
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
