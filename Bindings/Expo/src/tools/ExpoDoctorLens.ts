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

/** Counts from the summary line plus one finding per check the output names. */
export interface DoctorParse {
  /** Checks expo-doctor said it would run, or null when the line is absent. */
  total: number | null;
  /** Checks it reported as passed, or null when the summary line is absent. */
  passed: number | null;
  /** total - passed when both are known, else the number of FAIL findings. */
  failed: number;
  findings: DoctorFinding[];
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
    // expo-doctor names every check only under --verbose; the default run prints
    // just the summary line and a detail block per failure (1.x, confirmed against
    // 1.20.4 on 2026-09-24). `verbose` mode is how the agent gets the full table.
    const args = ["--yes", "expo-doctor"];
    if (cmd.mode === "verbose") args.push("--verbose");

    let result;
    try {
      result = await this.runner.run("npx", args, rootPath);
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
    const parsed = ExpoDoctorLens.parseOutput(combined);

    const overallStatus: CheckStatus = ExpoDoctorLens.overall(
      parsed,
      result.exitCode
    );

    const ctx: ReportContext = {
      tool: "expo_doctor_lens",
      freshness: {
        source: `subprocess: npx ${args.join(" ")} in ${rootPath}`,
        source_state: `exit code ${result.exitCode} at ${new Date().toISOString()}; stdout sha256 prefix ${ExpoDoctorLens.shortHash(combined)}`,
        guarantee:
          "Subprocess actually spawned this call; no cached output is ever returned",
      },
      body: this.renderBody(parsed, rootPath, overallStatus, result.exitCode),
    };
    if (cmd.mode !== undefined) ctx.mode = cmd.mode;
    if (cmd.scope !== undefined) {
      ctx.scope = cmd.scope as unknown as Record<string, unknown>;
    }
    const reportPath = this.reportWriter.write(ctx);
    return { reportPath };
  }

  /**
   * Parse expo-doctor output into counts + findings.
   *
   * expo-doctor 1.x emits, in order:
   *   Running 21 checks on your project...
   *   ✔ <check name>                      ← only under --verbose
   *   ✖ <check name>                      ← only under --verbose
   *   20/21 checks passed. 1 checks failed. Possible issues detected:
   *   ✖ <check name>                      ← detail block, every run
   *   <indented / free-form detail lines>
   *   1 check failed, indicating possible issues with the project.
   *
   * A failed check therefore appears TWICE under --verbose; findings are keyed by
   * check name so the detail block enriches the listed check rather than doubling it.
   * Older/other shapes ("PASS - <name>", "FAIL: <name>") are still accepted.
   */
  static parseOutput(output: string): DoctorParse {
    const lines = output.split(/\r?\n/);
    let total: number | null = null;
    let passed: number | null = null;

    const order: string[] = [];
    const byCheck = new Map<string, DoctorFinding>();
    const detail = new Map<string, string[]>();
    let collecting: string | null = null;

    const record = (
      check: string,
      status: CheckStatus,
      message?: string
    ): void => {
      const existing = byCheck.get(check);
      if (existing) {
        // A later FAIL outranks an earlier PASS for the same name; a message
        // arriving with the detail block fills in what the list line lacked.
        if (status === "FAIL") existing.status = "FAIL";
        if (message && existing.message === "(no detail)") {
          existing.message = message;
        }
        return;
      }
      order.push(check);
      byCheck.set(check, {
        check,
        status,
        message: message ?? "(no detail)",
      });
    };

    for (const raw of lines) {
      const line = raw.trim();

      const running = line.match(/^Running\s+(\d+)\s+checks?\b/i);
      if (running) {
        total = Number(running[1]);
        continue;
      }
      const summary = line.match(/^(\d+)\/(\d+)\s+checks?\s+passed\b/i);
      if (summary) {
        passed = Number(summary[1]);
        total = Number(summary[2]);
        collecting = null;
        continue;
      }
      if (/^\d+\s+checks?\s+failed\b/i.test(line)) {
        collecting = null;
        continue;
      }

      const glyph = line.match(/^([✔✓✖✗⚠])\s+(.+)$/);
      if (glyph) {
        const status: CheckStatus =
          glyph[1] === "✔" || glyph[1] === "✓"
            ? "PASS"
            : glyph[1] === "⚠"
              ? "WARN"
              : "FAIL";
        const check = glyph[2].trim();
        record(check, status);
        // Only a non-passing check is followed by a detail block worth keeping.
        collecting = status === "PASS" ? null : check;
        if (collecting && !detail.has(collecting)) detail.set(collecting, []);
        continue;
      }

      // Literal-prefixed, e.g. "PASS - Check Foo" / "FAIL: Check Bar".
      const literal = line.match(/^(PASS|WARN|FAIL)\b\s*[-:]?\s*(.+)$/);
      if (literal) {
        const status = literal[1] as CheckStatus;
        const rest = literal[2].trim();
        const sep = rest.indexOf(":");
        const check = sep === -1 ? rest : rest.slice(0, sep).trim();
        const message = sep === -1 ? undefined : rest.slice(sep + 1).trim();
        record(check, status, message || undefined);
        collecting = null;
        continue;
      }

      if (collecting && line) detail.get(collecting)!.push(line);
    }

    for (const [check, body] of detail) {
      const f = byCheck.get(check);
      if (!f) continue;
      const text = body.join(" · ").replace(/\s+/g, " ").trim();
      if (text) f.message = text.length > 400 ? `${text.slice(0, 397)}…` : text;
    }

    const findings = order.map((c) => byCheck.get(c)!);
    const failedFindings = findings.filter((f) => f.status === "FAIL").length;
    const failed =
      total !== null && passed !== null ? total - passed : failedFindings;

    return { total, passed, failed, findings };
  }

  /**
   * Kept so callers written against the pre-Phase-21 shape keep working;
   * new code reads the counts from {@link parseOutput}.
   */
  static parseFindings(output: string): DoctorFinding[] {
    return ExpoDoctorLens.parseOutput(output).findings;
  }

  static overall(parsed: DoctorParse, exitCode: number): CheckStatus {
    if (parsed.failed > 0 || parsed.findings.some((f) => f.status === "FAIL")) {
      return "FAIL";
    }
    // expo-doctor exits non-zero when at least one check failed; trust it even if
    // this build's output shape defeated the parser.
    if (exitCode !== 0) return "FAIL";
    if (parsed.findings.some((f) => f.status === "WARN")) return "WARN";
    return "PASS";
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
    parsed: DoctorParse,
    rootPath: string,
    overall: CheckStatus,
    exitCode: number
  ): string {
    const { total, passed, failed, findings } = parsed;
    const lines: string[] = [];
    lines.push(`# Expo Doctor Lens`);
    lines.push(``);
    lines.push(`**Project:** \`${rootPath}\``);
    lines.push(`**Overall:** ${overall} · **Exit code:** ${exitCode}`);
    if (total !== null && passed !== null) {
      lines.push(
        `**Checks:** ${passed}/${total} passed · ${failed} failed · ${findings.length} named in this output`
      );
    } else {
      lines.push(`**Checks named in this output:** ${findings.length}`);
    }
    lines.push(``);
    if (findings.length === 0) {
      if (total !== null && passed !== null) {
        lines.push(
          passed === total
            ? `_All ${total} checks passed. expo-doctor names individual checks only under \`--verbose\` — re-run with \`--mode verbose\` for the full table._`
            : `_The summary line reports ${failed} failure(s) but no check block was parsed; re-run with \`--mode verbose\`._`
        );
      } else {
        lines.push(
          `_No structured findings parsed from expo-doctor output. The tool may have failed early or emitted in an unrecognized format._`
        );
      }
      return lines.join("\n");
    }
    lines.push(`## Findings`);
    lines.push(``);
    lines.push(`| Status | Check | Message |`);
    lines.push(`|:---|:---|:---|`);
    for (const f of findings) {
      lines.push(
        `| ${f.status} | \`${f.check}\` | ${f.message.replace(/\|/g, "\\|")} |`
      );
    }
    return lines.join("\n");
  }
}
