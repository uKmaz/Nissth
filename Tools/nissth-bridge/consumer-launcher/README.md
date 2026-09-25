# Consumer-side launcher template

The two launchers in this directory are what `Tools/nissth-init` installs into a **consumer project** so it can invoke `nissth-bridge` from its own root:

| File | Purpose |
|:---|:---|
| `nissth-bridge` | POSIX shell launcher (Linux / macOS / Git Bash / WSL) |
| `nissth-bridge.ps1` | PowerShell launcher (Windows) |
| `README.md` | This file |

The launchers try, in order, `$NISSTH_FRAMEWORK_ROOT`, the `Tools/Nissth/` git-submodule path, and a `DEFAULT_ROOT` that `nissth-init --wiring local` bakes in. The first that holds `Tools/nissth-bridge/dispatcher.js` wins; the dispatcher then handles binding discovery, tool routing, and the rest.

---

## One-time install in a consumer project

The agent runs this **after** the HR#13 permission gate (`CLAUDE.md` §9.1 step 0) and the SRS/SDD stop (step 1):

```sh
# from the Nissth checkout
node Tools/nissth-init/init.mjs --target ~/projects/my-app --name "My App" --stack expo
```

That is the whole of §9.1 step 2. It copies `CLAUDE.md` (with a project banner), `AGENTS.md`, the plan and DBL templates, a `StatusUpdate.md` with its schema preamble and a filled "Bootstrap" entry, `.gitignore`, `.gitattributes`, a narrow `.claude/settings.json`, and both launchers from this directory — LF-normalised, refusing to overwrite anything. Details and refusal codes: `Tools/nissth-init/README.md`.

Two wirings:

| `--wiring` | Launchers resolve the dispatcher via | When to use |
|:---|:---|:---|
| `local` (default) | `$NISSTH_FRAMEWORK_ROOT` → `Tools/Nissth/` submodule → **the Nissth checkout init ran from** (baked in as `DEFAULT_ROOT`) | You develop against a local Nissth checkout; framework fixes are visible to the consumer immediately. Moving the checkout breaks the launchers until the env var is set. |
| `submodule` | `$NISSTH_FRAMEWORK_ROOT` → `Tools/Nissth/` submodule → (empty) | You want a version-pinned, self-contained consumer. Then: `git submodule add https://github.com/uKmaz/Nissth Tools/Nissth && git submodule update --init --recursive` |

Verify from the consumer root:

```sh
./nissth-bridge --list-bindings      # Windows: .\nissth-bridge.ps1 --list-bindings
# Expected: expo, postgres, spring-boot
```

After that, **the user experience is identical to the Nissth repo itself.** `./nissth-bridge schema_lens ...`, `./nissth-bridge route_lens ...`, etc. — all work. Bridge reports land in **your project's** `AgentReports/Bridge/`, not the framework's.

---

## How the dispatcher resolves your framework

Per `CLAUDE.md` §11.15 (framework-root resolution), the dispatcher checks (in order):

1. **`NISSTH_FRAMEWORK_ROOT` env var.** Highest precedence. Path must contain a `Bindings/` subdir. Use this when you want to point at a Nissth checkout that ISN'T a submodule (e.g., a local clone you're actively developing against).
2. **`<repoRoot>/Tools/Nissth/`** — the submodule convention (`--wiring submodule`).
3. **`<repoRoot>`** — fallback (Nissth's own dogfooding when developing the framework).

Your project's CLAUDE.md is what makes it the "repo root." The dispatcher walks up from your cwd until it finds CLAUDE.md, then applies the resolution order above to find where the bindings live.

---

## Updating Nissth in your project

```sh
cd Tools/Nissth
git fetch origin
git checkout <tag-or-branch>      # e.g., v0.2.0 once tagged
cd ../..
git add Tools/Nissth
git commit -m "chore: bump Nissth submodule to <ref>"
```

The framework files (CLAUDE.md, AGENTS.md, templates) you copied into your project root are **not** auto-updated by the submodule bump. Inspect the diff between the submodule's new revision and what you have, and copy across the parts you want. Most framework files only change at phase boundaries (§5 numbering, new §11.X sections, etc.).

---

## Customizing CLAUDE.md for your project

`nissth-init` writes a project banner (title + two blockquote paragraphs) above the framework body, which it copies verbatim. You'll typically:

- Keep the **Status** paragraph of that banner current as phases close (it is the one part of the file that is yours).
- Replace the **mandatory inputs** wording in §9 with your project's SRS+SDD references.
- Add a **project-specific §10.4 trigger** if your team has a unique Report category (e.g., "regulatory audit" for fintech).
- Leave **§1–§8 and §11** untouched — those are the framework rules. Customizing them breaks the contract for other agents.

If you change §11 (the Bridge contract), you've forked the framework. Don't do that lightly.

---

## Pointers

- **Framework spec:** `Tools/Nissth/CLAUDE.md`
- **Dispatcher source:** `Tools/Nissth/Tools/nissth-bridge/dispatcher.js`
- **Dispatcher reference:** `Tools/Nissth/Tools/nissth-bridge/README.md`
- **Practical use guide:** `Tools/Nissth/Ultimate_Guide.md`
- **Framework-root resolution spec:** `Tools/Nissth/CLAUDE.md` §11.15
