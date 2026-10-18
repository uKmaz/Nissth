#!/usr/bin/env node
// dbl-regen — turn "this DBL artifact is stale" into something you can act on.
//
//   node Tools/dbl-regen/regen.mjs [--root <dir>]                  # what needs regenerating, and why
//   node Tools/dbl-regen/regen.mjs --artifact DBL/Summaries/x.md   # the worksheet for one
//   node Tools/dbl-regen/regen.mjs --all                           # every worksheet
//   node Tools/dbl-regen/regen.mjs --stamp DBL/Summaries/x.md      # record that you rewrote it
//
// Exit 0 nothing to do / worksheet printed · 1 artifacts need regeneration · 2 usage.
//
// WHAT THIS DOES NOT DO: write an artifact's body. A Summary's gotchas and a
// DependencyMap's reasons are judgment, and a tool that generated them would be
// producing confident text nobody verified — the exact failure DBL exists to
// prevent. It does the mechanical half: which artifacts are due, what changed
// under their `covers`, what is there now, which stack lens answers the surface
// question, and the two frontmatter lines to write when the body is done.

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { check, parseFrontmatter, globToRegExp, sourceRef, refless } from "../dbl-check/check.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));

/**
 * Today, in the **local** calendar — never `toISOString()`, which is UTC.
 * A stamp written at 01:00 local on the 25th would otherwise read the 24th, and
 * every other date in this framework (ledger entries, `last_regenerated`, HR#8's
 * ISO conversion) is local. Found the first time the tool was used in anger, at
 * 01:05 local, when it stamped an artifact with yesterday.
 */
const pad2 = (n) => String(n).padStart(2, "0");
export function localDate(d = new Date()) {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
}

export class RegenError extends Error {
  constructor(code, message) {
    super(message);
    this.code = code;
  }
}

const git = (root, args, quiet = true) => {
  try {
    return execFileSync("git", ["-C", root, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", quiet ? "ignore" : "inherit"], maxBuffer: 32 * 1024 * 1024 }).trim();
  } catch {
    return null;
  }
};

/** The lens that answers "what is the public surface here", per artifact type. */
const LENS = {
  api_index: "`nissth-bridge route_lens` (Expo) or `endpoint_lens` (Spring Boot) — then reconcile the table",
  schema_index: "`nissth-bridge schema_lens --mode full` (Postgres) or `entity_lens` (Spring Boot)",
  summary: "`nissth-bridge component_lens` (Expo) for a component grouping; otherwise read the module's exports",
  dependency_map: "the project's own boundary enforcement (an ESLint `no-restricted-imports` block, a layer test) — and `nissth-bridge dependency_audit` for the package layer",
};

// ------------------------------------------------------------- inventory

function walk(dir, root, out = []) {
  const SKIP = new Set([".git", "node_modules", ".expo", "dist", "build", "target", "coverage", "bin", "obj"]);
  let entries;
  try {
    entries = readdirSync(dir);
  } catch {
    return out;
  }
  for (const e of entries) {
    if (SKIP.has(e)) continue;
    const abs = join(dir, e);
    let st;
    try {
      st = statSync(abs);
    } catch {
      continue;
    }
    if (st.isDirectory()) walk(abs, root, out);
    else out.push(abs.slice(root.length + 1).replace(/\\/g, "/"));
  }
  return out;
}

/** Files under the artifact's `covers`, as they are right now. */
export function inventory(root, covers) {
  const all = walk(root, root);
  const res = [];
  for (const f of all) {
    if (f.startsWith("DBL/")) continue;
    if (covers.some((g) => globToRegExp(g).test(f) || globToRegExp(g.endsWith("/**") ? g : `${g}/**`).test(f))) res.push(f);
  }
  return res.sort();
}


/**
 * Git refs the artifact's **body** cites, other than the one in its frontmatter.
 *
 * A body that says "Tree (as of M3, `ae15849`)" is making a currency claim of its
 * own, and `stale_when` does not have to mention it. One consumer's layout map
 * answered "no" to all three of its `stale_when` questions while its tree was two
 * milestones behind and its test counts were 38/252 against an actual 51/397 —
 * the conditions were narrower than what the body asserted. This surfaces the
 * mismatch instead of trusting the conditions to be complete.
 */
export function bodyRefs(text, frontmatterRef) {
  const NL = "\n";
  const body = text.replace(/^---[\s\S]*?\r?\n---\r?\n/, "");
  const out = new Map();
  for (const m of body.matchAll(/`([0-9a-f]{7,40})`/g)) {
    const ref = m[1];
    if (!/\d/.test(ref)) continue;
    if (frontmatterRef && (ref.startsWith(frontmatterRef) || frontmatterRef.startsWith(ref))) continue;
    const line = body.slice(0, m.index).split(NL).length;
    if (!out.has(ref)) out.set(ref, body.split(NL)[line - 1].trim().slice(0, 120));
  }
  return [...out].map(([ref, context]) => ({ ref, context }));
}

// ------------------------------------------------------------- the list

/** Every artifact with a reason to be regenerated, and why. */
export function due(root) {
  const abs = resolve(root);
  const result = check(abs);
  const byFile = new Map();
  for (const f of result.findings) {
    if (!["covers-changed-since", "stale-marked", "design-only-source-exists", "unrecognised-source-state"].includes(f.check)) continue;
    (byFile.get(f.file) ?? byFile.set(f.file, []).get(f.file)).push(f);
  }
  return [...byFile].map(([rel, findings]) => ({ rel, reasons: findings.map((f) => ({ check: f.check, message: f.message })) }));
}

// ------------------------------------------------------------- the worksheet

export function worksheet(root, rel) {
  const abs = resolve(root);
  const file = join(abs, rel);
  if (!existsSync(file)) throw new RegenError("no_artifact", `no such artifact: ${rel}`);
  const text = readFileSync(file, "utf8");
  const fm = parseFrontmatter(text);
  if (!fm) throw new RegenError("no_frontmatter", `${rel} has no frontmatter block (§7.2)`);
  const d = fm.data;
  const covers = Array.isArray(d.covers) ? d.covers : typeof d.covers === "string" ? [d.covers] : [];
  const staleWhen = Array.isArray(d.stale_when) ? d.stale_when : typeof d.stale_when === "string" ? [d.stale_when] : [];
  const ref = sourceRef(d.source_state);
  const head = git(abs, ["rev-parse", "--short", "HEAD"]);

  let changed = null;
  if (ref) {
    const out = git(abs, ["diff", "--name-status", ref, "--", ...covers.map((c) => `:(glob)${c.replace(/\\/g, "/")}`)]);
    changed = out === null ? null : out.split("\n").filter(Boolean).map((l) => {
      const [status, ...rest] = l.split(/\t/);
      return { status: status[0], path: rest.join("\t") };
    });
  }
  const files = inventory(abs, covers);
  const today = localDate();

  const L = [];
  L.push(`# Regeneration worksheet — \`${rel}\``);
  L.push("");
  L.push(`**Type:** ${d.artifact_type ?? "?"} · **Name:** ${d.name ?? "?"}`);
  L.push(`**covers:** ${covers.map((c) => `\`${c}\``).join(", ") || "(none)"}`);
  L.push(`**source_state:** \`${d.source_state ?? "(none)"}\`${ref ? ` → ref \`${ref}\`` : " — no git ref in it"}`);
  L.push(`**HEAD:** ${head ? `\`${head}\`` : "(not a git work tree)"}`);
  L.push("");

  L.push("## What changed under `covers`");
  L.push("");
  if (changed === null) L.push(ref ? "_git could not answer (unknown ref, or not a work tree)._" : "_No git ref in `source_state`, so there is nothing to diff against. Record the commit this artifact is written against when you stamp it._");
  else if (!changed.length) L.push("_Nothing. If this artifact is due for another reason, it is listed below._");
  else {
    L.push(`${changed.length} file(s): ${changed.filter((c) => c.status === "A").length} added, ${changed.filter((c) => c.status === "M").length} modified, ${changed.filter((c) => c.status === "D").length} deleted.`);
    L.push("");
    L.push("| | File |");
    L.push("|:--|:--|");
    for (const c of changed.slice(0, 60)) L.push(`| ${c.status} | \`${c.path}\` |`);
    if (changed.length > 60) L.push(`| … | _${changed.length - 60} more_ |`);
  }
  L.push("");

  L.push("## What is under `covers` now");
  L.push("");
  L.push(`${files.length} file(s).`);
  if (files.length) {
    L.push("");
    L.push("```");
    for (const f of files.slice(0, 80)) L.push(f);
    if (files.length > 80) L.push(`… ${files.length - 80} more`);
    L.push("```");
  }
  L.push("");

  const refs = bodyRefs(text, ref);
  if (refs.length) {
    L.push("## Refs the body cites, other than `source_state`");
    L.push("");
    L.push("An \"as of `<ref>`\" marker in the body is its own currency claim, and `stale_when` does not have to mention it — check each against HEAD before trusting the text around it.");
    L.push("");
    for (const r of refs) L.push(`- \`${r.ref}\` — ${r.context}`);
    L.push("");
  }

  L.push("## Answer this artifact's own `stale_when`");
  L.push("");
  if (!staleWhen.length) L.push("_The artifact states no `stale_when` conditions, which §7.2 requires._");
  for (const c of staleWhen) L.push(`- [ ] ${c}`);
  L.push("");

  const lens = LENS[d.artifact_type];
  if (lens) {
    L.push("## Surface");
    L.push("");
    L.push(`For the inventory half, ${lens}.`);
    L.push("");
  }

  L.push("## When the body is rewritten");
  L.push("");
  L.push("```yaml");
  L.push(`last_regenerated: ${today} by <you>`);
  L.push(`source_state: ${head ?? "<commit>"}`);
  L.push("```");
  L.push("");
  L.push(`Or let the tool write exactly those two lines: \`node Tools/dbl-regen/regen.mjs --root . --stamp ${rel}\` (it refuses while the body is untouched).`);
  return L.join("\n");
}

// ------------------------------------------------------------- the stamp

/**
 * Rewrite `last_regenerated` and `source_state`, one line each, preserving every
 * other byte — the Phase 19 lesson: a whole-block YAML re-serialise re-wraps long
 * lines and hands `dbl-check` a `bad-frontmatter` on a file the tooling just wrote.
 *
 * Refuses while the artifact is unmodified in the working tree. Stamping records
 * that a human rewrote the body; if nothing was rewritten there is nothing to
 * record, and a stamp on a stale body is worse than the stale stamp it replaces.
 */
export function stamp(root, rel, opts = {}) {
  const abs = resolve(root);
  const file = join(abs, rel);
  if (!existsSync(file)) throw new RegenError("no_artifact", `no such artifact: ${rel}`);
  const head = git(abs, ["rev-parse", "--short", "HEAD"]);
  if (!head) throw new RegenError("no_git", `${abs} is not a git work tree, so there is no commit to record`);
  if (!opts.force) {
    const status = git(abs, ["status", "--porcelain", "--", rel]);
    if (status === null) throw new RegenError("no_git", "git could not report the working-tree status");
    if (!status.trim()) {
      throw new RegenError("body_unchanged", `${rel} is unmodified in the working tree — stamping records that you rewrote the body, and nothing has been rewritten. Regenerate it first (\`--artifact ${rel}\` prints the worksheet).`);
    }
  }
  const text = readFileSync(file, "utf8");
  const who = opts.by ?? "agent";
  const today = localDate();
  const setLine = (src, key, value) => {
    const re = new RegExp(`^(${key}:)[ \\t]*.*$`, "m");
    if (!re.test(src)) throw new RegenError("missing_key", `${rel} has no \`${key}:\` line to stamp`);
    let seen = false;
    return src.replace(re, (m, p1) => {
      if (seen) return m;
      seen = true;
      return `${p1} ${value}`;
    });
  };
  let out = setLine(text, "last_regenerated", `${today} by ${who}`);
  out = setLine(out, "source_state", head);
  if (out === text) return { rel, changed: false, head };
  writeFileSync(file, out, "utf8");
  return { rel, changed: true, head, last_regenerated: `${today} by ${who}` };
}

// ------------------------------------------------------------- CLI

const USAGE = `dbl-regen — what needs regenerating under DBL/, and the worksheet to do it

  node Tools/dbl-regen/regen.mjs [--root <dir>]
  node Tools/dbl-regen/regen.mjs [--root <dir>] --artifact <DBL/...md>
  node Tools/dbl-regen/regen.mjs [--root <dir>] --all
  node Tools/dbl-regen/regen.mjs [--root <dir>] --stamp <DBL/...md> [--by "<name>"]

Exit 0 nothing due / worksheet printed · 1 artifacts are due · 2 usage or config error.

It never writes an artifact's body. --stamp writes exactly two frontmatter lines,
and refuses while the body is unmodified.`;

function main(argv) {
  if (argv.includes("-h") || argv.includes("--help")) {
    process.stdout.write(USAGE + "\n");
    return 0;
  }
  let root = process.cwd();
  let artifact = null;
  let stampRel = null;
  let by = null;
  let all = false;
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (["--root", "--artifact", "--stamp", "--by"].includes(a)) {
      const v = argv[i + 1];
      if (!v || v.startsWith("--")) {
        process.stderr.write(`error: ${a} requires a value\n`);
        return 2;
      }
      if (a === "--root") root = v;
      else if (a === "--artifact") artifact = v.replace(/\\/g, "/");
      else if (a === "--stamp") stampRel = v.replace(/\\/g, "/");
      else by = v;
      i++;
      continue;
    }
    if (a === "--all") {
      all = true;
      continue;
    }
    if (a !== "--json") {
      process.stderr.write(`error: unknown argument ${a}\n\n${USAGE}\n`);
      return 2;
    }
  }
  const json = argv.includes("--json");

  try {
    if (stampRel) {
      const r = stamp(root, stampRel, by ? { by } : {});
      process.stdout.write(json ? JSON.stringify(r, null, 2) + "\n" : `dbl-regen: stamped ${r.rel} — last_regenerated ${r.last_regenerated}, source_state ${r.head}\n`);
      return 0;
    }
    if (artifact) {
      process.stdout.write(worksheet(root, artifact) + "\n");
      return 0;
    }
    const list = due(root);
    if (all) {
      for (const d of list) process.stdout.write(worksheet(root, d.rel) + "\n\n---\n\n");
      return list.length ? 1 : 0;
    }
    if (json) {
      process.stdout.write(JSON.stringify({ root: resolve(root), due: list }, null, 2) + "\n");
      return list.length ? 1 : 0;
    }
    if (!list.length) {
      process.stdout.write(`dbl-regen: nothing due under ${resolve(root)}/DBL\n`);
      return 0;
    }
    for (const d of list) {
      process.stdout.write(`${d.rel}\n`);
      for (const r of d.reasons) process.stdout.write(`  ${r.check}: ${r.message}\n`);
    }
    process.stdout.write(`\ndbl-regen: ${list.length} artifact(s) due — \`--artifact <path>\` prints the worksheet for one\n`);
    return 1;
  } catch (e) {
    if (e instanceof RegenError) {
      process.stderr.write(`dbl-regen: ${e.message}\n`);
      return 2;
    }
    if (e?.constructor?.name === "ConfigError") {
      process.stderr.write(`dbl-regen: ${e.message}\n`);
      return 2;
    }
    throw e;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
