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

import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync, chmodSync } from "node:fs";
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
  for (const rel of ["AgentReports/Reports/.gitkeep", "AgentReports/Bridge/.gitkeep", "AgentReports/Snapshots/.gitkeep", "Tests/.gitkeep", "Tools/.gitkeep"]) {
    add(rel, "", "directory keeper");
  }

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

Creates the control-file skeleton only. Refuses to overwrite anything (exit 2).
Runs no subprocess. Does not git init, install dependencies, or author SRS/SDD/Phase 00.
Exit codes: 0 done · 2 usage/refusal · 3 write failure.`;

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
        `  next: run ./nissth-bridge.ps1 --list-bindings (or ./nissth-bridge), git init if desired, SRS + SDD (§9), then Phase_00_DBL_Bootstrap.md.\n`
    );
  }
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
