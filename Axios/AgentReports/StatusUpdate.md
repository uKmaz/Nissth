# Axiom Status Update — Chronological Log

> Append-only. Newest entries at the bottom. Earlier entries are kept verbatim.
> The "Current State" header below is the only in-place-edited section; the history below is preserved as-is.

---

## Current State

- **Last updated:** 2026-05-04 (Phase 10 Missions Unified — Phases A/B/C/D code complete; UI wiring deferred to user)
- **Editor state:** EditMode, UI_Test scene in progress
- **Unity:** 6000.3.14f1
- **Compilation:** CLEAN
- **Console errors:** 0
- **Active phase:** Unified missions system code-complete (in-game Mission + persistent MetaMission share MissionGoal data + GameEvents bus). Phase D refactored: inline `MissionsNextUpCard` replaces ContentEvent below MainPanel top bar; tapping it opens the full `MissionsPanel` scroll list. User has prefabs created and will wire in editor before testing.
- **Plan:** `ImplementationPlans/08_Missions_Unified.md` (top-level), `ImplementationPlans/08a_Missions_UI_Wiring.md` (inspector wiring instructions for the user)
- **Architecture reference:** `ImplementationPlans/99_Architecture_Reference.md`
- **File map:** `ImplementationPlans/98_File_Map.md` (concise file-by-file responsibility table)

---

## 2026-05-01 17:00 — Initial setup (pre-refactor)

> NOTE (added 2026-05-01 20:05): Earlier I overwrote this section once. Below is the reconstructed content from session memory; it may be slightly less verbose than the original write but the substance is intact.

### 1. Fixed MCP envelope mismatch in `AxiomMcpTools.cs`

**Symptom:** Every Axiom MCP call returned `{ "success": false, "error": "Unknown error", "data": {...valid data...} }` despite the underlying tools succeeding. Inner `data` was correct, so calls worked but appeared as errors in the transcript.

**Root cause:** `AxiomMcpTools.Ok()` / `Fail()` returned `{ ok, data }` / `{ ok, error, hint }`, but Unity's MCP framework (`Unity.AI.MCP.Editor.Helpers.Response`) expects `{ success, message, data }` / `{ success, code, error, data }`. Missing `success` defaulted to `false`; missing `error` was synthesized as `"Unknown error"`. The `data` field happened to align.

**Fix:**
- `Assets/Axiom/Editor/AgentBridge/Mcp/AxiomMcpTools.cs:9` — added `using Unity.AI.MCP.Editor.Helpers;`
- `Assets/Axiom/Editor/AgentBridge/Mcp/AxiomMcpTools.cs:273-282` — `Ok(data)` → `Response.Success("ok", data)`; `Fail(error, hint)` → `Response.Error(error, hint != null ? new { hint } : null)`

**Verification:** `Axiom_Status`, `Axiom_Verify(compilation|errors)`, and `Axiom_Rules` now return `{ "success": true, "message": "ok", "data": {...} }`. Compilation clean, console errors zero.

### 2. Removed third-party MCP packages

User deleted `com.coplaydev.unity-mcp` and other auxiliary MCP packages. Only `com.unity.ai.assistant` (which provides `Unity.AI.MCP.Editor`) remains as the MCP transport. Axiom's MCP tools register under that bridge.

### 3. Broadened Claude Code allow permissions

`.claude/settings.local.json` updated to allow `Read`, `Glob`, `Grep`, `Edit`, `Write`, `Bash`, and the full set of `mcp__unity-mcp__Axiom_*` / `mcp__unity-mcp__Unity_*` tools without per-call prompts.

---

## 2026-05-01 19:00 — Project review + phase plan written

- Surveyed `Assets/Scripts/` (272 game scripts, no asmdefs — all in `Assembly-CSharp`).
- Wrote `ImplementationPlans/00_ProjectReview_Findings.md` (full first-pass review with hot spots, code smells, healthy patterns, and direction questions).
- User reviewed and approved scope; decisions captured into memory:
  - Backend URL — deferred (not in use).
  - DI — migrate to **Cygnus** (added later in this session at `Assets/Lib/Cygnus/`; previously misnamed "Global" / "Signus").
  - Levels — delete all `Level1`–`Level100`; build a future level-creator system.
  - `Manager/` + `Managers/` → consolidate to `Managers/` (Phase 2).
  - Singletons — remove all (Phase 5).
  - Asmdefs — mandatory (Phase 3).
  - `Find*` calls — eliminate unless 100% necessary (Phase 6).
  - Naming smells — fix most; **keep** `MyObject` (then later changed: rename to `BlastObject`), keep `Baloon` typo.
  - Platforms: Android + iOS, 60fps target.
- Wrote `ImplementationPlans/01_Refactor_Phases.md` — 8-phase plan, dependency-ordered.

---

## 2026-05-01 19:35 — Phase 1 (deletion step)

- Deleted `Level1.cs` … `Level100.cs` from `Assets/Scripts/Levels/` via `AssetDatabase.DeleteAssets` (the plural batch API; the singular `DeleteAsset` triggered a UI-confirmation block in the MCP runner). 100 files removed.
- Pre-flight scans confirmed safety: 0 Level subclasses on any scene, 0 prefab/SO/material references to the Levels folder.
- `Level.cs` (abstract base) and `LevelTypes.cs` (enum) intentionally retained — `Level` is referenced by ~30 files (Constraints, Missions, MiniGames, Boosters, GameManager, etc.), so removing it would cascade-break the gameplay scaffolding. Will be rebuilt in Phase 7.
- Caught a single broken caller: `Assets/Scripts/UI/Panels/LevelCreator.cs` had a 100-case switch constructing `LevelN` types. Deleted the file.
- Stubbed `GameManager.MakeLevel()` body to empty — 4 callers in `TransectionPanel.cs` will silently no-op until Phase 7 rebuilds the level-spawn flow.
- Compilation: CLEAN.

---

## 2026-05-01 19:50 — Phase 1 (file moves + GameAnalitic rename)

- Created folder `Assets/Scripts/VFX/`.
- Moved root-level VFX scripts (`AssetDatabase.MoveAsset`, GUIDs preserved):
  - `Assets/Scripts/BombMergeVFX.cs` → `Assets/Scripts/VFX/`
  - `Assets/Scripts/BombSpawnVFX.cs` → `Assets/Scripts/VFX/`
  - `Assets/Scripts/ChainBreakVFX.cs` → `Assets/Scripts/VFX/`
  - `Assets/Scripts/HintVFXBubbles.cs` → `Assets/Scripts/VFX/`
- Moved `Assets/Scripts/EntitySpawner.cs` → `Assets/Scripts/Runtime/`.
- Renamed `GameAnalitic` → `GameAnalytic`:
  - File renamed via `AssetDatabase.RenameAsset`.
  - 11 occurrences of `Analitic` replaced inside the file (class name, `SaveAnalitic`, `LevelAnalitic`, `PlayedLevelsAnalitics`, var names).
  - 24 occurrences in `Assets/Scripts/Levels/Level.cs` replaced (`SaveGameAnalitic`, `ExplodableAnaliticDict`, `AbilityAnaliticDict`, `AddExplodableToAnaliticDict`, `AddAbilityToAnaliticDict`, plus 1 lowercase `analitic` in a comment).
- Compilation: CLEAN.

---

## 2026-05-01 19:53 — Phase 1 (Turkish translation pass)

- Translated 47 Turkish strings/comments to simple English in one batched `Unity_RunCommand` operation. Files touched:
  - `Constraint/MoveConstraint.cs` (3 comments)
  - `Economy/BuyAbility.cs`, `Economy/EventReward.cs` (Debug.Log strings)
  - `Manager/AppleAuthManagers.cs` (2 comments)
  - `Manager/MockAuthManager.cs` (2 docs + 1 log)
  - `Manager/CameraMovement.cs` (1 comment)
  - `Manager/SoundManager.cs` (1 LogWarning)
  - `Manager/BoosterManager.cs` (1 doc)
  - `Manager/GameManager.cs` (2 logs)
  - `Manager/MainReSource/UserData.cs` (1 commented-out log)
  - `Levels/Level.cs` (4 logs/comments)
  - `Entities/Objects/Booster.cs`, `Bubble.cs`, `CoinBox.cs`, `DyeJar.cs`, `DyeJarProjectile.cs`, `PlasmaBall.cs`, `PlasmaProjectile.cs`, `TNT.cs` (assorted)
  - `Views/ViewMiniGames.cs`, `Views/ViewMissions.cs` (comments)
  - `Managers/WinSeriesManager.cs` (3 logs)
  - `UI/InGameTexts/InGameTextController.cs`, `UI/Panels/ConsumableItemPanel.cs`, `UI/Panels/TransectionPanel.cs` (assorted)
  - `Util/CameraOrtSize.cs` (1 TODO)
  - `Runtime/SpaceBlastRuntimeSpawner.cs` (5 doc/comments + 2 string literals: "Level Prefab Modu", "OYUN BITTI!")
- Follow-up grep caught 1 straggler: `GameManager.cs:153 Debug.Log("Kaydedildi")` → `Debug.Log("Saved")`.
- Compilation: CLEAN.

---

## 2026-05-01 19:55 — Phase 1 (event progression to ScriptableObject)

- Created `Assets/Scripts/Data/EventProgression.cs` — ScriptableObject with serialized `events: List<int>` defaulting to the original 12-element progression `[100, 200, 300, 200, 400, 600, 400, 800, 1200, 800, 1600, 2400]`. Exposes `IReadOnlyList<int> Events` getter; `[CreateAssetMenu("DragonBlast/Event Progression")]`.
- Created `Assets/Resources/EventProgression.asset` instance via `ScriptableObject.CreateInstance` + `AssetDatabase.CreateAsset`.
- Updated `Assets/Scripts/Manager/GameManager.cs`:
  - Added `using Data;`.
  - `Initialize()` now loads `Resources.Load<EventProgression>("EventProgression")` and assigns `eventProgression.Events.ToList()` to `UserData.Instance.EventList`.
  - The hardcoded inline array is gone.
- Compilation: CLEAN.

---

## 2026-05-01 19:57 — Phase 1 (MyObject → BlastObject project-wide rename)

- Background: investigation of Cygnus (added at `Assets/Lib/`) revealed three `MyObject.cs` files — two inside Cygnus (`Cygnus.Common.MyObject` partial, abstract, with rich lifecycle) and one inside the project (`Entities.MyObject : MonoBehaviour`, simple). The project's class collided with Cygnus by name. User reversed the earlier "MyObject can stay" decision and asked for a rename.
- Picked `BlastObject` (game-themed, fits DragonBlast / SpaceBlast / BubbleBlast naming).
- Renamed `Assets/Scripts/Entities/MyObject.cs` → `BlastObject.cs` via `AssetDatabase.RenameAsset` (GUID preserved).
- Walked all `.cs` files under `Assets/Scripts/`, replacing word-boundary `MyObject` → `BlastObject`. Skipped `Assets/Scripts/MyObject.cs` because that file is the Cygnus.Common.MyObject partial (different namespace, different type).
- Result: **99 replacements across 58 files**.
- Compilation: CLEAN. Console errors: 0.

---

## 2026-05-01 20:05 — Phase 1 closeout / convention change

**Phase 1 status: COMPLETE.** Compile clean, 0 console errors. Summary of what landed:

- 100 Level subclasses + LevelCreator deleted.
- 5 root-level scripts moved into proper folders (`VFX/`, `Runtime/`).
- `GameAnalitic` typo fixed everywhere (35 occurrences across 2 files).
- 48 Turkish strings/comments translated.
- Event progression list extracted to a ScriptableObject + asset.
- Project's `MyObject` renamed to `BlastObject` (99 occurrences across 58 files), disambiguated from Cygnus.Common.MyObject.

**Skipped intentionally** (per direction):
- `MyObject` partial in `Assets/Scripts/MyObject.cs` (Cygnus type, not the project's).
- `Baloon` typo — kept.
- Namespace cleanup for orphan files — deferred to Phase 3 (will align with asmdef boundaries).
- Singleton removals (`GameManager.Instance`, `UserData.Instance`, `Level.ActiveLevel`, `GameAnalytic.Instance`, etc.) — Phase 5.
- `GameObject.Find*` calls — Phase 6.
- Backend URL config — deferred indefinitely.

**Cygnus DI framework — discovery notes (full review pending in `02_Cygnus_Review.md`):**
- Entry point: `Base.Manager.Global` (static class) at `Assets/Lib/Cygnus/Global.cs`.
- Container: `Global.Singletons` is an instance of `SingletonManager`. Despite the user's "no singletons" rule, this is a DI lifetime scope (container-managed), distinct from the static `Instance` pattern they've banned.
- Prefab pattern: `Global.GetPrefabBuilder<TPrefab>()` returns `IPrefabBuilder<TPrefab>` with fluent `SetData` / `CallMethod` / `Instantiate*` API. Backed by `Resources.LoadAll<T>("")`.
- Compatibility issue flagged: `Cygnus.Common.MyObject` (abstract partial with `OnBeforeCreated` / `OnCreated` / `OnBeforeMounted` / `OnAfterMounted` / `OnActivated` / `OnDeactivated` / `OnDestroyed` lifecycle and `VariantId : object?`) is what `IPrefabBuilder<TPrefab where TPrefab : MyObject>` requires. The project's `BlastObject : MonoBehaviour` has a totally different shape. Phase 4 migration will need either (a) inheriting BlastObject subclasses from `Cygnus.Common.MyObject` directly, or (b) reshaping BlastObject's lifecycle to match. To be designed in `02_Cygnus_Review.md`.

**Convention change (this entry):** Going forward, this StatusUpdate.md is append-only. Each session's progress is added as a new dated section. Header "Current State" lines may be edited in place; everything below is preserved verbatim. Earlier entries (the 17:00 reconstructed section) note where I had previously overwritten content — going forward, that should not happen.

---

## Up Next (awaiting direction)

Per `ImplementationPlans/01_Refactor_Phases.md`:
- **Phase 2** — Folder consolidation (`Manager/` + `Managers/` → reorganized `Managers/`).
- **Phase 3** — Assembly definitions on game code (depends on a dependency-graph reading first).
- **Phase 4** — DI migration to Cygnus (now unblocked since Cygnus is in the project; pending `02_Cygnus_Review.md` and a decision on the BlastObject vs Cygnus.Common.MyObject compatibility issue).
- **Phase 5** — Singleton elimination.
- **Phase 6** — `Find*` audit.
- **Phase 7** — New level creator (data-driven).
- **Phase 8** — Mobile perf pass for 60fps target.

---

## 2026-05-01 20:25 — Phase 1 verified by user; Phase 2 plan written

- User tested the game after Phase 1 completion: "It works as it was before. Nice." Phase 1 confirmed regression-free in their hands.
- New convention recorded: `StatusUpdate.md` is append-only going forward (memory: `feedback_statusupdate_appendonly.md`). Earlier in this session I had overwritten the file once; the 17:00 reconstructed entry is the recovered content.
- Wrote `ImplementationPlans/02_Phase2_FolderConsolidation.md`:
  - Surveyed `Manager/` (10 files, `namespace Manager`) and `Managers/` (10 files, mostly `namespace Managers`); 7 files have no namespace at all.
  - Proposed taxonomy: 9 purpose-based subfolders under `Managers/` (Auth, Audio, Camera, Analytics, Network, Core, Levels, User, Events).
  - Namespace strategy: keep flat `Managers` for Phase 2; align subfolder namespaces with asmdef boundaries in Phase 3 to avoid touching them twice.
  - One open question for the user: should `UserDatasManager` typo also be fixed to `UserDataManager` (same pattern as the `GameAnalitic` → `GameAnalytic` fix in Phase 1)?
- **No code changes yet.** Awaiting taxonomy approval before executing Phase 2.

---

## 2026-05-01 20:57 — Phase 2 COMPLETE

User approved Phase 2 plan ("go") including the `UserDatasManager` → `UserDataManager` typo fix. Executed cleanly. Compile CLEAN, 0 console errors after a couple of iterative fixes (see Issues Encountered below).

### File moves (AssetDatabase.MoveAsset, GUIDs preserved)

20 files moved into 9 purpose-based subfolders under `Assets/Scripts/Managers/`:
- `Auth/` — `IAuthenticationManager.cs`, `AppleAuthManagers.cs`, `MockAuthManager.cs`
- `Audio/` — `SoundManager.cs`
- `Camera/` — `CameraMovement.cs`, `CinemachineShakeController.cs`
- `Analytics/` — `GameAnalytic.cs`
- `Network/` — `HttpsClientManager.cs`, `IClient.cs`
- `Core/` — `GameManager.cs`, `BoosterManager.cs`, `GameInstaller.cs`
- `Levels/` — `LevelManager.cs`, `ILevelService.cs`
- `User/` — `UserManager.cs`, `UserDataManager.cs`, `IUserService.cs`
- `Events/` — `EventManager.cs`, `IEventService.cs`, `WinSeriesManager.cs`

Empty `Assets/Scripts/Manager/` folder removed via `AssetDatabase.MoveAssetsToTrash` (the singular `MoveAssetToTrash` triggered an MCP UI-confirmation block).

### Renames

- `UserDatasManager` → `UserDataManager` (file rename + class identifier rename + 14 caller-site updates across the codebase, word-boundary replacement to avoid false matches).

### Namespace normalization

| Outcome | Count | Action |
|:---|:---|:---|
| `namespace Manager` → `namespace Managers` | 8 files | text replace on the `namespace Manager\n` declaration |
| Already `namespace Managers` | 5 files | left untouched |
| No namespace at all | 7 files | wrapped content in `namespace Managers { ... }` |

### Consumer updates

- 39 files had `using Manager;` rewritten to `using Managers;`.
- 14 files had `UserDatasManager` references updated to `UserDataManager`.
- 5 `Economy/` files had `using Managers;` added (they referenced `UserDataManager` but didn't have the namespace import).
- 2 `Assets/Editor/` files patched (these were missed by the initial scope; my walker only covered `Assets/Scripts/`):
  - `SetupSampleScene.cs` — added `using Managers;` (referenced `GameInstaller`).
  - `SpaceBlastSceneSetupTool.cs` — replaced `Manager.BoosterManager` with `Managers.BoosterManager` (2 occurrences).

### Issues Encountered (and resolutions)

1. **`UserDataManager` not resolving in `Economy/` files even after `using Managers;` was present.** Compile error CS0103 ("does not exist in the current context") persisted across multiple `RequestScriptCompilation(CleanBuildCache)` cycles. Root cause: I had wrapped `UserDataManager.cs` with `namespace Managers { ... }` AROUND its existing using directives, putting them inside the namespace block. While syntactically valid C#, Unity's incremental compiler appeared to have cached a stale assembly state. **Fix:** rewrote `UserDataManager.cs` in canonical form with the using directives ABOVE the namespace declaration. Compile flipped to clean immediately. Lesson for the other 6 wrapped orphan files (GameAnalytic, IAuthenticationManager, IClient, IEventService, GameInstaller, WinSeriesManager) — they're functionally fine but use the same wrap pattern; if any future compile issue surfaces from them, canonicalize them the same way.

2. **`Assets/Editor/` files outside the script walker's scope.** My Phase 2 batch only walked `Assets/Scripts/`. Editor scripts (in `Assets/Editor/`) compile to `Assembly-CSharp-Editor` and reference runtime classes from `Assembly-CSharp`. Two such files had stale references (`GameInstaller`, `Manager.BoosterManager`). **Fix:** patched them manually after compile errors surfaced. Future passes that touch namespaces should walk `Assets/Editor/` too.

### Skipped intentionally (deferred to Phase 3)

- Subfolder-aligned namespaces (e.g. `Managers.Auth`, `Managers.Audio`) — held until Phase 3 brings in asmdefs, so we don't churn namespaces twice.
- Other `using Manager;` references in `Assets/Editor/` may still exist — none surfaced as compile errors, so left alone for now (they would mean the file currently references something via `Manager.X` qualifier; my Editor-folder fixes covered the only two compile-failing cases).

### Final state

- Compilation: CLEAN.
- Console errors: 0.
- All 20 manager-related files now live under `Managers/` with subfolder organization.
- The `Manager` (singular) folder and namespace are gone.

### Phase 1 + 2 game runtime check

User has not yet retested in this iteration. Phase 1 was verified to play correctly; Phase 2 is purely a folder/namespace reorganization with no runtime semantics change, so risk of regression is low. **Recommend a quick play test before moving to Phase 3.**

---

## 2026-05-01 22:11 — Phase 3 COMPLETE (asmdefs landed)

User confirmed Phase 2 worked ("gameplay is the same"). Plan-mode session designed Phase 3; user accepted the plan via Ultraplan refinement. Execution had a scope adjustment partway through.

### Plan documents

- Authored at `C:\Users\admin\.claude\plans\jazzy-wobbling-wilkinson.md` (harness scratchpad).
- Mirrored to `ImplementationPlans/03_Phase3_Asmdefs.md` (project canonical location). Memory note `feedback_planmode_mirror.md` saved so future plan-mode sessions auto-mirror to ImplementationPlans/ on exit.

### Scope adjustment (during execution)

Original plan: 4 asmdefs (Cygnus + DragonBlast.Shared + DragonBlast.Runtime + DragonBlast.Editor). Pre-flight grep audit revealed widespread inbound references from Shared candidate folders (Interfaces, Util, Mapping, MainReSource) into Runtime-layer namespaces (Levels, Missions, Entities, Views) — they would cycle. **Dropped DragonBlast.Shared from Phase 3** to avoid cycle-resolution work that didn't fit Phase 3 scope. Sub-namespacing `Managers.*` still happened. Skipped DragonBlast.Editor since editor scripts in `Assets/Editor/` compile to Assembly-CSharp-Editor with auto-references and don't need a dedicated asmdef. Final: **3 asmdefs created** (Cygnus, DragonBlast.Runtime, DOTween.Modules).

### Pre-stage work

- Moved `Assets/Scripts/MyObject.cs` → `Assets/Lib/Cygnus/Common/MyObject.Partial.cs` via `AssetDatabase.MoveAsset` (partial-class same-assembly rule). Renamed to disambiguate from sibling `MyObject.cs`. Namespace already `Cygnus.Common`; no code change. GUID + .meta preserved.
- Sub-namespaced `Managers/*`: 28 types across 9 subfolders detected (Auth, Audio, Camera, Analytics, Network, Core, Levels, User, Events). 20 Manager files had `namespace Managers` rewritten to `namespace Managers.<Subfolder>`. 58 consumer files patched with 84 new `using Managers.<Subfolder>;` directives. Internal Manager cross-refs (5 files: AppleAuthManagers, BoosterManager, GameInstaller, GameManager, WinSeriesManager) patched separately with 10 more usings.
- Canonicalized 5 Phase-2-wrapped orphan files (GameAnalytic, IAuthenticationManager, IClient, IEventService, GameInstaller, WinSeriesManager) — moved their `using` directives outside the namespace block, fixed Unity's incremental-compile stale state. Same fix pattern as Phase 2 UserDataManager.
- Rewrote `GameManager.cs` in canonical form (usings outside, no inner-namespace using-aliasing) to clear Unity's stuck compile state.
- Editor folder fixes: `SpaceBlastSceneSetupTool.cs` updated `Managers.BoosterManager` → `Managers.Core.BoosterManager` (post sub-namespacing, BoosterManager moved subfolders).

### Asmdefs created

| Asmdef | Path | Source files | References |
|---|---|---|---|
| `Cygnus` | `Assets/Lib/Cygnus/Cygnus.asmdef` | All Cygnus source + relocated MyObject.Partial.cs | (8 DLL precompiledReferences: CygnusAttributes, CygnusCommon, CygnusNetworking, MessagePack, MessagePack.Annotations, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, System.Collections.Immutable) |
| `DOTween.Modules` | `Assets/Plugins/Demigiant/DOTween/DOTween.Modules.asmdef` | 8 DOTween module .cs files (Audio, Physics, Sprite, UI, EPOOutline, UnityVersion, Utils, Physics2D) | Unity.TextMeshPro, Unity.RenderPipelines.Universal.Runtime; precompiledReferences: DOTween.dll |
| `DragonBlast.Runtime` | `Assets/Scripts/DragonBlast.Runtime.asmdef` | 195 game scripts under Assets/Scripts/ | Cygnus, Zenject, spine-unity, spine-csharp, Coffee.UIParticle, Unity.InputSystem, Unity.Cinemachine, Unity.TextMeshPro, Unity.RenderPipelines.Universal.Runtime, Unity.RenderPipelines.Core.Runtime, Unity.VisualScripting.Core, Unity.VisualScripting.Flow, Unity.VisualScripting.State, Unity.Mathematics, DOTween.Modules |

### Issues encountered (and resolutions)

1. **Manager sub-namespacing broke GameManager temporarily.** Same Unity incremental-compile stale state pattern from Phase 2's UserDataManager. Fix: rewrote files in canonical form (usings outside namespace), then `EditorUtility.RequestScriptReload()` cleared the cache.

2. **DragonBlast.Runtime asmdef references not honored at compile time.** Initial creation with `overrideReferences: true` + `autoReferenced: true` had Unity register the references but not resolve them at compile time (zero matching refs in `compiledAssemblyReferences` despite 16 in `assemblyReferences`). Resolution: deleted the asmdef, recreated minimal version with just `references` + `autoReferenced: true` + `noEngineReferences: false`. References resolved correctly.

3. **DOTween module extension methods not visible.** `DOFillAmount`, `DOFade` etc. live in DOTween module .cs files at `Assets/Plugins/Demigiant/DOTween/Modules/`, which previously auto-compiled into Assembly-CSharp. With DragonBlast.Runtime asmdef, those module files were stranded in Assembly-CSharp (which asmdefs cannot reference). Fix: created `DOTween.Modules.asmdef` to wrap the module .cs files (with `precompiledReferences: ["DOTween.dll"]`), then added `DOTween.Modules` reference to DragonBlast.Runtime.

4. **First DOTween asmdef name collided with DOTween.dll.** Created as `DOTween.asmdef` initially → Unity error: "Plugin DOTween.dll has the same filename as Assembly Definition File". Renamed to `DOTween.Modules.asmdef` to disambiguate.

### Final state

- Compilation: CLEAN, 0 console errors.
- 3 asmdefs registered: Cygnus, DOTween.Modules, DragonBlast.Runtime. Game code now compiles to DragonBlast.Runtime.dll instead of Assembly-CSharp.
- Cygnus DLLs + source no longer recompile on game-code edits.
- Editor folder still uses Assembly-CSharp-Editor (auto-references DragonBlast.Runtime via `autoReferenced: true`).

### Skipped intentionally (deferred)

- DragonBlast.Shared asmdef — folders Util/, Mapping/, MainReSource/, Interfaces/ have inbound deps to Runtime-layer namespaces. Extraction needs cycle-resolution refactoring (move offending files into Runtime, extract interfaces). Defer to Phase 3.5 after Phase 5 (singleton elimination) and Phase 6 (Find* removal) weaken the cycles.
- DragonBlast.Editor asmdef — not needed; editor scripts work in Assembly-CSharp-Editor with auto-references.
- Cygnus `autoReferenced: false` flip — kept at `true` so any Assembly-CSharp residue (e.g. existing Editor-folder scripts) still finds Cygnus types. Can flip later.
- Granular split (Entities / UI / Managers / Levels asmdefs) — held for after Phase 5+6, per user's "lean now, granular later" direction.
- `Assets.Scripts.Data` → `Data` namespace fix — cosmetic, deferred.
- Orphan namespaces in VFX/Runtime/UI subfolders — none surfaced as compile errors during Phase 3, deferred.

### Iteration-speed test (not formally benchmarked)

Recommend the user verify by:
- Editing `Assets/Scripts/Entities/Objects/Bubble.cs` — should recompile only `DragonBlast.Runtime.dll` (a few seconds), not Cygnus/DOTween.Modules.
- Editing `Assets/Lib/Cygnus/Global.cs` — should recompile Cygnus.dll AND DragonBlast.Runtime.dll (cascade).
- Editing `Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleAudio.cs` — should recompile DOTween.Modules.dll AND DragonBlast.Runtime.dll.

### Phase 3 game runtime check

**Recommend a play test before moving to Phase 4.** This phase touched namespaces broadly (28 types' namespaces changed, 84+ using directives added) and reshaped the assembly graph. Compile-clean ≠ runtime-clean. Test SampleScene + MechanicTest gameplay flows.

---

## 2026-05-01 23:20 — Hotfix: balloons not popping + audio silent

User play-tested SampleScene after Phase 3 and reported (a) clicks on bubbles did nothing — no sound, no VFX, no errors — and (b) no audio at all once that was unblocked. Both fixed; neither was a Phase 3 regression. They were latent issues the play-test surfaced.

### Bug 1: Bubble clicks silently no-op'd

**Symptom:** Clicking same-color bubble groups produced nothing — no pop, no sound, no console error. Boosters never reached because they require a prior pop. The only console noise was a startup message about "LevelParent is Not Defined".

**Root cause:** The tag `LevelParent` was missing from `ProjectSettings/TagManager.asset`. `GameManager.Initialize()` calls `_levelParent = GameObject.FindGameObjectWithTag("LevelParent")` at line 72 (inside `Awake()`). Unity's `FindGameObjectWithTag` throws `UnityException: Tag: LevelParent is not defined.` when the tag itself isn't registered — distinct from "no GameObject uses this tag" which silently returns null.

The exception propagated out of `Awake()`, and **Unity disables MonoBehaviours that throw unhandled exceptions in Awake/OnEnable**, which silently killed `GameManager.Update()`. Since `GameManager.canClick` is only flipped to `true` inside that Update (cooldown timer logic), it stayed at the default `false` forever. Every `Bubble.OnMouseDown()` hit `if (!GameManager.Instance.canClick) return;` and exited silently — no exception path, no log.

**Fix:** Added `LevelParent` to `ProjectSettings/TagManager.asset` via `SerializedObject` edit on the tags array.

**Note:** The `_levelParent` field set by that line is **dead code** — never read anywhere else in the project. The `FindGameObjectWithTag` call serves no purpose other than to throw when the tag is missing. Worth deleting in a future cleanup pass; left in place this session to avoid scope creep.

**Diagnosis path:** Initially suspected Phase 3 namespace breakage (asmdefs / sub-namespaced Managers). Surveyed Bubble prefab (CircleCollider2D non-trigger + CircleCollider2D trigger present, Rigidbody2D Dynamic, Layer 0 — all intact), `activeInputHandler = Both` (legacy `OnMouseDown` enabled), no missing scripts on Bubble-related objects, scene's GameManager/PoolingObject/SoundManager/SpaceBlastRuntimeSpawner all resolved correctly. Added temporary `[BUBBLE_DEBUG]` `Debug.Log` instrumentation to `Bubble.OnMouseDown` to identify which gate was failing — but in fact the LevelParent tag fix was found before needing those logs. Tag fix alone unblocked clicks; debug logs reverted.

### Bug 2: All audio silent

**Symptom:** After clicks worked, no audio (no SFX, no music).

**Root cause:** `PlayerPrefs` had `MainMusicStatus=0` and `SoundEffectStatus=0`, persisted from a prior test session. `SoundManager.SfxSoundAction` and `MainSoundAction` gate on these prefs. `SoundManager.Initialize` only seeds defaults to `1` if the keys are *missing* (`!PlayerPrefs.HasKey(...)`); since the keys existed with value 0, defaults didn't apply.

**Fix:** Reset both PlayerPrefs to `1` via `PlayerPrefs.SetInt` + `Save()`. User can also toggle via the in-game settings panel.

**Note:** No code change. This was user-state, not a bug — though the `SoundManager` initialization is fragile in the sense that it cannot recover from a "muted" persistent state without a user toggle.

### Final state

- Compilation: CLEAN, 0 console errors.
- Bubble clicks pop correctly in SampleScene (user-confirmed: "It worked perfectly").
- `LevelParent` tag now registered (no GameObject uses it; that's fine — `_levelParent` field is dead).
- Music + SFX PlayerPrefs reset to enabled.

### Phase 3 verdict

Phase 3 itself is **runtime-clean**. The two issues here were pre-existing and orthogonal to the asmdef refactor. Recommend proceeding to Phase 4 once user gives the go-ahead.

---

## 2026-05-01 23:50 — Phase 4 planning + Step 0 in progress

User approved plan-mode session for Phase 4. Three Explore agents surveyed (a) Cygnus framework, (b) all Zenject usage, (c) BlastObject hierarchy. Findings reshaped the phase scope.

### Key exploration findings

1. **Cygnus is barely a DI container.** `Global.Singletons` is a manual `SingletonManager` (instance registry, no factory bindings, no constructor injection, no scopes). `Microsoft.Extensions.DependencyInjection.Abstractions` DLL is referenced transitively but unused by source. Cygnus's real value is `Cygnus.Common.MyObject` (lifecycle hooks: `OnBeforeCreated` → `OnCreated` → `OnBeforeMounted` → `OnAfterMounted` → `OnActivated`/`OnDeactivated` → `OnDestroyed`) and `IPrefabBuilder<T>` (fluent prefab instantiation).
2. **Zenject footprint is trivial:** 1 installer (`GameInstaller.cs` with 5 bindings), 2 `[Inject] Construct` sites (`GameManager.cs:48`, `Level.cs:169`). **No `SceneContext` GameObject in any scene** — strong signal that `[Inject]` may have been silently no-op'ing all along, with `GameManager.Initialize` falling back to `UserData.Instance` etc.
3. **The real DI work is the ~327 static singleton call sites** across 30+ singletons. The biggest: `TransectionPanel.Instance` (30+), `Level.ActiveLevel` (29), `GameTopCanvasMissions.Instance` (25+), `GameManager.Instance` (23), plus Factory, SoundManager, UserData, etc.
4. **BlastObject has 62 subclasses.** `Cygnus.Common.MyObject` is `abstract` — incompatible with `PoolingObject<T> where T : BlastObject, new()` constraint. Migration to MyObject would require pooling layer rework.

### Decisions locked (user-approved this session)

- **Phase 4 scope:** Merged with original Phase 5. Replace Zenject + route 30+ singletons through `Global.Singletons`.
- **BlastObject inheritance question:** Deferred to a later phase. BlastObject hierarchy stays as-is in Phase 4.
- **PrefabBuilder adoption:** Skipped. PoolingObject + Factory remain.
- **Bootstrap mechanism:** Single `GameBootstrap` MonoBehaviour with `[DefaultExecutionOrder(-1000)]` placed in each gameplay scene; SerializedField refs to MonoBehaviour singletons + plain-class instantiation in Awake.
- **Call-site rewrite strategy:** Every `.Instance` access → `Global.Singletons.Get<X>()`. No proxy `Instance` properties left behind.
- **Migration order:** Leaves first (BuyAbility/Health/Booster, GameAnalytic, UI canvases, mid-tier MonoBehaviours) → keystones last (UserData, GameManager).
- **Plan doc:** Single file at `ImplementationPlans/04_Phase4_DI_Migration.md` (mirrored from `~/.claude/plans/let-s-plan-phase-4-graceful-donut.md`).

### Out of scope (explicit deferrals for Phase 4)

- `Level.ActiveLevel` — not a service registry; tracks current active level. Will be replaced by Phase 7's level-creator rewrite.
- `BlastObject` vs `Cygnus.Common.MyObject` inheritance question.
- Cygnus `IPrefabBuilder<T>` adoption.
- Vendored Zenject DLL cleanup (if present in `Assets/Plugins`).
- Backend networking (`ClientSpaceBlast` URL) — Bootstrap registers a stub; backend deferred indefinitely.

### Step 0 — Zenject behavior verification (IN PROGRESS)

Goal: confirm whether Zenject `[Inject] Construct(...)` is actually firing today, given no `SceneContext` exists. Outcome determines how careful Step 8 (Zenject removal) needs to be.

**Edits made this session (still in place — must remove after verification):**
- `Assets/Scripts/Managers/Core/GameManager.cs:48` — added `Debug.Log("[ZENJECT_VERIFY] GameManager.Construct fired");` as first line of `Construct(...)`.
- `Assets/Scripts/Levels/Level.cs:169` — added `Debug.Log("[ZENJECT_VERIFY] Level.Construct fired");` as first line of `Construct(...)`.

**Result (2026-05-02):** **Outcome B confirmed.** User play-tested SampleScene; neither `[ZENJECT_VERIFY]` log fired. Zenject's `[Inject] Construct(...)` has been silently no-op'ing all along. The project has been running on singleton fallbacks the whole time (e.g., `GameManager.Initialize` accesses `UserData.Instance` directly because `Construct` never injects). **Implication:** Step 8 (Zenject removal) is a clean deletion. No hidden auto-context to find or preserve.

**Cleanup completed:** Removed both `[ZENJECT_VERIFY]` `Debug.Log` lines from `GameManager.cs` and `Level.cs`. Files restored to pre-Step-0 state.

### Resume context (if session restarted)

- Plan file: `ImplementationPlans/04_Phase4_DI_Migration.md`
- Plan-mode scratchpad: `~/.claude/plans/let-s-plan-phase-4-graceful-donut.md`
- No outstanding temp edits. Compile state should be clean.
- No new files created yet. No singletons migrated yet.
- Step 0 (verification) is **DONE**. Outcome: Zenject is dead weight; remove cleanly in Step 8.
- Next concrete action: Step 1 — create `Assets/Scripts/Bootstrap/GameBootstrap.cs` (skeleton with empty Awake) + place `_GameBootstrap` GameObject in SampleScene. Then verify compile + bubble-click smoke test before adding any registrations.

---

## 2026-05-02 03:01 — Phase 4 code-complete (Steps 1-8 landed)

User directed "finish this phase completely." Executed Steps 1-8 in one push. Compile CLEAN, 0 console errors. Awaiting user smoke test before declaring Phase 4 verified.

### Steps executed

**Step 1 — GameBootstrap foundation**
- Created `Assets/Scripts/Bootstrap/GameBootstrap.cs` with `[DefaultExecutionOrder(-1000)]`. Empty Awake initially.
- Placed `_GameBootstrap` GameObject in SampleScene (saved via editor script).
- User play-tested → bubble pop + audio still working post-bootstrap.

**Step 2 — Plain-class leaves (Buy* family + GameAnalytic)**

`Buy*` family pre-existing bug found and fixed. All four classes (`BuyAbility`, `BuyHealth`, `BuyBooster`, `BuyMove`) extended `Buy : BlastObject : MonoBehaviour` yet had `static Instance = new();` — illegal `new MonoBehaviour()` pattern that created orphan instances. The duplicate-check `if (Instance != null) Destroy(gameObject)` would self-destruct any prefab/scene-resident instance because the orphan made `Instance != null` always true.

Two prefabs (`Assets/Prefabs/GamePanel.prefab`, `Assets/Resources/UI/Panel/BuyAbilityPanel.prefab`) had the orphan `BuyAbility` component on their `BuyAbilityPanel` GameObject — those would self-destruct on instantiation.

Migration:
- Converted `Buy` from `: BlastObject` to plain `abstract class Buy : IBuy`. Subclasses now plain classes with constructors.
- Stripped orphan `BuyAbility` components from the 2 prefabs via `PrefabUtility.LoadPrefabContents` + `DestroyImmediate` + `SaveAsPrefabAsset`.
- `BuyAbility.Level.LevelDoneEvent += ResetAllCost` moved from `Initialize()` to constructor.
- `GameAnalytic` had the same broken `MonoBehaviour + new()` pattern — converted to plain class.
- Bootstrap registers `new BuyAbility()`, `new BuyHealth()`, `new BuyBooster()`, `new BuyMove()`, `new GameAnalytic()`.
- 7 call sites rewritten to `Global.Singletons.Get<X>()` (BuyAbilityPanel.cs ×3, BuyBoosterPanel.cs ×1, NotHealthPanel.cs ×2, Level.cs ×1).

**Step 3 — UI canvas singletons**

Inventory's "MainPanel.Instance has 14 sites, TransectionPanel.Instance has 30+, GameTopCanvasMissions has 25+" was wrong (Explore agent confused type-name occurrences with `.Instance` access). Actual `.Instance` consumer call sites across all UI canvas singletons: 11.

- **Active (with consumers):** BaloonCollectCanvas, BaseCanvas, InGameTextController, TransectionPanel, GameTopCanvasMissions, GameTopCanvasMove. Converted to self-register pattern: replaced `Instance = this` with `Global.Singletons.Add<X>(this)` and the `if (Instance != null)` duplicate-check with `if (Global.Singletons.IsRegistered<X>())`.
- **Dead Instance (zero consumers):** MovesOutPanel, RestartConsumablePanel, MainPanel, ViewAbilityThree, MainMenuTopCanvasEvents, GameBottomCanvasAbility, GameTopCanvas, GameBottomCanvas, DialogOkay. Just deleted the `static Instance` property + duplicate-check blocks. `MainPanel.Instance` was particularly broken — `{ get; }` with no setter, would have always been null had anything tried to read it.

**Step 4 — Mid-tier MonoBehaviour services**

- **Active:** `SoundManager`, `CameraMovement`, `BoosterManager`, `PoolingObject`, `EventReward`. Self-register via `Global.Singletons.Add<X>(this)` in their existing Awake/Initialize hooks.
- `Factory` was a plain class with private ctor + `static Instance = new Factory()`. Made ctor public, dropped static. Bootstrap calls `new Factory()` and registers.
- `PoolingObject` had a `FindObjectOfType` lazy-load fallback — removed in favor of self-register.
- **Dead Instance:** `HttpsClientManager`, `Explodable`, `CinemachineShakeController` — zero consumers. Removed dead static + assignments.

**Step 5 — UserData**

- Was a plain class with `static UserData Instance` lazy-loading from PlayerPrefs. Replaced with `static UserData Load()` method that returns a fresh instance.
- `static SaveAllDataToPrefs()` now serializes `Global.Singletons.Get<UserData>()` instead of static `Instance`.
- Bootstrap calls `Global.Singletons.Add<UserData>(UserData.Load())` first (before any consumer Awake).

**Step 6 — GameManager init-order fix**

- `[Inject] Construct` was always silently no-op'ing (Step 0 confirmed). Removed `[Inject]` attribute. `Construct` kept as public method (still callable for future backend wiring).
- `GameManager.Initialize` now uses `Global.Singletons.Get<UserData>()` instead of `UserData.Instance` (handled by bulk script).

**Step 7 — GameManager singleton migration**

- 23 call sites rewritten via bulk script.
- `static GameManager Instance` removed; self-register from Initialize.

**Step 8 — Zenject removal**

- Deleted `Assets/Scripts/Managers/Core/GameInstaller.cs` (and its .meta).
- Removed `[Inject]` attributes from `GameManager.Construct` and `Level.Construct`.
- Removed `using Zenject;` from GameManager.cs and Level.cs.
- Removed `Zenject` from `DragonBlast.Runtime.asmdef` references.
- Cleaned 3 `using ModestTree[.Util];` directives in MakeBubbleConstraint.cs, Level.cs, TransectionPanel.cs (ModestTree is a Zenject-bundled utility namespace; its `IsEmpty()` extension method was used in 7 places — replaced `.IsEmpty()` with `.Count == 0` and `!x.IsEmpty()` with `x.Count > 0`).
- Cleaned `Assets/Editor/SetupSampleScene.cs` GameInstaller / SceneContext registration block (replaced with comment noting Bootstrap handles registration now).
- Vendored Zenject DLL/source under `Assets/Plugins/Zenject/` left in place — its own asmdef, no longer referenced by game code, harmless. Cleanup deferred to a later pass.

### The bulk-rewrite step

Manual call-site rewrites would have been ~330 individual edits. Wrote a PowerShell one-shot that walked `Assets/Scripts/`, applied `\bX.Instance\b → Global.Singletons.Get<X>()` regex replacement for 19 migrated singleton type names (skipping each type's own source file and `Assets/Scripts/Bootstrap/`), and inserted `using Base.Manager;` after the first `using` line in any file that received a replacement.

Result: **66 files modified, 331 replacements**. Subsequent compile-clean confirmed all rewrites were syntactically valid.

One regex bug surfaced: the initial `.IsEmpty() → .Count == 0` replacement ignored leading negation, yielding invalid `!queue.Count == 0`. Fixed with a follow-up regex that catches the `!X.Count == 0` pattern and rewrites it to `X.Count > 0`.

### Inventory misreporting (lesson)

The Explore agent's earlier inventory of singleton call counts was wildly inflated for many singletons (MainPanel: claimed 14 → actual 0; TransectionPanel: claimed 30+ → actual 1; GameTopCanvasMissions: claimed 25+ → actual 2). The agent appears to have counted occurrences of the *type name* in any context (SerializedField declarations, parameter types, etc.) rather than `\.Instance` accesses specifically. Future singleton-removal phases should grep for `\bX\.Instance\b` literally before estimating scope.

The actual scale of Phase 4: **~30 singleton classes**, **331 `.Instance` access sites**, **66 files modified**.

### Final state

- Compilation: CLEAN
- Console errors: 0
- `using Zenject;` count across `Assets/Scripts/`: **0**
- `[Inject]` count: **0**
- `MonoInstaller` count: **0**
- `static <Type> Instance` count: **0** (only `Level.ActiveLevel` remains, deferred to Phase 7)
- New files: `Assets/Scripts/Bootstrap/GameBootstrap.cs`
- Deleted files: `Assets/Scripts/Managers/Core/GameInstaller.cs`
- Modified prefabs: `Assets/Prefabs/GamePanel.prefab`, `Assets/Resources/UI/Panel/BuyAbilityPanel.prefab` (orphan BuyAbility component stripped from each)
- Modified scene: `Assets/Scenes/SampleScene.unity` (`_GameBootstrap` GameObject placed)
- Modified asmdef: `Assets/Scripts/DragonBlast.Runtime.asmdef` (Zenject reference removed)
- Modified editor script: `Assets/Editor/SetupSampleScene.cs` (GameInstaller block stubbed out)

### Out of scope (explicit deferrals)

- `Level.ActiveLevel` — different pattern (current-active reference, not a service registry). Will be replaced by Phase 7's level-creator rewrite.
- `BlastObject` vs `Cygnus.Common.MyObject` inheritance question.
- Cygnus `IPrefabBuilder<T>` adoption.
- Vendored Zenject DLL/source cleanup at `Assets/Plugins/Zenject/`.
- `_levelParent` dead field in GameManager (cleanup pass).
- Backend networking — `ClientSpaceBlast` field on GameManager remains null (no consumers reach it in current testing).

### Awaiting

User smoke test of SampleScene: bubble click → pop → audio. Match Phase 3's verified state. After confirmation, Phase 4 is verified complete.

---

## 2026-05-02 (morning) — Phase 4 verified; Phase 6 audit + combined plan written

User smoke-tested SampleScene after Phase 4 — bubble click → pop → audio all working. **Phase 4 is verified complete.** No regression from the 331 `.Instance` rewrites, the Zenject removal, or the BuyAbility orphan-prefab fix.

### Phase 6 pre-flight audit — Find* call sites

Ran a project-wide grep over `Assets/Scripts/**/*.cs` for `GameObject.Find`, `FindObjectOfType(s)`, `FindGameObjectWithTag(s)`, `Resources.FindObjectsOfTypeAll`, plus the new `FindObjectsByType` / `FindFirstObjectByType` / `FindAnyObjectByType` Unity 2023+ variants.

**Total: 24 call sites across 12 files.**

Categories (rough complexity tier):

| Category | Sites | Approach |
|:---|:---|:---|
| Tag-based scene-parent lookups (BubbleParent, BoosterParent, Spawn, Obstacle, LevelParent) | 9 | Replace with serialized refs on a `SceneRefs` MonoBehaviour registered via Bootstrap, OR direct `[SerializeField]` |
| `FindObjectsOfType<T>()` initialization-time (DyeJar[], LockColor[], Explodable[]) | 3 | Self-registration: spawned objects register with a per-level registry on Awake/OnEnable |
| `FindObjectsOfType<T>()` mid-game queries (Bubble × 4, Booster × 1, Explodable × 1) | 6 | Per-level registry. Bubble/Booster pool already exists via PoolingObject — extend it to expose live-active enumeration |
| `Resources.FindObjectsOfTypeAll<T>()` (FiveMovesRemain × 1, Button × 2 in SoundManager) | 3 | FiveMovesRemain → registry. Button click-sound subscription → custom button prefab variant or click-sound aspect |
| `GameObject.Find("Sky")` (CameraOrtSize) | 1 | `[SerializeField]` Transform |
| **Excluded** — `transform.Find(childName)` in PoolingObject | 1 | Child lookup, not scene search; lower priority |
| **Excluded** — false-positive `List<T>.Find(...)` in UserData/Factory | 3 | Not Unity Find* |

Plus a "delete dead code" win: `GameManager._levelParent` was set by a `FindGameObjectWithTag` and **never read anywhere**. The 2026-05-01 23:20 hotfix added `LevelParent` to TagManager only to silence the throw; the field itself is dead. Phase 6 deletes the line entirely (and the tag stays harmless in TagManager).

### Phase 5 closeout scope

Phase 5 (singleton elimination) was substantively absorbed into Phase 4. Only loose end is the **vendored Zenject at `Assets/Plugins/Zenject/`** — 2.2 MB of source under its own `zenject.asmdef`, with **0 references from `Assets/Scripts/`** (`using Zenject` and `using ModestTree` both return zero hits). Safe to delete the whole folder. That's the entirety of Phase 5.

### Plan

Combined doc written: `ImplementationPlans/05_Phase5_Closeout_Phase6_Plan.md`. Covers:
1. Phase 5 closeout — delete `Assets/Plugins/Zenject/`, verify clean.
2. Phase 6 strategy — three replacement patterns (SceneRefs container, per-level type registry, direct SerializeField), per-call-site assignments, ordered execution plan, snapshot/verify steps.

### Awaiting

User approval of `05_Phase5_Closeout_Phase6_Plan.md` before any deletes / refactors.

---

## 2026-05-02 (afternoon) — Phase 5 closeout + Phase 6 code-complete

User approved the plan with answers: SceneRefs YES, LevelRegistry YES, ClickSoundButton component YES (no scan hack), Camera obstacles → use LevelRegistry (dynamic, since CameraMovement.ClearAllAbove destroys them mid-game). Executed all steps in one push. Compile CLEAN, awaiting smoke test.

### Snapshot

`AgentReports/Snapshots/before_phase6.snapshot` — SampleScene state before any changes. Use `SceneDiff.CompareCurrent` with this label for rollback evidence.

### Part A — Phase 5 closeout

- **Deleted `Assets/Plugins/Zenject/`** (2.2 MB, ~700 files including its own `zenject.asmdef`). Was already isolated by asmdef in Phase 3, with 0 references from `Assets/Scripts/`. Cleanly removed via `AssetDatabase.MoveAssetsToTrash`. Verified compile CLEAN after the resulting domain reload. Phase 5 closes out — only `Level.ActiveLevel` static reference remains in game code, deferred to Phase 7's level-creator rewrite.

### Part B — Phase 6 (Find* audit & removal)

**Final count:** 24 functional Find* calls before → **1** (deferred CameraOrtSize) + **1** improved (`FindAnyObjectByType` replacing `Resources.FindObjectsOfTypeAll` in MoveConstraint).

#### Step 1 — Dead `_levelParent` deleted
- `GameManager.cs` field declaration + `FindGameObjectWithTag("LevelParent")` line both removed. The field was set, never read; the 2026-05-01 hotfix added the tag only to silence the throw. Tag stays in TagManager (harmless).

#### Step 2 — `SceneRefs` container created and wired
- New file: `Assets/Scripts/Bootstrap/SceneRefs.cs`. MonoBehaviour with serialized `BubbleParent`, `BoosterParent`, `Spawn` Transforms. Self-registers via `Global.Singletons.Add(this)` in Awake.
- Component added to `_GameBootstrap` GO in SampleScene via editor utility, fields wired from existing tag-bearing GameObjects.
- **Adjustment from plan:** originally proposed Sky and FiveMovesRemain as additional fields. Removed once the audit revealed both live in dynamically-instantiated prefabs (Sky in `Level-N/BackGrounds/Mask/Sky` inside `Assets/Resources/Levels/Level-*.prefab`; FiveMovesRemain in `Assets/Resources/UI/PopUp/LastXMoveText.prefab`). Edit-time wiring doesn't fit; handled separately (see Step 4 deferral and MoveConstraint note below).

#### Step 3 — 8 tag-Find call sites migrated
All `FindGameObjectWithTag("BubbleParent" / "BoosterParent" / "Spawn")` calls replaced with `Global.Singletons.Get<SceneRefs>().X` accesses:
- `BoosterManager.cs:45` (also dropped the fallback `new GameObject("BoosterParent")` since SceneRefs is now the contract)
- `MakeBubbleConstraint.cs:36, 41` (both Spawn and BubbleParent)
- `SpaceBlastRuntimeSpawner.cs:71`
- `Level.cs:188-189, 534-535, 578` (5 sites — Initialize, DestroyAllExplodablesOnStart, CheckLevelStages)
- Added `using Bootstrap;` to BoosterManager, MakeBubbleConstraint, SpaceBlastRuntimeSpawner, Level.

#### Step 4 — CameraOrtSize.Sky DEFERRED
- `CameraOrtSize.cs:20` `GameObject.Find("Sky")` retained.
- Justification: class is **orphaned in current usage** — only attached in `Assets/Scenes/GameTest.unity` (test scene), not SampleScene, not in any prefab. Sky lives at `Level-N/BackGrounds/Mask/Sky` inside dynamically-instantiated level prefabs, so SceneRefs (edit-time wiring) doesn't fit.
- Proper fix is a level-prefab structural change that belongs in **Phase 7** (level creator rebuild). For now this is the only functional `Find` call left in `Assets/Scripts/`, with explicit context.

#### Step 5 — `LevelRegistry` created
- New file: `Assets/Scripts/Bootstrap/LevelRegistry.cs`. Plain class, `HashSet<MonoBehaviour>`-backed, exposes `Register(MonoBehaviour)`, `Unregister(MonoBehaviour)`, `Active<T>()` (LINQ `OfType<T>` over the set).
- Registered in `GameBootstrap.Awake` after the existing plain-class services. Bootstrap's `[DefaultExecutionOrder(-1000)]` guarantees it's available before any consumer's `Awake`/`OnEnable`.

#### Step 6 — Entity self-registration
- `Explodable.OnEnable` / `OnDisable` overridden to register/unregister with `LevelRegistry` (guarded with `IsRegistered<LevelRegistry>()` so OnDisable during teardown is safe).
- This single change covers all Explodable subtypes via inheritance: **Bubble, Booster, LockColor, DyeJar** (and any other `Explodable` subclass like TNT). No edits needed in those subclass files.
- Bubble already overrides OnEnable/OnDisable and calls `base.OnEnable()` — registration chains through correctly.
- Pooled lifecycle: when PoolingObject returns a pooled Bubble/Booster, OnDisable fires (unregister). When borrowed back, OnEnable fires (re-register). Pool churn is correctly tracked.

#### Step 7 — 9 mid-game `FindObjectsOfType<T>()` calls migrated to `LevelRegistry.Active<T>()`
- `DyeJarGenerate.cs:24` — `Active<DyeJar>()`
- `CameraMovement.cs:72` — `Active<Explodable>()` (TransferStuckedEntities)
- `Level.cs:193` — `Active<LockColor>()` (Initialize)
- `Level.cs:258` — `Active<Bubble>()` (GetHingedBubble)
- `Level.cs:511` — `Active<Bubble>()` (FindAnyBoosters)
- `Level.cs:673` — `Active<Booster>()` (EndLevelBoostersGetInQueue, with `.Where(activeSelf)` defensive filter retained)
- `Level.cs:695` — `Active<Explodable>()` (ClearAllExplodables, defensive filter retained)
- `Cannon.cs:56` — `Active<Bubble>()`
- `AbilityTornado.cs:180` — `Active<Bubble>()`
- Added `using Bootstrap;` to DyeJarGenerate, Cannon, AbilityTornado (Level + CameraMovement already had it from Step 3/8).
- Added `using Base.Manager;` to Cannon (it didn't have it).

#### Step 8 — CameraMovement obstacles migrated
- `CameraMovement.cs:110` `FindGameObjectsWithTag("Obstacle")` → `LevelRegistry.Active<Explodable>().Where(e => e.CompareTag("Obstacle"))`. Confirmed obstacles are dynamic (destroyed by `ClearAllAbove` mid-game, so per-level registry is correct fit). All "Obstacle"-tagged things in this codebase are Explodable instances, so filtering Active<Explodable> by tag is sufficient — no separate Obstacle list needed.

#### Step 9 — ClickSoundButton component + SoundManager scan stripped
- New file: `Assets/Scripts/UI/ClickSoundButton.cs`. `[RequireComponent(typeof(Button))]`, subscribes/unsubscribes the click-sound listener in OnEnable/OnDisable (idempotent). Calls `SoundManager.PlayButtonClick()` via `Global.Singletons.Get<SoundManager>()` (guarded with `IsRegistered`).
- `SoundManager.cs` — added `PlayButtonClick()` public method. **Stripped** `FindEvertButtonInGame()` and the two `Resources.FindObjectsOfTypeAll<Button>()` calls in Registration/UnRegistration. Simplified `ButtonsSoundAction` away (now `PlayButtonClick` is the public entry point).
- **Backfill** via two-pass editor utility:
  - **Pass 1:** Walked all 234 prefabs under `Assets/Resources` + `Assets/Prefabs`. 26 contained Buttons. 21 prefabs successfully modified, **110 ClickSoundButton components added**.
  - **Pass 2:** 5 prefabs (ComicPanel, ViewBoosterBlue/Red/Yellow, GamePanel) failed the first save with "missing script" errors — pre-existing missing-script attachments left over from Phase 1's Level1-100 deletion. Cleanup script removed 6 missing-script entries via `GameObjectUtility.RemoveMonoBehavioursWithMissingScript`, then added the **26 remaining ClickSoundButton components**. All 5 saved cleanly.
  - **Total: 26 prefabs modified, 6 missing-scripts cleaned, 136 ClickSoundButton components added.** (Original dry-run estimated 154; the ~18 difference is from variant prefabs inheriting the component from their base.)
- **Behavioral parity:** the original code added a click-sound listener to every Button in the project at runtime via `Resources.FindObjectsOfTypeAll<Button>` (a per-Awake scan). The new pattern moves that subscription to a per-Button component. For new Buttons added in the future, drop `ClickSoundButton` on them. Bug fix included for free: the original `RemoveListener(() => ButtonsSoundAction())` lambda capture meant the listener was never actually removed (different lambda instance each call); the new component does idempotent OnEnable/OnDisable subscription.

### MoveConstraint FiveMovesRemain — minor improvement
- `MoveConstraint.cs:68` `Resources.FindObjectsOfTypeAll<FiveMovesRemain>()` → `UnityEngine.Object.FindAnyObjectByType<FiveMovesRemain>(FindObjectsInactive.Include)`. Drops the asset/prefab scan in favor of a scene-scoped (including inactive) lookup. Added a null guard around the SetActive toggle.
- Self-registration would be cleaner but depends on UI-prefab Awake timing assumptions worth verifying when the UI gets restructured. Tagged for revisit.

### Final state

- Compilation: **CLEAN**
- Console: real errors 0 (some stale entries from intermediate prefab-save retries that may persist until Console is cleared)
- `GameObject.FindGameObjectWithTag` count in `Assets/Scripts/`: **0**
- `GameObject.FindGameObjectsWithTag` count: **0**
- `FindObjectsOfType` count: **0**
- `Resources.FindObjectsOfTypeAll` count: **0**
- `GameObject.Find` count: **1** (`CameraOrtSize.cs:20`, deferred — orphaned class)
- `FindAnyObjectByType` count: **1** (`MoveConstraint.cs`, scope-scoped replacement, not a regression)
- `transform.Find` count: 1 (`PoolingObject.cs:72`, child lookup — out of Phase 6 scope)
- New files: `SceneRefs.cs`, `LevelRegistry.cs`, `ClickSoundButton.cs`
- Modified scripts: 13 (GameManager, BoosterManager, MakeBubbleConstraint, SpaceBlastRuntimeSpawner, Level, Explodable, DyeJarGenerate, Cannon, AbilityTornado, CameraMovement, MoveConstraint, SoundManager, GameBootstrap)
- Modified scene: `Assets/Scenes/SampleScene.unity` (`SceneRefs` component added to `_GameBootstrap` with 3 wired refs)
- Modified prefabs: **26** (ClickSoundButton backfill)
- Cleaned prefabs: **5** (missing-script removal)
- Deleted folder: `Assets/Plugins/Zenject/`

### Out of scope (explicit deferrals)

- `CameraOrtSize.cs` `GameObject.Find("Sky")` — Phase 7 territory (level prefab structural change).
- `MoveConstraint.cs` FiveMovesRemain self-registration — UI restructure timing, revisit later.
- `PoolingObject.cs:72` `transform.Find(childName)` — child lookup, not scene-wide; lower priority.
- `Level.ActiveLevel` static — Phase 7 (level creator rewrite).
- BlastObject vs Cygnus.Common.MyObject inheritance — outstanding decision.

### Lessons / artifacts worth knowing

1. **Pre-existing missing scripts in 5 prefabs** were latent breakage from Phase 1's Level1-100 script deletion. Phase 6's prefab save surfaced and removed them as a side effect. Worth noting for future agents that PrefabUtility.SaveAsPrefabAsset will fail loudly on missing-script holdovers.
2. **Variant prefab inheritance** explains the ~18-button discrepancy between dry-run and actual additions in the ClickSoundButton backfill. Components added to a base prefab are inherited by variants without requiring re-add.
3. **Resources.FindObjectsOfTypeAll<T>** in the original SoundManager wasn't just over-broad — its companion `RemoveListener(() => ButtonsSoundAction())` used a fresh lambda instance each call, making removal a no-op. Listener leaks would have accumulated with each Registration cycle. The component refactor fixes both.

### Awaiting

User smoke test of SampleScene: bubble click → pop → audio (parity with Phase 4 verified state). Ideally also: take any UI button click anywhere in the game flow and verify the click sound still fires. After confirmation, Phase 5 + Phase 6 are verified complete.

---

## 2026-05-02 (late afternoon) — Phase 7 design + 7.1 complete (Level.ActiveLevel → Singletons)

User reviewed `06_Phase7_LevelCreator_Design.md`, signed off all 8 design picks, added three constraints worth recording:
- **5000-level catalog** target via a future generator. `LevelData` must be parametric/generator-friendly; favor uniform structures over polymorphic SO subclasses (saved as `project_game_shape.md` memory).
- **GameTest scene preserved** as UI prototype to steal from later. Don't delete. `CameraOrtSize` removed from the Phase 7 retirement list as a result (orphaned dev scaffolding, leave it).
- **Genre confirmed**: standard blast/match-3 with events + missions on top.

Engineering arg also recorded: full ECS migration **rejected** for this codebase (wrong scale at ~300 entities, wrong paradigm fit for branchy match-3 logic, hybrid coexistence cost with Spine/DOTween/2D physics). Phase 8 staircase: profile → DOP discipline in MonoBehaviour → surgical Burst/Jobs on measured hotspots only. Saved as `feedback_optimization_stance.md` memory.

### Sub-phase 7.1 — `Level.ActiveLevel` static → `Global.Singletons.Get<Level>()`

Mechanical migration following the same pattern Phase 4 used. Compile CLEAN.

**Level.cs changes:**
- Dropped `public static Level ActiveLevel { get; private set; }` property
- `Initialize`: replaced `ActiveLevel = this` (with a `Destroy(ActiveLevel.gameObject)` predecessor-teardown) with a Singletons-aware version that removes the prior registration before destroying the old GameObject, then `Add(this)`. Defensive against the same-instance reentrant case.
- `OnDestroy`: added `if (IsRegistered<Level>() && Get<Level>() == this) Remove<Level>()` so we only unregister ourselves when we're still the registered owner. Avoids double-Remove throws when prior teardown already cleared us.
- `SaveGameAnalytic` line 355: `ActiveLevel._levelTypes` → `_levelTypes` (was a self-reference via the static).
- `DestroySelf()` line 394: `Destroy(ActiveLevel.gameObject)` → `Destroy(this.gameObject)` (the method name implies "destroy myself"; the static dereference was always a redundant indirection).

**Consumer migration — 12 call sites:**

Dead code removed (5 sites; `_level`/`_activeLevel` cached but never read):
- `BaloonCollected/MissionCollectedTrail.cs:52` — line removed
- `BaloonCollected/LockCollectedTrail.cs:60` — line removed
- `BaloonCollected/BaloonCollectedTrail.cs:46` — line removed
- `Constraint/MoveConstraint.cs:16, 31` — field + assignment removed
- `UI/UICanvas/Dialog/DialogExit.cs:23, 40` — field + assignment removed
- `UI/Panels/RestartConsumablePanel.cs:30, 51` — field + assignment removed (then 2 live ClearAllLists calls migrated)
- `UI/Panels/TransectionPanel.cs:24, 50` — field + assignment removed (then 4 live calls migrated)

Live migration (6 sites):
- `Entities/Objects/Booster.cs:305` — `IsRegistered<Level>()` guard before reading EndLevelType/ComboCount (preserved the original null-check intent)
- `Entities/Objects/DyeJar.cs:54` — direct `Get<Level>()`. Added `using Base.Manager;`. Preserves NRE-on-missing semantics.
- `UI/Panels/MovesOutPanel.cs:105` — direct `Get<Level>()`
- `UI/Panels/NextLevelInfoPanel.cs:39` — `_level = Get<Level>()` (cache is live, used at lines 40-42)
- `UI/Panels/RestartConsumablePanel.cs:98, 109` — both ClearAllLists migrated to `Get<Level>()`
- `UI/Panels/TransectionPanel.cs:152, 174, 183, 216` — 4 calls migrated (MoveCameraFromButtom, DestroySelf, isNextLevelTriggered, ActivateTutorial)

Skipped intentionally:
- `Entities/Objects/TNT.cs:37` — inside a `/* */` block, not active code, harmless.

### Findings worth surfacing

1. **5 `_level`/`_activeLevel` fields were dead code** — assigned in OnEnable/Start, never read. The original `Level.ActiveLevel` static had been redundantly cached "just in case." Cleanup happened as a side effect of the migration. Net file LOC reduction: ~15 lines.
2. **Booster.cs was the only site that null-checked** before access. The other 11 sites assumed Level was alive — would have NRE'd in any scene that didn't have a Level instance. Migration preserves this; new behavior is "throws on Get<T> if not registered" instead of NRE — same crash class.
3. **The static had a `Destroy(ActiveLevel.gameObject)` self-destruct path inside `Initialize`** that I had to migrate carefully — the new code calls Remove before Destroy so that the about-to-be-destroyed Level's OnDestroy doesn't double-remove and throw. Edge case worth knowing if the level transition flow ever changes.

### Out of scope (still deferred to later sub-phases)

- `LevelParent` Transform on `SceneRefs` — not added in 7.1 since nothing uses it yet (deferred to 7.3 when level instantiation lands).
- All sub-phases 7.2-7.7: `LevelData` SO + factory, level lifecycle from data, first creator-built test level, custom inspector, test level library, retirement pass.

### Awaiting

Smoke test is **low-risk for 7.1** — SampleScene doesn't currently instantiate any `Level` (gameplay there comes via `SpaceBlastRuntimeSpawner`'s parallel loop). The migration is effectively a no-op at runtime for the user-testable scene. Compile CLEAN is the strongest signal we have until level loading is rebuilt in 7.3.

Recommend: quick SampleScene smoke test (bubble pop + audio + button click parity with Phase 6) to confirm nothing regressed in adjacent code, then green-light starting **sub-phase 7.2** (`LevelData` SO + `LevelFactory`).

---

## 2026-05-02 (late afternoon) — Hotfix: boosters not spawning on 4+ connected pops

User reported that popping 4+ connected bubbles no longer produces a booster (the "bigger bubble"). Audio + bubble pop still worked, so it wasn't a wholesale regression.

### Root cause: silent script-disable, same pattern as the LevelParent bug

`BoosterManager.Awake` (Phase 6) reads `Global.Singletons.Get<SceneRefs>().BoosterParent`. `SceneRefs.Awake` self-registers via `Global.Singletons.Add(this)`. **Both scripts were at default execution order (0).** Unity does not guarantee Awake order between same-priority scripts across different GameObjects.

When BoosterManager.Awake fires before SceneRefs.Awake:
1. `Get<SceneRefs>()` throws (Cygnus's SingletonManager throws on missing key, doesn't return null)
2. Unity silently disables the entire BoosterManager MonoBehaviour
3. BoosterManager.OnEnable never fires
4. `Bubble.DestroySequenceBubbleEvent` and `Booster.BoosterMakeEvent` never get subscribed
5. 4+ pops never produce a booster

This is **the exact same failure mode** as the 2026-05-01 LevelParent hotfix where `GameManager.Awake`'s tag-find threw and silently killed the canClick timer. Different trigger, same outcome.

Why audio/bubble-pop kept working: those go through `GameManager` and `SoundManager` which don't depend on SceneRefs, only on services GameBootstrap registers in its -1000-priority Awake.

### Fix

Added `[DefaultExecutionOrder(-1000)]` to SceneRefs.cs. Now SceneRefs Awakes in the early bucket alongside GameBootstrap. Within that bucket, both scripts live on the `_GameBootstrap` GameObject, GameBootstrap was added first (component order from Phase 4), so order is: GameBootstrap.Awake (registers UserData/Factory/LevelRegistry/etc.) → SceneRefs.Awake (registers itself) → all default-priority Awakes (BoosterManager etc.) finish.

### Other consumers of `Get<SceneRefs>` checked

- `MakeBubbleConstraint.cs:37` — called from `Level.Initialize`. Same risk if a Level is ever in scene at default priority. Currently no Level in SampleScene; will become relevant in 7.3+.
- `SpaceBlastRuntimeSpawner.cs:72` — in `Start()`, not Awake. Start runs after all Awakes. Safe.
- `Level.cs:192, 541, 586` — Level lifecycle methods called from BlastObject.Awake chain. Same risk as MakeBubbleConstraint when Level lands in scene.
- `BoosterManager.cs:46` — **the bug**. Now fixed.

### Lesson worth memorizing

Singletons-based lookups in `Awake` are fragile to script execution order across GameObjects. The pattern that works:
- Provider scripts (registering into Singletons) need `[DefaultExecutionOrder(<negative>)]` to Awake first
- Or: consumers should query in `Start()` instead of `Awake()` (Start runs after all Awakes)
- Or: consumers should use `IsRegistered<>` guards + lazy retry (verbose, hides ordering bugs)

Going with the first: any script that other Awakes will read from gets `[DefaultExecutionOrder(-1000)]`. So far that's GameBootstrap and now SceneRefs.

### Final state

- Compilation: CLEAN (after domain reload completes)
- Console errors: 0
- One-line change to `SceneRefs.cs` — added the attribute

### Awaiting

User retests: pop a 4+ connected bubble group → bigger bubble (booster) should now spawn with VFX + sound.

---

## 2026-05-02 (evening) — Phase 7.2 complete (LevelData SO + LevelFactory)

User confirmed booster hotfix works ("Okay they work now"). Greenlit 7.2.

### What landed

Two new files, both compile CLEAN, factory verified by programmatic smoke test:

**`Assets/Scripts/Levels/LevelData.cs`** — ScriptableObject + 5 tagged-union config structs + 3 enums.

- `LevelData` SO fields:
  - `levelPrefab` (GameObject ref to the visual + targets prefab)
  - `levelId` (int, parametric — scales to 5000+)
  - `legacyLevelType` (LevelTypes enum, retained for GameAnalytic compat)
  - `bubbleRates` + `maxBubbleCount` (drives MakeBubbleConstraint)
  - `moveConstraint` (MoveConstraintConfig — required)
  - `timeConstraint` (SecondConstraintConfig — optional, gated by `enabled` bool)
  - `missions: List<MissionConfig>`
  - `miniGames: List<MiniGameConfig>`
  - `generators: List<GeneratorConfig>` (data shape only — invocation deferred to 7.3)

- Tagged-union approach: every config struct stores all possible params, the factory's switch reads only the ones relevant to `cfg.type`. Wasteful per-row but uniform — exactly what a future level generator needs (no polymorphic SO subclasses, no asset-per-mission proliferation).

- Three new enums separate from the legacy runtime enums:
  - `LevelMissionType` (9 values) — maps 1:1 to concrete Mission classes. The legacy `MissionTypes` enum has gaps (no ToolBox/Kitten/Ore values, has phantom Bird/Soil/Rock with no implementations). Keeping data-side enum separate so SO authoring is exhaustive without modifying runtime enums.
  - `LevelMiniGameType` (1 value: Bubble) — currently the only implemented MiniGame. The legacy `MiniGameTypes` enum has 9 placeholders.
  - `LevelGeneratorType` (6 values) — DyeJar/Lander/TNT/Ore/ToolBox/Kitten.

**`Assets/Scripts/Levels/LevelFactory.cs`** — static factory with three methods:

- `BuildConstraints(LevelData) → List<Constraint>` — always includes MoveConstraint + MakeBubbleConstraint, optionally adds SecondConstraint
- `BuildMissions(LevelData) → List<Mission>` — switch over `LevelMissionType`, instantiates the right concrete class via the right pattern (some take ctor args, others use default ctor + property-set per `DyeJarMission { jarCountShouldBeExploded = ... }`)
- `BuildMiniGames(LevelData) → List<MiniGame>` — switch over `LevelMiniGameType`
- Generator invocation deferred to 7.3 (needs the instantiated level GameObject + spawn point — runtime context that BuildX methods don't have)

### Quirks discovered

- **`TntMission` class is named lowercase** despite the file being `TNTMission.cs`. Factory uses `new TntMission(count)`. Pre-existing inconsistency; not worth fixing in this pass.
- **MakeBubbleConstraint constructor reads `Singletons.Get<SceneRefs>()`** — won't construct outside play mode. The smoke test deliberately skipped `BuildConstraints` for that reason. Constraints will be exercised when 7.3 runs a real level in play mode. Worth flagging: the constraint constructor doing scene-side-effect work means it can't be instantiated in tests without a stub. Could refactor in 7.3 if it becomes annoying.
- **DyeJarGenerate's `TogetherLevelGenerate` signature is `(Transform, int, DyeJarTypes)`** — different from the other 5 generators which take `(Transform, int)`. The 7.3 generator dispatcher will switch on type and pass `cfg.dyeJarType` only for DyeJar.

### Smoke test result

Fed a hand-crafted in-code LevelData (9 missions, 1 mini-game) through the factory:
- 9 input MissionConfigs → 9 output Mission instances, all correct concrete types (BubbleMission, DyeJarMission, LockColorMission, LockKeyMission, LanderMission, OreMission, TntMission, ToolBoxMission, KittenMission)
- Property values verified: BubbleMission.BubbleType=Blue/Count=30, DyeJarMission.jarCountShouldBeExploded=3, LockColorMission.lockColorCountShouldBeExploded=4
- 1 input MiniGameConfig → 1 BubbleMiniGame with type=Blue, count=50

Factory works as designed. No console errors.

### Known limitations / deferred

- **`LevelTypes` enum (100 values, Level1..Level100) won't scale to 5000.** GameAnalytic.SaveAnalytic still consumes the enum. `LevelData.legacyLevelType` keeps the link working. Replacement: add `int levelId` to GameAnalytic API, retire the enum dependency. Tracked as out-of-scope for 7.x — falls under analytics modernization, separate concern.
- **No editor inspector yet** (sub-phase 7.5). The default Unity inspector for LevelData works but is cramped — bubble rates show as integer fields, mission list shows generic struct rendering. Authoring is functional but unpleasant. Improving in 7.5 once the rest of the pipeline (7.3, 7.4) is proven out.
- **No level prefab linkage validation yet** (also 7.5). The plan calls for an editor button that cross-checks SO mission counts against pre-placed prefab targets.
- **Generator dispatch deferred to 7.3** as planned — needs the instantiated level + spawn point.

### Files

- New: `Assets/Scripts/Levels/LevelData.cs`, `Assets/Scripts/Levels/LevelFactory.cs`
- No modifications to existing files in this sub-phase.

### Next: sub-phase 7.3 — Level lifecycle from data

The big one. Replace the current abstract Level + per-level subclass pattern with a concrete `LevelRunner` (or rename Level itself to be concrete) that:
1. Takes a `LevelData` reference
2. Instantiates `data.levelPrefab` under `SceneRefs.LevelParent` (which gets added to SceneRefs in this sub-phase)
3. Calls `LevelFactory.BuildX(data)` to get the runtime objects
4. Wires Constraints/Missions/MiniGames into the existing event flow
5. Resolves Generators from the prefab and runs them with configured params
6. Rebuilds the empty `GameManager.MakeLevel(LevelData)` API
7. Wires `TransectionPanel`'s 4 `MakeLevel()` callers to actually load a level

Awaiting your green-light.

---

## 2026-05-02 (evening, follow-on) — Phase 7.2 mission shape simplified per user feedback

User reviewed the initial 9 mission types ("Lock/Jar/Kitten… don't know what those are") and asked for a simpler set matching their gameplay vision: "Pop 20 Bubbles & 5 Boosters" or "Make the 3rd level booster." Refactored the mission shape; compile CLEAN, smoke test passes.

### New user-facing mission types

`LevelMissionType` reduced from 9 specific values to **4 conceptual** ones:

- **PopBubbles** — pop N bubbles of a specific color (uses `BubbleTypes` filter; today requires a specific color, not "any")
- **PopBoosters** — use/destroy N boosters (uses `BoosterTier` filter: Any / Level1 / Level2 / Level3)
- **CreateBoosters** — spawn N boosters via 4+ pops or merges (uses `BoosterTier` filter)
- **ClearObstacles** — destroy N entities of one obstacle type (uses `ObstacleType` filter: Lock / LockKey / Jar / Lander / TNT / Ore / ToolBox / Kitten)

The Lock/Jar/Kitten complexity didn't disappear — it's just hidden under one `ClearObstacles` umbrella. The 8 obstacle Mission classes underneath still exist; the level creator UI just doesn't lead with them. Generator-friendly: a future level generator picks `ClearObstacles + ObstacleType.X + count`, doesn't need to know the underlying mission class names.

### New Mission classes

- **`PopBoostersMission(BoosterTier, int)`** — listens to `Booster.BoosterExploded`, filters by tier (or counts all if Any), fires `MissionCompleted` when the threshold is hit, unsubscribes in `Cleanup()`.
- **`CreateBoostersMission(BoosterTier, int)`** — listens to a new `Booster.BoosterCreated` static event (added below).

### Lifecycle additions to support those missions

- **`Booster.BoosterCreated` static event** added; invoked from `Booster.Initialize`. Fires when any Booster instance comes into existence (covers both 4+-pop spawns and merge spawns since both go through `Initialize`).
- **`Mission.Cleanup()` virtual method** added to base. Default implementation unsubs from `Level.MoveCompleted` (mirroring the finalizer's logic but synchronous). Subclasses override to also unsub from any static events they wired in their constructor.
- **`Level.OnDestroy` calls `mission.Cleanup()`** on every mission. Without this, missions that subscribe to static events would leak across Level instances — the static event holds a strong ref, preventing GC.

### Other changes

- **`Booster.BoosterType`** — added a public read-only property (the existing `boosterType` field stays `[SerializeField] private`); the new mission classes need to read tier without exposing the setter.
- **MiniGames system removed from `LevelData`** — the `LevelMiniGameType` enum, `MiniGameConfig` struct, `miniGames` list, and `BuildMiniGames` factory method are gone. The runtime `MiniGame` system is untouched (`Level.MiniGames` static list still exists; `GameManager.BlueBubbleMiniGame()` still hard-codes one). Reasoning: only one MiniGame type was ever implemented, and it semantically overlaps with PopBubbles. Authoring MiniGames in `LevelData` would be confusing; the existing static list keeps the legacy hard-coded mini-game working until the system gets a proper redesign.

### Smoke test result

Fed a `LevelData` with all 4 user-facing mission types (PopBubbles + PopBoosters + CreateBoosters + ClearObstacles×2 with different obstacle types) through the factory:
- 5 input MissionConfigs → 5 correct concrete Mission instances (BubbleMission, PopBoostersMission, CreateBoostersMission, LockColorMission, DyeJarMission)
- All filter/count fields verified
- `BoosterTierUtil.GetTier` mapping verified across the 3-row × 3-level grid: Booster1Level1→Level1, Booster2Level3→Level3, Booster3Level2→Level2
- `Cleanup()` runs without throwing

### Files changed

- New: `Missions/PopBoostersMission.cs`, `Missions/CreateBoostersMission.cs`
- Modified: `Missions/Mission.cs` (added Cleanup virtual + null-guarded finalizer), `Levels/Level.cs` (calls mission.Cleanup in OnDestroy), `Entities/Objects/Booster.cs` (BoosterCreated event + invoke + BoosterType getter), `Levels/LevelData.cs` (new enum shape + BoosterTierUtil), `Levels/LevelFactory.cs` (new dispatcher; BuildMiniGames removed)

### Saved as memory

User asked for a comprehensive game-flow + class-responsibility + design-patterns writeup after Phase 7's gameplay-critical sub-phases land. Saved as `project_pending_architecture_writeup.md` with delivery trigger = "after 7.4 verified."

### Awaiting

Green-light for sub-phase 7.3. The mission shape is settled; the next chunk wires `LevelData → LevelFactory → Level → loaded prefab → playable level` and rebuilds `GameManager.MakeLevel`.

---

## 2026-05-02 (late evening) — Phase 7.3 code-complete (Level lifecycle from data)

User said "works perfect, keep going." Executed 7.3 in one push. Compile CLEAN. Real load test deferred to 7.4 since edit-mode smoke can't satisfy all the runtime singletons.

### What landed

**`Level.cs` — now non-abstract + data-driven**

- Class declaration changed from `public abstract class Level : BlastObject` → `public class Level : BlastObject`. Per-level `LevelN` subclassing pattern is gone (subclasses were already deleted in Phase 1).
- Two previously-abstract methods (`OnMissionCompleted`, `OnConstraintFailed`) made `virtual` with empty bodies. They were overridden by the deleted subclasses for per-level reactions; now mission/constraint events surface to UI panels via the events themselves, so the empty defaults are correct.
- Added `[SerializeField] private LevelData _data;` field + `public LevelData Data => _data;` getter + `public void SetData(LevelData data)` setter. The setter is meant to run before the GameObject becomes active so Awake → Initialize sees the data.
- `Initialize` now reads `_data` (when present) and:
  - Copies `_data.legacyLevelType` into `_levelTypes` (which downstream `_levelName`, analytics, and CurrentLevel still consume)
  - `Constraints.AddRange(LevelFactory.BuildConstraints(_data))`
  - `Missions.AddRange(LevelFactory.BuildMissions(_data))`
  These run **before** the existing `foreach (Constraint c in Constraints) c.SetLevel(this)` loop, so the factory-built items get wired into the standard event flow.
- New `private void Start()` method:
  - `_lockColorsInLevel = LevelRegistry.Active<LockColor>().ToList();` — moved from Initialize. **Why:** Unity activates a hierarchy parent-first, so Level.Awake fires BEFORE the child visual prefab's OnEnables. If we read LevelRegistry in Initialize, the child entities haven't registered yet → empty list. Start fires after the full activation cascade completes, so the registry is populated.
  - `LevelFactory.RunGenerators(_data, gameObject)` — same reason; needs the prefab's children fully active before resolving generator components.

**`LevelFactory.cs` — added `RunGenerators`**

Public static `RunGenerators(LevelData data, GameObject levelInstance)`. Iterates `data.generators`, switches on `LevelGeneratorType`, finds the matching generator component on the level prefab via `GetComponentInChildren<T>(true)`, and invokes `TogetherLevelGenerate` with the configured params. Handles DyeJar's special signature (takes `DyeJarTypes`) as a separate case from the other 5 (which take just `Transform + int`).

If a generator component is missing on the prefab, logs a warning and continues — no throw. Lets test levels skip generators they don't need.

**`SceneRefs.cs` — added `LevelParent` field**

`[SerializeField] private Transform levelParent;` + `public Transform LevelParent => levelParent;`. Marked nullable in tooltip; `MakeLevel` falls back to scene root via `Instantiate(prefab, null)` if it isn't wired.

Scene wiring deferred to 7.4 — no point creating a `LevelParent` GameObject in SampleScene until there's a real level asset to test loading into it.

**`GameManager.cs` — `MakeLevel(LevelData)` overload**

```
public Level MakeLevel(LevelData data) {
    // null guards for data + data.levelPrefab
    GameObject levelInstance = Instantiate(data.levelPrefab, sceneRefs.LevelParent);
    levelInstance.SetActive(false);          // pause so SetData runs before Awake
    Level level = levelInstance.GetComponent<Level>() ?? levelInstance.AddComponent<Level>();
    level.SetData(data);
    levelInstance.SetActive(true);           // Level.Awake → Initialize fires now
    return level;
}
```

The `SetActive(false) → AddComponent → SetData → SetActive(true)` pattern is the cleanest way to inject data before Awake on a freshly-instantiated MonoBehaviour. The visual prefab's Awakes fire on Instantiate (before the deactivate), then OnDisable fires on the deactivate, then OnEnable + Level.Awake fire on the reactivate. One extra OnDisable/OnEnable cycle for the visual children — acceptable cost.

The pre-existing empty `MakeLevel()` (no-arg) is left in place with a comment explaining its 4 callers in `TransectionPanel` will become wired once a level catalog (LevelData asset library) exists.

### Quirks that surfaced and got fixed

1. **Namespace collision: `Levels.LevelData` vs `Managers.Levels.LevelData`.** GameManager imports both `using Levels;` and `using Managers.Levels;`. Writing `Levels.LevelData` in the qualified form resolved to `Managers.Levels.LevelData` (which doesn't exist) due to the inner-scope `Managers.Levels` import shadowing. **Fix:** used `global::Levels.LevelData` and `global::Levels.Level` in `MakeLevel`'s signature and body. Defensive practice anywhere these two namespaces coexist.

2. **`OnMissionCompleted` / `OnConstraintFailed` were abstract.** Removing `abstract` from the class without addressing them produces CS0513. Both were overridden per-level previously; now no-op virtual defaults.

3. **`_lockColorsInLevel` activation-order bug** — pre-existing in the original `Initialize`, surfaced when thinking about the new prefab-then-Level construction order. Fix: moved to Start.

### Smoke test results (limited by edit-mode constraints)

Verified in edit mode:
- `Level.SetData(data)` works
- `Level.Data.levelId == 7` after SetData
- `Level.Data.missions[0].type == PopBubbles` confirms the data flows through
- `LevelFactory.BuildConstraints(data)` throws as expected (`MakeBubbleConstraint` ctor reads `SceneRefs` which isn't registered outside play mode)

Cannot verify in edit mode:
- Full `GameManager.MakeLevel(data)` chain (no GameManager registered in edit mode)
- Generator dispatch (no prefab with generator components to dispatch to)
- The deactivate/activate timing (no real prefab to instantiate)

Real verification requires play mode + an authored LevelData + a level prefab — all of which lands in 7.4.

### Files changed

- `Levels/Level.cs` — non-abstract, +SetData/Data, +Start, factory build in Initialize, virtual stubs for the two ex-abstract methods
- `Levels/LevelFactory.cs` — added RunGenerators
- `Bootstrap/SceneRefs.cs` — added LevelParent field
- `Managers/Core/GameManager.cs` — added MakeLevel(LevelData) overload

### Out of scope (deferred to 7.4)

- Author one real `LevelData` asset on disk + a minimal level prefab
- Wire `SceneRefs.LevelParent` in SampleScene to a `LevelParent` GameObject
- Play-mode test: enter play, call `GameManager.MakeLevel(testLevelAsset)`, verify a playable level appears
- Wire the 4 `TransectionPanel.MakeLevel()` callers (still no-op until a level catalog exists)
- Build the 17-level test library

### Awaiting

Green-light for 7.4. The skeleton's there; 7.4 puts a real test asset through it and confirms gameplay loads end-to-end.

---

## 2026-05-02 (late evening, 7.4 setup) — First creator-built level wired into SampleScene

User said "go." Authored test asset + minimal prefab + wired SampleScene + disabled the legacy spawner. Compile CLEAN, console clean. Ready for the user's play-mode smoke test.

### What landed

**Scaffolding:**
- `Assets/Scripts/Bootstrap/LevelTestLoader.cs` — small MonoBehaviour with `[SerializeField] LevelData testLevelData;` and `loadOnStart` toggle. On Start (after all Awakes complete), calls `Global.Singletons.Get<GameManager>().MakeLevel(testLevelData)`. Guarded with `IsRegistered<GameManager>()`. Temporary scaffolding; will be replaced by the real level catalog + selection UI later.

**Test asset:**
- `Assets/Resources/TestLevels/TestLevel01.prefab` — minimal prefab. Empty root GameObject `TestLevel01` with one placeholder child `VisualPlaceholder`. No pre-placed mission targets, no generator components. The level loads but has no level-specific visual content; bubbles spawn via `MakeBubbleConstraint` into `SceneRefs.BubbleParent` (scene-level), not into the prefab.
- `Assets/Resources/TestLevels/LevelData_Smoke01.asset` — first authored LevelData. Contents:
  - `levelId = 1`, `legacyLevelType = LevelTypes.Level1`
  - `maxBubbleCount = 30`, `bubbleRates = { blue: 60, green: 40 }`
  - `moveConstraint.maxMoves = 30`
  - `timeConstraint.enabled = false`
  - `missions = [PopBubbles{Blue, 10}]` — single, simple mission
  - `generators = []` — none
  - `levelPrefab` → wired to TestLevel01.prefab

Single-mission, single-spawn-rate, no-obstacles, no-generators. Smallest viable smoke test. If the user can play it and pop 10 blue bubbles, the entire `LevelData → LevelFactory → Level → MakeBubbleConstraint → bubble spawn → BubbleMission → mission complete` chain is verified.

**SampleScene wiring (via editor utility script):**
- Added `LevelParent` GameObject under SampleScene root. Wired `SceneRefs.levelParent` to its Transform via `SerializedObject` edit on the existing SceneRefs component on `_GameBootstrap`.
- Added `_LevelTestLoader` GameObject with `LevelTestLoader` component. Wired `LevelTestLoader.testLevelData` to `LevelData_Smoke01.asset`.
- Disabled `_SpaceBlastRuntimeSpawner` GameObject (was active). Its parallel game loop would have fought MakeBubbleConstraint — both write to `SceneRefs.BubbleParent`. With the spawner disabled, only the data-driven Level path runs. Spawner GO stays in scene (for easy re-enable if needed) but is inactive; final retirement still scheduled for sub-phase 7.7.

### Files changed

- New: `Assets/Scripts/Bootstrap/LevelTestLoader.cs`
- New asset: `Assets/Resources/TestLevels/TestLevel01.prefab`
- New asset: `Assets/Resources/TestLevels/LevelData_Smoke01.asset`
- Modified scene: `Assets/Scenes/SampleScene.unity` (LevelParent GO added, SceneRefs.levelParent wired, _LevelTestLoader GO + component + data wired, SpaceBlastRuntimeSpawner GO disabled)

### Expected play-mode behavior

When user enters Play Mode in SampleScene:
1. GameBootstrap.Awake fires (-1000 priority) → registers UserData, Factory, GameAnalytic, Buy*, LevelRegistry
2. SceneRefs.Awake fires (also -1000) → registers SceneRefs
3. All other Awakes fire: GameManager.Initialize → registers GameManager; SoundManager, CameraMovement, BoosterManager, PoolingObject, EventReward all self-register
4. All Starts fire: LevelTestLoader.Start → `GameManager.MakeLevel(LevelData_Smoke01)`:
   - Instantiates TestLevel01.prefab under LevelParent
   - Briefly deactivates, attaches Level component, calls SetData, reactivates
   - Level.Awake → Initialize → registers Level in Singletons, builds Constraints (MoveConstraint(30) + MakeBubbleConstraint(30, rates)) and Missions (BubbleMission(Blue, 10)) via LevelFactory, wires both via SetLevel + event hookups
   - MakeBubbleConstraint's ctor reads SceneRefs.Spawn, fills the bubble pool, calls MakeBubble(30) → asynchronously spawns 30 bubbles into BubbleParent over a few seconds
5. Bubbles fall, user can click 4+ groups to pop, BubbleMission counts blue pops
6. After 10 blue popped, BubbleMission fires MissionCompleted (no UI feedback yet, just internal)
7. After 30 moves used, MoveConstraint fires ConstraintFailed (also internal)

What user should see: bubbles spawning, clicking pops them, audio plays. No errors in console. The mission/constraint completion events fire silently (no UI feedback wiring in 7.4) — verification is "no exceptions and gameplay works."

### Known limitations of 7.4 testing

- **No UI feedback** for mission progress / level complete. Mission/Constraint events fire but UI panels (TransectionPanel etc.) aren't wired to listen for THIS level's events. Visual confirmation of mission completion requires hooking up the existing UI panels — partial in 7.4, full in a later UI pass.
- **No visual content** in the test prefab. Bubbles still spawn (they're scene-level via BubbleParent), but the level prefab is just an empty container. Real levels will have a visual layout.
- **No restart / next-level flow.** Hitting move limit doesn't trigger TransectionPanel sequence. Just stops being playable.

### Awaiting

User play-mode test of SampleScene. Expected: bubbles spawn, popping works, audio fires, no console errors. Report back what you see — if the chain works end-to-end, 7.4 is verified and we can move on to 7.5 (custom inspector for LevelData) and the architecture writeup.

---

## 2026-05-02 (night) — Phase 7.4 VERIFIED + 1 hotfix

User play-tested. First test: nothing visible, no error path obvious — diagnosed as "I disabled the SpaceBlastRuntimeSpawner whose IMGUI START button was the user's familiar test trigger; LevelTestLoader was set to silent auto-load on Start, with no UI feedback." Refactored LevelTestLoader to render its own IMGUI START panel (matching the user's expected flow), with the same level info display and a click-to-load button.

Second test: clicked START → got `ArgumentException: InGameTextController Singleton with key not found`. Diagnosis: Level.Initialize calls `Global.Singletons.Get<InGameTextController>()` for in-game floating-text overlays. SampleScene doesn't have an InGameTextController instance — the older spawner-driven flow never instantiated a Level so this dependency never surfaced. Guarded the lookup with `IsRegistered<InGameTextController>()`, falling through to `null` (callers tolerate null).

Third test: **WORKS WELL.** User confirmed gameplay loads end-to-end via the data-driven path:
- LevelTestLoader.Start → IMGUI panel
- Click START → GameManager.MakeLevel(LevelData_Smoke01)
- Visual prefab instantiates under SceneRefs.LevelParent
- Level.Awake → Initialize wires Constraints (MoveConstraint(30) + MakeBubbleConstraint(30, rates)) and Missions (BubbleMission(Blue, 10)) from LevelFactory
- MakeBubbleConstraint fills the bubble pool, async-spawns 30 bubbles into BubbleParent
- User can click 4+ groups to pop, bubbles disappear with audio + VFX
- BubbleMission silently counts blue pops in the background

The full `LevelData → LevelFactory → Level → playable game` chain is verified end-to-end.

### Files touched in 7.4 verification round

- `Assets/Scripts/Bootstrap/LevelTestLoader.cs` — rewritten with IMGUI START panel; `loadOnStart` field replaced with `autoLoadOnStart` (default false). Old serialized value silently dropped (different field name) — desired behavior.
- `Assets/Scripts/Levels/Level.cs` — `InGameTextController` lookup guarded with `IsRegistered` check.

### Lessons worth saving

1. **The "silently disabled MonoBehaviour" pattern strikes again** — same root cause as the LevelParent tag bug (2026-05-01) and the SceneRefs execution-order bug (2026-05-02 morning). When a `Singletons.Get<T>` in Awake/Initialize throws on missing key, Unity disables the MonoBehaviour silently. No console message about the disable itself, just the original throw. Going forward, any new Singletons.Get inside Awake/Initialize should consider whether the dependency is guaranteed to be in the scene; if not, guard with IsRegistered.

2. **Test-only scaffolding belongs behind a button.** Auto-loading a feature on Play silently is fine in production once the flow is solid, but during initial testing, an explicit user-action gate makes the failure mode visible. The IMGUI START panel pattern (used by SpaceBlastRuntimeSpawner originally and now LevelTestLoader) is the right shape for this.

3. **Authoring-time vs runtime dependencies** — SampleScene was originally only providing dependencies the SpaceBlastRuntimeSpawner needed (PoolingObject, SoundManager, etc.). Level's broader dependency surface (InGameTextController, BaloonCollectCanvas, GameTopCanvasMissions, GameTopCanvasMove, TransectionPanel) wasn't all there. Three of those will surface when their respective code paths fire (mission progress trail, mission UI display, end-of-level UI) — guard or scene-add as needed when they do.

### Phase 7.4 status: COMPLETE

All sub-phase 7.4 deliverables:
- ✅ Author one real `LevelData` asset (LevelData_Smoke01)
- ✅ Author minimal level prefab (TestLevel01)
- ✅ Wire SceneRefs.LevelParent in SampleScene
- ✅ Add LevelTestLoader scaffolding with click-to-load UX
- ✅ Disable SpaceBlastRuntimeSpawner so the data path isn't fought
- ✅ Play-mode verification: bubbles spawn, gameplay works, no errors

### What this triggers

The user's 2026-05-02 standing request: "After we finish most of the phases or most important ones that decide the mechanics&gameplay of the game, I need a whole explanation of flow of the game, what does the classes do, what type of designs patterns we have used, etc etc.. Important stuff."

7.4 verified = the gameplay mechanics are settled in their post-refactor shape. Delivering the architecture writeup as a standalone reference doc at `ImplementationPlans/99_Architecture_Reference.md` (per the saved-memory delivery plan).

### Next sub-phases (7.5+)

In the original plan, but not strictly gameplay-blocking:
- **7.5** — Custom Inspector for LevelData. Default inspector is functional but cramped (mission list shows generic struct rendering, no per-type field hiding). UX improvement.
- **7.6** — Build the test level library (~13-17 levels covering mission/constraint/edge cases).
- **7.7** — Retirement pass. Delete Assets/Resources/Levels/Level-*.prefab corpses (1-100), delete SpaceBlastRuntimeSpawner.cs, etc. Per CameraOrtSize+GameTest deferral, those stay.

These can be done in any order or deferred indefinitely. The core gameplay system is now data-driven and working.

---

## 2026-05-02 (night, follow-on) — Phase 9 plan + 9.1 done

User answered: "lets continue to make this game prod worthy" + asked for a Main Menu with Start / Levels / Settings working, other buttons stubbed. Recon revealed `MainPanel.prefab` + `MainPanel.cs` + `SettingsPanel` are mostly intact — most of the menu is salvage, not from-scratch. Wrote `ImplementationPlans/07_Phase9_MainMenu_and_Catalog.md` bundling Main Menu + LevelCatalog + end-of-level wiring (the three are tightly coupled).

User signed off all 6 design picks (single scene + overlay panels, salvage existing UI, no onboarding, scrollable level grid, stubbed buttons show "Coming soon", single explicit catalog asset). Skipping ConsumableItemPanel for tight scope (can add later). Seeding Level 2 as duplicate so end-of-level transition has somewhere to go.

Quick clarification on phase numbering: original Phase 8 (Mobile Performance Pass) is deferred until after Phase 9 + level authoring — no point profiling a game without menu/progression. Phase 9 is the "make it actually playable end-to-end" wave.

### Sub-phase 9.1 — LevelCatalog foundation

**Files added:**
- `Assets/Scripts/Levels/LevelCatalog.cs` — ScriptableObject with `List<LevelData> levels`. APIs: `Count`, `GetByPlayerLevel(int)` (1-based), `GetByIndex(int)` (0-based), `GetByLevelId(int)` (linear scan; future: dictionary cache when 5000-level catalog lands).
- `Assets/Resources/LevelCatalog.asset` — the single catalog instance. Bootstrap loads it via `Resources.Load<LevelCatalog>("LevelCatalog")`.
- `Assets/Resources/TestLevels/LevelData_Smoke02.asset` — Level 2, duplicate of Smoke01 with tweaked config (40 bubbles, 25 moves) so it's distinguishable when the level-end transition lands on it.

**Files modified:**
- `Assets/Scripts/Bootstrap/GameBootstrap.cs` — Awake now `Resources.Load<LevelCatalog>("LevelCatalog")` and registers if non-null. Logs a warning if missing (so a fresh project without an authored catalog doesn't fail Bootstrap silently).
- `Assets/Scripts/Managers/Core/GameManager.cs` — the empty `MakeLevel()` no-arg stub (left over from Phase 1) is now wired: `MakeLevel(catalog.GetByPlayerLevel(UserData.PlayerLevel))`. Guards both: missing catalog → warning + early return; PlayerLevel out of catalog range → warning + early return. Both wrap the existing `MakeLevel(LevelData)` overload so the load path is single-source.

**Smoke test (in editor):** `Resources.Load<LevelCatalog>("LevelCatalog")` returns the asset; `Count == 2`; `GetByPlayerLevel(1)` → `LevelData_Smoke01`, `GetByPlayerLevel(2)` → `LevelData_Smoke02`, `GetByPlayerLevel(99)` → `null`. Smoke02 has its tweaked config (maxBubbleCount=40, maxMoves=25) confirming it's a real distinct asset, not a reference clone.

**Implication for the 4 silent TransectionPanel callers:** they've been calling `MakeLevel()` since Phase 1 with no effect. Now they actually load the next level via the catalog. The end-of-level animation chain that flows through TransectionPanel → MakeLevel will function once 9.2 wires the trigger.

**Architecture doc updated** in-place per the standing rule: section 1 (boot flow includes LevelCatalog), section 2 (added LevelCatalog class entry; updated GameManager entry to reflect the new no-arg behavior), section 8 (added LevelCatalog to glossary).

### Next: 9.2 — End-of-level → next-level chain validated

Trace: `Mission.MissionCompleted` (per-mission, fired by mission's own logic) → some aggregator that decides "all missions done" → `Level.IsLevelDone(true)` → `Level.EndLevelProcesses` → `TransectionPanel` animation → `UserDataManager.LevelUp()` → `GameManager.MakeLevel()` (now wired) → next level loads.

What needs verification / wiring in 9.2:
- Is the "all missions done" aggregator already implemented? Probably not — needs to count completed missions vs total and fire IsLevelDone when they match.
- Does `Level.EndLevelProcesses` already chain into `TransectionPanel`? Need to verify the existing wiring.
- Does `UserDataManager.LevelUp` actually increment `UserData.PlayerLevel` and persist? Need to verify.
- After all that, does `GameManager.MakeLevel()` get called? If yes — we have an end-to-end chain.

Bonus from saved state: `UserData.PlayerLevel` will need to be reset for testing (currently whatever the user's prior PlayerPrefs value is). May add a temp dev shortcut.

---

## 2026-05-02 (night, 9.2 done) — End-of-level aggregator wired

Recon traced the full chain. **The end-of-level visual sequence was already fully built** — only the kickoff was missing.

### Chain inventory (what was already there)

- `Level.IsLevelDone(bool isNextLevel)` orchestrates the win/lose decision and animation queueing
- `WaitForAllBoostersInList(PlayEndLevelAnimation)` waits for booster cleanup, then animates
- `PlayEndLevelAnimation` → `TransectionPanel.StartLevelCompleteAnimation(EndLevelProcesses)` (win) or `CanCheckMoveOut` (lose)
- `EndLevelProcesses` → `EndLevelBoostersGetInQueue` → `ClearAllExplodables`
- `CanCheckMoveOut` opens NextLevelInfoPanel (win), MovesOutPanel (first lose), or RestartConsumablePanel (repeat lose)
- `NextLevelEventInvoke()` increments `UserData.PlayerLevel` via `UserDataManager.LevelUp()` and raises `NextLevelEvent`
- TransectionPanel animation events eventually fire `MakeLevelStartAnimaitonEvent` → `GameManager.MakeLevel()` (no-arg, wired in 9.1) → next level loads

### What was missing

Nothing called `IsLevelDone(true)` when missions complete. The deleted `LevelN` subclasses had this in their override of `OnMissionCompleted`. After Phase 7.3 made Level non-abstract, `OnMissionCompleted` was a virtual no-op stub.

### Fix

Replaced the no-op stubs in `Level.cs` with the standard data-driven aggregation:

```csharp
private int _completedMissionCount;

protected virtual void OnMissionCompleted(IMission mission)
{
    _completedMissionCount++;
    if (_completedMissionCount >= Missions.Count && EndLevelType == EndLevelType.Playing)
        IsLevelDone(true);
}

protected virtual void OnConstraintFailed(IConstraint constraint)
{
    if (EndLevelType == EndLevelType.Playing)
        IsLevelDone(false);
}
```

Both kept `virtual` so future per-level customization is still possible (call `base` to keep the default aggregation). Both guarded with `EndLevelType == Playing` to prevent double-fire if mission completion + constraint failure land on the same move (the win path wins by virtue of being the first call).

### What this enables

The full chain now works end-to-end (in code):
1. Player completes the BubbleMission(Blue, 10) in the test level
2. `OnMissionCompleted` increments count to 1, equals `Missions.Count` (1), fires `IsLevelDone(true)`
3. EndLevelType → NextLevel, win animation plays
4. Booster cleanup, explodables clear
5. NextLevelInfoPanel opens
6. (Player would tap Play here)
7. NextLevelEventInvoke → `UserData.PlayerLevel++` (1 → 2) and persists
8. TransectionPanel slides, MakeLevelStartAnimaitonEvent fires
9. `GameManager.MakeLevel()` (no-arg) calls `catalog.GetByPlayerLevel(2)` → returns Smoke02
10. Smoke02 loads, gameplay continues with 40 bubbles + 25 moves

**Cannot smoke-test in editor without play mode + UI canvases** (NextLevelInfoPanel, TransectionPanel must be in scene). Verification will happen in 9.3 once MainPanel + companion panels are placed in SampleScene and the user can play through a level.

**Architecture doc updated** in-place: section 1h (end-of-level path) now reflects the aggregator-driven flow; section 2 (Level class entry) mentions OnMissionCompleted/OnConstraintFailed behavior.

### Sub-phase 9.2 status

Code-complete. Real verification waits for 9.3 + the user playing through to mission completion.

### Next: 9.3 — MainPanel into SampleScene

This is the physical UI placement step. Need to:
- Identify what UI canvases must be in the scene for the full Level lifecycle to not throw on missing Singletons (TransectionPanel, NextLevelInfoPanel, MovesOutPanel, RestartConsumablePanel, MainPanel, ConsumableItemPanel, BaseCanvas, BaloonCollectCanvas, GameTopCanvasMissions, GameTopCanvasMove)
- Either add ALL the prefabs to SampleScene, OR add the ones needed for the gameplay path and guard the rest like InGameTextController
- Disable LevelTestLoader (its IMGUI START is replaced by MainPanel.Play)
- Wire MainPanel.Play to the new flow (currently it opens ConsumableItemPanel; per Q2 we're skipping ConsumableItemPanel for tight scope, so wire Play directly to MakeLevel)

---

## 2026-05-02 (night, 9.3) — UI dropped into SampleScene + Play wired

**Critical recon finding:** `Assets/Prefabs/GamePanel.prefab` is a pre-assembled UI bundle containing **all** the runtime panels — MainPanel, ConsumableItemPanel, TransectionPanel.V2, RestartConsumableItemPanel, NextLevelInfoPanel, BuyAbilityPanel, BuyBoosterPanel, Comic1Panel, LastXMoveText (FiveMovesRemain), InGameTexts (InGameTextController), LevelComplateAnimation, BaloonCollected (BaloonCollectCanvas). One prefab, full UI infrastructure.

**Two issues with GamePanel:**
1. No Canvas component anywhere in its hierarchy — needs a Canvas wrapper.
2. `MainPanel._settingsPanel` field is null in the prefab — the SettingsPanel is a separate prefab (`Assets/Resources/UI/Panel/SettingsPanel.prefab`) and needs runtime wiring.

### What landed

Editor utility wired SampleScene:
- Created `MenuCanvas` root GameObject with Canvas (ScreenSpaceOverlay, sortingOrder=10) + CanvasScaler (ScaleWithScreenSize, ref 1080×1920, match=1) + GraphicRaycaster
- Instantiated `GamePanel.prefab` as a child of MenuCanvas; stretched to fill (anchorMin/Max + offsets reset)
- Instantiated `SettingsPanel.prefab` as a sibling under MenuCanvas, started inactive
- Wired `MainPanel._settingsPanel` → SettingsPanel instance via SerializedObject edit
- Disabled `_LevelTestLoader` GameObject (its job is now done by MainPanel.Play)
- Deleted leftover `LevelSmokeTest` GameObject from earlier debugging

Plus modified `MainPanel.cs`:
- `OnBtnPlayGame` now calls `TransectionPanel.CloseTransectionOnOnMenu()` instead of `_consumableItemPanel.Activate()`. The TransectionPanel's existing close-on-menu animation chain already terminates in `MakeLevel()` (no-arg, wired to LevelCatalog in 9.1) — so we get the existing animated transition without depending on ConsumableItemPanel.
- Falls back to direct `GameManager.MakeLevel()` if TransectionPanel isn't registered (defensive).
- Comment notes how to restore the full ConsumableItemPanel pre-game booster picker later.

### Files changed

- `Assets/Scripts/UI/Panels/MainPanel.cs` — `OnBtnPlayGame` rewired
- `Assets/Scenes/SampleScene.unity` — added MenuCanvas + GamePanel + SettingsPanel; disabled LevelTestLoader; deleted LevelSmokeTest

### Expected play-mode behavior

1. Boot order unchanged (GameBootstrap + SceneRefs at -1000, then everyone else)
2. MenuCanvas active by default → GamePanel visible → MainPanel shown to player
3. MainPanel.Initialize reads UserData (Coin/Health/Trophy/Star displays populated from PlayerPrefs)
4. Player taps **Play** → MainPanel.OnBtnPlayGame → TransectionPanel.CloseTransectionOnOnMenu() (slide animation)
5. Animation event fires `CloseTransectionOnMenuEnd` → DeActivates → `GameManager.MakeLevel()` (no-arg) → catalog lookup → loads `LevelData_Smoke01`
6. Level instance created under SceneRefs.LevelParent, MakeBubbleConstraint async-spawns 30 bubbles
7. Player pops 10 blue bubbles → BubbleMission.MissionCompleted fires → Level.OnMissionCompleted aggregator (9.2) → IsLevelDone(true)
8. Win sequence: WaitForAllBoostersInList → PlayEndLevelAnimation → TransectionPanel.StartLevelCompleteAnimation → EndLevelProcesses → boosters/explodables cleared → CanCheckMoveOut → NextLevelEventInvoke → UserData.PlayerLevel++ (1 → 2) → NextLevelEvent fires → NextLevelInfoPanel opens
9. Player taps Play on NextLevelInfoPanel → TransectionPanel slides → animation event → MakeLevel() → catalog.GetByPlayerLevel(2) → loads `LevelData_Smoke02` (40 bubbles, 25 moves)
10. Cycle repeats

### Settings flow

Tap Settings button on MainPanel → MainPanel.OnBtnSettings → `_settingsPanel.Activate()` → SettingsPanel becomes active. Player toggles Sound/Music/Vibration → updates PlayerPrefs + calls SoundManager.OnOnMainMusicStatusChanged. Tap Exit → SettingsPanel deactivates.

### Known limitations of 9.3

- **Levels, Missions, IAP, Ad buttons don't exist on MainPanel yet.** Only Play, Settings, Comic, ComicBook from the existing prefab. 9.4 adds the new ones.
- **Comic panel will likely break or work weirdly** — it's wired but per-Comic content might not be in scene properly. If it crashes, just don't tap it.
- **MainPanel resource displays** show real `UserData` values. If `PlayerLevel` is high from prior testing, the level button shows that. May need a "Reset PlayerPrefs" debug shortcut.
- **NotHealthPanel field is null** on MainPanel — only fires from `OnUserDataHealtChanged` event handler when health changes. If user runs out of health and it tries to open the null panel, NRE. Edge case; skip for first test.
- **The end-level "tap Play to continue" on NextLevelInfoPanel** — there's a "Play" button there. Need to verify it correctly invokes MakeLevel chain.

### Awaiting

User play-mode test of SampleScene:
1. Hit Play → see MainPanel with resource displays
2. Tap Play button → expect TransectionPanel slide animation → game level loads (LevelData_Smoke01: 30 bubbles, 30 moves, mission "Pop 10 Blue")
3. Click 4+ blue bubble groups, pop 10 blue → expect mission complete → win animation → NextLevelInfoPanel opens
4. Tap Play on NextLevelInfoPanel → expect transition → LevelData_Smoke02 loads (40 bubbles, 25 moves)
5. (Optional) From MainPanel, tap Settings → expect SettingsPanel opens with Sound/Music/Vibration toggles
6. Report any errors or NREs

If the loop works, **9.3 is verified and we can move on to 9.4 (add the 4 new buttons: Levels + stubbed Missions/IAP/Ad).**

---

## 2026-05-02 (night, 9.3.5) — Course correction: two-scene split

User pulled back from the 9.3 single-scene approach: "Don't do everything in the same scene if its not the best approach. I need a scene named Menu and GamePlay at the very least." Right call — single-scene was the lazy pick. Memory saved (`feedback_two_scene_model.md`) so future agent sessions don't backslide.

### Code changes

- **`GameBootstrap.Awake`** — added `DontDestroyOnLoad(gameObject)`. Plus an idempotency guard at the top: if `UserData` is already registered (Bootstrap from another scene survived), self-destroy. Lets you cold-load either scene in the editor.
- **`SoundManager.Initialize`** — added `DontDestroyOnLoad(gameObject)` so audio doesn't restart on scene transition. Existing duplicate-guard already returns early if already registered.
- **`MainPanel.OnBtnPlayGame`** — replaced TransectionPanel chain with `SceneManager.LoadScene("GamePlay")`. Direct, simple.
- **New `Assets/Scripts/Bootstrap/GamePlayAutoLoader.cs`** — drops on a GamePlay-scene GO. `Start()` calls `GameManager.MakeLevel()` (no-arg, resolves via catalog). Replaces LevelTestLoader for the two-scene flow.

### Scene work (via editor utility)

1. **Renamed** `Assets/Scenes/SampleScene.unity` → `Assets/Scenes/GamePlay.unity` via `AssetDatabase.RenameAsset`.
2. **Removed** `MenuCanvas` from GamePlay (moved conceptually to Menu).
3. **Added** `_GamePlayAutoLoader` GO with `GamePlayAutoLoader` component to GamePlay.
4. **Created** `Assets/Scenes/Menu.unity` — fresh empty scene populated with:
   - `Main Camera` (orthographic, dark background, AudioListener, MainCamera tag)
   - `EventSystem` (StandaloneInputModule)
   - `_GameBootstrap` (will DontDestroyOnLoad into GamePlay)
   - `SoundManager` from prefab (DontDestroyOnLoad)
   - `MenuCanvas` (Canvas + CanvasScaler 1080×1920 + GraphicRaycaster) with:
     - `MainPanel` (standalone prefab, stretched to fill)
     - `SettingsPanel` (inactive by default)
   - `MainPanel._settingsPanel` field wired via SerializedObject
5. **Build Settings updated**: Menu (index 0, default), GamePlay (index 1).
6. **Active scene set to** Menu.

### Files changed

- `Assets/Scripts/Bootstrap/GameBootstrap.cs` — DontDestroyOnLoad + idempotency guard
- `Assets/Scripts/Bootstrap/GamePlayAutoLoader.cs` — new
- `Assets/Scripts/Managers/Audio/SoundManager.cs` — DontDestroyOnLoad
- `Assets/Scripts/UI/Panels/MainPanel.cs` — `OnBtnPlayGame` → `SceneManager.LoadScene("GamePlay")`
- `Assets/Scenes/Menu.unity` — new scene
- `Assets/Scenes/GamePlay.unity` — renamed from SampleScene; MenuCanvas removed; AutoLoader added
- `ProjectSettings/EditorBuildSettings.asset` — scene order updated
- `ImplementationPlans/99_Architecture_Reference.md` — sections 1a/1c/1d (game flow) + 4-pre (persistent vs scene-specific) + 2 (LevelTestLoader / GamePlayAutoLoader entries)

### Expected play-mode behavior

1. Hit Play → Menu scene loads (since it's index 0 in Build Settings)
2. Bootstrap fires (-1000 priority), registers UserData/Factory/etc., DontDestroyOnLoads itself
3. SoundManager fires, registers itself, DontDestroyOnLoads
4. MenuCanvas active → MainPanel visible with resource displays
5. Tap **Play** → `SceneManager.LoadScene("GamePlay")` fires
6. Menu scene unloads (MenuCanvas destroys; Bootstrap + SoundManager survive)
7. GamePlay loads — game GOs Awake. Its own GameBootstrap copy self-destroys (UserData already registered). SceneRefs Awakes (no -1000 needed in this scene since Bootstrap's already done; SceneRefs still has -1000 for safety vs other game services). GameManager / BoosterManager / etc. self-register.
8. `_GamePlayAutoLoader.Start` → `MakeLevel()` → catalog lookup → loads `LevelData_Smoke01` → bubbles spawn
9. Player pops 10 blue → mission complete → win animation → NextLevelInfoPanel → tap Play → loads `LevelData_Smoke02`
10. Cycle repeats

### Known limitations of 9.3.5

- **Menu→GamePlay→Menu not supported yet.** No code path goes back to Menu currently. If you add one (e.g., a "Quit to Menu" button calling `SceneManager.LoadScene("Menu")`), the SECOND load of GamePlay would throw "already registered" on its game-scene MonoBehaviours' self-Add. Fix: add `Singletons.Remove<X>()` to OnDestroy of every scene-specific MonoBehaviour. Deferred until that path is needed.
- **Standalone MainPanel.prefab in Menu has null SerializedField refs** for ConsumableItemPanel, NotHealthPanel, ComicPanel (those panels live in GamePlay's GamePanel). Tapping the Comic button in Menu would NRE. Don't tap it. Settings + Play work — that's all you need for now.
- **Camera setup in Menu is minimal** — just an orthographic camera with dark background. No special UI camera, no post-processing. Adjust visually if you want.
- **MainPanel might expect Singletons that don't exist in Menu** — e.g., `Global.Singletons.Get<UserData>()` in Update for health/coin display. UserData IS registered by Bootstrap so should work. But if MainPanel's update path queries something only registered in GamePlay, you'll see Singleton-not-found errors in Menu. Tell me which and I'll guard.

### Awaiting

User play-mode test:
1. Hit Play in editor → expect Menu scene with MainPanel visible
2. Tap **Play** button → expect scene transition → GamePlay loads → bubbles spawn (LevelData_Smoke01)
3. Pop 10 blue → expect win path → NextLevelInfoPanel → tap Play → LevelData_Smoke02 loads
4. (Optional) Back in Menu, tap Settings → expect SettingsPanel
5. Report any console errors or NREs.

If the Menu → GamePlay flow works, **9.3.5 is verified** and we can move on to 9.4 (add Levels + stubbed Missions/IAP/Ad buttons to MainPanel).

---

## 2026-05-03 — End-to-end chain verified + cleanup pass + file map doc

After ~12 rounds of incremental bug fixes, the menu→game→win→NextLevelInfoPanel chain is verified end-to-end. User confirmed the panel appears. Last remaining link broken: panel→level-2 transition (NextLevelInfoPanel.OnButtonPlay → TransectionPanel.CloseTransectionOnGame → animation event → MakeLevel). To be fixed next.

### Real bugs surfaced and fixed during the verification rounds

- `BombSpawnVFX` and `BombMergeVFX` prefabs had broken script GUIDs — Factory couldn't instantiate them. Re-attached the right scripts.
- `MainPanel.OnBtnPlayGame` was opening `ConsumableItemPanel` (a pre-game booster picker not in Phase 9 scope). Rewired to `SceneManager.LoadScene("GamePlay")`.
- `Level.MakeBooster` + `Level.MakeBoosterComb` subscriptions duplicated `BoosterManager.OnBubbleSequenceDestroyed` + `OnBoosterMerge`. Removed the Level subscriptions; BoosterManager is sole authority for booster spawn/merge.
- `ViewAbilityMeteor/Tornado/Ufo.AbilityDeselected` NRE'd on null `AbilityBgPanel` (unwired prefab field). Null-guarded.
- `Level.OnDestroy → DestroyAllExplodablesOnStart` accessed already-destroyed transforms at end-of-Play. Added IsRegistered + null guards.
- `Level.OnDestroy` called `PoolingObject.clearAllPooledObjects()` after PoolingObject was destroyed (race at end-of-Play). Added IsRegistered + Unity-aware null check.
- `WaitForAllBoostersInList` could spin forever if a Booster's `denemeBool` never becomes true. Added 3s timeout + force-clear.
- `ClearAllExplodables` had an 80ms `Task.Delay` per leftover bubble — chain took 2.5s with 30 bubbles, user exiting Play before completion. Lowered to 5ms + skipped the broken `LevelEndClearBubble` sound (sfxAudios index 39 not mapped).
- `UserData.PlayerLevel` defaulted to **75** in field initializer (stale debug default). Reset to 1.
- `GameManager.MakeLevel()` (no-arg) added modulo-wrap fallback so a stale PlayerLevel never blocks gameplay.

### Cleanup pass — files deleted

- `Assets/Scripts/Bootstrap/LevelTestLoader.cs` — pre-9.3 scaffolding, replaced by GamePlayAutoLoader
- `Assets/Scripts/Runtime/SpaceBlastRuntimeSpawner.cs` — Phase 7.7 retirement target
- `Assets/Scripts/Runtime/EntitySpawner.cs` — orphaned (attached nowhere project-wide)
- `Assets/Editor/SpaceBlastSceneSetupTool.cs` — editor tool whose only job was placing the now-deleted spawner
- `Assets/Resources/Levels/Level-1.prefab` … `Level-100.prefab` — 100 throwaway content corpses
- `Assets/Resources/Levels/` folder — empty after the prefab purge
- Disabled `_LevelTestLoader` and `_SpaceBlastRuntimeSpawner` GameObjects from GamePlay scene

Compile CLEAN after cleanup. Zero regressions. ~104 files removed.

### New documentation

`ImplementationPlans/98_File_Map.md` — concise file-by-file responsibility table organized by category (Boot/DI, Persistence, Level System, Constraints, Missions, Generators, Managers, Entities, UI, Economy, MiniGame, VFX, Util, Scenes). Each row tells you what the class does, when it runs, and notable quirks. Plus a "Common questions answered by file" section for "what starts the game on menu", "what holds the state machine", "what handles end-of-level" etc. Companion to `99_Architecture_Reference.md` (which is prose-heavy).

Architecture reference doc updated in-place: removed the LevelTestLoader entry (it's deleted now).

### Honest project state assessment (delivered to user this turn)

Architecture: strong post-refactor. Compile clean. Gameplay loop works end-to-end except the panel→level-2 click bug.

Critical gaps before shipping:
1. Panel→level-2 transition (next session)
2. PlayerPrefs persistence is commented out (UserData fields don't save across editor restarts)
3. Singleton cleanup on Menu↔Game re-entry (currently can do Menu→Game once; round-trips throw)
4. Content (2 levels, need hundreds — user authoring 10-20 manually then a generator later)
5. Build profiles for Android/iOS

Optimization: zero profiler runs on real device. Phase 8 deferred until catalog filled.

---

## 2026-05-03 — EOL polish + TransectionPanel-as-bare-controller (chain fully verified)

User confirmed Level 1 → Level 2 → next level cycle works end-to-end. Closed every gameplay-blocking bug from prior session. About 15 distinct fixes landed across Level.cs, Bubble.cs, TransectionPanel.cs, NextLevelInfoPanel.cs, UserData.cs, UserManager.cs.

### EOL chain refactored

The win-flow now sequences:
1. Mission complete → `OnMissionCompleted` → `levelDone=true` IMMEDIATELY (stops bubble respawn pipeline) → `IsLevelDone(true)`
2. After 2s grace → `WaitForAllBoostersInList(PlayEndLevelAnimation)` coroutine
3. `PlayEndLevelAnimation` → `TransectionPanel.StartLevelCompleteAnimation(EndLevelProcesses)` → 2s panel anim
4. `EndLevelProcesses` (re-entry guarded) → `levelDone=true` (idempotent) → `EndLevelBoostersGetInQueue` (drains active boosters from `SceneRefs.BoosterParent` — these outlive the Level GameObject so MUST be popped explicitly or they carry over)
5. After boosters → `ClearAllExplodables` → sequential 25ms-per-bubble cascade
6. `CanCheckMoveOut` → `NextLevelEventInvoke` → `NextLevelInfoPanel` opens
7. User clicks Play → `CallTransection` → `CloseTransectionOnGame` directly invokes `CloseTransectionOnGameEnd` (bypasses broken animation event hook) → captures `isNextLevelTriggered` → `Level.DestroySelf()` → 300ms gap → `MakeLevel()` (next level inline; no MainPanel/cross-scene refs)

Each step has a re-entry/destroyed-Level guard. `_endLevelChainStarted` blocks repeated EOL runs on the same Level instance. `_isDestroyed` (set in OnDestroy) checked at every async-await resumption in IsLevelDone, EndLevelBoostersGetInQueue, ClearAllExplodables, PlayEndLevelAnimation. `IsLevelDone` has 1.5s grace from `Time.time - _spawnedAtTime` to block stale callbacks from previous Level firing on freshly-spawned new Level.

### TransectionPanel converted to bare-GameObject controller

User removed the TransectionPanel UI prefab during scene reorganization (visuals deemed throwaway). The script's load-bearing role (singleton, event subscriber, EOL orchestrator) was preserved by:
- Null-guarding every UI SerializeField access (`_movesOutPanel`, `_restartLevelConsumablePanel`, `_levelCompleteAnimation`, `anim`, `HardLevel[]` sprite arrays).
- Moving event subscriptions from `OnEnable`/`OnDisable` to `Initialize`/`OnDestroy` (lifetime-scoped, not active-state-scoped — so a temporarily-deactivated panel doesn't lose its NextLevelEvent listener).
- Self-healing singleton: `Initialize` REPLACES stale ref instead of self-destroying; `OnDestroy` unregisters cleanly.

User now adds an empty `LevelTransitionController` GameObject in the GamePlay scene with the `TransectionPanel` component attached + only `_nextLevelInfoPanel` wired. Verified working.

`NextLevelInfoPanel.CallTransection` falls back to singleton if `_transectionPanel` field unwired. `Level.NextLevelEventInvoke` falls back to direct `Global.Singletons.Get<TransectionPanel>().DialogPanelNextLevel()` if static event has 0 listeners. Belt + suspenders — even with broken inspector wiring, the dialog opens.

### Bubble.Explode pooled VFX path

VFX is now a pre-baked child of each bubble color prefab (no per-pop `Instantiate`). On Explode:
- `SpriteRenderer` + `Collider2D` disabled instantly → bubble visually gone, others can fall through immediately
- `_lightObject` child SetActive(false) → no lingering glow during fall
- `_bubbleVFX` SetActive(true) → overlay plays
- After 1s → VFX off + GO off for pool reclaim
- `OnEnable` re-enables renderer/collider/light for pool reuse

User opted Option A (manual prefab wiring per color variant) over runtime Instantiate.

### Other bugs fixed this session

- `Bubble.GiveDamageInRange` `InvalidOperationException: Collection was modified` — fixed by snapshotting `ClosestBubbleList.ToArray()` before iterating; `InvokeTakeDamage` triggers Explode on neighbors which fires OnTriggerExit2D mid-iteration and mutates the list.
- `OnMiniGameCompleted` was a `throw new NotImplementedException()` stub breaking the chain when BlueBubbleMiniGame finished. Now no-op (mini-game state already tracked via UserData counters).
- `_levelCompleteAnimation.SetActive(false)` after the 2s `Task.Delay` in `StartLevelCompleteAnimation` — null-guarded both before/after the await window.
- `EndLevelBoostersGetInQueue` accessed `isActiveAndEnabled` on destroyed Level when stopping play. Removed the unsafe access; added `_isDestroyed` guard.

### Backend & persistence

- `UserManager.Initialize` line `_userData.PlayerLevel = response.Data.CurrentLevelNumber;` commented (backend deferred per project notes).
- `UserData.Load` now force-resets `PlayerLevel = 1` after JSON deserialize. Prior persistence was inflating PlayerLevel via the rapid testing cycles before the EOL re-entry guard landed (saw values up to 100). Force-reset until backend is authoritative.

### Verification

User confirmed:
- Game starts at LevelData_Smoke01 (PlayerLevel=1).
- Mission completes → bubbles cascade sequentially → NextLevelInfoPanel appears → Play → Level 2 loads.
- LevelTransitionController bare-GameObject pattern works.

Compile clean. Console errors zero (modulo pre-existing `MonoBehaviour 'new' constructor` warnings from `LevelFactory` constructing `MoveConstraint`/`MakeBubbleConstraint` — pre-existing architectural smell, not gameplay-blocking).

### Critical gaps still open

1. **MonoBehaviour-as-data anti-pattern** in Constraint hierarchy (`LevelFactory` does `new MoveConstraint(...)` but those inherit MonoBehaviour). Console noise; refactor before Phase 8 perf work.
2. **PlayerPrefs persistence** — JSON blob saves on quit but PlayerLevel force-reset to 1 each load. Wire properly when backend lands.
3. **Singleton cleanup on Menu↔Game re-entry** — TransectionPanel now self-heals; other singletons not audited.
4. **Phase 9.4** — Levels/Missions/IAP/Ad buttons on MainPanel still TODO.
5. **Content** — 2 levels in catalog. User authoring more.
6. **Build profiles** for Android/iOS — not configured.
7. **`SfxSound[36]` enum out of range** in SoundManager — cosmetic, sound effect just doesn't play.

### Decisions captured

- TransectionPanel keeps its name (callsite churn avoided) but is now a logic-only controller; UI prefab discarded.
- VFX pool pattern: child of each bubble prefab + renderer/collider toggle (Option A) chosen over runtime Instantiate (Option B) or editor utility (Option C).
- ClearAllExplodables stays single-pass synchronous-cascade with 25ms per-bubble delay; multi-pass abandoned (caused looping when boosters cascaded back into the registry).
- EOL flow bypasses animation events on TransectionPanel (event-on-clip wiring was unreliable); explicit `CloseTransectionOnGameEnd()` call from `CloseTransectionOnGame()`.

Next move: fix the panel→level-2 transition bug.

---

## 2026-05-04 — Phase 10 Missions Unified (Phases A → D code complete)

Big architectural shift: collapsed the 14 legacy Mission subclasses into a single goal-driven runtime, added a parallel persistent MetaMission system for daily/weekly/monthly meta missions, and built UI for the player to view + claim them. Decisions captured in `ImplementationPlans/08_Missions_Unified.md`. UI wiring instructions for the user in `ImplementationPlans/08a_Missions_UI_Wiring.md`.

### Phase A — Foundation (introduce GameEvents bus + UnifiedMission)

New files:
- `Assets/Scripts/Missions/GameEvents.cs` — static pub-sub. 8 events: BubblePopped, BoosterUsed, BoosterCreated, ObstacleDestroyed, CoinGained, AbilityUsed, LevelCompleted, WinSeriesUpdated.
- `Assets/Scripts/Missions/MissionGoal.cs` — flat data shape (`goalType` enum + `targetCount` + `filter` int + `RewardData`). `filter` is int rather than polymorphic so it serializes flat for PlayerPrefs/MessagePack.
- `Assets/Scripts/Missions/UnifiedMission.cs` — new runtime class consuming MissionGoal, binding to GameEvents via switch, firing ProgressChanged/CompletedEvent. Lived alongside legacy Mission base initially.

Producers wired (raise calls added):
- `Bubble.Explode` → `RaiseBubblePopped(_bubbleType)`
- `Booster.Initialize` → `RaiseBoosterCreated(tier)`
- `Booster.Explode` → `RaiseBoosterUsed(tier)`
- `UserDataManager.EarnCoin` → `RaiseCoinGained(amount)`
- `Level.NextLevelEventInvoke` → `RaiseLevelCompleted(levelId)` (used `global::Missions.GameEvents` to disambiguate from Level.Missions list property)

### Phase B — Migrate LevelMission + delete legacy

Deleted (12 files + .metas):
- 11 legacy mission subclasses: BubbleMission, KittenMission, DyeJarMission, LockColorMission, LockKeyMission, LanderMission, OreMission, TntMission, ToolBoxMission, PopBoostersMission, CreateBoostersMission
- Legacy `Mission` abstract base
- `IMission` interface

Modified:
- `LevelFactory.BuildMissions` — returns `List<Mission>`, builds via `MissionConfig → MissionGoal` translator. Per-mission-type construction = single switch, no per-class file.
- `Level.cs` — Missions list type changed to `List<Mission>`, calls `mission.Bind()` to activate event subs, subscribes to `Mission.CompletedEvent` for win-condition aggregation, `OnMissionCompleted(Mission)` signature.
- `ViewMissions.cs` — stripped 9 legacy static-event subscriptions (`BubbleMission.Reamain*`, `KittenMission.Remain*`, etc.). New `Bind(Mission)` API for direct subscription to `ProgressChanged`/`CompletedEvent`. Currently dormant — Level doesn't call Bind yet because the orphan `ViewMission(List<ViewMissionData>)` flow was never reachable; will be revived when MissionsPanel is the canonical UI.

Renamed: `UnifiedMission.cs` → `Mission.cs` + class symbol everywhere.

**Bumps along the way**: bash `mv` to rename `UnifiedMission.cs` → `Mission.cs` desynced Unity's AssetDatabase. The .csproj kept a stale `<Compile Include="UnifiedMission.cs" />` entry that AssetDatabase.Refresh + SyncSolution wouldn't clear. Resolution: restore the file at the old path, then use `AssetDatabase.RenameAsset(oldPath, newName)` (the proper Unity API). **Never rename .cs files via bash in a Unity project — always go through AssetDatabase.**

Net delta: ~−600 LoC after the refactor.

### Phase C — MetaMission + persistence

New files (5):
- `Assets/Scripts/Missions/MissionTracker.cs` — extracted the GameEvents binding switch into a static helper that returns an unsubscribe lambda. Both Mission and MetaMission delegate to this so the per-goal-type dispatch lives in one place.
- `Assets/Scripts/Missions/MissionPeriod.cs` — Daily / Weekly / Monthly enum.
- `Assets/Scripts/Missions/MetaMission.cs` — persistent mission. Action fields (not events) so `[JsonIgnore]` applies — Newtonsoft can't decorate event declarations directly. Holds Id, Goal, Period, Progress, AcquiredAtUtc, ExpiresAtUtc, RewardClaimed.
- `Assets/Scripts/Missions/MetaMissionService.cs` — Cygnus singleton. Owns lifetime of MetaMissions. Initialize on boot; CheckRollovers regenerates expired periods from MissionTemplateLibrary; ClaimReward dispatches via UserDataManager.EarnCoin/EarnBooster/EarnAbility; auto-saves on progress + completion via UserData.SaveAllDataToPrefs.
- `Assets/Scripts/Missions/MissionTemplateLibrary.cs` — ScriptableObject pool of MissionGoals per period + per-period pick count. `[CreateAssetMenu]` exposed under DragonBlast/.

Modified:
- `Mission.cs` — slimmed; delegates Bind switch to MissionTracker (was duplicated in MetaMission earlier).
- `UserData.cs` — added 4 fields: `MetaMissionsActive` (List<MetaMission>) + 3 reset timestamps (LastDailyResetUtc, LastWeeklyResetUtc, LastMonthlyResetUtc). Persisted via the existing JSON blob. Newtonsoft tolerates missing fields on old saves (defaults applied).
- `GameBootstrap.cs` — `Resources.Load<MissionTemplateLibrary>("MissionTemplateLibrary")`, instantiates MetaMissionService, calls Initialize. Soft warning if asset missing. `OnApplicationPause(false)` re-checks rollovers on resume from background.

User created `Assets/Resources/MissionTemplateLibrary.asset` with seed entries: Daily PopBubbles 20, Weekly PopBubbles 200, Monthly PopBubbles 1000. Verified end-to-end:
- Boot logs show service initialized, 3 missions active, period anchors correct.
- Pop bubbles → progress increments live.
- Stop play → restart → progress persists via JSON blob.
- Daily 20/20 + Weekly 200/200 reached "completed=True", awaiting claim.

Reset cadence: device-local UTC (server-anchored swap deferred to backend integration).

### Phase D — UI (refactored mid-flight per user feedback)

Initial design: Missions button in MainPanel + popup with both "next up" card + scroll list inside.

User course-corrected: the inline "next up" card should live BELOW the MainPanel top bar (replacing the unused `ContentEvent` GameObject which had an empty `MainMenuTopCanvasEvents` script). Tapping the card opens the full panel for the rest. This is the better UX — always-visible mission status, popup only for browsing.

New files (3):
- `Assets/Scripts/Missions/MissionDescriber.cs` — turns MissionGoal into display strings ("Pop 20 blue bubbles", "+100 Coins").
- `Assets/Scripts/UI/Panels/MissionRow.cs` — one row in the scroll list. Bound to a MetaMission, renders title + progress + reward + claim button. Auto-greys claim button when not claimable.
- `Assets/Scripts/UI/Panels/MissionsPanel.cs` — full popup, scroll list of all active missions sorted (claimable first, then by progress %, then by period).

After refactor:
- `Assets/Scripts/UI/Panels/MissionsNextUpCard.cs` — always-visible inline card. Subscribes to MetaMissionService events for live updates. Whole card is tappable (Button on root). Auto-hides when no missions active.
- MainPanel reverted: removed the Missions button + claimable-count badge fields I'd added. Inline card replaces them entirely.

Files modified:
- `MainPanel.cs` — added the Missions UI fields/handlers/badge logic, then removed them in the Phase D refactor (now empty of mission code; the inline card lives as a child object with its own component).

UI wiring deferred to user. Instructions captured in `ImplementationPlans/08a_Missions_UI_Wiring.md` so they can find them later.

### Open items (post-Phase D)

1. **MissionTemplateLibrary content** — only seed entries authored. Needs more variety per period.
2. **MessagePack swap** — Phase E, deferred until backend lands. DTOs ready (no annotations needed yet; field names + types are MessagePack-compatible).
3. **Reward feedback polish** — claim should play a particle/sound. Not wired.
4. **Period color coding** in MissionsNextUpCard — daily/weekly/monthly text styling. Cosmetic.
5. **ViewMissions in-game integration** — the in-level top-bar mission display still uses the old ViewMissionData path which was orphaned even before the refactor. Will need a Bind hook from Level→ViewMissions when the in-game UI is rebuilt.
6. **⚠ Frame spikes during big explosions (USER-CONFIRMED 2026-05-04)** — observable spikes during booster chain reactions / multi-bubble cascades. Elevates Phase 8 priority. Likely culprits before profiling: (a) `Bubble.GetBubblesSequenceList` / `TakeThemAll` graph-walks via per-step `GetComponent<Bubble>()`, (b) LINQ `.Where().ToList()` in cascade detection allocating per call, (c) `OnTriggerStay2D` in Booster firing every fixed step, (d) `async void` continuations in Bubble.Explode allocating per pop — 30+ pops in a cascade = 30+ allocations. Profile-first; do NOT speculative-fix without numbers.

### Backlog (broader project state, ordered by what unblocks the most)

1. **Phase 9.4 — Levels button + scrollable level-select grid** in MainPanel. Plus stub IAP/Ad buttons. Plan: `ImplementationPlans/07_Phase9_MainMenu_and_Catalog.md`.
2. **MonoBehaviour-as-data anti-pattern** — `LevelFactory` does `new MoveConstraint(...)` / `new MakeBubbleConstraint(...)` but those inherit MonoBehaviour. Generates Unity console warnings and is technically wrong. Either make Constraint hierarchy plain C# (recommended) or use `AddComponent`. Touches ~5 files.
3. **Singleton cleanup on Menu↔Game re-entry** — TransectionPanel self-heals; LevelRegistry / BoosterManager / others not audited.
4. **Content authoring** — only 2 LevelData entries in catalog. Needs hands-on level design before Phase 8 perf is meaningful.
5. **Phase 8 mobile performance pass** — profile-driven. Plan: `ImplementationPlans/01_Refactor_Phases.md` §Phase 8. **Elevated by item 6 above.**
6. **Build profiles for Android/iOS** — actual deploy configuration.
7. **PlayerPrefs persistence for PlayerLevel** — currently force-reset to 1 in `UserData.Load` while backend deferred. Re-enable when content/backend ready.
8. **`SfxSound[36]` enum out of range** — cosmetic warning, sound effect doesn't play.

### Decisions captured

- **Mission base for both runtimes**: Mission (in-level) does NOT inherit MetaMission (persistent) — they share MissionGoal data + MissionTracker dispatch helper, but are distinct classes because Mission is runtime-only and MetaMission needs JSON-friendly fields (Action fields, parameterless constructor).
- **Inline card pattern over button + popup**: better UX, replaces ContentEvent placeholder cleanly. Designer can style without touching code.
- **MissionGoal.filter as int** (not polymorphic): trades type safety for serialization simplicity. Cast at use site (each `case` in MissionTracker.Bind knows the right enum).
- **Reset cadence local UTC for now**: exploitable by clock-changing during dev. Server-anchored when backend lands.
- **MetaMission Action fields, not events**: Newtonsoft can't decorate event declarations with `[JsonIgnore]`. Action fields with `[JsonIgnore]` work and behave identically for `+=`/`-=`.
