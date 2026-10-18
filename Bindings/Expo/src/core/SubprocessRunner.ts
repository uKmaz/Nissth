import { spawn } from "node:child_process";

export interface SubprocessResult {
  exitCode: number;
  stdout: string;
  stderr: string;
}

export interface SubprocessRunner {
  run(
    command: string,
    args: string[],
    cwd: string,
    env?: NodeJS.ProcessEnv
  ): Promise<SubprocessResult>;
}

export class DefaultSubprocessRunner implements SubprocessRunner {
  async run(
    command: string,
    args: string[],
    cwd: string,
    env?: NodeJS.ProcessEnv
  ): Promise<SubprocessResult> {
    return new Promise((resolvePromise, rejectPromise) => {
      const proc = spawn(command, args, {
        cwd,
        env: env ?? process.env,
        shell: process.platform === "win32",
        windowsHide: true,
      });
      let stdout = "";
      let stderr = "";
      proc.stdout.on("data", (d: Buffer) => {
        stdout += d.toString();
      });
      proc.stderr.on("data", (d: Buffer) => {
        stderr += d.toString();
      });
      proc.on("error", (err) => rejectPromise(err));
      proc.on("close", (code) => {
        resolvePromise({ exitCode: code ?? -1, stdout, stderr });
      });
    });
  }
}
