import Ajv2020, { type ValidateFunction } from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { stringify as yamlStringify } from "yaml";
import { findFrameworkRoot, findRepoRoot } from "./repoRoot";
import type { ReportContext, ReportFrontmatter } from "./types";
import { BridgeError } from "./BridgeError";

function loadFrontmatterValidator(): ValidateFunction {
  // Schema lives in the FRAMEWORK tree; reports (see write() below) go to
  // the CONSUMER repo via the constructor-injected this.repoRoot.
  // findFrameworkRoot honors NISSTH_FRAMEWORK_ROOT per CLAUDE.md §11.15.
  // Phase 09.5 fix.
  const frameworkRoot = findFrameworkRoot(findRepoRoot());
  const schemaPath = join(frameworkRoot, "Bindings", "_schemas", "bridge-command.schema.json");
  const schema = JSON.parse(readFileSync(schemaPath, "utf8")) as {
    $defs?: { reportFrontmatter?: object };
  };
  const fmSchema = schema.$defs?.reportFrontmatter;
  if (!fmSchema) {
    throw new Error("Could not find $defs.reportFrontmatter in schema");
  }
  const ajv = new (Ajv2020 as unknown as new (opts: unknown) => Ajv2020)({
    allErrors: true,
    strict: false,
  });
  addFormats(ajv as unknown as Parameters<typeof addFormats>[0]);
  return ajv.compile(fmSchema);
}

export class ReportWriter {
  private readonly binding: string;
  private readonly bindingVersion: string;
  private readonly repoRoot: string;
  private readonly validateFrontmatter: ValidateFunction;

  constructor(opts: {
    binding: string;
    bindingVersion: string;
    repoRoot: string;
    validateFrontmatter?: ValidateFunction;
  }) {
    this.binding = opts.binding;
    this.bindingVersion = opts.bindingVersion;
    this.repoRoot = opts.repoRoot;
    this.validateFrontmatter = opts.validateFrontmatter ?? loadFrontmatterValidator();
  }

  buildFrontmatter(ctx: ReportContext, generatedAt: Date = new Date()): ReportFrontmatter {
    const fm: ReportFrontmatter = {
      tool: ctx.tool,
      binding: this.binding,
      binding_version: this.bindingVersion,
      generated_at: generatedAt.toISOString(),
      freshness: ctx.freshness,
      contract_version: 1,
    };
    if (ctx.mode !== undefined) {
      fm.mode = ctx.mode;
    }
    if (ctx.scope !== undefined) {
      fm.scope = ctx.scope;
    }

    if (!this.validateFrontmatter(fm)) {
      const errors = (this.validateFrontmatter.errors ?? [])
        .map((e) => `${e.instancePath || "/"} ${e.message ?? "validation failed"}`)
        .join("; ");
      throw new BridgeError({
        stage: "format",
        tool: ctx.tool,
        message: `Frontmatter validation failed: ${errors}`,
      });
    }

    return fm;
  }

  write(ctx: ReportContext): string {
    const now = new Date();
    const fm = this.buildFrontmatter(ctx, now);
    const bridgeDir = join(this.repoRoot, "AgentReports", "Bridge");
    mkdirSync(bridgeDir, { recursive: true });

    const fileName = ctx.fileName ?? `${ctx.tool}_${ReportWriter.compactIso(now)}.md`;
    const filePath = join(bridgeDir, fileName);

    const yamlBody = yamlStringify(fm);
    const content = `---\n${yamlBody}---\n\n${ctx.body}\n`;
    writeFileSync(filePath, content, "utf8");
    return filePath;
  }

  static compactIso(d: Date): string {
    return d.toISOString().replace(/\.\d{3}Z$/, "Z").replace(/:/g, "");
  }
}
