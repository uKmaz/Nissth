#!/usr/bin/env node

import { readFileSync } from "node:fs";
import { BridgeError } from "../core/BridgeError";
import { BindingManifest } from "../core/BindingManifest";
import { JsonCommandParser } from "../core/JsonCommandParser";
import { ToolDispatcher } from "../core/ToolDispatcher";
import type { BridgeCommand, ToolHandler } from "../core/types";
import { SchemaLens } from "../tools/SchemaLens";
import { QueryPlan } from "../tools/QueryPlan";
import { IndexAudit } from "../tools/IndexAudit";
import { LockAudit } from "../tools/LockAudit";
import { MigrationStatus } from "../tools/MigrationStatus";

async function main(argv: string[]): Promise<number> {
  // Discovery-mode flags short-circuit before any command parsing.
  if (argv.includes("--list-bindings")) {
    const manifest = BindingManifest.load();
    process.stdout.write(`${manifest.binding}\n`);
    return 0;
  }
  if (argv.includes("--list-tools")) {
    const manifest = BindingManifest.load();
    process.stdout.write(`${manifest.toolNames().join("\n")}\n`);
    return 0;
  }
  const descIdx = argv.indexOf("--describe");
  if (descIdx >= 0) {
    const toolName = argv[descIdx + 1];
    if (!toolName) {
      process.stderr.write("--describe requires a tool name\n");
      return 2;
    }
    const manifest = BindingManifest.load();
    const tool = manifest.getTool(toolName);
    if (!tool) {
      process.stderr.write(`Unknown tool: ${toolName}\n`);
      return 4;
    }
    process.stdout.write(JSON.stringify(tool, null, 2));
    process.stdout.write("\n");
    const extraDoc = manifest.raw.scope_extra_keys_doc;
    if (extraDoc) {
      process.stdout.write(`\nBinding-wide scope.extra docs:\n`);
      process.stdout.write(JSON.stringify(extraDoc, null, 2));
      process.stdout.write("\n");
    }
    return 0;
  }

  // Build BridgeCommand
  const parser = new JsonCommandParser();
  let cmd: BridgeCommand;
  if (argv.includes("--json-stdin")) {
    const stdinText = readFileSync(0, "utf8");
    cmd = parser.parse(stdinText);
  } else {
    const flagCmd = parseFlagForm(argv);
    cmd = parser.parse(flagCmd);
  }

  // Wire dispatcher
  const manifest = BindingManifest.load();
  const dispatcher = new ToolDispatcher();
  const handlers: ToolHandler[] = [
    new SchemaLens(manifest.bindingVersion),
    new QueryPlan(manifest.bindingVersion),
    new IndexAudit(manifest.bindingVersion),
    new LockAudit(manifest.bindingVersion),
    new MigrationStatus(manifest.bindingVersion),
  ];
  for (const h of handlers) dispatcher.register(h);

  const result = await dispatcher.dispatch(cmd);
  const destination = cmd.output?.destination ?? "file";
  if (destination === "return") {
    process.stdout.write(readFileSync(result.reportPath, "utf8"));
  } else if (destination === "console") {
    process.stdout.write(readFileSync(result.reportPath, "utf8"));
    process.stdout.write(`\n${result.reportPath}\n`);
  } else {
    process.stdout.write(`${result.reportPath}\n`);
  }
  return 0;
}

function parseFlagForm(argv: string[]): BridgeCommand {
  const positional: string[] = [];
  const flags: Record<string, string | boolean> = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith("--")) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (next === undefined || next.startsWith("--")) {
        flags[key] = true;
      } else {
        flags[key] = next;
        i++;
      }
    } else {
      positional.push(a);
    }
  }
  if (positional.length === 0) {
    throw new BridgeError({
      stage: "parse",
      tool: "unknown",
      message:
        "Usage: nissth-bridge <tool> [--mode <m>] [--scope.<key> <value>] [--output.<key> <value>] OR nissth-bridge --json-stdin",
    });
  }
  const cmd: Record<string, unknown> = { tool: positional[0] };
  if (typeof flags.mode === "string") cmd.mode = flags.mode;
  if (typeof flags.context_id === "string") cmd.context_id = flags.context_id;
  const scope: Record<string, unknown> = {};
  const extra: Record<string, unknown> = {};
  const output: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(flags)) {
    if (k === "mode" || k === "context_id") continue;
    if (k.startsWith("scope.extra.")) {
      extra[k.slice("scope.extra.".length)] = parseValue(v);
    } else if (k.startsWith("scope.")) {
      scope[k.slice("scope.".length)] = parseValue(v);
    } else if (k.startsWith("output.")) {
      output[k.slice("output.".length)] = parseValue(v);
    }
  }
  if (Object.keys(extra).length > 0) scope.extra = extra;
  if (Object.keys(scope).length > 0) cmd.scope = scope;
  if (Object.keys(output).length > 0) cmd.output = output;
  return cmd as unknown as BridgeCommand;
}

function parseValue(v: string | boolean): unknown {
  if (typeof v === "boolean") return v;
  if (v === "true") return true;
  if (v === "false") return false;
  if (/^-?\d+$/.test(v)) return parseInt(v, 10);
  if (/^-?\d+\.\d+$/.test(v)) return parseFloat(v);
  // JSON array / object detection (for scope.extra.params and similar)
  if ((v.startsWith("[") && v.endsWith("]")) || (v.startsWith("{") && v.endsWith("}"))) {
    try {
      return JSON.parse(v);
    } catch {
      // fall through to string
    }
  }
  return v;
}

main(process.argv.slice(2))
  .then((exitCode) => process.exit(exitCode))
  .catch((err: unknown) => {
    if (err instanceof BridgeError) {
      process.stderr.write(JSON.stringify(err.toPayload()) + "\n");
      process.exit(err.exitCode());
    }
    const msg = err instanceof Error ? err.message : String(err);
    process.stderr.write(`error: ${msg}\n`);
    process.exit(1);
  });
