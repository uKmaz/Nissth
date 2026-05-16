# DragonBlast — Project Review & Direction Doc

**Date:** 2026-05-01
**Author:** Claude (Opus 4.7)
**Status:** Awaiting your direction

This is a first-pass review of the project's C# scripts. I am NOT proposing changes yet — I want to align with you before touching anything beyond small bug fixes. Please mark up this doc, point me at priorities, or tell me to ignore parts.

---

## What This Project Is

**Genre:** Bubble/blast puzzle game ("SpaceBlast"). Match-3 style with bombs, chains, abilities (Meteor/Tornado/UFO), boosters, and 100 hand-coded levels.

**Tech stack:**
- Unity 6 (6000.3.14f1), Universal RP 2D
- **DI:** Zenject (single `GameInstaller : MonoInstaller`)
- **Animation:** Spine 2D (skeletal animation), DOTween, Cinemachine
- **Input:** Unity Input System (`InputSystem_Actions.inputactions`)
- **Auth/IAP:** Apple + Google sign-in, Unity Purchasing, custom HTTP backend
- **Frameworks/tools in repo:** Axiom Diagnostic Bridge (`Assets/Axiom/`), TextMesh Pro, Coffee.UIParticle

**Code volume:** ~272 game scripts under `Assets/Scripts/`. No asmdef on game code → everything compiles into `Assembly-CSharp` (296 files).

---

## High-Level Architecture

```
MyObject (base : MonoBehaviour wrapper)
├── Explodable
│   ├── Bubble, BubbleChain, BubbleShine, Booster
│   ├── TNT, PlasmaBall, Cannon, Cupcake, CupcakeBox, CoinBox
│   ├── Jelly, IceCube, Lander, Kitten, Ore, ToolBox, DyeJar
│   ├── LockColor, LockKey, KeyFromLock
│   └── ChainBreakVFX, MechanicBar, Mile, Cloud
├── Level (abstract)
│   └── Level1, Level2, … Level100   ← 100 concrete files
├── GenerateObject → DyeJarGenerate, KittenGenerate, LanderGenerate, OreGenerate, …
├── Reward → CoinReward, HealthReward, MoveReward, AbilityReward, BoosterReward, StarReward
├── Buy → BuyAbility, BuyBooster, BuyHealth, BuyMove
├── Constraint → MoveConstraint, MakeBubbleConstraint, SecondConstraint
├── Mission → BubbleMission, DyeJarMission, KittenMission, LanderMission, …
├── MyUI → AbilityPanel, BuyAbilityPanel, ComicPanel, MainPanel, … (~20 panels)
└── Manager (concrete) + Managers (interfaces) — see "Smell" below
```

**DI bindings** (`GameInstaller.cs`):
- `IAuthenticationManager` → platform-conditional (Mock / Google / Apple)
- `IUserService` → `UserManager`
- `ILevelService` → `LevelManager`
- `ClientSpaceBlast` → backend HTTP client

---

## Concrete Findings

### 🟥 Hot spots that I think need attention (will not touch without your sign-off)

1. **`GameInstaller.cs:25` — Hardcoded LAN IP backend URL**
   `http://192.168.1.75:5041` is committed in code. The production URL is commented out. This will not work for anyone but the original developer's machine.

2. **100 hand-coded `Level1.cs`–`Level100.cs` files**
   Each file appears to manually configure constraints, missions, and view data via `Initialize()`. From `Level1.cs`:
   - `bubbleRates`, `makeBubbleCount`, `moveCount`, `missionCount` are inspector fields
   - `Initialize()` wires up `MoveConstraint`, `MakeBubbleConstraint`, missions, view data
   - Each level overrides `VariantID` to return `(int)LevelTypes.LevelN`

   Strong candidate for becoming **data-driven** (ScriptableObject per level, or single `Level` MonoBehaviour with a config asset). Would collapse 100 files → 1 + 100 assets.

3. **Two folders: `Manager/` and `Managers/`** — looks like an unfinished refactor.
   - `Manager/`: concrete classes (`GameManager`, `BoosterManager`, `SoundManager`, `AppleAuthManagers`, `MockAuthManager`, `HttpsClientManager`, `CameraMovement`, `CinemachineShakeController`, `GameAnalitic`, `UserDatasManager`)
   - `Managers/`: interfaces + service implementations (`IAuthenticationManager`, `IClient`, `IEventService`, `ILevelService`, `IUserService`, `LevelManager`, `UserManager`, `EventManager`, `WinSeriesManager`, `GameInstaller`)

   Suggests a partial migration toward interface-based services. Worth completing or at minimum documenting which is "current."

4. **Singleton + DI mixed**
   `GameManager.cs:27` — `public static GameManager Instance { get; private set; }` alongside `[Inject] Construct(...)`. Both patterns coexist. This usually causes confusion and ordering bugs.

5. **No assembly definitions on game code**
   Every script change recompiles `Assembly-CSharp` (~300 files). Splitting into asmdefs (e.g. `DragonBlast.Core`, `DragonBlast.Entities`, `DragonBlast.UI`, `DragonBlast.Levels`) would dramatically speed up iteration. Risk: cyclic dependencies will surface and need fixing.

### 🟧 Code-quality smells (smaller, opportunistic)

6. **Base class named `MyObject`** — placeholder name, used by ~80% of MonoBehaviours. Renaming to something like `GameObjectBase` / `BehaviourBase` is straightforward but high-churn.
7. **`BaloonCollected` namespace/folder** — should be `Balloon` (English misspelling baked in throughout: `BaloonCollectedTrail`, `BaloonCollectCanvas`, etc.). Affects ~5 files.
8. **VFX scripts at the root of `Scripts/`** — `BombMergeVFX.cs`, `BombSpawnVFX.cs`, `ChainBreakVFX.cs`, `EntitySpawner.cs`, `HintVFXBubbles.cs` — should live in a subfolder (`Scripts/VFX/`?).
9. **Many classes with `(none)` namespace** — e.g. `GameInstaller`, `WinSeriesManager`, `GameAnalitic`, `MillControl`, `EntitySpawner`, the Level1–100 files… inconsistent namespace discipline.
10. **`GameManager.cs:67`** — Turkish debug log `"Oyun bası para miktari="` shipped in code. Many similar logs likely scattered.
11. **`Level1.cs:38`** — `GameObject.FindGameObjectWithTag("LevelParent")` in `Initialize()`. Done in every Level subclass per the pattern; expensive at runtime.
12. **`GameAnalitic.cs`** — typo for `GameAnalytic`.
13. **`GameManager.cs:74`** — hardcoded event progression list `[100, 200, 300, …]` inline in code; should be a config asset.

### 🟩 Things that look healthy

- Clear interface segregation (`IAbility`, `IConstraint`, `IExplodable`, `IFactory`, `IGenerateObject`, `ILevel`, `IMiniGame`, `IMission`, `IPooling`, `IReward`, `IViewAbility`)
- Object pooling (`PoolingObject`, `IPooling`)
- Mission/Constraint/Reward subsystems are cleanly factored
- View/Data separation (`ViewMissionData`, `ViewMoveData`, `ViewMiniGameData`)
- Axiom Diagnostic Bridge framework is a real asset — saves agent tokens and gives you reproducible reports
- Compilation status: **CLEAN, 0 console errors**
- Editor tooling already exists (`ChainPlacementTool.cs`, `SpaceBlastSceneSetupTool.cs`, `SetupSampleScene.cs`)

---

## Open Questions for You

I'd like your direction on each of these before I start any work beyond small bug fixes.

### A. Scope: which of these do you want me to tackle?

- [ ] **Backend URL config** — move to a ScriptableObject + per-build profile
- [ ] **Asmdef split** — meaningful but risky; will surface circular deps
- [ ] **Manager / Managers consolidation** — finish the interface-based migration
- [ ] **Singleton/DI cleanup** — pick one for `GameManager` etc.
- [ ] **Level1–Level100 → data-driven levels** — biggest refactor, biggest payoff
- [ ] **Naming cleanup** — `MyObject` → ?, `Baloon` → `Balloon`, `GameAnalitic` → `GameAnalytic`
- [ ] **VFX organization** — move root-level VFX scripts under `Scripts/VFX/`
- [ ] **Debug log audit** — strip Turkish/dev logs, leave only intentional analytics

### B. Constraints I should respect

1. Is this project **shipped / live**? If players already use it, refactors must preserve save data and prefab refs.
2. Are the 100 Level scripts **actually different**, or mostly clones with different numbers? (I'd skim 5 before recommending a refactor.)
3. Is there a **target platform** I should keep in mind (Android-first? iOS?)
4. Any **deadlines** I should know about?
5. Anyone else editing this code, so I should avoid certain folders during this session?

### C. How should I report?

You said: *"Create me a .md document for me to understand what is going on and direct you towards my intuitive."* Confirming the convention going forward:

- For **anything bigger than a small bug fix**: I write a new file in `ImplementationPlans/` (numbered), explain the plan + tradeoffs, **wait for your direction**, then execute.
- For **trivial fixes** (typo, one-line): I just do it and mention it in the turn summary.
- After finishing a phase: I update `AgentReports/StatusUpdate.md` with what changed.

Tell me if you want a different cadence.

---

## What I'd Recommend Doing First (if you ask me to pick)

If you want low-risk wins to validate the workflow:

1. **`GameInstaller.cs:25`** — replace the hardcoded LAN IP with a ScriptableObject (`Resources/BackendConfig.asset` or a per-build-profile binding). 30-min fix, immediate benefit.
2. **Sample 5 random Level scripts** to confirm the duplication hypothesis. If they really are clones, plan a data-driven migration as a follow-up doc.
3. **Asmdef for game code** — only after I understand the dependency graph. ProjectCartographer Mode C (DependencyMap) can produce that report.

But this is your call. Tell me where to focus.
