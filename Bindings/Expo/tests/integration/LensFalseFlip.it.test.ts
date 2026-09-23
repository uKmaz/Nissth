// Phase 19 regression suite: the two lenses used to STALE-flip DBL artifacts that had not
// drifted at all. Both cases below come from a real consumer run (FinansYönetimApp,
// 2026-09-21: nine artifacts flipped, zero real drift) and both fail on the pre-Phase-19 code.
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildDispatcher, copyFixtureToTmp, readDBLFrontmatter } from "./_support";
import { ComponentLens } from "../../src/tools/ComponentLens";
import { RouteLens } from "../../src/tools/RouteLens";

function write(root: string, rel: string, content: string): string {
  const full = join(root, rel);
  mkdirSync(join(full, ".."), { recursive: true });
  writeFileSync(full, content, "utf8");
  return full;
}

const MONEY_TEXT = `import { Text } from "react-native";

export interface MoneyTextProps {
  minor: number;
}

export function MoneyText({ minor }: MoneyTextProps) {
  return <Text>{minor}</Text>;
}
`;

const SPACER = `import { View } from "react-native";

export function Spacer() {
  return <View />;
}
`;

const BOOT_PROVIDER = `import { createContext } from "react";
import { View } from "react-native";

export function BootProvider({ children }: { children: unknown }) {
  return <View>{children as never}</View>;
}
`;

function summary(name: string, covers: string[], body: string): string {
  return [
    "---",
    "artifact_type: summary",
    `name: ${name}`,
    "last_regenerated: 2026-09-01 by a human",
    "source_state: git abc1234",
    "covers:",
    ...covers.map((c) => `  - ${c}`),
    "stale_when:",
    "  - the module's public surface changes",
    "---",
    "",
    body,
    "",
  ].join("\n");
}

describe("lens false flips (Phase 19)", () => {
  let tmpRoot: string;
  let cleanup: () => void;

  beforeEach(() => {
    ({ tmpRoot, cleanup } = copyFixtureToTmp("nissth-flip-"));
  });

  afterEach(() => {
    cleanup();
  });

  describe("component_lens", () => {
    beforeEach(() => {
      write(tmpRoot, "src/ui/MoneyText.tsx", MONEY_TEXT);
      write(tmpRoot, "src/domain/ledger/totals.ts", "export const total = (a: number) => a;\n");
      write(
        tmpRoot,
        "DBL/Summaries/ui.md",
        summary("src/ui — presentational components", ["src/ui/**"], "| `MoneyText` | formats kuruş |")
      );
      write(
        tmpRoot,
        "DBL/Summaries/domain-ledger.md",
        summary(
          "src/domain/ledger — pure engine",
          ["src/domain/ledger/**"],
          "Pure functions only. No React component lives here."
        )
      );
    });

    async function run(): Promise<void> {
      const { dispatcher } = buildDispatcher(tmpRoot, (writer, flipper) => [
        new ComponentLens(writer, flipper, tmpRoot),
      ]);
      await dispatcher.dispatch({
        tool: "component_lens",
        scope: { root_path: tmpRoot, package: "src" },
      });
    }

    it("does NOT flip an artifact that covers no component file", async () => {
      await run();
      const fm = readDBLFrontmatter(join(tmpRoot, "DBL", "Summaries", "domain-ledger.md"));
      expect(String(fm!.last_regenerated)).toBe("2026-09-01 by a human");
    });

    it("does NOT flip an artifact that documents the component it covers", async () => {
      await run();
      const fm = readDBLFrontmatter(join(tmpRoot, "DBL", "Summaries", "ui.md"));
      expect(String(fm!.last_regenerated)).toBe("2026-09-01 by a human");
    });

    it("documents the defect: the pre-Phase-19 global comparison flagged it", () => {
      // The old caller passed the whole scan's names to every artifact. `detectDrift` then
      // reported drift for an artifact that documents no component at all — which is how a
      // consumer collected eight false flips in one run. The fix is in the caller: narrowed
      // to this artifact's `covers`, the live set is empty and `flipIfStale` never asks.
      const wholeTree = new Set(["MoneyText", "Greeting"]);
      expect(
        ComponentLens.detectDrift("Pure functions only. No React component lives here.", wholeTree)
      ).toBe(true);
      expect(ComponentLens.detectDrift("Pure functions only.", new Set<string>())).toBe(false);
    });

    it("still flips when a NEW component under its covers is undocumented", async () => {
      // ui.md keeps its MoneyText row; a second component appears next to it and is not listed.
      write(tmpRoot, "src/ui/Spacer.tsx", SPACER);
      await run();
      const flipped = readDBLFrontmatter(join(tmpRoot, "DBL", "Summaries", "ui.md"));
      expect(String(flipped!.last_regenerated)).toMatch(/^STALE — superseded by/);
      const untouched = readDBLFrontmatter(join(tmpRoot, "DBL", "Summaries", "domain-ledger.md"));
      expect(String(untouched!.last_regenerated)).toBe("2026-09-01 by a human");
    });

    it("does NOT flip a whole-tree catch-all artifact (the layout map)", async () => {
      // `covers: src/**` at the scan root is the project's layout artifact, not the ui
      // inventory — even though it does name some component FILES in its directory tree.
      write(
        tmpRoot,
        "DBL/Summaries/_layout.md",
        summary(
          "repository layout",
          ["src/**", "app/**"],
          "Tree: `src/ui/MoneyText.tsx`, `src/ui/AmountInput.tsx`, `src/domain/ledger/`."
        )
      );
      write(tmpRoot, "src/ui/Spacer.tsx", SPACER);
      await run();
      const fm = readDBLFrontmatter(join(tmpRoot, "DBL", "Summaries", "_layout.md"));
      expect(String(fm!.last_regenerated)).toBe("2026-09-01 by a human");
    });

    it("does NOT flip an artifact that documents none of the components it covers", async () => {
      // features-hooks.md covers src/features/** and is about hooks; a provider component
      // living there does not make it a component inventory.
      write(tmpRoot, "src/features/boot.tsx", BOOT_PROVIDER);
      write(
        tmpRoot,
        "DBL/Summaries/features-hooks.md",
        summary("src/features — hooks", ["src/features/**"], "| `useLedgerVersion` | version key |")
      );
      await run();
      const fm = readDBLFrontmatter(join(tmpRoot, "DBL", "Summaries", "features-hooks.md"));
      expect(String(fm!.last_regenerated)).toBe("2026-09-01 by a human");
    });
  });

  describe("route_lens", () => {
    it("does NOT flip a route table written in `:id` notation", async () => {
      write(
        tmpRoot,
        "app/user/[id].tsx",
        "export default function UserScreen() {\n  return null;\n}\n"
      );
      write(
        tmpRoot,
        "DBL/APIIndex/routes.md",
        [
          "---",
          "artifact_type: api_index",
          "name: routes",
          "last_regenerated: 2026-09-01 by a human",
          "source_state: git abc1234",
          "covers:",
          "  - app/",
          "stale_when:",
          "  - a route file is added, removed or renamed",
          "---",
          "",
          "| URL path | File | Component | Params | Layout parent | Classification |",
          "|:---|:---|:---|:---|:---|:---|",
          "| `/` | `app/index.tsx` | `Index` | `—` | `_layout.tsx` | static |",
          "| `/user/:id` | `app/user/[id].tsx` | `UserScreen` | `{ id: string }` | `_layout.tsx` | dynamic |",
          "",
        ].join("\n")
      );

      const { dispatcher } = buildDispatcher(tmpRoot, (writer, flipper) => [
        new RouteLens(writer, flipper, tmpRoot),
      ]);
      await dispatcher.dispatch({ tool: "route_lens", scope: { root_path: tmpRoot } });

      const fm = readDBLFrontmatter(join(tmpRoot, "DBL", "APIIndex", "routes.md"));
      expect(String(fm!.last_regenerated)).toBe("2026-09-01 by a human");
    });

    it("canonicalRoute maps both notations onto the same key", () => {
      expect(RouteLens.canonicalRoute("/account/[id]")).toBe(RouteLens.canonicalRoute("/account/:id"));
      expect(RouteLens.canonicalRoute("/[...rest]")).toBe(RouteLens.canonicalRoute("/:rest*"));
      expect(RouteLens.canonicalRoute("/settings/")).toBe("/settings");
      expect(RouteLens.canonicalRoute("/a/[id]")).not.toBe(RouteLens.canonicalRoute("/a/b"));
    });
  });

  it("the flipped file keeps every other byte, including long unwrapped lines", async () => {
    const longName =
      "src/domain/ledger — balance engine v2, statements, totals, valuation, reconcile, debts (L1)";
    const artifact = write(
      tmpRoot,
      "DBL/Summaries/long-lines.md",
      summary(longName, ["src/**"], "No component here at all.")
    );
    const before = readFileSync(artifact, "utf8");

    const ok = require("../../src/core/StaleFlipper").StaleFlipper.setLastRegenerated(
      artifact,
      "STALE — superseded by AgentReports/Bridge/component_lens_x.md"
    );
    expect(ok).toBe(true);

    const after = readFileSync(artifact, "utf8");
    const strip = (s: string): string =>
      s
        .split("\n")
        .filter((l) => !l.startsWith("last_regenerated:"))
        .join("\n");
    expect(strip(after)).toBe(strip(before));
    expect(after).toContain(`name: ${longName}`); // not re-wrapped at 80 columns
    expect(after).toContain(
      "last_regenerated: STALE — superseded by AgentReports/Bridge/component_lens_x.md"
    );
  });
});
