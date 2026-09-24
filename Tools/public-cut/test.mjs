import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { stripAxiomRow, scrub, loadScrubMap, preflight, planCut, CutError } from "./cut.mjs";

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
