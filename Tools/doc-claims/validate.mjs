#!/usr/bin/env node
/**
 * Nissth doc-claim validator.
 *
 * Checks repo-root prose against ground truth in Bindings/(stack)/*.bridge.json.
 *
 * Exists because HR#11's Doc Sync Mandate keys off DBL `covers` globs and plan
 * cross-references. Repo-root prose has neither, so nothing mechanically points
 * at README.md when a binding ships — and three binding-status claims plus one
 * fictional tool name (`index_drift`) survived seven phase closes.
 *
 * Zero runtime dependencies. Node 20+.
 *
 * Exit codes:  0 clean · 1 findings · 2 usage or config error
 */

import { readFileSync, readdirSync, existsSync, statSync } from "node:fs";
import { join, resolve, basename } from "node:path";

const DOCS = ["CLAUDE.md", "README.md", "AGENTS.md", "Ultimate_Guide.md"];

/** Phrases that assert a binding does not exist yet. */
const STALE_PHRASES = [
  "queued",
  "not yet authored",
  "not on disk",
  "not yet on disk",
  "in flight",
  "plan not yet authored",
];

/** A line only counts as a tool enumeration if it claims tools are shipped. */
const SHIP_VERBS = ["ships", "will ship", "delivers", "registers"];

const BACKTICKED_SNAKE = /`([a-z][a-z0-9]*(?:_[a-z0-9]+)+)`/g;

// ---------------------------------------------------------------- ground truth

/**
 * Read every binding manifest under <root>/Bindings.
 * Returns [{ id, dir, tools:Set<string>, manifestPath }]
 */
export function loadBindings(root) {
  const bindingsDir = join(root, "Bindings");
  if (!existsSync(bindingsDir)) return [];
  const out = [];
  for (const entry of readdirSync(bindingsDir)) {
    if (entry.startsWith("_") || entry.startsWith(".")) continue;
    const dir = join(bindingsDir, entry);
    let st;
    try {
      st = statSync(dir);
    } catch {
      continue;
    }
    if (!st.isDirectory()) continue;
    const manifest = readdirSync(dir).find((f) => f.endsWith(".bridge.json"));
    if (!manifest) continue; // no manifest => not a shipped binding
    const manifestPath = join(dir, manifest);
    let parsed;
    try {
      parsed = JSON.parse(readFileSync(manifestPath, "utf8"));
    } catch (err) {
      throw new ConfigError(`${manifestPath}: invalid JSON — ${err.message}`);
    }
    const tools = new Set(
      (parsed.tools ?? []).map((t) => (typeof t === "string" ? t : t.name))
    );
    out.push({
      id: parsed.binding ?? entry.toLowerCase(),
      dirName: entry,
      dir,
      tools,
      manifestPath,
    });
  }
  return out;
}

export class ConfigError extends Error {}

function loadAllowlist(toolDir) {
  const p = join(toolDir, "known-non-tools.json");
  if (!existsSync(p)) return new Map();
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(p, "utf8"));
  } catch (err) {
    throw new ConfigError(`${p}: invalid JSON — ${err.message}`);
  }
  return new Map(Object.entries(parsed.identifiers ?? {}));
}

// --------------------------------------------------------------------- checks

/**
 * Names a binding may be referred to by in prose: its manifest id, its
 * directory name, and a couple of well-known spellings.
 */
function bindingAliases(b) {
  const a = new Set([b.id, b.dirName, b.dirName.toLowerCase()]);
  if (b.dirName === "SpringBoot") a.add("Spring Boot");
  if (b.dirName === "Postgres") {
    a.add("PostgreSQL");
    a.add("Postgres");
  }
  return [...a];
}

/**
 * Plan filenames embed stack names (`Phase_05_Bridge_SpringBoot_FirstSlice.md`),
 * and a sentence about a *plan* is not a claim about the *binding*. CLAUDE.md
 * §11.12's "(not yet authored at the time §11 is written)" is the canonical
 * case: accurate historical prose about a plan, which a naive substring match
 * reads as "the Spring Boot binding does not exist".
 */
const PLAN_FILENAME = /`?Phase_\d+[A-Za-z0-9_]*(?:\.md)?`?/g;

function claimText(line) {
  return line.replace(PLAN_FILENAME, " ");
}

function mentionsBinding(line, b) {
  const lower = claimText(line).toLowerCase();
  return bindingAliases(b).some((n) => lower.includes(n.toLowerCase()));
}

/**
 * Inline suppression. Documentation legitimately quotes past defects — §12.1
 * of CLAUDE.md quotes the very "queued — plan not yet authored" line this tool
 * exists to catch. Rewording around the checker would make the prose worse, so
 * a line may be exempted by a marker on the line before it:
 *
 *   <!-- doc-claims:allow stale-binding-status — quoting a historical defect -->
 *
 * The check name is required (no blanket `*`), and everything after it is a
 * free-text reason. Like the allowlist, this is deliberate and auditable: it
 * shows up in the diff and names what is being waived and why.
 */
const SUPPRESS = /doc-claims:allow\s+([a-z-]+)/;

function suppressedFor(lines, index, check) {
  const prev = index > 0 ? lines[index - 1] : "";
  const m = prev.match(SUPPRESS);
  return Boolean(m && m[1] === check);
}

/** Check 1 — a shipped binding described as not existing yet. */
function checkStaleStatus(file, lines, bindings, findings) {
  lines.forEach((line, i) => {
    if (suppressedFor(lines, i, "stale-binding-status")) return;
    const lower = line.toLowerCase();
    const phrase = STALE_PHRASES.find((p) => lower.includes(p));
    if (!phrase) return;
    for (const b of bindings) {
      if (!mentionsBinding(line, b)) continue;
      findings.push({
        check: "stale-binding-status",
        file,
        line: i + 1,
        message:
          `"${phrase}" is claimed about the ${b.dirName} binding, but ` +
          `Bindings/${b.dirName}/ exists with a manifest (${basename(b.manifestPath)}). ` +
          `The binding ships.`,
      });
      break; // one finding per line is enough
    }
  });
}

/** Is this line enumerating a binding's tools? */
function isToolEnumeration(line, bindings) {
  const lower = line.toLowerCase();
  if (!SHIP_VERBS.some((v) => lower.includes(v))) return null;
  const owner = bindings.find((b) => mentionsBinding(line, b));
  if (!owner) return null;
  const ids = [...line.matchAll(BACKTICKED_SNAKE)].map((m) => m[1]);
  if (ids.length < 2) return null; // a single identifier is a mention, not a list
  return { owner, ids };
}

/** Check 2 — a fictional tool name inside a tool-enumeration line. */
function checkToolNames(file, lines, bindings, allowlist, findings) {
  const real = new Set();
  for (const b of bindings) for (const t of b.tools) real.add(t);
  lines.forEach((line, i) => {
    if (suppressedFor(lines, i, "fictional-tool")) return;
    const hit = isToolEnumeration(line, bindings);
    if (!hit) return;
    for (const id of hit.ids) {
      if (real.has(id) || allowlist.has(id)) continue;
      findings.push({
        check: "fictional-tool",
        file,
        line: i + 1,
        message:
          `\`${id}\` is presented as a shipped tool but appears in no binding manifest. ` +
          `If it is deliberately hypothetical, add it to Tools/doc-claims/known-non-tools.json with a reason.`,
      });
    }
  });
}

/** Check 3 — README stack-table tool count disagreeing with the manifest. */
function checkToolCounts(file, lines, bindings, findings) {
  lines.forEach((line, i) => {
    if (suppressedFor(lines, i, "tool-count-drift")) return;
    if (!line.startsWith("|")) return;
    for (const b of bindings) {
      if (!line.includes(`Bindings/${b.dirName}/`)) continue;
      const cells = line.split("|").map((c) => c.trim());
      const last = cells.filter(Boolean).pop();
      if (!/^\d+$/.test(last ?? "")) return;
      const claimed = Number(last);
      if (claimed !== b.tools.size) {
        findings.push({
          check: "tool-count-drift",
          file,
          line: i + 1,
          message:
            `table claims ${claimed} tools for ${b.dirName}, manifest registers ${b.tools.size}.`,
        });
      }
      return;
    }
  });
}

// ----------------------------------------------------------------------- main

export function validate(root, toolDir) {
  const bindings = loadBindings(root);
  if (bindings.length === 0) {
    throw new ConfigError(
      `no binding manifests found under ${join(root, "Bindings")} — ` +
        `is --root pointing at a Nissth repo?`
    );
  }
  const allowlist = loadAllowlist(toolDir);
  const findings = [];
  for (const doc of DOCS) {
    const p = join(root, doc);
    if (!existsSync(p)) continue; // not every tree carries every doc
    const lines = readFileSync(p, "utf8").split(/\r?\n/);
    checkStaleStatus(doc, lines, bindings, findings);
    checkToolNames(doc, lines, bindings, allowlist, findings);
    checkToolCounts(doc, lines, bindings, findings);
  }
  return { findings, bindings: bindings.map((b) => b.dirName) };
}

function main(argv) {
  const args = argv.slice(2);
  if (args.includes("--help") || args.includes("-h")) {
    process.stdout.write(
      "usage: validate.mjs [--root <path>] [--json]\n\n" +
        "Checks CLAUDE.md, README.md, AGENTS.md, Ultimate_Guide.md against the\n" +
        "binding manifests. Exit 0 clean, 1 findings, 2 usage/config error.\n"
    );
    return 0;
  }
  let root = process.cwd();
  const rootIdx = args.indexOf("--root");
  if (rootIdx !== -1) {
    if (!args[rootIdx + 1]) {
      process.stderr.write("error: --root requires a path\n");
      return 2;
    }
    root = resolve(args[rootIdx + 1]);
  }
  const toolDir = resolve(new URL(".", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"));

  let result;
  try {
    result = validate(root, toolDir);
  } catch (err) {
    if (err instanceof ConfigError) {
      process.stderr.write(`error: ${err.message}\n`);
      return 2;
    }
    throw err;
  }

  if (args.includes("--json")) {
    process.stdout.write(JSON.stringify(result, null, 2) + "\n");
    return result.findings.length ? 1 : 0;
  }

  if (result.findings.length === 0) {
    process.stdout.write(
      `doc-claims: no findings (checked ${DOCS.length} documents against ` +
        `${result.bindings.length} binding manifests: ${result.bindings.join(", ")})\n`
    );
    return 0;
  }
  for (const f of result.findings) {
    process.stdout.write(`${f.file}:${f.line}  [${f.check}]  ${f.message}\n`);
  }
  process.stdout.write(`\ndoc-claims: ${result.findings.length} finding(s)\n`);
  return 1;
}

const invokedDirectly =
  process.argv[1] &&
  resolve(process.argv[1]) === resolve(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"));
if (invokedDirectly) process.exit(main(process.argv));
