# Phase 4 — DI Migration & Singleton Elimination

> Mirrored from `~/.claude/plans/let-s-plan-phase-4-graceful-donut.md` (plan-mode scratchpad).
> Approved by user. Auto mode active.

---

## Context

`01_Refactor_Phases.md` queued Phase 4 as "DI migration to Cygnus" and Phase 5 as "singleton elimination." Exploration in this session revealed that **Cygnus's container layer is just a manual `SingletonManager`** (no factory bindings, no constructor injection, no scopes) — so the two phases are really the same problem. They're being merged.

Decisions (user-confirmed this session):
- **Scope:** Replace Zenject + eliminate the 30+ static `.Instance` singletons by routing them through Cygnus's `Global.Singletons`.
- **BlastObject vs Cygnus.MyObject:** Deferred to a later phase. BlastObject hierarchy stays as-is.
- **PrefabBuilder:** Skipped. PoolingObject + Factory remain.
- **Bootstrap:** Single `GameBootstrap` MonoBehaviour with `[DefaultExecutionOrder(-1000)]` placed in each gameplay scene.
- **Call-site rewrite:** Every singleton access (~327 sites) becomes `Global.Singletons.Get<X>()`. No proxy `Instance` properties left behind.
- **Migration order:** Leaves first, keystones last.

---

## Pre-flight verification (Step 0)

Before touching anything, confirm whether Zenject's `[Inject] Construct(...)` is actually firing today. No `SceneContext` GameObject exists in any scene — so `[Inject]` may have been silently no-op'ing all along.

- Add a one-line `Debug.Log("[ZENJECT_VERIFY] Construct fired")` to `GameManager.Construct()` at `Assets/Scripts/Managers/Core/GameManager.cs:48` and to `Level.Construct()` at `Assets/Scripts/Levels/Level.cs:169`.
- Play SampleScene, click START, observe console.
- Outcome A: logs fire → Zenject IS running. Migration must preserve the post-Construct injected state.
- Outcome B: logs don't fire → Zenject is already a no-op. Removal is pure deletion; the project has been running on singleton fallbacks all along (which is why `GameManager.Initialize` accesses `UserData.Instance` directly). This is the more likely outcome based on the missing SceneContext.
- Remove the verification logs after.

The result determines how careful Step 9 (Zenject removal) needs to be.

---

## Architecture: GameBootstrap

**File:** `Assets/Scripts/Bootstrap/GameBootstrap.cs` (new folder + new file)

```csharp
namespace Bootstrap
{
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        // Serialized references to scene-resident MonoBehaviour singletons
        // populated as each is migrated. Bootstrap registers them into
        // Global.Singletons before any consumer's Awake runs.

        [SerializeField] private GameManager gameManager;
        [SerializeField] private SoundManager soundManager;
        [SerializeField] private BoosterManager boosterManager;
        [SerializeField] private PoolingObject poolingObject;
        // ... grows as we migrate

        private void Awake()
        {
            // Plain-class services: instantiate + register
            Global.Singletons.Add<UserData>(UserData.Load());
            Global.Singletons.Add<Factory>(new Factory());
            Global.Singletons.Add<GameAnalytic>(new GameAnalytic());
            Global.Singletons.Add<BuyAbility>(new BuyAbility());
            Global.Singletons.Add<BuyHealth>(new BuyHealth());
            Global.Singletons.Add<BuyBooster>(new BuyBooster());
            // ... grows as we migrate

            // MonoBehaviour singletons: register the scene-placed instances
            // (their own Awake won't have fired yet — execution order -1000
            //  guarantees Bootstrap is first)
            Global.Singletons.Add(gameManager);
            Global.Singletons.Add(soundManager);
            Global.Singletons.Add(boosterManager);
            Global.Singletons.Add(poolingObject);
            // ... grows as we migrate

            // Phase 4 also calls explicit init methods that used to be
            // [Inject] Construct(...) — see Step 7 (GameManager).
        }
    }
}
```

Place a `_GameBootstrap` GameObject in `SampleScene` (and any other gameplay scene that needs it). Wire SerializedField refs in the Inspector via a temp `[MenuItem]` editor utility (per CLAUDE.md wiring rules).

---

## Inventory: 30+ singletons to migrate

Grouped by tier (call-site counts approximate, from exploration):

### Plain-class singletons (instantiated by Bootstrap)
- `UserData.Instance` — heavy use, persistent state
- `Factory.Instance` — high use, prefab loader
- `GameAnalytic.Instance` — high use (Level.cs especially)
- `BuyAbility.Instance` — 7 sites
- `BuyHealth.Instance` — moderate
- `BuyBooster.Instance` — 5 sites

### MonoBehaviour singletons (registered by Bootstrap from serialized refs)
**Tier-leaf (lowest coupling):**
- UI canvases: `TransectionPanel.Instance` (30+), `MainPanel.Instance` (14), `MovesOutPanel.Instance` (4), `RestartConsumablePanel.Instance` (6), `BaloonCollectCanvas.Instance`, `BaseCanvas.Instance`, `DialogOkay.Instance` (5), `GameTopCanvasMissions.Instance` (25+), `GameTopCanvasMove.Instance`, `GameBottomCanvas.Instance`, `GameTopCanvas.Instance`, `GameBottomCanvasAbility.Instance`, `MainMenuTopCanvasEvents.Instance`, `InGameTextController.Instance`, `ViewAbilityThree.Instance`
- `EventReward.Instance` (8), `HttpsClientManager.Instance`, `Explodable.Instance` (low)

**Tier-mid:**
- `SoundManager.Instance` — high use
- `CameraMovement.Instance` — high use
- `BoosterManager.Instance` (7 sites)
- `CinemachineShakeController.Instance`
- `PoolingObject.Instance`

**Tier-keystone (last):**
- `GameManager.Instance` — 23 sites, most central
- `Level.ActiveLevel` — 29 sites, **different pattern (not a service registry; tracks current active level)**. Not a typical singleton; defer to Phase 7's level rewrite. Documented as out-of-scope here.

---

## Execution steps

### Step 1 — Create GameBootstrap (foundation)

- Create `Assets/Scripts/Bootstrap/GameBootstrap.cs` with empty Awake (no registrations yet).
- Place `_GameBootstrap` GameObject in `SampleScene`.
- Verify compile clean. Verify SampleScene still plays correctly (regression-check bubble click).

### Step 2 — Migrate plain-class leaves (lowest blast radius)

Order:
1. `BuyAbility` — 7 sites. Rewrite each to `Global.Singletons.Get<BuyAbility>()`. Register in Bootstrap. Delete `static Instance`.
2. `BuyHealth` — same.
3. `BuyBooster` — same.
4. `GameAnalytic` — high use but isolated to event paths.

Per item: edit the singleton class, edit consumers, register in Bootstrap, compile clean, smoke-test.

### Step 3 — Migrate UI canvas leaves

Group by file count, smallest-impact first:
1. `MovesOutPanel`, `RestartConsumablePanel`, `BaloonCollectCanvas`, `BaseCanvas`, `DialogOkay`, `ViewAbilityThree`, `MainMenuTopCanvasEvents`, `GameBottomCanvasAbility`, `InGameTextController`, `GameTopCanvas`, `GameBottomCanvas`, `GameTopCanvasMove`
2. Heavy: `MainPanel` (14), `GameTopCanvasMissions` (25+), `TransectionPanel` (30+)

Each Canvas singleton: scene-resident MonoBehaviour. Replace `Instance` setter (currently in Awake/Initialize) with no-op; Bootstrap registers via SerializedField. Replace `X.Instance` consumer calls with `Global.Singletons.Get<X>()`.

**Risk:** UI canvases are often on inactive GameObjects until shown. If a consumer calls `Get<TransectionPanel>()` before the Canvas activates, the registration may not have happened yet. Solution: register from Bootstrap's Awake using direct SerializedField references — Bootstrap doesn't care if the canvas GameObject is currently active.

### Step 4 — Migrate mid-tier MonoBehaviour services

Order:
1. `EventReward` (8 sites)
2. `HttpsClientManager`, `Explodable.Instance` (low usage)
3. `CinemachineShakeController`, `CameraMovement` (high but isolated)
4. `BoosterManager` (7 sites)
5. `SoundManager` (high)
6. `PoolingObject` + `Factory` (chicken-and-egg pair — migrate together)

### Step 5 — Migrate UserData

Plain class with lazy PlayerPrefs load. ~20+ sites. Bootstrap calls `UserData.Load()` and registers result. Replace `UserData.Instance.X` with `Global.Singletons.Get<UserData>().X`.

**Verify:** save/load lifecycle (`OnApplicationQuit` calls `UserData.SaveAllDataToPrefs()`). Bootstrap doesn't own save — keep that on `GameManager.OnApplicationQuit` for now.

### Step 6 — Resolve GameManager init-order issue

Currently: `BlastObject.Awake → GameManager.Initialize → (later) Zenject.Construct → never`.

After Phase 4:
- Remove `[Inject]` attribute from `GameManager.Construct(...)`. Rename to `Initialize(ClientSpaceBlast, IAuthenticationManager, ILevelService, IUserService)` or similar.
- Bootstrap, after registering services, calls `gameManager.Initialize(client, auth, levelSvc, userSvc)` explicitly.
- Move logic from current `GameManager.Initialize()` (the parameterless override) into the explicit Initialize. Old override becomes empty or deleted.
- This fixes the Awake-before-Construct race that's been masked by singleton fallbacks.

### Step 7 — Migrate GameManager (keystone)

23 sites. Replace each `GameManager.Instance` with `Global.Singletons.Get<GameManager>()`. Delete the static `Instance` property.

### Step 8 — Remove Zenject

Only after Step 0 confirms what we're removing:
- Delete `Assets/Scripts/Managers/Core/GameInstaller.cs` (its bindings now live in GameBootstrap).
- Delete `[Inject]` attributes from `GameManager.Construct` and `Level.Construct`.
- Mass-replace `using Zenject;` → (delete line) across the codebase.
- Remove `Zenject` from `Assets/Scripts/DragonBlast.Runtime.asmdef`'s `references`.
- Verify Zenject DLL location; if a vendored DLL exists, leave it for now and clean in a later cleanup pass (not load-bearing).

### Step 9 — Final verification

- `Axiom_Verify` `compilation` → CLEAN.
- `Axiom_Verify` `errors` → 0.
- Play SampleScene: bubble click → pop → audio. Match Phase 3's verified state.
- Verify no `.Instance` properties remain except `Level.ActiveLevel` (deferred).
- Append section to `AgentReports/StatusUpdate.md`.

---

## Critical files to read / modify

**To read first (before editing):**
- `Assets/Lib/Cygnus/Global.cs` — confirm `Global.Singletons.Add` / `Get` signatures
- `Assets/Lib/Cygnus/DI/SingletonManager.cs` (or similar) — keyed registration semantics
- `Assets/Scripts/Managers/Core/GameInstaller.cs` — current 5 bindings
- `Assets/Scripts/Managers/Core/GameManager.cs:29-79` — Instance + Initialize + Construct
- `Assets/Scripts/Levels/Level.cs:54,169` — ActiveLevel + Construct
- `Assets/Scripts/MainReSource/UserData.cs` — Instance + persistence
- `Assets/Scripts/Entities/Factory.cs` — Instance + prefab loading
- `Assets/Scripts/Entities/PoolingObject.cs:18` — Instance + Factory dependency

**To modify (full list):**
- All ~40 files containing singleton calls (see exploration inventory)
- `Assets/Scripts/DragonBlast.Runtime.asmdef` — drop Zenject reference
- New: `Assets/Scripts/Bootstrap/GameBootstrap.cs`
- New: `_GameBootstrap` GameObject in `SampleScene`
- Wire-up via temp `[MenuItem]` editor script (`Assets/Axiom/Editor/AgentBridge/Temp/`)
- Delete: `Assets/Scripts/Managers/Core/GameInstaller.cs`

---

## Verification checklist (after every step)

1. `Axiom_Verify` `compilation` → `Status: CLEAN`
2. `Axiom_Verify` `errors` → 0
3. Play SampleScene, click START, click a same-color group of bubbles, confirm pop + sound
4. After every 3-4 singletons migrated, take a `SceneDiff Snapshot` for rollback safety

End-to-end test (after Step 9):
- Full SampleScene flow: spawn bubbles, pop groups, hear sound, exhaust moves, see panel
- Restart flow
- No console errors / warnings

---

## Out of scope (explicit deferrals)

- `Level.ActiveLevel` — different pattern (current-active reference, not service registry). Will be replaced by Phase 7's level-creator rewrite. Out of scope here.
- BlastObject vs Cygnus.MyObject inheritance question — deferred to its own future phase.
- Cygnus `IPrefabBuilder<T>` adoption — deferred (depends on inheritance decision).
- Vendored Zenject DLL cleanup — leave for later if it exists.
- Backend/networking bindings (`ClientSpaceBlast` URL) — Bootstrap registers a stub; backend work is deferred indefinitely per project memory.

---

## Risks

- **Bootstrap timing:** `[DefaultExecutionOrder(-1000)]` should beat any other Awake. If a UI canvas or manager has a more negative execution order set, Bootstrap loses the race. Mitigation: audit `Edit → Project Settings → Script Execution Order` before Step 1; Bootstrap must be the lowest.
- **Inactive scene objects:** Some UI canvases live on initially-inactive GameObjects. Their Awake fires later. Bootstrap registering via SerializedField bypasses this — registration happens on Bootstrap's Awake regardless of canvas active state.
- **Plain-class singleton init side effects:** `UserData` does PlayerPrefs reads on first access. Bootstrap calling `UserData.Load()` early may shift when prefs are read; usually harmless, but verify nothing assumes lazy-load.
- **Hidden DI consumers:** Editor scripts in `Assets/Editor/` (auto-references DragonBlast.Runtime). If any reference Zenject types, they'll break on removal. Quick grep before Step 8.
- **Per-scene Bootstrap:** SampleScene gets one for sure. If `MechanicTest.unity` or `GameTest.unity` are also playable, they need their own `_GameBootstrap` instance. Audit scenes before declaring done.

---

## Estimated scope

- Files modified: ~40
- New files: 1 (`GameBootstrap.cs`)
- Deleted files: 1 (`GameInstaller.cs`)
- Lines changed: ~400 (mostly mechanical `.Instance` → `.Get<>()` rewrites)
- Compile checkpoints: 8-10
- Play-test checkpoints: 4-6 (after each major tier)
