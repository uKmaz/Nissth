import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { execFileSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { stripAxiomRow, stripChildRow, scrub, residue, loadScrubMap, preflight, planCut, CutError } from "./cut.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, "..", "..");
const map = loadScrubMap();

// --- the strip, against the two real trees ----------------------------------
// Fixtures would not have caught the three shape changes that broke this by hand;
// the real documents are the only input that matters.

for (const rel of ["CLAUDE.md", "README.md"]) {
  test(`stripAxiomRow removes the row and repairs the connector in ${rel}`, () => {
    const before = readFileSync(join(REPO, rel), "utf8");
    assert.match(before, /^└──\s+Axiom\//m, "the real document should still have an Axiom row");
    const after = stripAxiomRow(before, rel);
    assert.doesNotMatch(after, /^└──\s+Axiom\//m);
    assert.equal(after.split("\n").length, before.split("\n").length - 1);

    // The row above is promoted to last, and its children lose the spine.
    const lines = after.split("\n");
    const lastTop = lines.findIndex((l) => /^└──\s/.test(l));
    assert.ok(lastTop > 0, "some row must now carry the closing connector");
    for (let i = lastTop + 1; i < lines.length; i++) {
      if (/^[├└]──\s/.test(lines[i])) assert.fail(`a top-level row follows the closing connector: ${lines[i]}`);
      if (!lines[i].trim() || lines[i].startsWith("```")) break;
      assert.ok(!lines[i].startsWith("│"), `child of the promoted row still carries the spine: ${lines[i]}`);
    }
  });
}

test("stripAxiomRow raises rather than passing silently when the shape moves", () => {
  assert.throws(() => stripAxiomRow("├── Tools/\n└── Other/\n", "t"), (e) => e instanceof CutError && /STRIP PATTERN MISMATCH/.test(e.message));
  // Axiom present but nothing above it to promote.
  assert.throws(() => stripAxiomRow("└── Axiom/   ← x\n", "t"), /no "├──" row above/);
  // A second closing connector above means the tree is not the shape we assume.
  assert.throws(() => stripAxiomRow("├── A/\n└── B/\n└── Axiom/   ← x\n", "t"), /second "└──"/);
});

test("stripAxiomRow is exact on a synthetic tree", () => {
  const before = ["├── Bindings/", "│   └── Expo/", "├── Tools/", "│   ├── a/", "│   └── b/", "└── Axiom/   ← ref"].join("\n");
  const after = ["├── Bindings/", "│   └── Expo/", "└── Tools/", "    ├── a/", "    └── b/"].join("\n");
  assert.equal(stripAxiomRow(before), after);
});

// --- the scrub ---------------------------------------------------------------

test("scrub replaces every consumer identifier, longest first", () => {
  const before = [
    "package com.supruz.reservation;",
    "The Süprüz project and the Supruz backend",
    "Example-Frontend, Example-Backend and plain Example",
    "C:\\Users\\admin\\Desktop\\Nissth\\Tools",
    "ran in C:\\Users\\Ucmaz pc\\Git\\FinansY-netimApp",
    "and /c/Users/admin/Desktop/PostPilot",
    "paid via iyzico",
  ].join("\n");
  const after = scrub(before, map);
  for (const term of ["supruz", "Süprüz", "Example", "iyzico", "PostPilot", "FinansY", "Ucmaz", "Users\\admin", "Users/admin"]) {
    assert.ok(!after.includes(term), `"${term}" survived the scrub:\n${after}`);
  }
  assert.match(after, /com\.example\.reservation/);
  assert.match(after, /Example-Frontend/);
  assert.match(after, /<repo-root>/);
  assert.match(after, /ExampleDesktopApp/);
  assert.match(after, /ExampleFinanceApp/);
});

test("scrub leaves framework text alone", () => {
  const text = "Nissth's `nissth-bridge` dispatches to Bindings/Expo; see CLAUDE.md §11.15.";
  assert.equal(scrub(text, map), text);
});

test("the scrub map is well formed and every entry states its reason", () => {
  assert.ok(map.replacements.length >= 10);
  for (const r of map.replacements) {
    assert.ok(r.reason.length > 10, `thin reason: ${r.find}`);
    assert.doesNotThrow(() => new RegExp(r.find));
  }
  for (const d of [...map.delete, ...map.deleteGlobs]) assert.ok(d.reason);
});

// --- preconditions -----------------------------------------------------------

test("preflight sees this repo, and the Axiom hard gate has a count to check", () => {
  const p = preflight(REPO);
  assert.match(p.head, /^[0-9a-f]{40}$/);
  assert.equal(p.axiomOnDisk, true);
  assert.ok(p.axiomTracked > 0, "Axiom/ must be tracked for the hard gate to mean anything");
});

test("planCut refuses a directory whose Axiom/ is absent", () => {
  // The framework's own Tools/ directory is a git repo path with no Axiom/.
  assert.throws(() => planCut(join(REPO, "Bindings", "Expo")), (e) => e instanceof CutError && ["axiom_missing", "dirty_tree"].includes(e.code));
});

// --- the residue gate --------------------------------------------------------
// Regression: the first version shelled out to `git grep -E`, which rejects a
// `(?:…)` group, and read the resulting non-zero exit as "no matches". The gate
// reported clean while printing `fatal:`. It must now catch what the scrub leaves.

test("residue finds what the scrub missed, including patterns git grep -E cannot parse", () => {
  const d = mkdtempSync(join(tmpdir(), "nissth-residue-"));
  execFileSync("git", ["init", "-q"], { cwd: d });
  writeFileSync(join(d, "a.md"), "clean framework text\n", "utf8");
  writeFileSync(join(d, "b.md"), ["ran in C:", "Users", "admin", "Desktop", "PostPilot", "Tests"].join("\\") + "\n", "utf8");
  execFileSync("git", ["add", "-A"], { cwd: d });

  const hits = residue(d, map);
  const found = hits.map((h) => h.find);
  assert.ok(hits.length >= 2, `expected the consumer name and the user path to be caught, got ${JSON.stringify(found)}`);
  assert.ok(found.some((f) => /PostPilot/.test(f)), "the consumer name was not caught");
  // The pattern with the non-capturing group is the one git grep -E could not read.
  assert.ok(found.some((f) => f.includes("(?:Desktop|Git)")), "the (?:…) pattern was not evaluated at all");
  for (const h of hits) assert.match(h.sample, /b\.md:1:/);

  writeFileSync(join(d, "b.md"), scrub(readFileSync(join(d, "b.md"), "utf8"), map), "utf8");
  execFileSync("git", ["add", "-A"], { cwd: d });
  assert.deepEqual(residue(d, map), [], "a scrubbed tree must leave no residue");
  rmSync(d, { recursive: true, force: true });
});

test("stripChildRow removes a nested row and repairs the sibling connector", () => {
  const before = ["├── Tools/", "│   ├── a/", "│   └── public-cut/   ← x", "└── Axiom/"].join("\n");
  assert.equal(stripChildRow(before, "public-cut/"), ["├── Tools/", "│   └── a/", "└── Axiom/"].join("\n"));
  // Not the last child: no promotion needed.
  const mid = ["├── Tools/", "│   ├── public-cut/", "│   └── b/"].join("\n");
  assert.equal(stripChildRow(mid, "public-cut/"), ["├── Tools/", "│   └── b/"].join("\n"));
  assert.throws(() => stripChildRow("├── Tools/\n│   └── a/", "public-cut/"), /STRIP PATTERN MISMATCH/);
});

test("stripChildRow handles the real documents, which must list public-cut on dev", () => {
  for (const rel of ["CLAUDE.md", "README.md"]) {
    const before = readFileSync(join(REPO, rel), "utf8");
    assert.match(before, /public-cut\//, `${rel} should document the tool on the development branch`);
    const after = stripChildRow(stripAxiomRow(before, rel), "public-cut/", rel);
    assert.doesNotMatch(after.split("```")[1] ?? "", /public-cut/);
  }
});

test("machine names are scrubbed, ordinary capitalised words are not", () => {
  const before = "Host: DESKTOP-DQBFP0O (two monitors). Also WIN-AB12CD34 and LAPTOP-ZZZ999X1.";
  const after = scrub(before, map);
  assert.ok(!/DESKTOP-|WIN-|LAPTOP-/.test(after), after);
  assert.equal((after.match(/<host>/g) || []).length, 3);
  // Not a machine name: too short, and not the auto-generated shape.
  assert.equal(scrub("WIN-AB1 is prose", map), "WIN-AB1 is prose");
});

test("loadScrubMap refuses a pattern containing a control character", () => {
  // How this guard was earned: writing `\b` through a tool that ate one backslash
  // produced a literal backspace, and the pattern then matched nothing while
  // looking correct in the file — the silent-skip class, inside the scrub map.
  const d = mkdtempSync(join(tmpdir(), "nissth-map-"));
  const bad = join(d, "map.json");
  writeFileSync(bad, JSON.stringify({
    replacements: [{ find: "DESKTOP" + String.fromCharCode(8) + "-X", replace: "<host>", reason: "deliberately broken for this test" }],
    delete: [], deleteGlobs: [],
  }), "utf8");
  assert.throws(() => loadScrubMap(bad), (e) => e instanceof CutError && e.code === "bad_scrub_map" && /control character \(U\+0008\)/.test(e.message));
  rmSync(d, { recursive: true, force: true });

  // The shipped map is clean.
  assert.ok(loadScrubMap().replacements.every((r) => [...r.find].every((c) => c.charCodeAt(0) >= 0x20)));
});
