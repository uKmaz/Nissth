# Tests/

Nissth-owned root for **test sources and verification artifacts** (`CLAUDE.md` §5).

- Put the project's test project(s) here — e.g. `Tests/<Project>.Tests/`, `Tests/unit/`, `Tests/e2e/`.
- Verification output lands here too: TRX/JUnit reports under `Tests/Results/` (ignore it), `dbl-check` JSON, perf samples.
- **Never create `tests/` or `test/` at the repo root.** Case-insensitive filesystems (Windows, default macOS) merge them into this directory; a case-sensitive clone then sees two.
- A stack convention that mandates another location (Expo's `__tests__/` mirroring `app/`, Jest defaults) wins — record the divergence in `DBL/Summaries/_layout.md` and leave this directory to verification artifacts.
