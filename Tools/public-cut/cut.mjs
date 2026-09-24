#!/usr/bin/env node
// public-cut — rebuild the public branch from the development tip.
//
// The procedure this automates is CLAUDE.md-adjacent history: Phase 10 authored it
// as prose, and phases 12, 13 and 14 each re-derived its `Axiom/`-row strip by hand
// because the project tree had changed shape underneath it. The ledger has carried
// "script the public re-cut" as an open item since 2026-08-24. This is that script.
//
//   node Tools/public-cut/cut.mjs --dry-run
//   node Tools/public-cut/cut.mjs
//   node Tools/public-cut/cut.mjs --push        # force-push the branch to origin/master
//
// Exit 0 done · 1 a verification gate failed · 2 usage/precondition refusal.
//
// HARD GATE: the primary working directory is never checked out to the orphan
// branch and `Axiom/` is never removed from it. Every deletion happens inside a
// scratchpad worktree. An in-place `git checkout --orphan` would empty `Axiom/`
// from disk, which Phase 10 §3.2 forbids outright — it is the user's live
// reference material, not a build input.

import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
export const BRANCH = "nissth/public";
export const REMOTE_REF = `${BRANCH}:master`;

export class CutError extends Error {
  constructor(code, message, details = []) {
    super(message);
    this.code = code;
    this.details = details;
  }
}

const git = (cwd, args) => execFileSync("git", args, { cwd, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }).trim();

export function loadScrubMap(file = join(HERE, "scrub-map.json")) {
  const m = JSON.parse(readFileSync(file, "utf8"));
  if (!Array.isArray(m.replacements) || !m.replacements.length) {
    throw new CutError("bad_scrub_map", `${file} has no replacements`);
  }
  for (const r of m.replacements) {
    if (!r.find || r.replace === undefined || !r.reason) {
      throw new CutError("bad_scrub_map", `every replacement needs find, replace and reason: ${JSON.stringify(r)}`);
    }
    r.regex = new RegExp(r.find, r.ignoreCase ? "gi" : "g");
  }
  return m;
}

// ---------------------------------------------------------------- tree strip

/**
 * Remove the `Axiom/` row from an ASCII project tree and repair the connector of
 * the row above it.
 *
 * Written as an algorithm rather than a pattern because the pattern is what kept
 * breaking: `Axiom/` is the last top-level row, so deleting it promotes whatever
 * row precedes it to last, and that row's children must lose their `│` spine.
 * Raises when the shape is not what it expects, so a future tree change fails
 * loudly instead of publishing a row pointing at a directory that is not there.
 */
export function stripAxiomRow(text, label = "tree") {
  const lines = text.split("\n");
  const idx = lines.findIndex((l) => /^└──\s+Axiom\//.test(l));
  if (idx === -1) {
    throw new CutError("strip_pattern_mismatch", `STRIP PATTERN MISMATCH in ${label}: no "└── Axiom/" row found`);
  }
  let prev = -1;
  for (let i = idx - 1; i >= 0; i--) {
    if (/^├──\s/.test(lines[i])) {
      prev = i;
      break;
    }
    if (/^└──\s/.test(lines[i])) {
      throw new CutError("strip_pattern_mismatch", `STRIP PATTERN MISMATCH in ${label}: a second "└──" row sits above the Axiom row`);
    }
  }
  if (prev === -1) {
    throw new CutError("strip_pattern_mismatch", `STRIP PATTERN MISMATCH in ${label}: no "├──" row above the Axiom row to promote`);
  }
  lines[prev] = lines[prev].replace(/^├──/, "└──");
  for (let i = prev + 1; i < idx; i++) {
    if (lines[i].startsWith("│   ")) lines[i] = "    " + lines[i].slice(4);
    else if (lines[i].startsWith("│")) {
      throw new CutError("strip_pattern_mismatch", `STRIP PATTERN MISMATCH in ${label}: line ${i + 1} continues the spine in an unexpected shape: ${lines[i]}`);
    }
  }
  lines.splice(idx, 1);
  return lines.join("\n");
}

export function scrub(text, map) {
  let out = text;
  for (const r of map.replacements) out = out.replace(new RegExp(r.regex.source, r.regex.flags), r.replace);
  return out;
}

/**
 * Every scrub pattern that still matches the committed tree, with a sample line.
 * Empty = clean.
 *
 * Scans in Node with the **same regex engine that did the scrubbing**. The first
 * version shelled out to `git grep -E`, which rejects a `(?:…)` group as an invalid
 * ERE — and because a non-matching `git grep` also exits non-zero, the catch block
 * read "this pattern could not be checked" as "this pattern is clean". The gate
 * passed while printing `fatal:` to the terminal. A verification step that cannot
 * tell failure from success is worse than no verification step, so it no longer
 * shells out at all.
 */
export function residue(root, map) {
  const hits = [];
  const files = textFiles(root);
  for (const r of map.replacements) {
    const re = new RegExp(r.regex.source, r.regex.flags.includes("g") ? r.regex.flags : r.regex.flags + "g");
    let count = 0;
    let sample = null;
    for (const rel of files) {
      const abs = join(root, rel);
      if (!existsSync(abs) || statSync(abs).isDirectory()) continue;
      const text = readFileSync(abs, "utf8");
      re.lastIndex = 0;
      if (!re.test(text)) continue;
      for (const [n, line] of text.split("\n").entries()) {
        re.lastIndex = 0;
        if (!re.test(line)) continue;
        count++;
        sample ??= `${rel}:${n + 1}: ${line.trim()}`;
      }
    }
    if (count) hits.push({ find: r.find, count, sample: sample.slice(0, 200) });
  }
  return hits;
}

// ---------------------------------------------------------------- the cut

const TEXT_EXT = /\.(md|json|ts|tsx|js|mjs|cjs|java|kt|sql|yml|yaml|txt|sh|ps1|gitignore|gitattributes)$/i;

function textFiles(root) {
  return git(root, ["ls-files"])
    .split("\n")
    .filter(Boolean)
    .filter((rel) => TEXT_EXT.test(rel) || /(^|\/)\.git(ignore|attributes)$/.test(rel) || /(^|\/)nissth-bridge$/.test(rel));
}

export function preflight(repoRoot) {
  const branch = git(repoRoot, ["rev-parse", "--abbrev-ref", "HEAD"]);
  const dirty = git(repoRoot, ["status", "--porcelain"])
    .split("\n")
    .filter((l) => l && !/\.claude[\\/]settings\.local\.json/.test(l) && !/tsconfig\.tsbuildinfo/.test(l));
  const axiomTracked = git(repoRoot, ["ls-files", "Axiom"]).split("\n").filter(Boolean).length;
  const axiomOnDisk = existsSync(join(repoRoot, "Axiom"));
  return { branch, head: git(repoRoot, ["rev-parse", "HEAD"]), dirty, axiomTracked, axiomOnDisk };
}

export function planCut(repoRoot, map = loadScrubMap()) {
  const pre = preflight(repoRoot);
  if (pre.dirty.length) {
    throw new CutError("dirty_tree", `${repoRoot} has uncommitted changes; the cut copies the committed tree and would publish a state nobody reviewed`, pre.dirty);
  }
  if (!pre.axiomOnDisk || pre.axiomTracked === 0) {
    throw new CutError("axiom_missing", `Axiom/ is not present in ${repoRoot} — refusing to run the cut in a tree whose hard gate cannot be checked`);
  }
  const deletes = map.delete.map((d) => d.path);
  const rootPdfs = readdirSync(repoRoot).filter((f) => f.toLowerCase().endsWith(".pdf"));
  return { repoRoot, ...pre, deletes, rootPdfs, map };
}

/**
 * Do the cut in a scratchpad worktree and commit it. Returns a report.
 * The branch is left in place; pushing is a separate, explicit step.
 */
export function runCut(repoRoot, opts = {}) {
  const plan = planCut(repoRoot, opts.map);
  const log = [];
  const say = (s) => {
    log.push(s);
    if (!opts.quiet) process.stdout.write(s + "\n");
  };

  say(`public-cut: ${plan.branch} @ ${plan.head.slice(0, 7)} → ${BRANCH}`);
  say(`  Axiom/ hard gate: ${plan.axiomTracked} tracked files present in the primary working directory`);
  if (opts.dryRun) {
    say(`  would delete: ${[...plan.deletes, ...plan.rootPdfs].join(", ")}`);
    say(`  would scrub ${plan.map.replacements.length} pattern(s) across the committed text files`);
    say(`  would strip the Axiom row from CLAUDE.md and README.md, reset AgentReports/StatusUpdate.md to its seed entry, and commit one orphan commit`);
    say(`  dry run — nothing written, no branch touched`);
    return { dryRun: true, log };
  }

  const work = mkdtempSync(join(tmpdir(), "nissth-public-"));
  const cleanup = () => {
    try {
      execFileSync("git", ["worktree", "remove", "--force", work], { cwd: repoRoot, encoding: "utf8" });
    } catch {
      try {
        rmSync(work, { recursive: true, force: true });
      } catch { /* the caller is told below */ }
    }
    try {
      execFileSync("git", ["worktree", "prune"], { cwd: repoRoot });
    } catch { /* ignore */ }
  };

  try {
    // A previous cut leaves the branch behind; it is rebuilt, never appended to.
    try {
      git(repoRoot, ["branch", "-D", BRANCH]);
      say(`  deleted the previous ${BRANCH} branch`);
    } catch { /* first cut */ }

    rmSync(work, { recursive: true, force: true });
    git(repoRoot, ["worktree", "add", "--orphan", "-b", BRANCH, work]);
    git(work, ["checkout", plan.head, "--", "."]);
    say(`  populated a scratchpad worktree from ${plan.head.slice(0, 7)}`);

    // 1. deletions
    for (const rel of [...plan.deletes, ...plan.rootPdfs]) {
      const abs = join(work, rel);
      if (existsSync(abs)) rmSync(abs, { recursive: true, force: true });
    }
    assertAxiomIntact(repoRoot, plan.axiomTracked, "immediately after the deletion step");
    say(`  deleted ${plan.deletes.length + plan.rootPdfs.length} path(s) inside the worktree; Axiom/ verified intact in the primary directory`);

    // 2. scrub
    let changed = 0;
    for (const rel of textFiles(work)) {
      const abs = join(work, rel);
      if (!existsSync(abs) || statSync(abs).isDirectory()) continue;
      const before = readFileSync(abs, "utf8");
      const after = scrub(before, plan.map);
      if (after !== before) {
        writeFileSync(abs, after, "utf8");
        changed++;
      }
    }
    say(`  scrubbed ${plan.map.replacements.length} pattern(s); ${changed} file(s) changed`);

    // 3. Axiom rows out of the project trees
    for (const rel of ["CLAUDE.md", "README.md"]) {
      const abs = join(work, rel);
      writeFileSync(abs, stripAxiomRow(readFileSync(abs, "utf8"), rel), "utf8");
    }
    say(`  stripped the Axiom row from CLAUDE.md and README.md, connectors repaired`);

    // 4. ledger reset
    const seed = readFileSync(join(HERE, "seed-status.md"), "utf8");
    const ledger = join(work, "AgentReports", "StatusUpdate.md");
    const preamble = preambleOf(readFileSync(ledger, "utf8"));
    writeFileSync(ledger, preamble + seed, "utf8");
    say(`  reset AgentReports/StatusUpdate.md to its preamble + one seed entry`);

    // 5. commit
    git(work, ["add", "-A"]);
    git(work, ["-c", "core.hooksPath=/dev/null", "commit", "-q", "-m", commitMessage(plan)]);
    const sha = git(work, ["rev-parse", "HEAD"]);

    // 6. gates
    const gates = verifyCut(work, plan.map);
    assertAxiomIntact(repoRoot, plan.axiomTracked, "after the commit");
    say(`  committed ${sha.slice(0, 7)} — ${gates.fileCount} files, ${gates.commitCount} commit`);
    if (gates.failures.length) {
      throw new CutError("gate_failed", `the cut failed ${gates.failures.length} verification gate(s)`, gates.failures);
    }
    say(`  gates: no Axiom path, no PDF, no settings.local.json, LICENSE present, zero scrub residue`);
    return { sha, log, gates, worktree: work, branch: BRANCH };
  } finally {
    cleanup();
  }
}

function preambleOf(ledger) {
  const i = ledger.search(/^### \d{4}-\d{2}-\d{2}/m);
  if (i === -1) throw new CutError("ledger_shape", "AgentReports/StatusUpdate.md has no `### YYYY-MM-DD` entry heading");
  return ledger.slice(0, i);
}

function commitMessage(plan) {
  return `Nissth — token-lean deterministic execution framework for coding agents\n\nPublic cut of the framework: the rules (CLAUDE.md), the plan template, the three\nDiagnostic Bridge bindings, the dispatcher and the repo tooling.\n\nCut from the development branch at ${plan.head.slice(0, 7)} by Tools/public-cut.\nConsumer material, the reference predecessor framework and local paths are not\npart of this branch; its history is deliberately a single commit.\n`;
}

function assertAxiomIntact(repoRoot, expected, when) {
  const tracked = git(repoRoot, ["ls-files", "Axiom"]).split("\n").filter(Boolean).length;
  const status = git(repoRoot, ["status", "--porcelain", "Axiom"]);
  if (tracked !== expected || status) {
    throw new CutError("axiom_violated", `HARD GATE FAILED ${when}: Axiom/ has ${tracked} tracked files (expected ${expected})${status ? ` and a dirty status` : ""}`);
  }
}

export function verifyCut(work, map) {
  const files = git(work, ["ls-tree", "-r", "--name-only", "HEAD"]).split("\n").filter(Boolean);
  const failures = [];
  const add = (cond, msg) => {
    if (cond) failures.push(msg);
  };
  add(files.some((f) => f.startsWith("Axiom/")), "the tree still contains Axiom/ paths");
  add(files.some((f) => f.toLowerCase().endsWith(".pdf")), "the tree still contains a PDF");
  add(files.includes(".claude/settings.local.json"), "the tree still contains .claude/settings.local.json");
  add(!files.includes("LICENSE"), "LICENSE is missing (it lives on the development branch so every cut inherits it)");
  add(!files.includes("CLAUDE.md"), "CLAUDE.md is missing");
  const commitCount = Number(git(work, ["rev-list", "--count", "HEAD"]));
  add(commitCount !== 1, `expected exactly 1 commit, found ${commitCount}`);
  for (const h of residue(work, map)) {
    failures.push(`scrub residue: /${h.find}/ still matches ${h.count} line(s) — e.g. ${h.sample}`);
  }
  return { fileCount: files.length, commitCount, failures };
}

// ---------------------------------------------------------------- CLI

const USAGE = `public-cut — rebuild the public branch from the development tip

  node Tools/public-cut/cut.mjs [--dry-run] [--push] [--repo <dir>] [--json]

--dry-run   report what would happen; write nothing
--push      after a clean cut, force-push ${REMOTE_REF} (asks nothing; use deliberately)

Exit 0 done · 1 a gate failed · 2 usage or precondition refusal.
The primary working directory is never checked out to the orphan branch, and
Axiom/ is verified intact before and after. See README.md.`;

function main(argv) {
  const opts = { dryRun: argv.includes("--dry-run"), push: argv.includes("--push"), json: argv.includes("--json") };
  if (argv.includes("-h") || argv.includes("--help")) {
    process.stdout.write(USAGE + "\n");
    return 0;
  }
  const ri = argv.indexOf("--repo");
  const repoRoot = resolve(ri === -1 ? resolve(HERE, "..", "..") : argv[ri + 1] ?? ".");
  for (const a of argv) {
    if (!["--dry-run", "--push", "--json", "--repo"].includes(a) && argv[argv.indexOf(a) - 1] !== "--repo") {
      process.stderr.write(`error: unknown argument ${a}\n\n${USAGE}\n`);
      return 2;
    }
  }
  let r;
  try {
    r = runCut(repoRoot, opts);
  } catch (e) {
    if (!(e instanceof CutError)) throw e;
    process.stderr.write(`public-cut: ${e.message}\n${e.details.map((d) => `  ${d}`).join("\n")}\n`);
    return e.code === "gate_failed" || e.code === "axiom_violated" || e.code === "strip_pattern_mismatch" ? 1 : 2;
  }
  if (opts.dryRun) return 0;
  if (opts.push) {
    try {
      execFileSync("git", ["push", "--force", "origin", REMOTE_REF], { cwd: repoRoot, encoding: "utf8", stdio: "inherit" });
      process.stdout.write(`  pushed ${REMOTE_REF}\n`);
    } catch {
      process.stderr.write(`public-cut: the cut is committed on ${BRANCH} but the push failed; re-run "git push --force origin ${REMOTE_REF}"\n`);
      return 1;
    }
  } else {
    process.stdout.write(`  not pushed. When you are ready: git push --force origin ${REMOTE_REF}\n`);
  }
  if (opts.json) process.stdout.write(JSON.stringify({ ok: true, sha: r.sha, branch: BRANCH, files: r.gates.fileCount }, null, 2) + "\n");
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
