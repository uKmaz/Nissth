# Phase 3 — Assembly Definitions (Lean, with Granular-Ready Hooks)

> **Mirrored from** `C:\Users\admin\.claude\plans\jazzy-wobbling-wilkinson.md` after exiting plan mode. This is the canonical project copy. The harness scratchpad is no longer authoritative.
> **Status:** APPROVED — execution in progress.
>
> **Scope adjustment (during execution, 2026-05-01 ~21:30):** pre-flight grep audit revealed that nearly every Shared-candidate folder (Interfaces, MainReSource, Util, Mapping) contains files that import `Levels`, `Missions`, `Entities`, `Views`, etc. — Runtime-layer namespaces. Putting Shared above Runtime creates cycles. **Dropped DragonBlast.Shared from Phase 3.** Going with **3 asmdefs**: Cygnus + DragonBlast.Runtime + DragonBlast.Editor. Sub-namespacing `Managers.*` still happens. Shared extraction deferred to Phase 3.5 once the offending files are moved into Runtime as part of cycle-resolution. This is the 80% iteration-speed win without risk.

## Context

DragonBlast (Unity 6 game project at `C:\Users\admin\Desktop\DragonBlast\`) currently has zero asmdefs on game code. Every script edit recompiles `Assembly-CSharp` (~300 source files) — a major drag on solo iteration. After Phase 1 (cleanup) and Phase 2 (folder consolidation, MyObject→BlastObject rename), the codebase is structurally ready for asmdef partitioning.

The cycle constraints discovered during exploration prevent a clean granular split today: `Level` is referenced by `Constraint/`, `Missions/`, `GenerateObjectInLevel/`, `BaloonCollected/`, `UI/`, and many `Managers/` files (via `Level.ActiveLevel` static, event subscriptions, and direct references). Splitting `Levels` into its own asmdef would force `Levels` to also reference everything that consumes it — a cycle. Breaking these cycles requires extracting interfaces (`ILevelEvents`, `ILevelHost`, etc.) — that's actual refactoring, not asmdef setup.

**User decision (confirmed):** lean 4-asmdef plan now; design it so Phase 5 (singleton elimination) and Phase 6 (`Find*` removal) naturally unblock a future granular split. Sub-namespace `Managers.*` now to match folder structure; pre-stage that boundary so a future `DragonBlast.Managers` asmdef extraction is a 1-step move rather than 50 file edits.

## Final Asmdef Layout

| # | Asmdef | Location | Folders covered | References | precompiledReferences |
|---|---|---|---|---|---|
| 1 | `Cygnus` | `Assets/Lib/Cygnus/Cygnus.asmdef` | Entire `Assets/Lib/Cygnus/` tree + relocated `MyObject.cs` partial | (none) | 8 DLLs (see §3) |
| 2 | `DragonBlast.Shared` | `Assets/Scripts/_Shared/DragonBlast.Shared.asmdef` | `Interfaces/`, `Data/`, `MainReSource/`, `Util/`, `Mapping/` (all moved into `_Shared/`) | `Cygnus`, `Unity.TextMeshPro` (if Util needs it) | (none) |
| 3 | `DragonBlast.Runtime` | `Assets/Scripts/DragonBlast.Runtime.asmdef` | Everything else under `Assets/Scripts/` (Entities, Levels, Managers, UI, Missions, Constraint, Economy, GenerateObjectInLevel, BaloonCollected, Views, VFX, SpineScripts, Runtime) — recursive minus `_Shared/` | `Cygnus`, `DragonBlast.Shared`, `Zenject`, `spine-unity`, `spine-csharp`, `Coffee.UIParticle`, `Unity.InputSystem`, `Unity.Cinemachine`, `Unity.TextMeshPro`, `Unity.RenderPipelines.Universal.Runtime` | (DOTween if it's DLL-only) |
| 4 | `DragonBlast.Editor` | `Assets/Editor/DragonBlast.Editor.asmdef` | `Assets/Editor/` (3 tools) | `DragonBlast.Shared`, `DragonBlast.Runtime`, `Cygnus`, `Zenject`. **includePlatforms: ["Editor"]** | (none) |

Existing asmdefs (Zenject, spine-csharp, spine-unity, Coffee.UIParticle, Axiom.Editor.AgentBridge, etc.) remain unchanged.

## Pre-Stage Work

### A. Sub-namespace `Managers.*`
9 subfolders → `Managers.Auth`, `Managers.Audio`, `Managers.Camera`, `Managers.Analytics`, `Managers.Network`, `Managers.Core`, `Managers.Levels`, `Managers.User`, `Managers.Events`. Consumers updated.

### B. Other namespace fixes
- `Data/ClientData.cs` + `Data/ClientSpaceBlast.cs`: `Assets.Scripts.Data` → `Data`
- Verify VFX/Runtime/UI subfolders have proper namespaces

### C. Move MyObject.cs into Cygnus tree
`Assets/Scripts/MyObject.cs` → `Assets/Lib/Cygnus/Common/MyObject.Partial.cs` (partial-class same-assembly rule). Renamed to disambiguate from sibling.

### D. Move 5 Shared folders into `_Shared/`
`Interfaces/`, `Data/`, `MainReSource/`, `Util/`, `Mapping/` → all under `Assets/Scripts/_Shared/`. GUIDs preserved. Namespaces unchanged.

## Cygnus Asmdef JSON

```json
{
  "name": "Cygnus",
  "rootNamespace": "Cygnus",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [
    "CygnusAttributes.dll",
    "CygnusCommon.dll",
    "CygnusNetworking.dll",
    "MessagePack.dll",
    "MessagePack.Annotations.dll",
    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
    "Microsoft.Extensions.Logging.Abstractions.dll",
    "System.Collections.Immutable.dll"
  ],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`autoReferenced: true` initially; flip to `false` after Runtime explicitly references Cygnus.

## Execution Order

Each step gated by `Axiom_Verify compilation` until CLEAN.

0. ✅ **Mirror plan to `ImplementationPlans/03_Phase3_Asmdefs.md`** (this file).
1. **Snapshot** — `SceneDiff.Snapshot label: "before_phase_3"` for rollback.
2. **Pre-flight grep audit** — verify Shared candidate folders don't consume Runtime-layer namespaces.
3. **Move `Assets/Scripts/MyObject.cs` → `Assets/Lib/Cygnus/Common/MyObject.Partial.cs`** (AssetDatabase). Verify.
4. **Sub-namespace `Managers/*`** — rewrite namespaces + update consumer `using` directives. Verify.
5. **Other namespace fixes** — `Assets.Scripts.Data` → `Data`; orphan namespaces. Verify.
6. **Move 5 Shared folders into `_Shared/`** (AssetDatabase). Verify.
7. **Create `Cygnus.asmdef`** at `Assets/Lib/Cygnus/`. Verify.
8. **Create `DragonBlast.Shared.asmdef`** at `Assets/Scripts/_Shared/`. Verify.
9. **Create `DragonBlast.Runtime.asmdef`** at `Assets/Scripts/`. Verify.
10. **Flip Cygnus `autoReferenced: false`**. Verify.
11. **Create `DragonBlast.Editor.asmdef`** at `Assets/Editor/`. Verify.
12. **Iteration-speed benchmark** — edit Bubble.cs and CameraOrtSize.cs; measure recompile deltas.
13. **Update `AgentReports/StatusUpdate.md`** with Phase 3 entry (append-only).

## Risk Mitigation, Verification, Effort Estimate

(Same as the harness scratchpad — see that file for full risk table and verification gates. Summary: GUID-stable moves prevent scene/prefab regression; compile-error iteration handles missing usings; Editor asmdef must reference both Runtime and Shared; ~6 file moves, 4 new asmdef files, ~32 namespace rewrites, ~50 consumer using updates; 11–13 compile cycles; 2–3 hours wall-clock.)
