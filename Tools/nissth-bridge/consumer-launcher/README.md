# Consumer-side launcher template

Three files in this directory let a **consumer project** install Nissth as a git submodule and invoke `nissth-bridge` from its own root:

| File | Purpose |
|:---|:---|
| `nissth-bridge` | POSIX shell launcher (Linux / macOS / Git Bash / WSL) |
| `nissth-bridge.ps1` | PowerShell launcher (Windows) |
| `README.md` | This file |

The launchers expect the Nissth framework to live at `Tools/Nissth/` in your project (git submodule convention). The dispatcher inside the submodule then handles binding discovery, tool routing, and the rest.

---

## One-time install in a consumer project

Assume your project lives at `~/projects/my-app/` and you want to add Nissth to it.

```sh
cd ~/projects/my-app

# 1. Add Nissth as a submodule at the canonical path
git submodule add https://github.com/uKmaz/Nissth Tools/Nissth
git submodule update --init --recursive

# 2. Copy the consumer-side launchers to your project root
cp Tools/Nissth/Tools/nissth-bridge/consumer-launcher/nissth-bridge ./nissth-bridge
cp Tools/Nissth/Tools/nissth-bridge/consumer-launcher/nissth-bridge.ps1 ./nissth-bridge.ps1
chmod +x ./nissth-bridge

# 3. Copy the framework files into your project root
cp Tools/Nissth/CLAUDE.md ./CLAUDE.md
cp Tools/Nissth/AGENTS.md ./AGENTS.md
mkdir -p ImplementationPlans DBL/{Summaries,DependencyMaps,APIIndex,SchemaIndex} AgentReports/{Reports,Bridge,Snapshots} Tests Tools
cp Tools/Nissth/ImplementationPlans/_TEMPLATE.md ImplementationPlans/_TEMPLATE.md
cp Tools/Nissth/DBL/Summaries/_TEMPLATE.md DBL/Summaries/_TEMPLATE.md
cp Tools/Nissth/DBL/DependencyMaps/_TEMPLATE.md DBL/DependencyMaps/_TEMPLATE.md
cp Tools/Nissth/DBL/APIIndex/_TEMPLATE.md DBL/APIIndex/_TEMPLATE.md
cp Tools/Nissth/DBL/SchemaIndex/_TEMPLATE.md DBL/SchemaIndex/_TEMPLATE.md

# 4. Initialize StatusUpdate.md with a Bootstrap entry
#    (See CLAUDE.md §9.1 — the agent will guide you through this.)

# 5. Verify the dispatcher resolves the submodule's Bindings/
./nissth-bridge --list-bindings
# Expected: expo, postgres, spring-boot
```

After that, **the user experience is identical to the Nissth repo itself.** `./nissth-bridge schema_lens ...`, `./nissth-bridge route_lens ...`, etc. — all work. Bridge reports land in **your project's** `AgentReports/Bridge/`, not the submodule's.

---

## How the dispatcher resolves your framework

Per `CLAUDE.md` §11.15 (framework-root resolution), the dispatcher checks (in order):

1. **`NISSTH_FRAMEWORK_ROOT` env var.** Highest precedence. Path must contain a `Bindings/` subdir. Use this when you want to point at a Nissth checkout that ISN'T a submodule (e.g., a local clone you're actively developing against).
2. **`<repoRoot>/Tools/Nissth/`** — the submodule convention. What the launcher in this directory expects.
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

The CLAUDE.md you copied is the framework's reference. You'll typically:

- Replace the **Status** banner at the top with your own project state.
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
