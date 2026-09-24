#!/usr/bin/env node
// nissth-init — consumer-project bootstrap (CLAUDE.md §9.1 step 2).
//
// Creates the Nissth control-file skeleton in a target directory: CLAUDE.md
// (project banner + the framework body copied verbatim), AGENTS.md, the plan and
// DBL templates, an AgentReports/StatusUpdate.md with its schema preamble and a
// filled "Bootstrap" entry, stack-specific .gitignore, .gitattributes, a narrow
// .claude/settings.json allow-list, and the two consumer launchers.
//
// Contract (deliberately narrow):
//   - never overwrites: every target path is checked before anything is written;
//     a single collision aborts with nothing written (exit 2, error_code file_exists)
//   - never spawns a subprocess: no git init, no npm, no npx create-*
//   - never authors SRS/SDD or Phase 00 — those are the agent's, after the HR#13 gate
//   - every written text file is LF, whatever the template's line endings were
//
// Zero runtime dependencies. Node 20+.
//
//   node Tools/nissth-init/init.mjs --target <dir> --name <ProjectName> --stack <expo|spring-boot|postgres|none>
//                                  [--wiring local|submodule] [--framework-root <abs>] [--dry-run] [--json]

import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync, chmodSync } from "node:fs";
import { dirname, join, resolve, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const TEMPLATES = join(HERE, "templates");
export const VERSION = JSON.parse(readFileSync(join(HERE, "package.json"), "utf8")).version;

export const STACKS = ["expo", "spring-boot", "postgres", "none"];
export const WIRINGS = ["local", "submodule"];

const STACK_SENTENCE = {
  expo: "Stack binding: **Expo** (§8.2) — Expo Router project; `route_lens`, `component_lens`, `dependency_audit`, `expo_doctor_lens`, `route_scaffold` apply.",
  "spring-boot": "Stack binding: **Spring Boot** (§8.1) with **PostgreSQL** (§8.3) alongside — `compile_verify`, `endpoint_lens`, `entity_lens`, `migration_status`, `entity_field_add`, plus the Postgres diagnostic tools apply.",
  postgres: "Stack binding: **PostgreSQL** (§8.3), diagnostic-only — `schema_lens`, `query_plan`, `index_audit`, `lock_audit`, `migration_status` apply; the application-side binding is added separately.",
  none: "No stack binding selected yet — choose one in the SDD and re-stamp this banner; the Bridge tool catalog (`./nissth-bridge --list-tools`) is still available.",
};

export class InitError extends Error {
  constructor(code, message, details = []) {
    super(message);
    this.code = code;
    this.details = details;
  }
}

// ---------------------------------------------------------------- helpers

const lf = (s) => s.replace(/\r\n?/g, "\n");
const readTemplate = (name) => lf(readFileSync(join(TEMPLATES, name), "utf8"));
const readFramework = (root, rel) => lf(readFileSync(join(root, rel), "utf8"));

function render(template, vars) {
  return template.replace(/\{\{([A-Z_]+)\}\}/g, (m, key) => {
    if (!(key in vars)) throw new InitError("template_var_missing", `template variable ${key} has no value`);
    return vars[key];
  });
}

/** Forward-slash form of an absolute path, usable from sh on every platform incl. Git Bash. */
const posixPath = (p) => p.replace(/\\/g, "/");

function pad2(n) {
  return String(n).padStart(2, "0");
}

export function localDate(d = new Date()) {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
}
export function localDateTime(d = new Date()) {
  return `${localDate(d)} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
}

// ---------------------------------------------------------------- framework inputs

/** Split the framework CLAUDE.md into (banner, body). Body starts at the first `---` rule line. */
export function frameworkBody(claudeMd) {
  const lines = claudeMd.split("\n");
  const idx = lines.findIndex((l, i) => i > 0 && l.trim() === "---");
  if (idx < 0) throw new InitError("framework_claude_shape", "framework CLAUDE.md has no `---` rule after its banner");
  const head = lines.slice(0, idx).join("\n");
  if (!/\*\*Status:\*\*/.test(head)) {
    throw new InitError("framework_claude_shape", "framework CLAUDE.md banner has no **Status:** line before the first `---`");
  }
  return lines.slice(idx).join("\n");
}

export function validateFrameworkRoot(root) {
  const abs = resolve(root);
  const ok = (p) => {
    try {
      return statSync(p);
    } catch {
      return null;
    }
  };
  if (!ok(join(abs, "Bindings"))?.isDirectory()) {
    throw new InitError("invalid_framework_root", `${abs} does not contain a Bindings/ directory`);
  }
  for (const rel of ["CLAUDE.md", "AGENTS.md", "ImplementationPlans/_TEMPLATE.md", "Tools/nissth-bridge/dispatcher.js"]) {
    if (!ok(join(abs, rel))?.isFile()) throw new InitError("invalid_framework_root", `${abs} is missing ${rel}`);
  }
  return abs;
}

// ---------------------------------------------------------------- plan

/**
 * Compute everything init would write. Pure apart from reading templates and the
 * framework checkout; touches the target only to check for collisions.
 */
export function plan(opts) {
  const { target, name, stack, wiring = "local", now = new Date() } = opts;
  if (!target) throw new InitError("usage", "--target <dir> is required");
  if (!name || !/^[A-Za-z0-9][A-Za-z0-9 _.\-À-ſĀ-ɏ]*$/u.test(name)) {
    throw new InitError("usage", "--name <ProjectName> is required (letters, digits, space, _ . -)");
  }
  if (!STACKS.includes(stack)) throw new InitError("usage", `--stack must be one of ${STACKS.join("|")}`);
  if (!WIRINGS.includes(wiring)) throw new InitError("usage", `--wiring must be one of ${WIRINGS.join("|")}`);

  const frameworkRoot = validateFrameworkRoot(opts.frameworkRoot ?? resolve(HERE, "..", ".."));
  const targetAbs = resolve(target);
  if (targetAbs === frameworkRoot) throw new InitError("target_is_framework", "target must not be the framework checkout itself");
  if (existsSync(join(targetAbs, "AgentReports", "StatusUpdate.md"))) {
    throw new InitError("already_initialized", `${targetAbs} already has AgentReports/StatusUpdate.md — this is a Nissth project; resume via the boot protocol (§1), do not re-init`);
  }

  const wiringSentence =
    wiring === "local"
      ? `local checkout — \`NISSTH_FRAMEWORK_ROOT\` defaults to \`${frameworkRoot}\`; moving that directory breaks the launchers until the env var is set`
      : "git submodule at `Tools/Nissth/` — run `git submodule add <nissth-remote> Tools/Nissth` and `git submodule update --init`";

  const vars = {
    NAME: name,
    DATE: localDate(now),
    DATE_TIME: localDateTime(now),
    VERSION: `v${VERSION}`,
    STACK: stack,
    STACK_SENTENCE: STACK_SENTENCE[stack],
    FRAMEWORK_ROOT: frameworkRoot,
    WIRING_SENTENCE: wiringSentence,
  };

  const files = []; // { rel, content, executable, role }
  const add = (rel, content, role, executable = false) => files.push({ rel, content: lf(content), role, executable });

  // CLAUDE.md = banner + framework body
  add("CLAUDE.md", render(readTemplate("CLAUDE.banner.md"), vars) + "\n" + frameworkBody(readFramework(frameworkRoot, "CLAUDE.md")), "framework rules + project banner");

  // AGENTS.md with the project name
  const agents = readFramework(frameworkRoot, "AGENTS.md");
  const agentsMarker = /This project \(\*\*Nissth\*\*\)/;
  if (!agentsMarker.test(agents)) throw new InitError("framework_agents_shape", "framework AGENTS.md no longer contains `This project (**Nissth**)`");
  add("AGENTS.md", agents.replace(agentsMarker, `This project (**${name}**, a Nissth consumer)`), "non-Claude agent redirect");

  // templates copied verbatim (LF-normalised)
  for (const rel of ["ImplementationPlans/_TEMPLATE.md", "DBL/Summaries/_TEMPLATE.md", "DBL/DependencyMaps/_TEMPLATE.md", "DBL/APIIndex/_TEMPLATE.md", "DBL/SchemaIndex/_TEMPLATE.md"]) {
    add(rel, readFramework(frameworkRoot, rel), "framework template");
  }

  // keepers
  for (const rel of ["AgentReports/Reports/.gitkeep", "AgentReports/Bridge/.gitkeep", "AgentReports/Snapshots/.gitkeep", "Tools/.gitkeep"]) {
    add(rel, "", "directory keeper");
  }
  // Tests/ gets a README, not a keeper: the "test sources live here, never tests/" rule (CLAUDE.md §5)
  // was one tree comment that a consumer agent never saw — PostPilot Phase 00, 2026-09-13.
  add("Tests/README.md", readTemplate("Tests.README.md"), "test-sources root; states the Tests/ rule");
  // Same shape, same project: §5 named AgentReports/Archive/ but init never created it and no
  // procedure existed, so PostPilot invented a rotation at ~100 KB and carried the gap as an open
  // item from 2026-09-19 to Phase 22. The directory now ships with the procedure inside it.
  add("AgentReports/Archive/README.md", readTemplate("Archive.README.md"), "rotated ledger slices; states the rotation procedure");

  // .claude/settings.json — base allow-list + stack additions; never a bypass key
  const base = JSON.parse(readTemplate("settings.json"));
  const extra = JSON.parse(readTemplate(`settings.${stack}.json`));
  base.permissions.allow = [...base.permissions.allow, ...extra];
  add(".claude/settings.json", JSON.stringify(base, null, 2) + "\n", "narrow Claude Code allow-list");

  // .gitignore / .gitattributes
  const ignoreHeader = `# ${name} — repo-root ignore (installed by nissth-init, stack: ${stack})\n\n`;
  const ignore = readTemplate("gitignore.none") + (stack === "none" ? "" : readTemplate(`gitignore.${stack}`));
  add(".gitignore", ignoreHeader + ignore, "stack-specific ignore rules");
  add(".gitattributes", readTemplate("gitattributes"), "LF baseline");

  // launchers, with DEFAULT_ROOT filled in for local wiring
  let sh = readFramework(frameworkRoot, "Tools/nissth-bridge/consumer-launcher/nissth-bridge");
  let ps1 = readFramework(frameworkRoot, "Tools/nissth-bridge/consumer-launcher/nissth-bridge.ps1");
  if (!sh.includes('DEFAULT_ROOT=""') || !ps1.includes("$DefaultRoot = ''")) {
    throw new InitError("framework_launcher_shape", "consumer-launcher templates no longer carry the DEFAULT_ROOT placeholders");
  }
  if (wiring === "local") {
    sh = sh.replace('DEFAULT_ROOT=""', `DEFAULT_ROOT="${posixPath(frameworkRoot)}"`);
    ps1 = ps1.replace("$DefaultRoot = ''", `$DefaultRoot = '${frameworkRoot.replace(/'/g, "''")}'`);
  }
  add("nissth-bridge", sh, "POSIX launcher", true);
  add("nissth-bridge.ps1", ps1, "PowerShell launcher");

  // StatusUpdate.md = preamble + Bootstrap entry (lists everything above plus itself)
  const statusRel = "AgentReports/StatusUpdate.md";
  const allRels = [...files.map((f) => f.rel), statusRel].sort();
  const roleOf = Object.fromEntries(files.map((f) => [f.rel, f.role]));
  roleOf[statusRel] = "append-only ledger (this file)";
  const srs = existsSync(join(targetAbs, "ImplementationPlans", "SRS.md"));
  const sdd = existsSync(join(targetAbs, "ImplementationPlans", "SDD.md"));
  const entryVars = {
    ...vars,
    FILE_LIST: allRels.map((r) => `- \`${r}\` — ${roleOf[r]}`).join("\n"),
    FILE_COUNT: String(allRels.length),
    SRS_SDD_STATUS:
      srs && sdd
        ? "both present at init time (approval state not checked by init)"
        : `absent (${[!srs && "SRS.md", !sdd && "SDD.md"].filter(Boolean).join(", ")}) — author and get approval before Phase 00`,
  };
  add(statusRel, render(readTemplate("StatusUpdate.preamble.md"), vars) + "\n" + render(readTemplate("Bootstrap.entry.md"), entryVars), roleOf[statusRel]);

  // collisions — all or nothing
  const collisions = files.filter((f) => existsSync(join(targetAbs, f.rel))).map((f) => f.rel);
  if (collisions.length) {
    throw new InitError("file_exists", `refusing to overwrite ${collisions.length} existing file(s) in ${targetAbs}; nothing written`, collisions);
  }

  return { target: targetAbs, frameworkRoot, name, stack, wiring, files };
}

// ---------------------------------------------------------------- apply

export function apply(p) {
  for (const f of p.files) {
    const abs = join(p.target, f.rel);
    if (existsSync(abs)) throw new InitError("file_exists", `race: ${f.rel} appeared before write; aborting`, [f.rel]);
  }
  const created = [];
  for (const f of p.files) {
    const abs = join(p.target, f.rel);
    mkdirSync(dirname(abs), { recursive: true });
    writeFileSync(abs, f.content, { encoding: "utf8", flag: "wx" });
    if (f.executable && process.platform !== "win32") chmodSync(abs, 0o755);
    created.push(f.rel);
  }
  return created;
}

// ---------------------------------------------------------------- check

/**
 * Compare an initialised consumer against this framework checkout.
 *
 * A consumer's `CLAUDE.md` is a project banner followed by the framework body
 * *verbatim* — that is what the banner promises the next agent. Nothing enforced
 * it: two Phase 18 hunks sat unsynced in one consumer for ten days, and the only
 * reason they surfaced is that Phase 20 happened to diff the two files by hand.
 * `doc-claims` makes the same argument about repo-root prose (§12.1).
 *
 * Reports; never writes. The caller re-syncs.
 */
export function checkConsumer(targetDir, frameworkRoot) {
  const targetAbs = resolve(targetDir);
  if (!existsSync(targetAbs) || !statSync(targetAbs).isDirectory()) {
    throw new InitError("usage", `--check needs a directory; ${targetAbs} is not one`);
  }
  const claudeMd = join(targetAbs, "CLAUDE.md");
  if (!existsSync(claudeMd) || !existsSync(join(targetAbs, "AgentReports", "StatusUpdate.md"))) {
    throw new InitError("not_a_consumer", `${targetAbs} has no CLAUDE.md + AgentReports/StatusUpdate.md — not an initialised Nissth project`);
  }
  const fwRoot = validateFrameworkRoot(frameworkRoot);
  const expected = frameworkBody(readFramework(fwRoot, "CLAUDE.md")).split("\n");

  let actual;
  try {
    // lf() first: a CRLF checkout must not read as drift on every single line.
    actual = frameworkBody(lf(readFileSync(claudeMd, "utf8"))).split("\n");
  } catch {
    // A consumer CLAUDE.md with no banner rule cannot be split into banner + body.
    return {
      target: targetAbs,
      frameworkRoot: fwRoot,
      inSync: false,
      bodyShape: "unreadable",
      driftLines: null,
      firstDrift: null,
      missing: missingSkeleton(targetAbs),
    };
  }

  let firstDrift = null;
  let driftLines = 0;
  for (let i = 0; i < Math.max(expected.length, actual.length); i++) {
    if (expected[i] === actual[i]) continue;
    driftLines++;
    if (firstDrift === null) {
      firstDrift = {
        line: i + 1,
        expected: expected[i] ?? "(end of framework body)",
        actual: actual[i] ?? "(end of consumer body)",
      };
    }
  }
  const missing = missingSkeleton(targetAbs);
  return {
    target: targetAbs,
    frameworkRoot: fwRoot,
    inSync: driftLines === 0 && missing.length === 0,
    bodyShape: "ok",
    driftLines,
    firstDrift,
    missing,
    openFeedback: openFeedback(targetAbs),
  };
}

/**
 * Rows a consumer's framework-feedback digest still marks `open`.
 *
 * A consumer records framework friction in its own Report and feeds it upstream
 * (CLAUDE.md §10). Nothing pointed the other way: one digest carried ten open
 * items for ten days while Nissth sessions came and went, because the only copy
 * lived in a repo no framework session opens. This makes the list readable from
 * here — reported, never counted as drift, because an open feedback row is
 * information about the framework, not a defect in the consumer.
 */
export function openFeedback(targetAbs) {
  const dir = join(targetAbs, "AgentReports", "Reports");
  if (!existsSync(dir)) return [];
  const rows = [];
  for (const name of readdirSync(dir)) {
    if (!/feedback/i.test(name) || !name.endsWith(".md")) continue;
    const text = readFileSync(join(dir, name), "utf8");
    for (const line of text.split(/\r?\n/)) {
      if (!line.trim().startsWith("|")) continue;
      const cells = line.split("|").map((c) => c.trim());
      if (!cells.some((c) => /^\*{0,2}open\*{0,2}$/i.test(c))) continue;
      const id = cells.find((c) => c && !/^:?-+:?$/.test(c)) ?? "?";
      const note = cells.slice(1).find((c) => c.length > 20) ?? "";
      rows.push({ file: `AgentReports/Reports/${name}`, id: id.replace(/\*/g, ""), note: note.slice(0, 120) });
    }
  }
  return rows;
}

/**
 * Print the window around the first differing character, not the first 96
 * characters: two lines that differ at column 400 look identical when truncated
 * from the left, which tells the reader nothing at all.
 */
function driftWindow(a, b, width = 92) {
  let i = 0;
  while (i < a.length && i < b.length && a[i] === b[i]) i++;
  const start = Math.max(0, i - Math.floor(width / 3));
  const cut = (line) =>
    (start > 0 ? "…" : "") +
    line.slice(start, start + width) +
    (line.length > start + width ? "…" : "");
  return [cut(a), cut(b)];
}

/** Skeleton paths §5 names that a consumer may predate (e.g. AgentReports/Archive/). */
function missingSkeleton(targetAbs) {
  const want = [
    "AgentReports/Reports",
    // AgentReports/Bridge is deliberately absent: §11.10 #6 has every consumer
    // gitignore it wholesale, so a fresh clone never carries it and the runtime
    // recreates it. Requiring it would fail every cloned consumer, which is how a
    // check teaches its reader to ignore it.
    "AgentReports/Snapshots",
    "AgentReports/Archive",
    "ImplementationPlans/_TEMPLATE.md",
    "DBL/Summaries",
    "DBL/DependencyMaps",
    "DBL/APIIndex",
    "DBL/SchemaIndex",
    "Tests/README.md",
  ];
  return want.filter((rel) => !existsSync(join(targetAbs, ...rel.split("/"))));
}

// ---------------------------------------------------------------- CLI

export function parseArgs(argv) {
  const out = { dryRun: false, json: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const val = () => {
      if (i + 1 >= argv.length) throw new InitError("usage", `${a} needs a value`);
      return argv[++i];
    };
    switch (a) {
      case "--target": out.target = val(); break;
      case "--check": out.check = val(); break;
      case "--name": out.name = val(); break;
      case "--stack": out.stack = val(); break;
      case "--wiring": out.wiring = val(); break;
      case "--framework-root": out.frameworkRoot = val(); break;
      case "--dry-run": out.dryRun = true; break;
      case "--json": out.json = true; break;
      case "-h": case "--help": out.help = true; break;
      default: throw new InitError("usage", `unknown argument ${a}`);
    }
  }
  return out;
}

const USAGE = `nissth-init v${VERSION} — Nissth consumer bootstrap (CLAUDE.md §9.1 step 2)

  node init.mjs --target <dir> --name <ProjectName> --stack <${STACKS.join("|")}>
                [--wiring ${WIRINGS.join("|")}] [--framework-root <abs>] [--dry-run] [--json]
  node init.mjs --check <dir> [--framework-root <abs>] [--json]

Creates the control-file skeleton only. Refuses to overwrite anything (exit 2).
Runs no subprocess. Does not git init, install dependencies, or author SRS/SDD/Phase 00.

--check compares an already-initialised consumer against this framework checkout:
the CLAUDE.md framework body must be verbatim, and the §5 skeleton directories
must exist. It reports and never writes.

Exit codes: 0 done / in sync · 1 drift found (--check) · 2 usage/refusal · 3 write failure.`;

function main(argv) {
  let args;
  try {
    args = parseArgs(argv);
  } catch (e) {
    process.stderr.write(`${e.message}\n\n${USAGE}\n`);
    return 2;
  }
  if (args.help) {
    process.stdout.write(USAGE + "\n");
    return 0;
  }
  if (args.check !== undefined) {
    let r;
    try {
      r = checkConsumer(args.check, args.frameworkRoot ?? resolve(HERE, "..", ".."));
    } catch (e) {
      if (!(e instanceof InitError)) throw e;
      if (args.json) process.stdout.write(JSON.stringify({ ok: false, error_code: e.code, error: e.message }, null, 2) + "\n");
      else process.stderr.write(`nissth-init --check: ${e.message}\n`);
      return 2;
    }
    if (args.json) {
      process.stdout.write(JSON.stringify({ ok: r.inSync, ...r }, null, 2) + "\n");
      return r.inSync ? 0 : 1;
    }
    // Open feedback is information about the framework, not a defect in the
    // consumer, so it prints in both branches and changes no exit code.
    const feedback = () => {
      if (!r.openFeedback?.length) return;
      process.stdout.write(`  open framework feedback raised by this consumer: ${r.openFeedback.length} row(s)\n`);
      for (const row of r.openFeedback.slice(0, 8)) {
        process.stdout.write(`    ${row.id}${row.note ? ` — ${row.note}` : ""}  (${row.file})\n`);
      }
      if (r.openFeedback.length > 8) process.stdout.write(`    …and ${r.openFeedback.length - 8} more\n`);
    };
    if (r.inSync) {
      process.stdout.write(`nissth-init --check: ${r.target} is in sync with ${r.frameworkRoot}\n`);
      feedback();
      return 0;
    }
    if (r.bodyShape === "unreadable") {
      process.stdout.write(`nissth-init --check: ${r.target}/CLAUDE.md has no banner rule — its framework body cannot be located\n`);
    } else if (r.driftLines) {
      process.stdout.write(
        `nissth-init --check: ${r.target}\n` +
          `  CLAUDE.md framework body differs on ${r.driftLines} line(s); first at body line ${r.firstDrift.line}\n` +
          `    framework: ${driftWindow(r.firstDrift.expected, r.firstDrift.actual)[0]}\n` +
          `    consumer:  ${driftWindow(r.firstDrift.expected, r.firstDrift.actual)[1]}\n` +
          `  the banner promises this body is verbatim — re-sync from ${r.frameworkRoot}/CLAUDE.md, keeping the consumer's banner\n`
      );
    }
    if (r.missing.length) {
      process.stdout.write(`  missing skeleton path(s): ${r.missing.join(", ")}\n`);
    }
    feedback();
    return 1;
  }
  let p;
  try {
    p = plan(args);
  } catch (e) {
    if (!(e instanceof InitError)) throw e;
    if (args.json) process.stdout.write(JSON.stringify({ ok: false, error_code: e.code, error: e.message, details: e.details }, null, 2) + "\n");
    else process.stderr.write(`nissth-init: ${e.message}${e.details.length ? "\n  " + e.details.join("\n  ") : ""}\n`);
    return 2;
  }
  if (args.dryRun) {
    if (args.json) process.stdout.write(JSON.stringify({ ok: true, dry_run: true, target: p.target, framework_root: p.frameworkRoot, would_create: p.files.map((f) => f.rel) }, null, 2) + "\n");
    else process.stdout.write(`nissth-init (dry run) — would create ${p.files.length} files in ${p.target}:\n` + p.files.map((f) => `  ${f.rel}`).join("\n") + "\n");
    return 0;
  }
  let created;
  try {
    created = apply(p);
  } catch (e) {
    if (args.json) process.stdout.write(JSON.stringify({ ok: false, error_code: e.code ?? "write_failed", error: e.message }, null, 2) + "\n");
    else process.stderr.write(`nissth-init: write failed — ${e.message}\n`);
    return 3;
  }
  if (args.json) {
    process.stdout.write(JSON.stringify({ ok: true, target: p.target, framework_root: p.frameworkRoot, stack: p.stack, wiring: p.wiring, created }, null, 2) + "\n");
  } else {
    process.stdout.write(
      `nissth-init: created ${created.length} files in ${p.target} (stack ${p.stack}, wiring ${p.wiring}, framework ${p.frameworkRoot})\n` +
        `  reminder: HR#13 — this run must have been preceded by the user's explicit consent; init cannot check that.\n` +
        `  next: run ./nissth-bridge.ps1 --list-bindings (or ./nissth-bridge), git init if desired, SRS + SDD (§9), then Phase_00_DBL_Bootstrap.md.\n` +
        `\n` +
        `  HANDOFF — initialisation is complete and this session's work on ${p.name} is done.\n` +
        `  Open the next session IN the new project:  ${p.target}\n` +
        `  A session run from the framework checkout boots the framework's ledger, not ${p.name}'s,\n` +
        `  and every shell call returns to the framework's directory. Continue there, from §1.\n`
    );
  }
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
