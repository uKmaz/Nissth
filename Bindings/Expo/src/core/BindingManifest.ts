import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import type { BindingManifestData, BindingManifestEntry } from "./types";

const MANIFEST_FILE = "expo.bridge.json";

export class BindingManifest {
  private readonly data: BindingManifestData;

  constructor(data: BindingManifestData) {
    this.data = data;
  }

  /**
   * Load the manifest from `expo.bridge.json` at the binding root.
   * Resolution: walks up from __dirname until a directory containing the manifest file is found.
   */
  static load(explicitPath?: string): BindingManifest {
    const path = explicitPath ?? BindingManifest.findManifestPath();
    const text = readFileSync(path, "utf8");
    const data = JSON.parse(text) as BindingManifestData;
    return new BindingManifest(data);
  }

  private static findManifestPath(): string {
    let dir = __dirname;
    for (let i = 0; i < 8; i++) {
      const candidate = resolve(dir, MANIFEST_FILE);
      try {
        readFileSync(candidate);
        return candidate;
      } catch {
        // not here, walk up
      }
      const parent = dirname(dir);
      if (parent === dir) break;
      dir = parent;
    }
    throw new Error(
      `Could not locate ${MANIFEST_FILE} in any ancestor of ${__dirname}`
    );
  }

  get binding(): string {
    return this.data.binding;
  }
  get bindingVersion(): string {
    return this.data.binding_version;
  }
  get contractVersion(): number {
    return this.data.contract_version;
  }
  get tools(): BindingManifestEntry[] {
    return this.data.tools;
  }
  get raw(): BindingManifestData {
    return this.data;
  }

  getTool(name: string): BindingManifestEntry | undefined {
    return this.data.tools.find((t) => t.name === name);
  }

  toolNames(): string[] {
    return this.data.tools.map((t) => t.name);
  }
}
