#!/usr/bin/env node
// dbl-check — DBL frontmatter + freshness validator (CLAUDE.md §7.2, §7.3, §13).
//
// Scans <root>/DBL/**/*.md (excluding _TEMPLATE.md) and reports, never edits.
// Zero runtime dependencies. Node 20+.
//
//   node Tools/dbl-check/check.mjs [--root <dir>] [--json] [--strict]
//   node Tools/dbl-check/check.mjs --budget <file> [--budget <file> ...] [--json]
//
// Exit 0 clean (info/warn only, unless --strict) · 1 error-severity findings
// (or any finding with --strict) · 2 usage/config error (no DBL/ dir, bad args).
//
// --budget word-counts any file — a draft in a scratchpad, an artifact not yet
// written — against the §7.4 split threshold, so an over-budget artifact can be
// split before it is committed rather than after. Exit 1 when any file is over.

import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, resolve, sep } from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

export const REQUIRED_KEYS = ["artifact_type", "name", "last_regenerated", "source_state", "covers", "stale_when"];
export const DIR_TYPES = {
  Summaries: "summary",
  DependencyMaps: "dependency_map",
  APIIndex: "api_index",
  SchemaIndex: "schema_index",
};
export const WORD_BUDGET = 1100; // ≈ 1 500 tokens — CLAUDE.md §7.4 split threshold
const WALK_SKIP = new Set([".git", "node_modules", ".expo", "dist", "build", "target", "coverage"]);

export class ConfigError extends Error {}

// ------------------------------------------------------------- frontmatter (YAML subset)

/**
 * Parse the `---` block at the top of an artifact. Supports `key: scalar`,
 * `key:` + `- item` lists, quoted scalars, and free text containing `—` or `:`
 * after the first colon. Anything else is reported as bad-frontmatter.
 * Returns { data, endLine, errors } or null when there is no leading `---`.
 */
export function parseFrontmatter(text) {
  const lines = text.split("\n");
  if (lines[0]?.trim() !== "---") return null;
  const data = {};
  const errors = [];
  let current = null;
  let i = 1;
  for (; i < lines.length; i++) {
    const raw = lines[i].replace(/\r$/, "");
    if (raw.trim() === "---") break;
    if (raw.trim() === "" || raw.trim().startsWith("#")) continue;
    const item = raw.match(/^\s+-\s+(.*)$/);
    if (item) {
      if (!current || !Array.isArray(data[current])) {
        errors.push({ line: i + 1, message: `list item outside a list key: ${raw.trim()}` });
        continue;
      }
      data[current].push(unquote(item[1]));
      continue;
    }
    const kv = raw.match(/^([A-Za-z_][A-Za-z0-9_]*):(?:\s+(.*))?$/);
    if (!kv) {
      errors.push({ line: i + 1, message: `unparseable frontmatter line: ${raw.trim()}` });
      continue;
    }
    const [, key, val] = kv;
    if (val === undefined || val.trim() === "") {
      data[key] = [];
      current = key;
    } else {
      data[key] = unquote(val.trim());
      current = null;
    }
  }
  if (i >= lines.length) errors.push({ line: 1, message: "frontmatter block never closed with `---`" });
  return { data, endLine: i + 1, errors };
}

function unquote(s) {
  const t = s.trim();
  if ((t.startsWith('"') && t.endsWith('"')) || (t.startsWith("'") && t.endsWith("'"))) return t.slice(1, -1);
  return t;
}

// ------------------------------------------------------------- glob (covers)

/** Convert a covers glob (`src/**`, `app/**\/*.tsx`, `app.json`) into a RegExp over posix relative paths. */
export function globToRegExp(glob) {
  let g = glob.trim().replace(/\\/g, "/").replace(/^\.\//, "").replace(/\/$/, "/**");
  let re = "";
  for (let i = 0; i < g.length; i++) {
    const c = g[i];
    if (c === "*") {
      if (g[i + 1] === "*") {
        const slashAfter = g[i + 2] === "/";
        re += slashAfter ? "(?:.*/)?" : ".*";
        i += slashAfter ? 2 : 1;
      } else re += "[^/]*";
    } else if (c === "?") re += "[^/]";
    else if (/[.+^${}()|[\]\\]/.test(c)) re += "\\" + c;
    else re += c;
  }
  return new RegExp("^" + re + "$");
}

function* walk(dir, root) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory()) {
      if (WALK_SKIP.has(e.name)) continue;
      yield* walk(join(dir, e.name), root);
    } else yield relative(root, join(dir, e.name)).split(sep).join("/");
  }
}

/** First file under root matching any covers glob, or null. Lazily walks; stops at the first hit. */
export function firstCoveredFile(root, covers) {
  const res = covers.map(globToRegExp);
  for (const rel of walk(root, root)) {
    if (rel.startsWith("DBL/")) continue; // an artifact never "covers" DBL itself
    if (res.some((r) => r.test(rel))) return rel;
  }
  return null;
}

// ------------------------------------------------------------- git

function gitChangedSince(root, ref, covers) {
  try {
    const pathspecs = covers.map((c) => `:(glob)${c.replace(/\\/g, "/")}`);
    const out = execFileSync("git", ["-C", root, "diff", "--name-only", ref, "--", ...pathspecs], { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] });
    return { ok: true, files: out.split("\n").filter(Boolean) };
  } catch (e) {
    return { ok: false, reason: e.code === "ENOENT" ? "git not on PATH" : "not a git work tree or unknown ref" };
  }
}

// ------------------------------------------------------------- checks

const DATE_BY = /^\d{4}-\d{2}-\d{2} by \S/;
const STALE = /^STALE\s+—\s+\S/;

/**
 * The git ref inside a `source_state`, whatever surrounds it.
 *
 * This used to be an anchored `^[0-9a-f]{7,40}$`, so the check below ran only
 * when the whole value was a bare hash. §7.2 invites more than that
 * ("<git commit hash, OR "uncommitted state at …">"), and a consumer wrote
 * `git c5e6a34 (Phase 06 M5 feature commit — reports)` on **all 17** of its
 * artifacts. Every one of them had `covers-changed-since` skipped in silence —
 * four were stale, one by 74 covered files, while `dbl-check --strict` reported
 * 0/0/0. A check that cannot fire is worse than no check, because it is believed.
 *
 * Returns null for the forms that legitimately have no ref; those are recognised
 * by their own branches rather than passed to git.
 */
export function sourceRef(sourceState) {
  const s = String(sourceState ?? "");
  if (!s.trim()) return null;
  if (/^design-only\b/i.test(s)) return null;
  if (/^uncommitted\b/i.test(s)) return null;
  if (/^</.test(s.trim())) return null; // the unfilled template placeholder
  const m = s.match(/\b([0-9a-f]{7,40})\b/);
  if (!m) return null;
  // A 7+ run of hex letters can be an ordinary word (`deadbeef` aside, think
  // `accede`); require at least one digit, which every real short hash has.
  return /\d/.test(m[1]) ? m[1] : null;
}

/** True when the value is one of the forms that deliberately carries no ref. */
export function refless(sourceState) {
  const s = String(sourceState ?? "").trim();
  return /^design-only\b/i.test(s) || /^uncommitted\b/i.test(s) || /^</.test(s) || s === "";
}

export function checkArtifact(root, rel, opts = {}) {
  const findings = [];
  const notes = [];
  const f = (check, severity, message, line) => findings.push({ check, severity, file: rel, ...(line ? { line } : {}), message });
  const text = readFileSync(join(root, rel), "utf8");

  if (text.includes("\r")) f("crlf", "error", "file contains CR characters; the framework baseline is LF (binding parsers anchor on \\n)");

  const fm = parseFrontmatter(text);
  if (!fm) {
    f("missing-frontmatter", "error", "file does not start with a `---` frontmatter block (CLAUDE.md §7.2: no exceptions)", 1);
    return { findings, notes };
  }
  for (const e of fm.errors) f("bad-frontmatter", "error", e.message, e.line);
  const d = fm.data;

  for (const k of REQUIRED_KEYS) {
    if (!(k in d)) f("missing-key", "error", `frontmatter lacks \`${k}\``, 1);
    else if (Array.isArray(d[k]) && d[k].length === 0 && (k === "covers" || k === "stale_when")) f("missing-key", "error", `\`${k}\` is empty`, 1);
  }

  const dir = rel.split("/")[1];
  const expectedType = DIR_TYPES[dir];
  if (!expectedType) f("unknown-dir", "warn", `DBL/${dir}/ is not one of ${Object.keys(DIR_TYPES).join(", ")}`);
  else if (d.artifact_type && d.artifact_type !== expectedType) f("type-dir-mismatch", "error", `artifact_type \`${d.artifact_type}\` but directory implies \`${expectedType}\``, 1);

  const lr = typeof d.last_regenerated === "string" ? d.last_regenerated : "";
  if (lr) {
    if (STALE.test(lr)) f("stale-marked", "info", `STALE-flipped: ${lr.replace(/^STALE\s+—\s+/, "")} — regenerate before citing (§11.4)`, 1);
    else if (!DATE_BY.test(lr)) f("bad-regenerated-format", "error", `last_regenerated must be \`YYYY-MM-DD by <who>\` or \`STALE — <reason>\`; got \`${lr}\``, 1);
  }

  const covers = Array.isArray(d.covers) ? d.covers : typeof d.covers === "string" ? [d.covers] : [];
  const ss = typeof d.source_state === "string" ? d.source_state : "";
  if (ss.startsWith("design-only") && covers.length) {
    const hit = firstCoveredFile(root, covers);
    if (hit) f("design-only-source-exists", "error", `source_state is design-only but \`${hit}\` exists under covers — regenerate from source (Phase 01 §5 / CLAUDE.md §7.6)`, 1);
  }
  const ref = sourceRef(ss);
  if (ref && covers.length && !opts.noGit) {
    const r = gitChangedSince(root, ref, covers);
    if (!r.ok) notes.push(`${rel}: covers-changed-since skipped (${r.reason})`);
    else if (r.files.length) f("covers-changed-since", "warn", `${r.files.length} covered file(s) changed since ${ref.slice(0, 7)}: ${r.files.slice(0, 3).join(", ")}${r.files.length > 3 ? ", …" : ""} — regenerate (§7.3; \`dbl-regen --artifact ${rel}\`)`, 1);
  } else if (!ref && !refless(ss)) {
    f("unrecognised-source-state", "warn", `\`source_state: ${ss.slice(0, 60)}\` carries no git ref and is not \`design-only …\` or \`uncommitted state at …\` — freshness cannot be checked, so say which commit the artifact was written against (§7.2)`, 1);
  }

  const words = text.split(/\s+/).filter(Boolean).length;
  if (words > WORD_BUDGET) f("over-budget", "warn", `${words} words > ${WORD_BUDGET} (≈1 500 tokens) — split per CLAUDE.md §7.4`);

  return { findings, notes, words };
}

export function check(root, opts = {}) {
  const abs = resolve(root);
  const dbl = join(abs, "DBL");
  if (!existsSync(dbl) || !statSync(dbl).isDirectory()) throw new ConfigError(`${abs} has no DBL/ directory`);
  const artifacts = [];
  for (const rel of walk(dbl, abs)) {
    if (!rel.endsWith(".md")) continue;
    if (/(^|\/)_TEMPLATE\.md$/.test(rel)) continue;
    artifacts.push(rel);
  }
  artifacts.sort();
  const findings = [];
  const notes = [];
  const words = {};
  for (const rel of artifacts) {
    const r = checkArtifact(abs, rel, opts);
    findings.push(...r.findings);
    notes.push(...r.notes);
    words[rel] = r.words;
  }
  const summary = { error: 0, warn: 0, info: 0 };
  for (const x of findings) summary[x.severity]++;
  return { root: abs, scanned: artifacts.length, artifacts, findings, notes, summary, words };
}

/**
 * Word-count one file against WORD_BUDGET. Counts the whole file, frontmatter
 * included — the same text `over-budget` counts, so the preview and the scan
 * cannot disagree.
 */
export function budget(file) {
  const abs = resolve(file);
  if (!existsSync(abs) || statSync(abs).isDirectory()) throw new ConfigError(`no such file: ${file}`);
  const words = readFileSync(abs, "utf8").split(/\s+/).filter(Boolean).length;
  return { file, path: abs, words, budget: WORD_BUDGET, over: words > WORD_BUDGET };
}

// ------------------------------------------------------------- CLI

const FLAGS = ["--json", "--strict"];
const VALUE_FLAGS = ["--root", "--budget"];

function main(argv) {
  const args = argv.slice(2);
  if (args.includes("--help") || args.includes("-h")) {
    process.stdout.write(
      "usage: check.mjs [--root <path>] [--json] [--strict]\n" +
        "       check.mjs --budget <file> [--budget <file> ...] [--json]\n\n" +
        "Validates DBL/**/*.md frontmatter and freshness (CLAUDE.md §7.2, §7.3, §13).\n" +
        "--budget word-counts arbitrary files against the §7.4 split threshold before they are written.\n" +
        "Exit 0 clean, 1 error findings (any finding with --strict; any over-budget file), 2 usage/config error.\n"
    );
    return 0;
  }
  let root = process.cwd();
  const budgetFiles = [];
  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (VALUE_FLAGS.includes(a)) {
      const v = args[i + 1];
      if (!v || v.startsWith("--")) {
        process.stderr.write(`error: ${a} requires a path\n`);
        return 2;
      }
      if (a === "--root") root = v;
      else budgetFiles.push(v);
      i++;
      continue;
    }
    if (!FLAGS.includes(a)) {
      process.stderr.write(`error: unknown argument ${a}\n`);
      return 2;
    }
  }
  const strict = args.includes("--strict");

  if (budgetFiles.length) {
    const rows = [];
    for (const f of budgetFiles) {
      try {
        rows.push(budget(f));
      } catch (e) {
        if (e instanceof ConfigError) {
          process.stderr.write(`error: ${e.message}\n`);
          return 2;
        }
        throw e;
      }
    }
    if (args.includes("--json")) process.stdout.write(JSON.stringify({ budget: WORD_BUDGET, files: rows }, null, 2) + "\n");
    else {
      for (const r of rows) {
        const verdict = r.over ? `OVER by ${r.words - WORD_BUDGET}` : `ok, ${WORD_BUDGET - r.words} to spare`;
        process.stdout.write(`${r.file}: ${r.words} words / ${WORD_BUDGET} — ${verdict}\n`);
      }
      if (rows.some((r) => r.over)) process.stdout.write(`\nsplit before committing — CLAUDE.md §7.4\n`);
    }
    return rows.some((r) => r.over) ? 1 : 0;
  }

  let result;
  try {
    result = check(root);
  } catch (e) {
    if (e instanceof ConfigError) {
      process.stderr.write(`error: ${e.message}\n`);
      return 2;
    }
    throw e;
  }
  const failing = strict ? result.findings.length : result.summary.error;
  if (args.includes("--json")) {
    process.stdout.write(JSON.stringify(result, null, 2) + "\n");
    return failing ? 1 : 0;
  }
  if (result.scanned === 0) {
    process.stdout.write(`dbl-check: no artifacts under ${result.root}/DBL (templates only)\n`);
    return 0;
  }
  const byFile = new Map();
  for (const f of result.findings) (byFile.get(f.file) ?? byFile.set(f.file, []).get(f.file)).push(f);
  for (const [file, fs] of byFile) {
    process.stdout.write(`${file}\n`);
    for (const f of fs) process.stdout.write(`  [${f.severity}] ${f.check}: ${f.message}\n`);
  }
  for (const n of result.notes) process.stdout.write(`note: ${n}\n`);
  const s = result.summary;
  process.stdout.write(`\ndbl-check: ${result.scanned} artifact(s) scanned — ${s.error} error, ${s.warn} warn, ${s.info} info${strict ? " (strict)" : ""}\n`);
  return failing ? 1 : 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv));
}
