# plan-lint — validate a phase plan before it is approved

Checks every `ImplementationPlans/Phase_NN_*.md` against the `CLAUDE.md` §6 contract,
and — the part that earns the tool — against the `DBL/DependencyMaps/` artifacts whose
`covers` overlap what the plan's §3 says it will touch. Zero runtime dependencies,
Node 20+, no network.

```sh
node Tools/plan-lint/lint.mjs                                  # this checkout
node Tools/plan-lint/lint.mjs --root ../my-app                 # a consumer project
node Tools/plan-lint/lint.mjs --plan ImplementationPlans/Phase_07_Thing.md
node Tools/plan-lint/lint.mjs --json --strict
```

Exit **0** clean (info/warn only) · **1** error findings (any finding with `--strict`) ·
**2** usage or config error.

## Why this exists

§6 says every plan MUST conform to the template and §1.1 says to read the DBL. Nothing
checked either, and the gap has a dated cost: a consumer's Phase 06 plan placed classes
in a project that its own `DBL/DependencyMaps/layers.md` forbids tests from referencing.
The map was on disk, correct, and simply not read; the conflict surfaced at execution,
and the fix was a mid-phase relocation.

The same consumer shows the drift directly. Its plans 01–12 each cite `layers.md` in
§1.1; from plan 13 onward — a new wave of work, months later — the citation stops. **The
practice did not fail loudly. It just stopped**, and nothing noticed for six plans. Same
argument as §12.1 and §13.1: a rule that is followed and can still miss the defect needs
a mechanism, not a stricter restatement. That is why this is a tool and not Hard Rule #14.

## What it checks

| Check | Severity | Fires when |
|:---|:---|:---|
| `missing-section` | error | any of the seven `## 0.`–`## 6.` sections is absent (§6) |
| `plan-id-mismatch` | error | §0 `Plan ID` disagrees with the file name |
| `missing-key` | error | §0 has no `Plan ID`, `Approved` or `Depends on` |
| `bad-approved` | error | `Approved` is neither `pending` nor an ISO date |
| `approved-pending` | info | the plan is not approved yet — §3 must not run |
| `unknown-dependency` | error | `Depends on` names a plan that does not exist (short ids like `Phase_17` and reserved wildcards like `Phase_07_*` both resolve) |
| `empty-forbidden` | error | §3.2 has no entries — it is mandatory, and it is the anti-scope-creep guard |
| `missing-cited-artifact` | error | §1.1 cites a `DBL/…` or `ImplementationPlans/…` file that is not there |
| `dependency-map-not-cited` | error | §3 targets a path covered by a DependencyMap that states boundary rules, and §1.1 cites neither the map nor its file name |
| `no-step-targets` | info | §3 names no file paths — expected for a plan whose execution is a branch or a verification run |

`dependency-map-not-cited` does **not** claim the plan violates a boundary: a plan names
target files, not imports, so no honest tool can decide that from the plan alone. It
claims something narrower and checkable — *this plan works inside a boundary somebody
wrote down, and does not say it read it.* The finding quotes the map's first rule so the
author can settle it in one look.

## Waivers

A single plan can waive a single check in place:

```markdown
<!-- plan-lint:allow missing-cited-artifact - the artifacts on the line above are consumer-relative, not paths in this repo -->
```

The check name is required, and waiving one check never silences another. It is used once
in this repository, in `Phase_19`, for exactly the case it describes.

## What it is not

- **Not an action tool.** It reports and exits, like `doc-claims` and `dbl-check`. There
  is no `--fix`: a tool that rewrites a plan is a different risk with its own plan.
- **Not a Hard Rule.** §6 already carries the requirement.
- **Not wired into a hook or CI.** That remains a separate decision, open for all four
  validators.

## Tests

```sh
node --test Tools/plan-lint/test.mjs
```

The last case lints **this repository's real plan corpus** and asserts zero errors. A
linter that only passes its own fixtures is the fixture problem phases 19 and 23 both
recorded, and it is the reason the checks here were shaped against real plans and the two
live consumers' dependency maps rather than against the template alone.
