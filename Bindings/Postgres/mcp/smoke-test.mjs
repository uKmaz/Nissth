// Standalone MCP smoke test for the Nissth Bridge postgres shim.
// Spawns ./index.js over stdio and exercises all four registered tools.
//
// With NISSTH_PG_URL set: exercises tools end-to-end against the live database.
// Without NISSTH_PG_URL: verifies the graceful-failure path (no_connection_string).
// Either is a PASS — the load-bearing checks are MCP protocol surface integrity.
//
// Run from this directory: node smoke-test.mjs

import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { fileURLToPath } from "node:url";
import path from "node:path";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const serverScript = path.resolve(__dirname, "index.js");
const hasLivePg = Boolean(process.env.NISSTH_PG_URL);

function header(s) { console.log(`\n=== ${s} ===`); }
function snip(text, n = 600) {
  if (typeof text !== "string") return JSON.stringify(text).slice(0, n);
  return text.length > n ? text.slice(0, n) + `\n... [truncated, total ${text.length} chars]` : text;
}

const transport = new StdioClientTransport({
  command: process.execPath,
  args: [serverScript],
});

const client = new Client({ name: "nissth-postgres-smoke", version: "0.0.1" }, { capabilities: {} });

let failures = 0;
console.log(`Mode: ${hasLivePg ? "LIVE (NISSTH_PG_URL set)" : "OFFLINE (no NISSTH_PG_URL — testing graceful failure)"}`);

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
  if (status.isError) {
    failures++;
    console.log("FAIL: Nissth_Status should never error.");
  } else {
    console.log("PASS: Nissth_Status returned health probe.");
  }

  header("Nissth_Gateway / schema_lens");
  const gw = await client.callTool({
    name: "Nissth_Gateway",
    arguments: {
      command: {
        tool: "schema_lens",
        mode: "tables",
        scope: { package: "public" },
        output: { destination: "file" },
      },
    },
  });
  console.log(`isError=${gw.isError === true}`);
  console.log(snip(gw.content?.[0]?.text));
  if (hasLivePg) {
    if (gw.isError) {
      failures++;
      console.log("FAIL: schema_lens errored with NISSTH_PG_URL set.");
    } else {
      console.log("PASS: schema_lens returned a report.");
    }
  } else {
    const text = gw.content?.[0]?.text ?? "";
    if (gw.isError && text.includes("no_connection_string")) {
      console.log("PASS: graceful no_connection_string error as expected.");
    } else {
      failures++;
      console.log("FAIL: expected no_connection_string error in offline mode.");
    }
  }

  if (hasLivePg) {
    header("Nissth_ReadReport / latest:schema_lens");
    const rr = await client.callTool({
      name: "Nissth_ReadReport",
      arguments: { relativePath: "latest:schema_lens", maxChars: 800 },
    });
    console.log(`isError=${rr.isError === true}`);
    console.log(snip(rr.content?.[0]?.text));
    if (rr.isError) failures++;

    header("Nissth_Verify / schema");
    const ver = await client.callTool({
      name: "Nissth_Verify",
      arguments: { operation: "schema" },
    });
    console.log(`isError=${ver.isError === true}`);
    console.log(snip(ver.content?.[0]?.text, 400));
    if (ver.isError) failures++;
  } else {
    header("Nissth_Verify / migrations (offline-mode failure check)");
    const ver = await client.callTool({
      name: "Nissth_Verify",
      arguments: { operation: "migrations" },
    });
    console.log(`isError=${ver.isError === true}`);
    const text = ver.content?.[0]?.text ?? "";
    if (ver.isError && text.includes("no_connection_string")) {
      console.log("PASS: graceful no_connection_string error.");
    } else {
      failures++;
      console.log("FAIL: expected no_connection_string error in offline mode.");
    }
  }

} catch (err) {
  failures++;
  console.error("FATAL:", err?.stack || err);
} finally {
  try { await client.close(); } catch {}
  header("Summary");
  console.log(failures === 0 ? "ALL CHECKS PASSED" : `FAILURES: ${failures}`);
  process.exit(failures === 0 ? 0 : 1);
}
