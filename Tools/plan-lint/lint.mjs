#!/usr/bin/env node
// plan-lint — validates ImplementationPlans/Phase_NN_*.md against the §6 contract,
// and against the DBL/DependencyMaps/ artifacts that cover what the plan touches.
//
//   node Tools/plan-lint/lint.mjs [--root <dir>] [--plan <file>] [--json] [--strict]
//
// Exit 0 clean (info/warn only, unless --strict) · 1 error findings (any finding
// with --strict) · 2 usage/config error.
//
// Reports and exits; never edits a plan. There is no --fix: a tool that rewrites
// a plan is a different risk, with its own plan.
//
// The load-bearing check is `dependency-map-not-cited`. CLAUDE.md §6 already says
// every plan must conform to the template and §1.1 says to read the DBL; nothing
// checked either. A consumer's Phase 06 placed classes in a project its own
// DependencyMap forbids tests from referencing, and it surfaced at execution —
// the map was on disk, correct, and simply not read. Same shape as §12.1: a rule
// that is followed and still misses the defect needs a mechanism.

import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseFrontmatter, globToRegExp } from "../dbl-check/check.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));

export class ConfigError extends Error {}

export const SECTIONS = [
  [0, "Metadata"],
  [1, "Pre-Flight Diagnostic"],
  [2, "Expected State"],
  [3, "Execution"],
  [4, "Post-Flight Verification"],
  [5, "Cleanup"],
  [6, "Status Update Entry"],
];

const WAIVER = /<!--\s*plan-lint:allow\s+([a-z-]+)\s*-\s*(.+?)-->/g;

// ------------------------------------------------------------- parsing

/** Split a plan into `## N.` sections and `### N.M` subsections. */
export function parsePlan(text) {
  const lines = text.split(/\r?\n/);
  const sections = new Map();
  const subs = new Map();
  let cur = null;
  let sub = null;
  for (const [i, line] of lines.entries()) {
    const h2 = line.match(/^##\s+(\d+)\.\s*(.*)$/);
    if (h2) {
      cur = Number(h2[1]);
      sub = null;
      sections.set(cur, { title: h2[2].trim(), line: i + 1, body: [] });
      continue;
    }
    const h3 = line.match(/^###\s+(\d+)\.(\d+[a-z]?)\s*(.*)$/);
    if (h3) {
      sub = `${h3[1]}.${h3[2]}`;
      subs.set(sub, { title: h3[3].trim(), line: i + 1, body: [] });
      if (cur !== null) sections.get(cur)?.body.push(line);
      continue;
    }
    if (sub !== null) subs.get(sub).body.push(line);
    if (cur !== null) sections.get(cur)?.body.push(line);
  }
  const waivers = new Map();
  for (const m of text.matchAll(WAIVER)) waivers.set(m[1], m[2].trim());
  return { sections, subs, waivers, lines };
}

const bodyOf = (p, sub, section) =>
  (p.subs.get(sub)?.body ?? p.sections.get(section)?.body ?? []).join("\n");

/** Backticked tokens from a blob that look like repository paths. */
export function pathsIn(text) {
  const out = new Set();
  for (const m of text.matchAll(/`([^`\n]+)`/g)) {
    let t = m[1].trim();
    t = t.replace(/:\d+(-\d+)?(,\s*\d+(-\d+)?)*$/, ""); // strip :12-30 line ranges
    if (!/[\\/]/.test(t)) continue;
    if (/\s(—|→|\||and)\s/.test(t)) continue; // prose inside backticks
    if (/^https?:/.test(t)) continue;
    t = t.replace(/\\/g, "/").replace(/^\.\//, "");
    if (!/^[A-Za-z0-9_@.][A-Za-z0-9_@./*{},<>-]*$/.test(t)) continue;
    out.add(t);
  }
  return [...out];
}

/** Target paths a plan's §3 step list names. */
export function stepTargets(plan) {
  const body = bodyOf(plan, "3.1", 3);
  const targets = new Set();
  for (const line of body.split("\n")) {
    if (!/^\s*-\s*\[[ x]\]/.test(line)) continue;
    // Prefer the **File:**/**Files:** clause when the step has one; many plans
    // (9 of this repo's 23) never adopted the marker, so fall back to the line.
    const m = line.match(/\*\*Files?:\*\*(.*?)(\*\*(Lines|Operation|Acceptance|Command):\*\*|$)/s);
    for (const p of pathsIn(m ? m[1] : line)) targets.add(p);
  }
  return [...targets];
}

/** Artifact paths a plan's §1.1 Inputs cites. */
export function citedArtifacts(plan) {
  return pathsIn(bodyOf(plan, "1.1", 1)).filter((p) => p.endsWith(".md"));
}

export function metadata(plan) {
  const body = bodyOf(plan, "0.0", 0);
  const field = (name) => {
    const m = body.match(new RegExp(`\\*\\*${name}:?\\*\\*\\s*(.*)`, "i"));
    return m ? m[1].trim() : null;
  };
  return {
    planId: field("Plan ID"),
    authored: field("Authored"),
    approved: field("Approved"),
    dependsOn: field("Depends on"),
  };
}

// ------------------------------------------------------------- dependency maps

/** Every DependencyMap under <root>/DBL, with its covers and whether it states rules. */
export function dependencyMaps(root) {
  const dir = join(root, "DBL", "DependencyMaps");
  if (!existsSync(dir)) return [];
  const out = [];
  for (const name of readdirSync(dir)) {
    if (!name.endsWith(".md") || name === "_TEMPLATE.md") continue;
    const rel = `DBL/DependencyMaps/${name}`;
    const text = readFileSync(join(dir, name), "utf8");
    const fm = parseFrontmatter(text);
    const covers = Array.isArray(fm.data?.covers)
      ? fm.data.covers
      : typeof fm.data?.covers === "string"
        ? [fm.data.covers]
        : [];
    out.push({ rel, covers, rules: boundaryRules(text) });
  }
  return out;
}

/**
 * The "MUST NOT" rules a dependency map states, as one-line strings.
 * Handles both shapes in the wild: the template's bullets and the table both
 * live consumers actually wrote.
 */
export function boundaryRules(text) {
  const start = text.search(/^##\s+Boundary rules/im);
  if (start === -1) return [];
  const rest = text.slice(start).split("\n").slice(1);
  const rules = [];
  for (const line of rest) {
    if (/^##\s/.test(line)) break;
    const t = line.trim();
    if (!t || t.startsWith(">")) continue;
    if (/no enforced boundaries yet/i.test(t)) continue;
    if (/^\|/.test(t)) {
      const cells = t.split("|").map((c) => c.trim()).filter(Boolean);
      if (!cells.length) continue;
      if (/^:?-+:?$/.test(cells[0])) continue; // separator row
      if (/^MUST NOT$/i.test(cells[0])) continue; // header row
      rules.push(cells.slice(0, 2).join(" — "));
      continue;
    }
    if (/^[-*]\s/.test(t)) rules.push(t.replace(/^[-*]\s*/, ""));
  }
  return rules;
}

const coversPath = (covers, path) =>
  covers.some((glob) => {
    const re = globToRegExp(glob);
    if (re.test(path)) return true;
    // `src/**` should also match `src/a/b.ts` when the glob omits a trailing slash
    return globToRegExp(glob.endsWith("/**") ? glob : `${glob}/**`).test(path);
  });

// ------------------------------------------------------------- checks

export function lintPlan(root, rel, opts = {}) {
  const abs = join(root, rel);
  const text = readFileSync(abs, "utf8");
  const plan = parsePlan(text);
  const findings = [];
  const f = (check, severity, message, line = 1) => {
    if (plan.waivers.has(check)) return;
    findings.push({ file: rel, check, severity, message, line });
  };

  for (const [n, title] of SECTIONS) {
    if (!plan.sections.has(n)) f("missing-section", "error", `no \`## ${n}. ${title}…\` section — CLAUDE.md §6 requires all seven`);
  }

  const meta = metadata(plan);
  const stem = basename(rel).replace(/\.md$/, "");
  if (!meta.planId) f("missing-key", "error", "§0 has no `**Plan ID:**`");
  else if (meta.planId.replace(/`/g, "").trim() !== stem) {
    f("plan-id-mismatch", "error", `§0 Plan ID \`${meta.planId}\` does not match the file name \`${stem}\``);
  }
  if (!meta.approved) f("missing-key", "error", "§0 has no `**Approved:**` — a plan is `pending` until the user fills it in (§6)");
  else if (/^pending/i.test(meta.approved)) f("approved-pending", "info", "§0 Approved is `pending` — §3 must not be executed yet");
  else if (!/\d{4}-\d{2}-\d{2}/.test(meta.approved)) {
    f("bad-approved", "error", `§0 Approved is neither \`pending\` nor an ISO date: \`${meta.approved}\``);
  }
  if (!meta.dependsOn) f("missing-key", "error", "§0 has no `**Depends on:**` (use `none`)");
  else if (!/^none/i.test(meta.dependsOn.replace(/`/g, ""))) {
    const plans = readdirSync(join(root, "ImplementationPlans")).filter((n) => n.endsWith(".md"));
    for (const raw of meta.dependsOn.match(/Phase_[A-Za-z0-9_]+/g) ?? []) {
      // Plans cite each other loosely: `Phase_17` for a full ID, and `Phase_07_*`
      // as a wildcard when reserving a number. Resolve by prefix, and treat a
      // trailing underscore as the wildcard stem it is.
      const dep = raw.replace(/_+$/, "");
      if (plans.some((n) => n === `${dep}.md` || n.startsWith(`${dep}_`))) continue;
      f("unknown-dependency", "error", `§0 Depends on \`${raw}\`, which matches no plan in ImplementationPlans/`);
    }
  }

  const forbidden = bodyOf(plan, "3.2", 3);
  if (plan.sections.has(3) && !/^\s*-\s+\S/m.test(forbidden)) {
    f("empty-forbidden", "error", "§3.2 Forbidden in this phase is empty — it is mandatory and is the anti-scope-creep guard (§6)");
  }

  const cited = citedArtifacts(plan);
  for (const c of cited) {
    if (!/^(DBL|ImplementationPlans)\//.test(c)) continue;
    if (c.includes("*") || c.includes("<")) continue; // a pattern, not a path
    if (!existsSync(join(root, c))) f("missing-cited-artifact", "error", `§1.1 cites \`${c}\`, which does not exist`);
  }

  // --- the load-bearing check
  const targets = stepTargets(plan);
  const citedBlob = bodyOf(plan, "1.1", 1);
  for (const map of opts.maps ?? dependencyMaps(root)) {
    if (!map.rules.length) continue;
    const hit = targets.find((t) => coversPath(map.covers, t));
    if (!hit) continue;
    if (citedBlob.includes(map.rel) || citedBlob.includes(basename(map.rel))) continue;
    f(
      "dependency-map-not-cited",
      "error",
      `§3 targets \`${hit}\`, covered by \`${map.rel}\`, which states ${map.rules.length} boundary rule(s) — cite it in §1.1 and confirm the targets respect it. First rule: ${map.rules[0].slice(0, 120)}`
    );
  }

  if (plan.sections.has(3) && targets.length === 0) {
    f("no-step-targets", "info", "§3 names no file paths — fine for a plan whose execution is a branch or a verification run, worth a second look otherwise");
  }

  return { findings, targets, cited, waivers: [...plan.waivers.keys()] };
}

export function lint(root, opts = {}) {
  const abs = resolve(root);
  const dir = join(abs, "ImplementationPlans");
  if (!existsSync(dir) || !statSync(dir).isDirectory()) throw new ConfigError(`${abs} has no ImplementationPlans/ directory`);
  const plans = opts.only
    ? [opts.only]
    : readdirSync(dir)
        .filter((n) => /^Phase_.*\.md$/.test(n))
        .sort()
        .map((n) => `ImplementationPlans/${n}`);
  const maps = dependencyMaps(abs);
  const findings = [];
  for (const rel of plans) findings.push(...lintPlan(abs, rel, { maps }).findings);
  const summary = { error: 0, warn: 0, info: 0 };
  for (const x of findings) summary[x.severity]++;
  return { root: abs, scanned: plans.length, plans, maps: maps.map((m) => ({ rel: m.rel, rules: m.rules.length })), findings, summary };
}

// ------------------------------------------------------------- CLI

const USAGE = `plan-lint — validate ImplementationPlans/Phase_NN_*.md (CLAUDE.md §6)

  node Tools/plan-lint/lint.mjs [--root <dir>] [--plan <file>] [--json] [--strict]

--root   project root holding ImplementationPlans/ (default: this checkout)
--plan   lint one plan, path relative to --root
--strict any finding fails the exit code

Exit 0 clean, 1 error findings (any finding with --strict), 2 usage/config error.
Reports only; never edits a plan. Waive one check on one plan with
<!-- plan-lint:allow <check> - reason --> inside it.`;

function main(argv) {
  if (argv.includes("-h") || argv.includes("--help")) {
    process.stdout.write(USAGE + "\n");
    return 0;
  }
  let root = resolve(HERE, "..", "..");
  let only = null;
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--root" || a === "--plan") {
      const v = argv[i + 1];
      if (!v || v.startsWith("--")) {
        process.stderr.write(`error: ${a} requires a path\n`);
        return 2;
      }
      if (a === "--root") root = v;
      else only = v.replace(/\\/g, "/");
      i++;
      continue;
    }
    if (!["--json", "--strict"].includes(a)) {
      process.stderr.write(`error: unknown argument ${a}\n\n${USAGE}\n`);
      return 2;
    }
  }
  let result;
  try {
    result = lint(root, only ? { only } : {});
  } catch (e) {
    if (e instanceof ConfigError) {
      process.stderr.write(`error: ${e.message}\n`);
      return 2;
    }
    if (e.code === "ENOENT") {
      process.stderr.write(`error: no such plan: ${only}\n`);
      return 2;
    }
    throw e;
  }
  const strict = argv.includes("--strict");
  const failing = strict ? result.findings.length : result.summary.error;
  if (argv.includes("--json")) {
    process.stdout.write(JSON.stringify(result, null, 2) + "\n");
    return failing ? 1 : 0;
  }
  const byFile = new Map();
  for (const x of result.findings) (byFile.get(x.file) ?? byFile.set(x.file, []).get(x.file)).push(x);
  for (const [file, fs] of byFile) {
    process.stdout.write(`${file}\n`);
    for (const x of fs) process.stdout.write(`  [${x.severity}] ${x.check}: ${x.message}\n`);
  }
  const s = result.summary;
  const mapNote = result.maps.length ? `, ${result.maps.length} dependency map(s)` : ", no dependency maps";
  process.stdout.write(`\nplan-lint: ${result.scanned} plan(s) scanned${mapNote} — ${s.error} error, ${s.warn} warn, ${s.info} info${strict ? " (strict)" : ""}\n`);
  return failing ? 1 : 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
