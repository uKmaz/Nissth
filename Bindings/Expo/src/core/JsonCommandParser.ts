import Ajv2020, { type ValidateFunction } from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { findRepoRoot } from "./repoRoot";
import type { BridgeCommand } from "./types";
import { BridgeError } from "./BridgeError";

function loadSchema(): object {
  const repoRoot = findRepoRoot();
  const schemaPath = join(repoRoot, "Bindings", "_schemas", "bridge-command.schema.json");
  return JSON.parse(readFileSync(schemaPath, "utf8"));
}

export class JsonCommandParser {
  private readonly validate: ValidateFunction;

  constructor(schemaOverride?: object) {
    const ajv = new (Ajv2020 as unknown as new (opts: unknown) => Ajv2020)({
      allErrors: true,
      strict: false,
    });
    addFormats(ajv as unknown as Parameters<typeof addFormats>[0]);
    const schema = schemaOverride ?? loadSchema();
    this.validate = ajv.compile(schema);
  }

  parse(input: string | object, toolNameForError: string = "unknown"): BridgeCommand {
    let parsed: unknown;
    if (typeof input === "string") {
      try {
        parsed = JSON.parse(input);
      } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e);
        throw new BridgeError({
          stage: "parse",
          tool: toolNameForError,
          message: `Invalid JSON: ${msg}`,
        });
      }
    } else {
      parsed = input;
    }

    if (!this.validate(parsed)) {
      const errors = (this.validate.errors ?? [])
        .map((err) => `${err.instancePath || "/"} ${err.message ?? "validation failed"}`)
        .join("; ");
      const obj = (parsed ?? {}) as { tool?: string };
      throw new BridgeError({
        stage: "validate",
        tool: obj.tool ?? toolNameForError,
        message: `Schema validation failed: ${errors}`,
      });
    }

    return parsed as BridgeCommand;
  }
}
