# Nissth Diagnostic Bridge — Expo Binding

Implements the Nissth Diagnostic Bridge contract (`CLAUDE.md` §11, `Bindings/_schemas/bridge-command.schema.json`) for Expo / React Native projects.

**Binding ID:** `expo` &nbsp;·&nbsp; **Version:** `0.1.0` &nbsp;·&nbsp; **Language:** TypeScript 5+ &nbsp;·&nbsp; **Build:** npm + tsc (target Node 20+)

This binding's own build is npm. The diagnostic tools target Expo Router projects (Expo SDK 50+ with the `app/` directory + file-based routing); legacy `@react-navigation/*` apps without Expo Router are out of scope.

---

## Tool catalog

| Tool | Kind | Modes | Key scope fields | Freshness source |
|:---|:---|:---|:---|:---|
| `route_lens` | diagnostic | `default`, `with_params` | `root_path`, `package`, `max_depth` | Filesystem walk of `app/` + ts-morph AST parse of each route file |
| `component_lens` | diagnostic | `default`, `with_hooks` | `root_path`, `package`, `max_depth` | ts-morph AST parse of `**/*.{ts,tsx}` under scope |

**Stale-flip condition (CLAUDE.md §11.4), as implemented.** A lens flips a DBL artifact only
on evidence that lives under **that artifact's own `covers`**: `component_lens` compares the
artifact's body against the components whose files it covers — an artifact covering no
component file is never flipped — and `route_lens` compares route sets **canonically**, so a
table written `/account/:id` does not "drift" from the live `/account/[id]`. The flip itself is
a single-line edit: `last_regenerated` is replaced in the raw text and every other byte of the
frontmatter, including long unwrapped lines, is preserved (`Tools/dbl-check` used to report
`bad-frontmatter` on files the Bridge had just rewritten). Phase 19.
| `dependency_audit` | diagnostic | `default` | `root_path` | `package.json` + lockfile cross-check vs ts-morph import scan, `tsconfig.paths`-aware |
| `expo_doctor_lens` | diagnostic | `default` · `verbose` | `root_path` | Subprocess: `npx --yes expo-doctor` (every invocation) |
| `route_scaffold` | **action** | `default` | `root_path` + `scope.extra` (see below) | Direct file write of route + matching Jest test, atomic |

### What the two lenses do *not* report (Phase 21)

Both filters exist because a report full of false rows is a report the reader stops
opening — the consumer that raised these ran `dependency_audit` for eight phases and
hand-discounted nine rows every time.

- **`dependency_audit` reads `tsconfig.json` `compilerOptions.paths`** (following relative
  `extends`, JSONC tolerated). A specifier matching an alias — `@/db`, `~/lib/x` — is
  internal and produces no finding; without a `paths` entry, `@scope/pkg` is still a package
  and still reported as `missing`. The aliases in force are printed in the report header.
- **Build-time config files do not count as production usage.** A devDependency imported
  only from `*.config.{ts,js,mjs,cjs}` or `jest.setup.*` is `used`, not `dev_in_prod` —
  `drizzle-kit` in `drizzle.config.ts` is a devDependency doing exactly its job. Tests and
  `__mocks__` are excluded as before, and the exclusion list is printed in the header.
- **`expo_doctor_lens` reads the summary counts.** expo-doctor 1.x names individual checks
  only under `--verbose`; a clean default run prints `Running 21 checks…` then
  `21/21 checks passed.` and nothing else, which the pre-Phase-21 parser reported as
  "Checks parsed: 0". The report now states `21/21 passed · 0 failed`, lists any failure with
  its detail block, and `--mode verbose` returns the full per-check table. A failing check is
  named twice in verbose output (list line + detail block) and is recorded once.

### Hard-enforce contracts

Two tools enforce previously-soft `CLAUDE.md` rules at the runtime layer (`CLAUDE.md` §11.7):

- **`route_scaffold`** — refuses to commit a partial state. Writes `app/<route_path>.tsx` AND the matching `__tests__/<route_path>.test.tsx` in one atomic operation; on partial failure (target dir read-only, route file already exists, parent `_layout.tsx` missing without `force_create_layout=true`), rolls back any successful writes and exits 5 with `stage="execute"`, `error_code="hard-enforce_route_pair_atomicity"`. Enforces `CLAUDE.md` §8.2.8 (Expo route ripple — every new screen comes with a test).
- **`expo_doctor_lens`** — every invocation actually spawns the subprocess; never returns cached output. If npx is unavailable or `expo-doctor` cannot be fetched (offline host), returns `stage="execute"`, `error_code="expo_doctor_unavailable"` with an exit code 3 — not a cached PASS.

---

## `scope.extra` keys (per tool)

The cross-stack contract (`CLAUDE.md` §11.2) allows binding-specific filters in `scope.extra`. This binding consumes them only for `route_scaffold`:

| Tool | Key | Type | Required | Default | Meaning |
|:---|:---|:---|:---:|:---|:---|
| `route_scaffold` | `route_path` | string | yes | — | Route path relative to `app/`, no leading slash, no `.tsx` extension. E.g., `settings/account`, `profile/[id]`. |
| `route_scaffold` | `component_name` | string | yes | — | PascalCase component name. E.g., `AccountScreen`. |
| `route_scaffold` | `has_params` | boolean | no | `false` | When true, the route component receives params via `useLocalSearchParams()` typed by `params_type`. |
| `route_scaffold` | `params_type` | string | conditional | — | Inline TypeScript type literal, e.g., `{ id: string }`. Required when `has_params=true`; ignored otherwise. |
| `route_scaffold` | `force_create_layout` | boolean | no | `false` | When true, scaffolds a parent Stack-layout (`_layout.tsx`) atomically alongside the route + test if the parent layout is missing. When false (default) and parent layout is missing, refuses with `error_code="layout_missing"`. |

The other four tools do not consume any `scope.extra` keys — only top-level `scope.*` fields.

### Default route + test scaffold templates (`route_scaffold`)

When `route_scaffold` emits `app/<route_path>.tsx`, the template is:

```tsx
import { View, Text } from 'react-native';
{has_params ? "import { useLocalSearchParams } from 'expo-router';" : ""}

export default function <component_name>() {
  {has_params ? `const params = useLocalSearchParams<${params_type}>();` : ""}
  return (
    <View>
      <Text><component_name></Text>
    </View>
  );
}
```

The matching `__tests__/<route_path>.test.tsx`:

```tsx
import { render } from '@testing-library/react-native';
import <component_name> from '../app/<route_path>';

describe('<component_name>', () => {
  it('renders without crashing', () => {
    const { getByText } = render(<<component_name> />);
    expect(getByText('<component_name>')).toBeTruthy();
  });
});
```

Both files are written atomically. The consumer project is expected to have `@testing-library/react-native` available in `devDependencies`; `expo_doctor_lens` flags missing it as a WARN.

---

## Install

### Prerequisites

- **Node 20+** (tested with Node v22 LTS and Node v24 LTS).
- **npm 10+** (bundled with Node).
- The target project's own toolchain — Node, the Expo CLI via `npx expo` — for tools that drive subprocesses (only `expo_doctor_lens`).

### Build

```bash
cd Bindings/Expo
npm ci             # clean install from lockfile (for verification runs)
# or:
npm install        # for dev / first run
npm run build      # tsc -p .
npm test           # jest (runs unit + integration + contract tests)
```

Produces:
- `dist/cli/index.js` — Node CLI entrypoint with shebang `#!/usr/bin/env node`
- `dist/core/*.js`, `dist/tools/*.js` — compiled binding source
- `coverage/` — Jest coverage when `--coverage` is passed

### Run from the repo

```bash
# POSIX shell
./scripts/nissth-bridge --list-tools
./scripts/nissth-bridge route_lens --scope.root_path ../../tests/fixture

# PowerShell
.\scripts\nissth-bridge.ps1 --list-tools
.\scripts\nissth-bridge.ps1 route_lens --scope.root_path ..\..\tests\fixture
```

### Put on PATH

Add the absolute path to `Bindings/Expo/scripts/` to your shell's PATH, or symlink `nissth-bridge` into `/usr/local/bin/` (POSIX). The launcher resolves the `dist/cli/index.js` path relative to its own location, so it works from any cwd.

**Cross-binding PATH collision heads-up:** Phase 05's SpringBoot binding ships its own `Bindings/SpringBoot/scripts/nissth-bridge` launcher with the same name. If both are on PATH, the user-set precedence wins. Cross-binding dispatch via a single top-level `nissth-bridge` is reserved for a later framework-hardening plan (see `CLAUDE.md` §11.5 + Phase 05 snapshot Report's "Implications for downstream bindings → Cross-cutting").

### MCP integration

The Node MCP shim under `mcp/` registers four MCP tools (`Nissth_Gateway`, `Nissth_Verify`, `Nissth_ReadReport`, `Nissth_Status`) that shell out to `nissth-bridge`. See `mcp/README.md` for the MCP server registration block.

---

## Where reports land

Every tool writes its report to `<repo-root>/AgentReports/Bridge/<tool>_<ISO8601>.md` by default (or `output.file_name` if provided). The report's mandatory frontmatter conforms to `Bindings/_schemas/bridge-command.schema.json` `$defs.reportFrontmatter` (see `CLAUDE.md` §11.3). Validation happens at write time; a failing write is `stage="format"` error.

---

## Pointers

- **Contract spec:** `CLAUDE.md` §11
- **JSON Schema:** `Bindings/_schemas/bridge-command.schema.json`
- **Expo stack rules:** `CLAUDE.md` §8.2 (forbidden patterns, verification protocol, DBL mapping, route ripple)
- **Plan that built this binding:** `ImplementationPlans/Phase_06_Bridge_Expo_FirstSlice.md`
- **Reference binding** (Spring Boot): `Bindings/SpringBoot/README.md`
