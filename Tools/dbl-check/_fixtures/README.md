# dbl-check fixtures

| Fixture | Expected result |
|:---|:---|
| `clean/` | 4 artifacts (one per type) + a `_TEMPLATE.md` that must be ignored → 0 findings, `scanned: 4` |
| `stale/` | 1 artifact STALE-flipped in the exact binding format → 1 `stale-marked` (info); exit 0, exit 1 with `--strict` |
| `design-only/` | 2 design-only artifacts: `with-source.md` covers `src/**` and `src/x.ts` exists → 1 `design-only-source-exists` (error); `no-source.md` covers `lib/**` (absent) → clean |
| broken (built at test time) | no-frontmatter, missing keys, wrong type for dir, bad date, CRLF, over budget, unknown dir — one finding each; built in a temp dir because `.gitattributes` would normalise a committed CRLF file |
| git-ref (built at test time) | temp git repo; `source_state: <sha>`; a covered file modified after the commit → `covers-changed-since` (warn); unchanged → clean; no git → skip note |
