import { ReportWriter } from "../core/ReportWriter";
import {
  DefaultSubprocessRunner,
  type SubprocessRunner,
} from "../core/SubprocessRunner";
import type {
  BridgeCommand,
  ReportContext,
  ToolHandler,
  ToolResult,
} from "../core/types";
import { BridgeError } from "../core/BridgeError";

export type CheckStatus = "PASS" | "WARN" | "FAIL";

export interface DoctorFinding {
  check: string;
  status: CheckStatus;
  message: string;
}

export class ExpoDoctorLens implements ToolHandler {
  readonly name = "expo_doctor_lens";

  constructor(
    private readonly reportWriter: ReportWriter,
    private readonly defaultRoot: string,
    private readonly runner: SubprocessRunner = new DefaultSubprocessRunner()
  ) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const rootPath = cmd.scope?.root_path ?? this.defaultRoot;

    let result;
    try {
      result = await this.runner.run("npx", ["--yes", "expo-doctor"], rootPath);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      throw new BridgeError({
        stage: "execute",
        tool: this.name,
        message: `expo-doctor failed to spawn: ${msg}. Confirm npx is on PATH and the host has network access for first-run fetch.`,
        errorCode: "expo_doctor_unavailable",
      });
    }

    if (result.exitCode === -1) {
      throw new BridgeError({
        stage: "execute",
        tool: this.name,
        message:
          "expo-doctor subprocess did not return an exit code. Check Node + npx availability.",
        errorCode: "expo_doctor_unavailable",
      });
    }

    const combined = `${result.stdout}\n${result.stderr}`;
    const findings = ExpoDoctorLens.parseFindings(combined);

    const overallStatus: CheckStatus =
      findings.some((f) => f.status === "FAIL")
        ? "FAIL"
        : findings.some((f) => f.status === "WARN")
          ? "WARN"
          : "PASS";

    const ctx: ReportContext = {
      tool: "expo_doctor_lens",
      freshness: {
        source: `subprocess: npx --yes expo-doctor in ${rootPath}`,
        source_state: `exit code ${result.exitCode} at ${new Date().toISOString()}; stdout sha256 prefix ${ExpoDoctorLens.shortHash(combined)}`,
        guarantee:
          "Subprocess actually spawned this call; no cached output is ever returned",
      },
      body: this.renderBody(findings, rootPath, overallStatus, result.exitCode),
    };
    if (cmd.mode !== undefined) ctx.mode = cmd.mode;
    if (cmd.scope !== undefined) {
      ctx.scope = cmd.scope as unknown as Record<string, unknown>;
    }
    const reportPath = this.reportWriter.write(ctx);
    return { reportPath };
  }

  /**
   * Parse expo-doctor output. expo-doctor emits one named check per line,
   * typically with a leading status glyph ("✔" / "✖" / "⚠") or a literal
   * "PASS"/"FAIL"/"WARN" marker.
   */
  static parseFindings(output: string): DoctorFinding[] {
    const findings: DoctorFinding[] = [];
    const lines = output.split(/\r?\n/);
    for (const raw of lines) {
      const line = raw.trim();
      if (!line) continue;
      // Glyph-prefixed: "✔ Check Foo" / "✖ Check Foo: details"
      const glyph = line.match(/^([✔✖⚠])\s+(.+)$/);
      if (glyph) {
        const status: CheckStatus =
          glyph[1] === "✔" ? "PASS" : glyph[1] === "⚠" ? "WARN" : "FAIL";
        const rest = glyph[2];
        const [check, ...msgParts] = rest.split(":");
        findings.push({
          check: check.trim(),
          status,
          message: msgParts.join(":").trim() || "(no detail)",
        });
        continue;
      }
      // Literal-prefixed: "PASS - Check Foo" / "FAIL: Check Bar"
      const literal = line.match(/^(PASS|WARN|FAIL)\b[-:]?\s*(.+)$/);
      if (literal) {
        const status = literal[1] as CheckStatus;
        const rest = literal[2];
        const [check, ...msgParts] = rest.split(":");
        findings.push({
          check: check.trim(),
          status,
          message: msgParts.join(":").trim() || "(no detail)",
        });
      }
    }
    return findings;
  }

  static shortHash(text: string): string {
    // Lightweight FNV-1a 32-bit hash, hex-encoded — avoids pulling crypto.
    let hash = 2166136261;
    for (let i = 0; i < text.length; i++) {
      hash ^= text.charCodeAt(i);
      hash = (hash * 16777619) >>> 0;
    }
    return hash.toString(16).padStart(8, "0");
  }

  private renderBody(
    findings: DoctorFinding[],
    rootPath: string,
    overall: CheckStatus,
    exitCode: number
  ): string {
    const lines: string[] = [];
    lines.push(`# Expo Doctor Lens`);
    lines.push(``);
    lines.push(`**Project:** \`${rootPath}\``);
    lines.push(`**Overall:** ${overall} · **Exit code:** ${exitCode}`);
    lines.push(`**Checks parsed:** ${findings.length}`);
    lines.push(``);
    if (findings.length === 0) {
      lines.push(
        `_No structured findings parsed from expo-doctor output. The tool may have failed early or emitted in an unrecognized format._`
      );
      return lines.join("\n");
    }
    lines.push(`## Findings`);
    lines.push(``);
    lines.push(`| Status | Check | Message |`);
    lines.push(`|:---|:---|:---|`);
    for (const f of findings) {
      lines.push(`| ${f.status} | \`${f.check}\` | ${f.message} |`);
    }
    return lines.join("\n");
  }
}
