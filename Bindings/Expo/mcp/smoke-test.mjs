// Standalone MCP smoke test for the Nissth Bridge expo shim.
// Spawns ./index.js over stdio and exercises all four registered tools
// against the in-repo fixture (Bindings/Expo/tests/fixture/).
// Run from this directory: node smoke-test.mjs

import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { fileURLToPath } from "node:url";
import path from "node:path";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const serverScript = path.resolve(__dirname, "index.js");
const fixtureRoot = path.resolve(__dirname, "..", "tests", "fixture");

function header(s) { console.log(`\n=== ${s} ===`); }
function snip(text, n = 600) {
  if (typeof text !== "string") return JSON.stringify(text).slice(0, n);
  return text.length > n ? text.slice(0, n) + `\n... [truncated, total ${text.length} chars]` : text;
}

const transport = new StdioClientTransport({
  command: process.execPath,
  args: [serverScript],
});

const client = new Client({ name: "nissth-expo-smoke", version: "0.0.1" }, { capabilities: {} });

let failures = 0;

try {
  header("Connecting to shim");
  await client.connect(transport);
  console.log("connected.");

  header("tools/list");
  const tools = await client.listTools();
  console.log(`registered tools: ${tools.tools.map(t => t.name).join(", ")}`);
  const expected = ["Nissth_Gateway", "Nissth_Verify", "Nissth_ReadReport", "Nissth_Status"];
  const got = tools.tools.map(t => t.name).sort();
  if (JSON.stringify(got) !== JSON.stringify(expected.sort())) {
    failures++;
    console.log(`FAIL: expected ${expected.sort()}, got ${got}`);
  } else {
    console.log("PASS: all four tools registered.");
  }

  header("Nissth_Status");
  const status = await client.callTool({ name: "Nissth_Status", arguments: { recent: 5 } });
  console.log(`isError=${status.isError === true}`);
  console.log(snip(status.content?.[0]?.text));
  if (status.isError) failures++;

  header("Nissth_Gateway / route_lens against fixture");
  const gw = await client.callTool({
    name: "Nissth_Gateway",
    arguments: {
      command: {
        tool: "route_lens",
        scope: { root_path: fixtureRoot },
        output: { destination: "file" },
      },
    },
  });
  console.log(`isError=${gw.isError === true}`);
  console.log(snip(gw.content?.[0]?.text));
  if (gw.isError) failures++;

  header("Nissth_ReadReport / latest:route_lens");
  const rr = await client.callTool({
    name: "Nissth_ReadReport",
    arguments: { relativePath: "latest:route_lens", maxChars: 800 },
  });
  console.log(`isError=${rr.isError === true}`);
  console.log(snip(rr.content?.[0]?.text));
  if (rr.isError) failures++;

  header("Nissth_Verify / dependencies against fixture");
  const ver = await client.callTool({
    name: "Nissth_Verify",
    arguments: {
      operation: "dependencies",
      root_path: fixtureRoot,
    },
  });
  console.log(`isError=${ver.isError === true}`);
  console.log(snip(ver.content?.[0]?.text, 400));
  if (ver.isError) failures++;

} catch (err) {
  failures++;
  console.error("FATAL:", err?.stack || err);
} finally {
  try { await client.close(); } catch {}
  header("Summary");
  console.log(failures === 0 ? "ALL CHECKS PASSED" : `FAILURES: ${failures}`);
  process.exit(failures === 0 ? 0 : 1);
}
