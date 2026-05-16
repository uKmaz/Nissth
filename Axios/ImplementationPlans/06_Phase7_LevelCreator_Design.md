# Phase 7 — Level Creator: Design Plan

**Date:** 2026-05-02
**Status:** Design draft, awaiting your decisions on the choice points
**Pre-req:** Phases 1-6 verified (clean DI, asmdefs, no singletons except `Level.ActiveLevel`, Find* removed)

---

## Why this needs design before code

Phase 7 is the largest lift in this refactor. It rebuilds the gameplay scaffolding:
- The empty `GameManager.MakeLevel()` (no-op since Phase 1) needs an implementation
- The deleted `Level1.cs`-`Level100.cs` per-level subclass pattern needs a replacement
- The 12 call sites that read `Level.ActiveLevel` static need a clean lifecycle source
- The corpse `Level-1.prefab` … `Level-100.prefab` need to either be migrated or replaced
- The 4 callers in `TransectionPanel.cs` that silently no-op need to be wired again
- And per the Phase 6 closeout, `SpaceBlastRuntimeSpawner` and `CameraOrtSize` retire here

This doc maps the system, names the design choice points, and gives you my recommendation on each. Nothing ships until you sign off on the choices.

---

## 1. Inventory — what exists today

### Level lifecycle
- `Level : BlastObject` (abstract). Owns:
  - `List<Constraint> Constraints` (instance)
  - `List<Mission> Missions` (instance)
  - `static List<MiniGame> MiniGames`
  - `EndLevelType` (Playing | NextLevel | LoseLevel | LevelCleaning)
  - `static Level ActiveLevel` ← deferred from Phase 6, killed here
- `Initialize()`: assigns `ActiveLevel = this`, sets up Constraints/Missions/MiniGames via `SetLevel(this)` + event hookups, wires camera, clears pools
- `Update()`: tracks game time, hint bubble logic
- `OnDestroy()`: saves analytics, clears pools, invokes `LevelDestroyed`

### Constraint hierarchy (3 concrete classes, `Constraint : BlastObject`)
| Class | Tracks | Constructor |
|---|---|---|
| `MoveConstraint` | Move count remaining | `(int maxMoveCount)` |
| `SecondConstraint` | Time limit | 5 overloads (DateTime, TimeSpan, …) |
| `MakeBubbleConstraint` | Spawn rate / count / types | `(int maxBubbleCount, BubbleRates)` |

### Mission hierarchy (9 concrete classes, plain classes implementing `IMission`)
| Class | Tracks | Constructor |
|---|---|---|
| `BubbleMission` | Pop N of color | `(BubbleTypes, int count)` |
| `DyeJarMission` | Explode N dye jars | props set externally |
| `LockColorMission` | Explode N lock-colors | props set externally |
| `LockKeyMission` | Explode N lock-keys | props set externally |
| `LanderMission` | Explode N landers | `(int count)` |
| `OreMission` | Explode N ore | `(int count)` |
| `TNTMission` | Explode N TNT | `(int count)` |
| `ToolBoxMission` | Explode N toolboxes | `(int count)` |
| `KittenMission` | Explode N kittens | `(int count)` |

### MiniGame hierarchy (1 concrete, plain class)
| Class | Tracks | Constructor |
|---|---|---|
| `BubbleMiniGame` | Pop N of color (bonus tracker) | `(BubbleTypes, int count)` |

(`MiniGameTypes` enum has 9 values — placeholders for future expansion.)

### Generators (6 classes, `GenerateObject : BlastObject`)
DyeJarGenerate, LanderGenerate, TNTGenerate, OreGenerate, ToolBoxGenerate, KittenGenerate. Each is a MonoBehaviour attached to the level prefab; spawns its target type via `Factory.CreateObject<T>()` with random offset. Two methods: `TogetherLevelGenerate(spawn, sameTimeCount)` and `InLevelGenerate(spawn, missionCount, sameTimeCount)`.

### What's in the existing level prefabs
Looking at Level-1, Level-50, Level-100 prefabs:
- Spawn point hierarchy (stages 1, 2, 3, 4)
- Pre-placed mission targets: `LockColor` instances, `Chain` instances, `Wall`, `Lock-Rock` containers, `Jar`, `TNT`, `Rocket` (Ability)
- Visual layout: Sky, BackGrounds, level-specific art
- Generator GameObjects (DyeJarGenerate etc.) when relevant

### What's in the deleted code (we have to infer)
The 100 deleted `LevelN.cs` files were `Level` subclasses. Each one:
- Constructed the right `Constraints` (MoveConstraint with N moves, sometimes SecondConstraint, always MakeBubbleConstraint with that level's BubbleRates)
- Constructed the right `Missions` (mix of types per level)
- Wired Generators

So the per-level *data* lived in code, the per-level *visuals + gameplay objects* lived in the prefab. **The system is already hybrid.** Phase 7 just makes the data side data.

### Level.ActiveLevel call sites (12 files, must migrate)
- 3 trail-coroutine UI scripts (BaloonCollected/*)
- 4 entity scripts (Booster, DyeJar, TNT — checking `EndLevelType`; MoveConstraint caching in ctor)
- 5 UI panel scripts (Dialog, MovesOut, NextLevelInfo, RestartConsumable, Transection)

---

## 2. Design choice points

Eight decisions to make. My recommendation in **bold** for each.

### Choice 1 — Data shape: `LevelData` ScriptableObject + linked prefab

**Recommended: hybrid (SO + prefab pair).**

- `LevelData` SO holds the gameplay config (move count, bubble rates, mission specs, time limit, generator config).
- Prefab holds the visual layout + pre-placed mission targets (LockColor instances, Chains, Wall, etc.) + spawn points.
- SO has `[SerializeField] GameObject levelPrefab` reference.

Pure-data alternative (config + procedurally-built level) is wrong fit — your levels have hand-placed art and mission-target geometry. Trying to express "this LockColor sits at (3.2, 4.5) with Chain attached to anchor X" in pure data would be a rebuild of the Unity scene system.

Open sub-question: does the SO reference the prefab, or does the prefab reference the SO via a `LevelDataHolder` MonoBehaviour on its root?
- **A) SO → prefab.** Loader takes a `LevelData` asset; uses its prefab field; instantiates it. *Cleanest separation, my pick.*
- B) Prefab → SO. Loader takes a prefab path; the prefab's `LevelDataHolder` self-loads. Easier to author "everything for level 5 lives in this prefab" — drag-and-drop. But couples the prefab to the data type.

I prefer A. Your call.

### Choice 2 — Mission count: derive from prefab, or set in data?

When the level prefab has 3 `LockColor` GameObjects pre-placed and the LockColorMission says "destroy 3 of them," that 3 is duplicated.

**Recommended: set in data, validate at edit-time.**

- Mission count is whatever the SO says.
- An editor validator warns if SO mission count mismatches the prefab's pre-placed instance count (gives a button to auto-sync).
- Reason: keeps data authoritative (you can have "destroy 3 of 5 placed" if you want), avoids surprising "mission count secretly changes when I delete a prefab GameObject" behavior.

### Choice 3 — Constraint/Mission instantiation: typed configs vs polymorphic SO

How does the SO express "this level has a 30-move constraint and a 60-second time constraint"?

**Recommended: typed config structs in `LevelData`, factory builds the runtime classes.**

```csharp
[CreateAssetMenu(menuName = "DragonBlast/Level Data")]
public class LevelData : ScriptableObject
{
    public GameObject levelPrefab;
    public BubbleRates bubbleRates;
    public int maxBubbleCount = 60;

    public MoveConstraintConfig moveConstraint;       // serializable struct
    public SecondConstraintConfig timeConstraint;      // optional, nullable

    public List<MissionConfig> missions;               // each is a tagged-union
    public List<MiniGameConfig> miniGames;
}

[Serializable] public struct MoveConstraintConfig { public int maxMoves; }

[Serializable] public struct SecondConstraintConfig { public bool enabled; public int seconds; }

[Serializable] public struct MissionConfig
{
    public MissionTypes type;       // enum already exists
    public BubbleTypes bubbleType;  // for BubbleMission
    public int count;
}
```

Plus a factory:
```csharp
public static class LevelFactory
{
    public static List<Constraint> BuildConstraints(LevelData data) { /* switch on configs */ }
    public static List<Mission> BuildMissions(LevelData data) { /* switch on MissionConfig.type */ }
    // ...
}
```

Polymorphic alternative (each mission type as its own SO subclass referenced in a `List<MissionDataBase>`) is more open-ended but heavier — you need an asset per mission per level, custom inspector to spawn typed assets, etc. The tagged-union struct is enough for your 9 mission types.

### Choice 4 — Generators: list in `LevelData` or attach to prefab?

Currently generators are MonoBehaviour components on level prefabs. They get invoked by the level subclass.

**Recommended: keep generators on the prefab, but list which generators run + their config in `LevelData`.**

- Prefab has the generator components attached (DyeJarGenerate, LanderGenerate, etc.) where they should be.
- `LevelData` has `List<GeneratorConfig>` that says "run DyeJarGenerate with sameTimeCount=3, dyeJarType=Blue."
- Level lifecycle iterates the configs, finds the matching generator on the instantiated prefab, calls `TogetherLevelGenerate` / `InLevelGenerate` with the configured params.

Why not just configure them inline on the prefab? Because the same prefab might support multiple difficulty configs of the same generator (easy = 2 jars, hard = 5).

### Choice 5 — Editor UX

**Recommended: Inspector-driven first; EditorWindow if Inspector cramps.**

- Phase 7.1 deliverable: a custom inspector (`[CustomEditor(typeof(LevelData))]`) that:
  - Shows BubbleRates as sliders summing to 100
  - Shows missions as a clean reorderable list with type dropdown + per-type fields
  - Has a "Validate" button that cross-checks counts against the prefab
  - Has a "Test in Play Mode" button that loads this level into SampleScene
- Defer EditorWindow (visual canvas, drag-place mission targets in scene, etc.) unless inspector authoring proves too painful. EditorWindow is 5x the work.

### Choice 6 — `Level.ActiveLevel` replacement

12 call sites read it. Same pattern as Phase 4 singletons:

**Recommended: `Global.Singletons.Add<Level>(this)` in `Level.Initialize`, `Remove<Level>()` in `OnDestroy`. Consumers do `Global.Singletons.Get<Level>()`. **

Risk: only one Level can be alive at a time. That's the current behavior anyway (`ActiveLevel` is a single static). Multi-level scenes would need a different pattern, but you don't have that.

### Choice 7 — Scene model: one shared scene, or per-level scenes?

**Recommended: one shared scene (SampleScene rebranded "Game"), level prefab instantiated under SceneRefs.LevelParent.**

- Eliminates the per-level scene asset pile (which the deleted Level-N.prefab corpses already mirror).
- Level loading = `Instantiate(levelData.levelPrefab, sceneRefs.LevelParent)`.
- Restart = destroy current level instance, instantiate fresh.
- SceneRefs gets a new field: `LevelParent` (Transform). Edit-time wired.

This also gives `CameraOrtSize` its replacement — the level prefab can publish its own camera bounds via a small `LevelBounds` component on its root, read by the camera setup at instantiation time. Then `CameraOrtSize` deletes.

### Choice 8 — Coverage: which test levels does the creator need to produce?

You picked 10-20 in the original plan. Given the inventory, I'd cover:

**Mission-type coverage (9 levels — one per mission type, simplest config):**
1. BubbleMission: pop 30 blue
2. DyeJarMission: 3 jars
3. LockColorMission: 3 lock-colors
4. LockKeyMission: 3 lock-keys
5. LanderMission: 2 landers
6. OreMission: 5 ore
7. TNTMission: 3 TNT
8. ToolBoxMission: 3 toolboxes
9. KittenMission: 3 kittens

**Constraint variation (3 levels):**
10. Tight-move level (BubbleMission pop 20, only 10 moves)
11. Time-limited level (60s, BubbleMission pop 30 mixed colors)
12. Multi-stage level (30 bubbles, 3 stages with camera scrolling)

**Mixed-mission (3 levels):**
13. 2 mission types (10 blue + 5 ore)
14. 3 mission types (lock-color + lock-key + 10 bubbles)
15. Mission + MiniGame (BubbleMission + BubbleMiniGame bonus)

**Edge cases (2 levels):**
16. Single-color spawn (only blue bubbles, 30 to pop)
17. All-6-colors-equal-rate (max color variety)

That's 17 levels. Drop / merge as you prefer. **Open question for you: which subset?**

---

## 3. Sub-phases of Phase 7

Once you've answered the choice points, execution breaks down into ~7 sub-phases, each independently verifiable:

### 7.1 — Foundations (no creator yet)
- Replace `Level.ActiveLevel` with Singletons pattern across the 12 call sites
- Add `LevelParent` Transform to `SceneRefs`, wire in SampleScene
- Compile + smoke (existing prefab still loads via the spawner)

### 7.2 — `LevelData` SO + factory
- `Assets/Scripts/Levels/LevelData.cs` (the SO)
- Config structs: `MissionConfig`, `MiniGameConfig`, `GeneratorConfig`, etc.
- `LevelFactory` static class to build runtime instances from configs
- Unit-style smoke: feed a hand-crafted `LevelData` into the factory, assert it produces the expected runtime objects

### 7.3 — Level lifecycle from data
- New non-abstract `Level` (or `LevelRunner`) that takes `LevelData` instead of being abstract subclassed
- `MakeLevel(LevelData)` rebuilt in `GameManager` (replaces the empty stub from Phase 1)
- Hooks into the prefab's pre-placed targets + generators

### 7.4 — First test level via creator
- Build one `LevelData` SO + minimal prefab by hand
- Load via `MakeLevel`, play through, verify
- Iterate until parity with what the deleted `Level1.cs` would have done

### 7.5 — Custom inspector for `LevelData`
- `[CustomEditor]` with usable authoring UX
- "Validate" + "Test in Play Mode" buttons

### 7.6 — Build the test level library
- Author the chosen ~10-17 test levels via the creator
- Each one is a regression artifact for future changes

### 7.7 — Retirement pass
- Delete `SpaceBlastRuntimeSpawner.cs` (Phase 6 closeout)
- Delete `CameraOrtSize.cs` (camera bounds now from prefab)
- Delete `Assets/Resources/Levels/Level-*.prefab` corpses (1-100)
- Delete `MakeLevel()` callers in `TransectionPanel.cs` if obsolete
- Final compile + full play-through smoke

---

## 4. Out of scope (for Phase 7)

- **Level progression / unlocks** — current `UserData.PlayerLevel` increments; Phase 7 just needs to load a level by index. Progression UX is its own thing.
- **Difficulty curves / level tuning** — that's gameplay design, not engineering. The creator gives you the knobs; balancing is your job.
- **Level art creation** — visual prefabs still need an artist (you). The creator is for gameplay config.
- **Backend** — still deferred indefinitely.
- **Mobile perf** — Phase 8.

---

## 5. Open questions for you (signal-vs-noise short list)

Answer these and I can start 7.1:

1. **Choice 1 sub-question:** SO → prefab reference (my recommendation A) or prefab → SO via holder MonoBehaviour (B)?
2. **Choice 4:** keep generator MonoBehaviours on prefabs, list in `LevelData` (my recommendation), or pure-data inline configs?
3. **Choice 8:** the 17-level coverage list — keep all, or pick a subset (e.g., 9 mission types + 3 constraint variations + 1 mixed = 13)?
4. **Restart UX:** when a player restarts a level, should the prefab fully tear down + re-instantiate (clean slate, my default), or should we preserve some state?
5. **GameTest scene** (the only thing using `CameraOrtSize`): keep, retire, or convert to a creator-built level?
6. **Anything I'm missing?** This plan is from one round of recon. If you have requirements that aren't visible from the code (target level count for ship, design intentions, gameplay mechanics planned but not yet built), now's the time to surface them.

---

## What I'd like to do right now

Get your answers to the 6 open questions, then start with sub-phase 7.1 (Singletons migration of `Level.ActiveLevel` + `SceneRefs.LevelParent` wiring). That's the lowest-risk, highest-clarity step and unblocks everything else.

---

## Decisions confirmed (2026-05-02)

User signed off all 8 picks. Plus three new constraints to design around:

1. **~5000-level catalog** via a future generator. `LevelData` shape **must be parametric and generator-friendly**. No per-level special cases. Uniform data over polymorphic SO subclasses. On-demand load, not all-resident.
2. **GameTest scene stays** — UI prototype to steal from later. Don't delete it. `CameraOrtSize` is only attached there; treat as orphaned dev scaffolding, not a refactor target. (Removes one entry from the original retirement list.)
3. **Genre confirmed**: standard blast/match-3 with events + missions on top.

These shift sub-phase 7.7 (Retirement pass): `CameraOrtSize.cs` no longer deleted, GameTest.unity preserved.

### Sub-phase 7.1 scope (executing now)

Tightened scope: just the `Level.ActiveLevel` migration. `SceneRefs.LevelParent` deferred to 7.3 when there's something to instantiate into it (no point wiring an unused field).

- Replace `static Level ActiveLevel { get; private set; }` write site with `Global.Singletons.Add(this)` in `Initialize`, `Remove<Level>()` in `OnDestroy`
- Migrate 12 read call sites (some null-check, some don't — preserve existing semantics per site)
- `Get<T>()` throws if unregistered, so null-checking sites use `IsRegistered<Level>() ? Get<Level>() : null` guard
- Compile + smoke test

Won't change runtime behavior — Level isn't currently instantiated in SampleScene anyway (SpaceBlastRuntimeSpawner provides a parallel game loop). Migration is mechanical.
