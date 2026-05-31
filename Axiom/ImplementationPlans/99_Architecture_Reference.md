# DragonBlast — Architecture Reference

> Written 2026-05-02 after Phase 7.4 verification. Reflects the post-refactor architecture (Phases 1–7.4 landed). Reference doc, not roadmap — keep this updated as the codebase evolves; do not let it rot.

---

## Table of contents

1. [Game flow walkthrough](#1-game-flow-walkthrough)
2. [Class responsibilities](#2-class-responsibilities)
3. [Design patterns used](#3-design-patterns-used)
4. [Key invariants and "rules of the road"](#4-key-invariants-and-rules-of-the-road)
5. [Asmdef boundaries](#5-asmdef-boundaries)
6. [What's intentionally NOT here](#6-whats-intentionally-not-here)
7. [Known limitations and sharp edges](#7-known-limitations-and-sharp-edges)
8. [Glossary](#8-glossary)

---

## 1. Game flow walkthrough

This is the lifecycle of a single play session — what happens between hitting "Play" and clicking the first bubble.

### 1a. Boot (the -1000 priority bucket)

The game uses a **two-scene model**: `Menu.unity` (default-loaded, build index 0) and `GamePlay.unity` (build index 1). Unity starts at Menu. Two scripts have `[DefaultExecutionOrder(-1000)]` and Awake before everyone else:

1. **`GameBootstrap.Awake`** runs first (component order on the `_GameBootstrap` GameObject puts it before SceneRefs). Creates and registers the **plain-class services** in `Global.Singletons`:
   ```
   UserData (loaded from PlayerPrefs)
   Factory (the prefab-pool factory)
   GameAnalytic (event/level metrics)
   BuyAbility / BuyHealth / BuyBooster / BuyMove (purchase logic)
   LevelRegistry (active-entity tracker)
   LevelCatalog (loaded from Resources/LevelCatalog.asset; null-guarded)
   ```
   These are plain C# classes (or ScriptableObjects in the catalog's case) — no MonoBehaviour, no GameObject. They live in the registry until the scene unloads.

   GameBootstrap also calls `DontDestroyOnLoad(gameObject)` so it survives the Menu→GamePlay scene transition. Both scenes have a `_GameBootstrap` GO; the second copy detects the first via `IsRegistered<UserData>()` and self-destroys (idempotent).

2. **`SceneRefs.Awake`** (GamePlay scene only) runs second. Just one line: `Global.Singletons.Add(this)`. Now `Get<SceneRefs>()` returns the component on `_GameBootstrap` (in GamePlay), which holds the inspector-wired Transform refs to `BubbleParent`, `BoosterParent`, `Spawn`, `LevelParent`. Menu scene doesn't have SceneRefs (no game-scene transforms to reference).

After this bucket finishes, the registry is populated with everything scene-specific scripts may need to read.

### 1b. The default-priority bucket (everyone else's Awake)

Every other MonoBehaviour in the scene fires Awake in some order Unity doesn't guarantee. The pattern they follow: **self-register on Awake/Initialize**:

- `GameManager.Initialize` → `Singletons.Add<GameManager>(this)`, loads event progression from a Resources SO, hooks input timers
- `SoundManager.Initialize` → `Singletons.Add<SoundManager>(this)`, seeds PlayerPrefs defaults
- `CameraMovement.Initialize` → `Singletons.Add<CameraMovement>(this)`, subscribes to level/booster events
- `BoosterManager.Awake` → `Singletons.Add<BoosterManager>(this)`, reads `SceneRefs.BoosterParent` (this is why SceneRefs needs the -1000 priority)
- `PoolingObject.Awake` → `Singletons.Add<PoolingObject>(this)`
- `EventReward.Initialize` → `Singletons.Add<EventReward>(this)`
- UI canvases (BaloonCollectCanvas, BaseCanvas, GameTopCanvasMissions, GameTopCanvasMove, TransectionPanel, InGameTextController) — same pattern, conditional on being in the scene

Order between these is non-deterministic, but they don't depend on each other in Awake — each one only registers itself. Cross-references happen later (in events, in lifecycle methods called from gameplay).

### 1c. Menu scene: menu shown

`Start` fires after every Awake/OnEnable in the scene completes. **Menu scene** contains a `MenuCanvas` with the standalone `MainPanel.prefab` and `SettingsPanel.prefab` (inactive). MainPanel is visible by default, displaying user resources and the Play / Settings buttons.

Plus persistent (DontDestroyOnLoad'd) GOs that survive into GamePlay: GameBootstrap, SoundManager.

Player taps **Play** → `MainPanel.OnBtnPlayGame` → `SceneManager.LoadScene("GamePlay")`.

### 1d. GamePlay scene loads

Menu scene unloads (everything except DontDestroyOnLoad'd Bootstrap + SoundManager destroys). GamePlay scene loads. Its own `_GameBootstrap` self-destroys (already-registered guard). Game-specific managers (GameManager, BoosterManager, PoolingObject, CameraMovement, EventReward, SceneRefs, all UI canvases inside GamePanel) Awake and self-register.

`_GamePlayAutoLoader.Start` then calls `Global.Singletons.Get<GameManager>().MakeLevel()` (no-arg, resolves via `LevelCatalog.GetByPlayerLevel(UserData.PlayerLevel)`).

### 1d. MakeLevel: turning data into a playable level

`GameManager.MakeLevel(LevelData data)`:

1. Null-guards data and `data.levelPrefab`
2. `Instantiate(data.levelPrefab, sceneRefs.LevelParent)` — visual prefab spawned under the scene's level container. Awake fires for everything inside the visual prefab.
3. `levelInstance.SetActive(false)` — pauses the GameObject so we can attach + configure Level before its Awake fires. Visual children OnDisable here.
4. `AddComponent<Level>` (or reuse if the prefab already has one)
5. `level.SetData(data)` — stores the SO reference in the Level's `_data` field
6. `levelInstance.SetActive(true)` — Level.Awake → Initialize fires now. Visual children OnEnable.

The `false → AddComponent → SetData → true` dance is the cleanest way to inject data into a fresh MonoBehaviour before its Awake fires. Costs one extra OnDisable/OnEnable cycle on the visual children — acceptable.

### 1e. Level.Initialize: wiring the runtime objects

```
Singletons.Add(this)                         // Level becomes the active level
read SceneRefs (BubbleParent, BoosterParent) // for entity parenting
DestroyAllExplodablesOnStart()                // clears stale bubbles from prior play
read UserData, GameManager                    // cached refs
GameManager.PanelActive = false              // unblock clicks
GameManager.UsingSkill = false

if (_data != null):
  _levelTypes = _data.legacyLevelType         // for analytics
  Constraints.AddRange(LevelFactory.BuildConstraints(_data))
  Missions.AddRange(LevelFactory.BuildMissions(_data))

foreach Constraint:
  constraint.SetLevel(this)                  // wires constraint to MoveCompleted event
  constraint.ConstraintFailed += OnConstraintFailed

foreach Mission:
  mission.SetLevel(this)
  mission.MissionCompleted += OnMissionCompleted
```

`LevelFactory.BuildConstraints` returns `[MoveConstraint(maxMoves), MakeBubbleConstraint(maxBubbleCount, bubbleRates), (SecondConstraint?)]`. `BuildMissions` returns whatever the SO's mission list specifies.

`MakeBubbleConstraint`'s constructor is non-trivial — it reads `SceneRefs.Spawn`, fills the bubble pool via `PoolingObject.FillPool`, and kicks off `MakeBubble(maxBubbleCount)` async. That's where bubbles actually start spawning into the scene.

### 1f. Level.Start: the deferred work

Some lookups can't run in Initialize because they depend on the visual prefab's children having OnEnabled (which happens AFTER the level GO's Awake). So they live in `Start`:

- `_lockColorsInLevel = LevelRegistry.Active<LockColor>().ToList()` — finds pre-placed LockColors in the visual prefab
- `LevelFactory.RunGenerators(_data, gameObject)` — finds Generator components on the visual prefab and invokes their `TogetherLevelGenerate` methods with the per-config params

By the time `Start` fires, all child entities have OnEnabled and registered themselves into `LevelRegistry`.

### 1g. Gameplay loop

Now bubbles are spawning and the player can click. Each click:

1. `Bubble.OnMouseDown` → guards (`PanelActive`, `UsingSkill`, `canClick`), then asks `GameManager.ClickReset(0.2f)` to start a cooldown, then if 4+ same-color bubbles are connected (via `NumberOfBubblesInList`), kicks off `ExplodeEveryoneInSequence`.
2. `ExplodeEveryoneInSequence` fires `Bubble.DestroySequenceBubbleEvent(count, transform)` and calls `bubble.Explode()` on each bubble in the sequence.
3. `BoosterManager.OnBubbleSequenceDestroyed` listens to that event. If `count >= 4`, it spawns a Booster (via `Factory.CreateObject<Booster>`) at the click position.
4. Each `Bubble.Explode()` returns the bubble to the pool (`gameObject.SetActive(false)`), which fires OnDisable → `Explodable.OnDisable` → `LevelRegistry.Unregister(this)`. The bubble is now invisible and out of the active set.
5. `MakeBubbleConstraint` listens to `Bubble.BubbleDestroyedEvent` and queues a replacement bubble via `AddBubbleOnRemakeList`. The replacement fires from the pool back into the scene → OnEnable → re-registers in LevelRegistry.

At the same time, **Constraints and Missions** are listening to `Level.MoveCompleted` (Constraints) and the `GameEvents` static bus (Missions). Each move:

- `MoveConstraint.MoveDecrease` decrements the counter; if 0, fires `ConstraintFailed`.
- Each `Mission` instance is bound to one `GameEvent` matching its `MissionGoal.goalType` (`BubblePopped`, `BoosterUsed`, `BoosterCreated`, `ObstacleDestroyed`, `CoinGained`, `AbilityUsed`, `LevelCompleted`, `WinSeriesUpdated`). Producers raise these directly: `Bubble.Explode → RaiseBubblePopped`, `Booster.Initialize/Explode → RaiseBoosterCreated/Used`, etc. Mission increments its progress, fires `CompletedEvent` when threshold reached. See §4g for the full architecture.

### 1h. End-of-level (out of moves, all missions complete, etc.)

`Level.OnMissionCompleted` (subscribed to each Mission's `MissionCompleted` event) increments a `_completedMissionCount`. When the count hits `Missions.Count` and `EndLevelType == Playing`, it fires `IsLevelDone(true)` → win path.

`Level.OnConstraintFailed` (subscribed to each Constraint's `ConstraintFailed`) fires `IsLevelDone(false)` on the first failure → lose path. The `EndLevelType == Playing` guard on both handlers prevents double-fire when a constraint failure and final mission completion land on the same move.

`Level.IsLevelDone(bool isNextLevel)` then orchestrates the visual sequence. Multiple guards keep this idempotent and crash-safe under teardown:
- **Grace period**: `Time.time - _spawnedAtTime < 1.5s` → bail. Catches stale callbacks from a prior destroyed Level firing on the freshly-spawned new one.
- **`_isDestroyed` checked after every `await`** in IsLevelDone, EndLevelBoostersGetInQueue, ClearAllExplodables, PlayEndLevelAnimation. Pending Tasks/coroutines on a destroyed Level bail instead of touching `this`.
- **`_endLevelChainStarted` re-entry guard** on `EndLevelProcesses` — chain runs at most once per Level instance.

The chain itself:
- `OnMissionCompleted` → `levelDone = true` IMMEDIATELY (stops `MakeBubbleConstraint.ReMakeBubble` spawn pipeline) → `IsLevelDone(true)`
- Sets `EndLevelType` to `NextLevel` or `LoseLevel`. Win path also bumps `BlueBubbleMiniGameCount`.
- Waits 2s → `WaitForAllBoostersInList(PlayEndLevelAnimation)` (3s timeout fallback so a stuck booster doesn't hang the EOL)
- `PlayEndLevelAnimation` for the win path → `TransectionPanel.StartLevelCompleteAnimation(EndLevelProcesses)` (singleton lookup with fallback warning if unregistered)
- `EndLevelProcesses` → `levelDone = true` (idempotent) → `EndLevelBoostersGetInQueue` (drains active boosters from `SceneRefs.BoosterParent`; these live OUTSIDE the Level GameObject so MUST be popped explicitly or they carry over to the next level)
- After boosters resolve via WaitForAllBoostersInList → `ClearAllExplodables` runs **single-pass sequential cascade with 25ms per-bubble delay**. Snapshot taken once; no re-query (multi-pass caused infinite loops when boosters cascaded back into the registry).
- `CanCheckMoveOut` decides which panel to open: NextLevelInfoPanel (win, fires `NextLevelEventInvoke()` which calls `UserDataManager.LevelUp()` + raises `NextLevelEvent`), MovesOutPanel (first lose), or RestartConsumablePanel (subsequent lose).
- `NextLevelEventInvoke` has a **direct singleton fallback**: if the static `NextLevelEvent` has 0 listeners, it calls `Global.Singletons.Get<TransectionPanel>().DialogPanelNextLevel()` directly. This belt-and-suspenders catches scenes where TransectionPanel didn't subscribe (e.g. inactive at scene load).
- Player taps Play on NextLevelInfoPanel → `OnButtonPlay` (0.15s scale tween) → `CallTransection` → `TransectionPanel.CloseTransectionOnGame` → **directly invokes `CloseTransectionOnGameEnd`** (bypasses the broken animation-event hook that caused indefinite stalls; animator still drives the visual but is no longer load-bearing).
- `CloseTransectionOnGameEnd` captures `isNextLevelTriggered` BEFORE destroying Level (else the deferred read hits a dead singleton), then `Level.DestroySelf()` → 300ms gap → `GameManager.MakeLevel()` (no-arg) → resolves next level via `LevelCatalog.GetByPlayerLevel(UserData.PlayerLevel)`. No MainPanel cross-scene refs (two-scene mode).

### 1i. Level teardown

When the next level loads (or scene unloads):
- `Level.OnDestroy` fires: removes self from Singletons, calls `Cleanup()` on every Mission (so they unsubscribe from static events like `Booster.BoosterCreated`), saves analytics, destroys any leftover bubble pool entries
- The new MakeLevel instantiates a new prefab and the cycle restarts

---

## 2. Class responsibilities

One paragraph each for the load-bearing types. These are the names that come up most often when reading the codebase.

### Bootstrap layer

- **`GameBootstrap`** (`Assets/Scripts/Bootstrap/`). MonoBehaviour with `[DefaultExecutionOrder(-1000)]`, lives on the `_GameBootstrap` scene GO. Single Awake method that constructs and registers all the plain-class services (UserData, Factory, GameAnalytic, Buy*, LevelRegistry) in `Global.Singletons`. Doesn't hold state itself — it's a wire-up script.

- **`SceneRefs`** (`Assets/Scripts/Bootstrap/`). MonoBehaviour with `[DefaultExecutionOrder(-1000)]`, also on `_GameBootstrap`. Holds inspector-wired Transform references to scene-resident parent objects: `BubbleParent`, `BoosterParent`, `Spawn`, `LevelParent`. Self-registers in Awake. Other code does `Global.Singletons.Get<SceneRefs>().BubbleParent` instead of `FindGameObjectWithTag("BubbleParent")`.

- **`LevelRegistry`** (`Assets/Scripts/Bootstrap/`). Plain C# class. Tracks currently-active `MonoBehaviour` entities via a `HashSet`. Exposes `Register(mb)`, `Unregister(mb)`, `Active<T>()` (LINQ OfType filter). Entities (Explodable subclasses + others) self-register in OnEnable, unregister in OnDisable. Replaces the old `FindObjectsOfType<T>` mid-game queries.

- **`GamePlayAutoLoader`** (`Assets/Scripts/Bootstrap/`). Drops on a GamePlay-scene GO. On Start, calls `Global.Singletons.Get<GameManager>().MakeLevel()` (no-arg) which resolves via `LevelCatalog.GetByPlayerLevel(UserData.PlayerLevel)`. By the time GamePlay loads, the player has already chosen "Play" in Menu.

### Level layer

- **`Level`** (`Assets/Scripts/Levels/Level.cs`). The runtime level controller. Used to be `abstract` with per-level subclasses (Level1.cs–Level100.cs, all deleted in Phase 1). Now concrete and data-driven — takes a `LevelData` reference via `SetData(data)` before its first Awake, then `Initialize` uses `LevelFactory` to populate the `Constraints` and `Missions` lists from the data. `OnMissionCompleted` aggregates mission completions and fires `IsLevelDone(true)` when all done; `OnConstraintFailed` fires `IsLevelDone(false)` on first constraint failure. Exposes `EndLevelType`, `RemainMoveCount`, `GainedCoin`, `LevelName`, `ActivateTutorial()`, `DestroySelf()`. Singletons-registered as `Level` (one active at a time).

- **`LevelData`** (`Assets/Scripts/Levels/LevelData.cs`). ScriptableObject. The data side of the hybrid SO+prefab pair. Fields: `levelPrefab` (visual prefab ref), `levelId` (parametric int ID for analytics/persistence), `legacyLevelType` (LevelTypes enum kept for analytics compat), `bubbleRates`, `maxBubbleCount`, `moveConstraint`, `timeConstraint`, `missions: List<MissionConfig>`, `generators: List<GeneratorConfig>`. Tagged-union config structs — every struct has all possible fields, the factory reads only the ones relevant to its `type`.

- **`LevelCatalog`** (`Assets/Scripts/Levels/LevelCatalog.cs`). ScriptableObject. Ordered list of `LevelData` references that make up the playable catalog. Asset lives at `Resources/LevelCatalog.asset` so Bootstrap can `Resources.Load` it once. Three lookup APIs: `GetByPlayerLevel(int)` (1-based, pass `UserData.PlayerLevel` directly), `GetByIndex(int)` (0-based, for level-select grids), `GetByLevelId(int)` (linear scan, for matching `LevelData.levelId`). Future: when the catalog grows past a few hundred entries, swap the linear scan for a Dictionary cache.

- **`LevelFactory`** (`Assets/Scripts/Levels/LevelFactory.cs`). Static class. Three public methods:
  - `BuildConstraints(LevelData) → List<Constraint>` — always MoveConstraint + MakeBubbleConstraint, optionally SecondConstraint
  - `BuildMissions(LevelData) → List<Mission>` — switch on `LevelMissionType` (PopBubbles, PopBoosters, CreateBoosters, ClearObstacles), each routes to a concrete Mission class
  - `RunGenerators(LevelData, GameObject) → void` — resolves generator components on the instantiated level prefab via `GetComponentInChildren<T>()` and invokes them with config params

- **`Constraint`** (`Assets/Scripts/Constraint/Constraint.cs`). Abstract base. Owns a `Level` reference, a `canPass` flag, a `ConstraintFailed` event. `SetLevel(level)` registers the constraint for `MoveCompleted`/`MoveDecreaseEvent` etc. Subclasses override `OnMove(List<IExplodable>)`. Concrete: `MoveConstraint` (move counter), `SecondConstraint` (time limit, polling), `MakeBubbleConstraint` (bubble spawn rate + count).

- **`Mission`** (`Assets/Scripts/Missions/Mission.cs`). Abstract base. Owns a `Level` reference, a `MissionCompleted` event, a `Cleanup()` virtual (called from `Level.OnDestroy` to unsubscribe from static events). Subclasses override `OnMove(List<IExplodable>)` for move-cycle tracking, or subscribe to static events directly (e.g. `Booster.BoosterCreated`) for spawn/destruction tracking. 11 concrete classes — see the mission list in the design plan.

- **`MiniGame`** (`Assets/Scripts/Interfaces/Events_and_MiniGames/MiniGame.cs`). Abstract base, similar shape to Mission. Only one concrete: `BubbleMiniGame` (bonus tracker for popping bubbles of a specific color). Held in a `static List<MiniGame>` on Level and populated by `GameManager.BlueBubbleMiniGame()` (currently hard-coded). Largely vestigial — overlaps with PopBubbles missions; deprecation candidate.

- **`GenerateObject`** (`Assets/Scripts/GenerateObjectInLevel/GenerateObject.cs`). Abstract base for in-level entity spawners (DyeJarGenerate, LanderGenerate, etc.). MonoBehaviour. Lives as a component on the level prefab. Two methods: `TogetherLevelGenerate(spawn, count)` (batch spawn) and `InLevelGenerate(spawn, missionCount, count)` (single spawn). DyeJar's `TogetherLevelGenerate` has a third `DyeJarTypes` param — special-cased in `LevelFactory.RunGenerators`.

### Entity layer

- **`BlastObject`** (`Assets/Scripts/Entities/BlastObject.cs`). Base MonoBehaviour for game entities. Calls `Initialize` then `Registration` from Awake; calls `Destroyed?.Invoke` + `UnRegistration` from OnDestroy. Exposes virtual `OnEnable`/`OnDisable`/`Activate`/`DeActivate` and `TakeDamageEvent`. Most entities extend this directly or via `Explodable`.

- **`Explodable : BlastObject`** (`Assets/Scripts/Entities/Explodable.cs`). Abstract. Adds an `Explode(bool isSilent)` abstract method, plus static events `ExplodablePrototype`/`ExplodedEntitiesEvent`/`MoveDecreaseEvent`. Critically: overrides `OnEnable`/`OnDisable` to register/unregister itself with `LevelRegistry` (guarded with `IsRegistered<LevelRegistry>()` for teardown safety). All Explodable subclasses inherit this self-registration.

- **`Bubble : Explodable`** (`Assets/Scripts/Entities/Objects/Bubble.cs`). The core gameplay entity. Tracks neighbor bubbles via `OnTriggerEnter2D`/`OnTriggerExit2D`. Click handling in `OnMouseDown`. Cascade detection in `GetBubblesSequenceList` and `TakeThemAll`. Pooled via PoolingObject — Awake fires once, OnEnable/OnDisable fire each pool borrow/return.

- **`Booster : Explodable`** (`Assets/Scripts/Entities/Objects/Booster.cs`). Power-up entities created by 4+ pops or merge. Spine-animated. `OnTriggerStay2D` handles merge detection (expensive — flagged for Phase 8). Static events: `BoosterMakeEvent` (fired by merge), `BoosterSequenceListEvent` (queued for explosion), `BoosterExploded` (after explosion), `BoosterCreated` (when a Booster's Initialize fires — covers both 4+-pop spawns and merges, used by `CreateBoostersMission`), `ShakeBooster`.

- **`PoolingObject`** (`Assets/Scripts/Entities/PoolingObject.cs`). Pool manager for Bubble/Booster instances. `FillPool<T>(variant, count)` pre-instantiates inactive copies; `GetObjectFromPool<T>(variant, position)` borrows one and SetActive(true)s. Pool returns happen via `gameObject.SetActive(false)` at the entity's end-of-life. Singletons-registered.

- **`Factory`** (`Assets/Scripts/Entities/Factory.cs`). Plain class. `CreateObject<T>(variantId, parent)` instantiates a non-pooled entity by looking up the prefab in a list and Instantiate'ing it. Used for one-shot entities (TNT, Lander, VFX, UI panels, etc.). Singletons-registered.

### Manager layer

- **`GameManager`** (`Assets/Scripts/Managers/Core/GameManager.cs`). Top-level game state. Self-registers, holds `canClick` (gated by a click cooldown), tracks `PanelActive`/`UsingSkill` (static), exposes `MakeLevel(LevelData)` for data-driven loading and `MakeLevel()` (no-arg) which now resolves the level via `LevelCatalog.GetByPlayerLevel(UserData.PlayerLevel)`. The 4 TransectionPanel callers are now wired through this. Both methods null-guard. Reads UserData via Singletons.

- **`UserData`** (`Assets/Scripts/MainReSource/UserData.cs`). Plain class. PlayerPrefs-backed persistent state: Coin, Health, Trophy, PlayerLevel, EventList, BoosterCounts, AbilityTornadoCount, etc. `static UserData.Load()` reads `PlayerPrefs.GetString("Data")` (JSON blob), deserializes via Newtonsoft, and returns the instance. **Currently force-resets `PlayerLevel = 1` on every Load** while backend is deferred — prior persistence inflated PlayerLevel via rapid testing cycles. `static UserData.SaveAllDataToPrefs()` (Application.quitting) serializes the live instance back. Bootstrap calls `Singletons.Add(UserData.Load())`.

- **`UserDataManager`** (`Assets/Scripts/Managers/User/UserDataManager.cs`). Static helper for UserData operations: EarnHealth, LoseHealth, LevelUp, LoseCoin, etc. Operates on `Singletons.Get<UserData>()`.

- **`SoundManager`** (`Assets/Scripts/Managers/Audio/SoundManager.cs`). Self-registered MonoBehaviour. Two AudioSources (main, side, button), one AudioClip table indexed by `SfxSound` enum. `MainSoundAction(TrackStatus)` for music tracks, `SfxSoundAction(SfxSound)` for one-shots, `PlayButtonClick()` for the click sound (called by `ClickSoundButton` components on every Button).

- **`BoosterManager`** (`Assets/Scripts/Managers/Core/BoosterManager.cs`). Self-registered. Listens to `Bubble.DestroySequenceBubbleEvent` to spawn boosters on 4+ pops. Listens to `Booster.BoosterMakeEvent` to handle merge-spawned boosters. Manages a queue for sequential booster explosions (so chain reactions don't all fire at once).

- **`CameraMovement`** (`Assets/Scripts/Managers/Camera/CameraMovement.cs`). Self-registered. Camera follow + shake. Subscribes to `Level.CameraFixEvent`, `Level.ChangeSpawnPositionEvent`, `Booster.ShakeBooster`. Listens to `OnTriggerEnter2D` for stage advancement.

- **`GameAnalytic`** (`Assets/Scripts/Managers/Analytics/GameAnalytic.cs`). Plain class. Tracks per-level metrics (explosions, ability uses, end state). Bootstrap-registered.

- **UI canvases** — `BaloonCollectCanvas`, `BaseCanvas`, `GameTopCanvasMissions`, `GameTopCanvasMove`, `InGameTextController` are MonoBehaviours that self-register if present in scene. They host UI prefabs. Level / panels query them via `Singletons.Get<X>().transform` to parent UI elements correctly.

- **`TransectionPanel`** (`Assets/Scripts/UI/Panels/TransectionPanel.cs`). Despite the UI name, this is now the **end-of-level orchestrator** — the visuals were discarded; the script lives on a bare empty GameObject (typically named `LevelTransitionController`) in the GamePlay scene. Lifetime-scoped event subscriptions (Initialize/OnDestroy, NOT OnEnable/OnDisable — so a temporarily DeActivated panel doesn't lose listeners). Self-healing singleton: `Initialize` REPLACES stale ref instead of self-destroying; `OnDestroy` unregisters cleanly. Subscribes to `Level.NextLevelEvent` → `DialogPanelNextLevel` (activates `_nextLevelInfoPanel`); `Level.MovesOutPanelEvent` → `DialogPanelMoveOut`; `Level.RestartComsumablePanelEvent` → `DialogPanelRestartLevel`; `Level.LevelCreated` → `OpenTransectionOnMenu`. All UI SerializeField accesses null-guarded — only `_nextLevelInfoPanel` is required to be wired; everything else (animator, `_levelCompleteAnimation`, `_movesOutPanel`, `_restartLevelConsumablePanel`, `_consumableItemPanel`, `_mainPanel`, sprite arrays) silently no-ops if absent. `CloseTransectionOnGame` directly invokes `CloseTransectionOnGameEnd` (bypasses broken animation-event hook); `CloseTransectionOnGameEnd` captures `isNextLevelTriggered` BEFORE destroying the Level, then `Level.DestroySelf()` → 300ms gap → `GameManager.MakeLevel()`.

- **UI behaviours** — `ClickSoundButton` lives on every Button (134 attachments project-wide); subscribes/unsubscribes the sound listener on OnEnable/OnDisable. Replaced the old per-Awake `Resources.FindObjectsOfTypeAll<Button>` scan from the original SoundManager.

### DI layer

- **`Global`** (`Assets/Lib/Cygnus/Global.cs`). Static class in `Base.Manager` namespace. Exposes `Global.Singletons` (a `SingletonManager` instance) and `Global.GetPrefabBuilder<T>()` (Cygnus's prefab builder API — currently unused by game code). `Global.Dispose()` tears down the registry.

- **`SingletonManager`** (same file). Plain C# class. `Add<T>(instance)`, `Get<T>()`, `Remove<T>()`, `IsRegistered<T>()`. Internally `Dictionary<Type, Dictionary<object, object>>`. `Get<T>()` THROWS on missing key — guard with `IsRegistered<T>()` if the dependency is optional.

---

## 3. Design patterns used

The architecture uses six recurring patterns. Knowing these by name makes the codebase easier to navigate.

### 3a. Service locator (via Cygnus Singletons)

`Global.Singletons` is a service locator, not a true DI container. Consumers call `Get<T>()` to resolve dependencies at runtime, instead of receiving them via constructor injection. Pros: zero ceremony, works for both plain classes and MonoBehaviours. Cons: dependencies are invisible at the type level (you have to read the body to know what a class needs), and it hides ordering bugs (the SceneRefs/BoosterManager bug from 7.4).

The user's "no singletons" rule was about the static `Instance` anti-pattern — a class privately holding its own static reference. `Global.Singletons` is different: it's an external registry, swappable, with explicit registration. The semantic feel is similar but the coupling is in the registry, not the class.

### 3b. Self-registration

MonoBehaviours that should be discoverable register themselves into `Global.Singletons` from their own Awake or Initialize. Patterns:

- **Plain-class services** registered by `GameBootstrap.Awake` (UserData, Factory, etc.)
- **Per-scene MonoBehaviours** register from their own Awake (SoundManager, GameManager, BoosterManager, etc.)
- **Per-entity tracking** via `Explodable.OnEnable`/`OnDisable` registering with `LevelRegistry`

The MonoBehaviour self-registration pattern requires careful execution-order thinking. If consumer A's Awake reads provider B from the registry, B's Awake must have already fired. The `[DefaultExecutionOrder(-1000)]` attribute on GameBootstrap and SceneRefs guarantees they Awake first; everything else can safely query them.

### 3c. Factory (LevelFactory)

`LevelFactory` is a classic factory pattern: takes data (LevelData), returns runtime objects (Constraints, Missions). The switch-on-tagged-union dispatch (`switch (cfg.type)`) keeps the data layer separate from the runtime class hierarchy. New mission types = add an enum value + a switch case + a concrete class. No SO subclassing needed.

### 3d. Event-driven gameplay

Most cross-system communication happens via static C# events on the source class:

- `Bubble.BubbleDestroyedEvent`, `Bubble.DestroySequenceBubbleEvent`
- `Booster.BoosterCreated`, `BoosterExploded`, `BoosterMakeEvent`, `BoosterSequenceListEvent`, `ShakeBooster`
- `Explodable.ExplodablePrototype`, `ExplodedEntitiesEvent`, `MoveDecreaseEvent`
- `Level.MoveCompleted`, `LevelDoneEvent`, `LevelDestroyed`, `CameraFixEvent`, `ChangeSpawnPositionEvent`, `NextLevelEvent`, `LevelCreated`, `MovesOutPanelEvent`, `RestartComsumablePanelEvent`, `SetSkillBarActivity`
- `Constraint.ConstraintFailed` (per-instance, not static)
- `Mission.MissionCompleted` (per-instance)

This decouples sources from listeners — Bubble doesn't know that BoosterManager exists; it just fires DestroySequenceBubbleEvent and someone (or no one) handles it. The cost is **leak risk on static events**: a listener that subscribes but never unsubscribes prevents itself from being garbage-collected. The new `Mission.Cleanup()` virtual + `Level.OnDestroy` call addresses this for Mission classes; older classes (Constraints, UI panels) handle it ad-hoc via OnDisable hooks.

### 3e. Hybrid SO + prefab pair (LevelData + level prefab)

Level content has two sides: data (gameplay config) and visuals (layout, art, pre-placed mission targets). Pure-data would lose the visual layout; pure-prefab would scatter gameplay numbers across hundreds of inspector fields.

The hybrid: `LevelData` SO holds the data; `LevelData.levelPrefab` references the visual prefab. Loading instantiates the prefab and grafts a Level component onto it that reads the data. Generators are components on the prefab; their config is in the SO. Mission targets (LockColors, etc.) live in the prefab; mission counts referencing them live in the SO.

### 3f. Tagged-union configs

Every config struct (`MissionConfig`, `GeneratorConfig`) has all possible parameter fields. The dispatching code (LevelFactory) reads only the fields relevant to `cfg.type`. The unused fields are wasted memory per row, but the structure is **uniform** — perfect for a future level generator that needs to programmatically populate configs without knowing the union's runtime shape.

Alternative approaches considered and rejected:
- **Polymorphic SO subclasses** — would need an asset per mission per level. 5000 levels × N missions = lots of files and inspector overhead. Rejected.
- **Object hierarchy with inheritance** — same problem at runtime, plus serialization complexity.

---

## 4. Key invariants and "rules of the road"

The architecture relies on a small set of invariants that aren't enforced by the type system. Violating them causes weird, often-silent failures.

### 4-pre. Persistent vs scene-specific GOs

**DontDestroyOnLoad'd:** `GameBootstrap` and `SoundManager`. They survive the Menu→GamePlay scene transition. Plain-class services (UserData, Factory, GameAnalytic, Buy*, LevelRegistry, LevelCatalog) registered by Bootstrap stay alive in `Global.Singletons` for the whole session.

**Menu scene only:** MenuCanvas (MainPanel, SettingsPanel, future LevelSelectPanel), Camera, EventSystem.

**GamePlay scene only:** SceneRefs, GameManager, BoosterManager, PoolingObject, CameraMovement, EventReward, GamePanel (in-game UI panels), all the gameplay parents (BubbleParent, BoosterParent, Spawn, LevelParent, GameWalls, Chains, GameSafeArea), `_GamePlayAutoLoader`.

When entering GamePlay for the first time, the scene's gameplay services Awake and self-register; the autoloader's Start calls `MakeLevel()`. **Going back to Menu is not yet wired** — current code path supports Menu→GamePlay→keep-playing-levels indefinitely. Pause-to-menu and end-of-level-to-menu paths need additional cleanup work (Singletons.Remove on OnDestroy for game-scene MonoBehaviours) before re-entry works.

### 4a. Provider scripts use `[DefaultExecutionOrder(-1000)]`

**Rule:** any MonoBehaviour that registers something in `Global.Singletons` from its Awake/Initialize, and is read by another MonoBehaviour's Awake/Initialize, **must** carry `[DefaultExecutionOrder(-1000)]`.

**Currently has it:** `GameBootstrap`, `SceneRefs`.

**Why:** Unity's Awake order between same-priority MonoBehaviours on different GameObjects is undefined. Without this attribute, a consumer can fire its Awake before the provider, get a `Singleton not found` exception, get silently disabled by Unity, and silently break its dependent gameplay flow. This has happened twice (LevelParent tag bug, SceneRefs/BoosterManager bug). Both surfaced only via downstream symptom — the provider's Awake-time error wasn't visible because the consumer's disable was silent.

**Alternative:** consumers can query in `Start()` instead of `Awake()` — Start fires after all Awakes complete. But Start can't be used for state that needs to be in place by other Awakes/OnEnables.

### 4b. Pooled entity OnEnable/OnDisable triggers register/unregister

`Explodable.OnEnable` calls `LevelRegistry.Register(this)`; `OnDisable` calls `Unregister`. Pooled entities (Bubble, Booster) get borrowed via `gameObject.SetActive(true)` (fires OnEnable) and returned via `SetActive(false)` (fires OnDisable). Every borrow re-registers, every return de-registers. So `LevelRegistry.Active<Bubble>()` always returns currently-visible bubbles — never stale references to pooled-but-inactive ones, never null refs for destroyed ones.

This is why the entire `FindObjectsOfType<Bubble>()` removal in Phase 6 worked without behavior changes.

### 4c. Mission lifecycle: subscribe in ctor, unsubscribe in `Cleanup()`

All Missions subscribe to GameEvents (the static bus) via `MissionTracker.Bind`, which returns an unsubscribe lambda. `Mission.Cleanup()` invokes it; `Level.OnDestroy` calls Cleanup on every Mission to unsubscribe. Without this, missions would leak across Level instances (the static GameEvents would hold strong refs to dead Mission closures). Same shape for `MetaMission` — it auto-unsubscribes on period rollover via `MetaMissionService.Regenerate`.

### 4d. Singletons.Get<T>() throws if not registered

Cygnus's `SingletonManager.Get<T>()` throws `ArgumentException` on missing key. Use `IsRegistered<T>()` first if the dependency is optional (e.g. UI components that may or may not be in the scene). Don't catch the exception — it indicates a real bug somewhere; let it surface.

### 4e. `SetData → SetActive(true)` order for Level

`GameManager.MakeLevel` follows the strict pattern: `Instantiate → SetActive(false) → AddComponent<Level> → SetData(data) → SetActive(true)`. This guarantees `Level.Awake → Initialize` sees `_data` set. Skipping the SetActive cycle (or doing AddComponent on an active GO) means Awake fires before SetData, and Initialize sees null data → builds an empty level.

### 4f. Level.Initialize vs Level.Start (the LevelRegistry visibility gap)

When a Level GO activates (its prefab + Level component all together), Unity fires Awake on the parent first, then OnEnable on the parent, then descends recursively to children. **Level.Awake → Initialize fires BEFORE child entities' OnEnable** (which is when Explodables register into LevelRegistry). So Initialize cannot read LevelRegistry — it'd see empty.

`Level.Start` fires after the entire activation cascade completes, by which point all children have OnEnabled. That's where `_lockColorsInLevel = LevelRegistry.Active<LockColor>().ToList()` and `LevelFactory.RunGenerators` happen.

---

## 4g. Missions system (in-game + meta)

**Two consumers, one data shape.** Both per-level missions ("destroy 5 blue this game") and meta missions ("destroy 50 blue today") consume the same `MissionGoal` struct. They differ only in lifetime + persistence:

- **`Mission`** — in-level runtime. Built from `LevelData.MissionConfig` by `LevelFactory.BuildMissions`, lives one Level instance, in-memory only. Cleaned up in `Level.OnDestroy`.
- **`MetaMission`** — persistent. Lives across sessions, scoped to a daily/weekly/monthly period. JSON-serialized into the `UserData` blob.

**The shared dispatch.** `MissionTracker.Bind(MissionGoal, Action<int>)` is the one place that contains the `goalType → GameEvent` switch. Both Mission and MetaMission call it from their own `Bind()` and store the returned unsubscribe lambda. Adding a new goal type = add an enum value, add one `case` in `MissionTracker`, raise the corresponding event somewhere. ~5 minutes per type, no new files.

**The bus.** `GameEvents` is a static class with 8 events: `BubblePopped(BubbleTypes)`, `BoosterUsed(BoosterTier)`, `BoosterCreated(BoosterTier)`, `ObstacleDestroyed(ObstacleType)`, `CoinGained(int)`, `AbilityUsed(AbilityTypes)`, `LevelCompleted(int levelId)`, `WinSeriesUpdated(int streak)`. Producers (Bubble.Explode, Booster.Initialize/Explode, UserDataManager.EarnCoin, Level.NextLevelEventInvoke) raise; the missions subscribe.

**MetaMissionService** (Cygnus singleton, registered by GameBootstrap if `Resources/MissionTemplateLibrary` asset exists):
- `Initialize` on app boot — calls `CheckRollovers`, then binds every active MetaMission to GameEvents.
- `CheckRollovers` — for each period (Daily/Weekly/Monthly), if the local-UTC anchor (Date / WeekStart / MonthStart) has rolled past the last-reset timestamp, regenerates that period's mission set by picking N entries from `MissionTemplateLibrary.{daily|weekly|monthly}Pool`.
- `ClaimReward(MetaMission)` — dispatches the reward via `UserDataManager.EarnCoin/EarnBooster/EarnAbility` and sets `RewardClaimed=true`. Idempotent.
- Auto-saves via `UserData.SaveAllDataToPrefs()` on every progress + completion + claim event.
- `OnApplicationPause(false)` (in GameBootstrap) re-runs CheckRollovers on app resume so opening the game on a new day rotates daily missions even without a full restart.

**Persistence**: `UserData` has 4 missions-related fields (`MetaMissionsActive` + 3 reset timestamps) serialized via the existing Newtonsoft JSON blob in `PlayerPrefs["Data"]`. MessagePack swap (Phase E, deferred until backend) is a serializer-line change — DTO shape is already MessagePack-compatible.

**UI**: two pieces, decoupled.
- `MissionsNextUpCard` — always-visible inline card living below the MainPanel top bar (replaced the unused `ContentEvent` GameObject). Shows the closest-to-completion mission. Whole card is a button → opens MissionsPanel.
- `MissionsPanel` — popup overlay, hidden by default. Scroll list of `MissionRow` instances, sorted (claimable first → progress % → period). Per-row claim button greys out when the mission isn't claimable.

**`ViewMissions`** (in-game top-bar mission display) was decoupled from the legacy class statics in Phase B and exposes a `Bind(Mission)` API but isn't currently called from anywhere; the in-game mission UI is dormant pending a MissionsPanel-style rebuild.

---

## 5. Asmdef boundaries

Three assembly definitions, each with a clear scope:

- **`Cygnus`** (`Assets/Lib/Cygnus/Cygnus.asmdef`). The DI library. `Base.Manager.Global`, `SingletonManager`, `Cygnus.Common.MyObject` (an unused lifecycle base — separate from the project's `BlastObject`), prefab builder API. Has 8 precompiled-reference DLLs (CygnusAttributes, CygnusCommon, etc.). Does not depend on game code.

- **`DOTween.Modules`** (`Assets/Plugins/Demigiant/DOTween/DOTween.Modules.asmdef`). Wraps the 8 DOTween module files (Audio, Physics, Sprite, UI, EPOOutline, UnityVersion, Utils, Physics2D) so their extension methods (`DOFillAmount`, `DOFade`, etc.) are visible to game code. References Unity.TextMeshPro, URP runtime, and the DOTween.dll precompiled.

- **`DragonBlast.Runtime`** (`Assets/Scripts/DragonBlast.Runtime.asmdef`). All game scripts (~200 files). References Cygnus, DOTween.Modules, and a list of standard Unity packages (InputSystem, Cinemachine, TextMeshPro, Spine, Coffee.UIParticle, etc.). `noEngineReferences: false` and `autoReferenced: true` so editor scripts in `Assets/Editor/` can find runtime types.

Editor scripts in `Assets/Editor/` and `Assets/Axiom/Editor/` compile to `Assembly-CSharp-Editor` (no asmdef needed; auto-references everything visible).

**No Shared asmdef yet.** Originally planned (Util/, Mapping/, MainReSource/, Interfaces/) but cycle-resolved deferred — those folders have inbound deps to runtime-layer namespaces. Extraction needs cycle-breaking refactors. Held until the architecture stabilizes further.

---

## 6. What's intentionally NOT here

Worth knowing what was removed and why, so it doesn't get reintroduced.

- **No `static T Instance` singletons.** Every type that used to have a static Instance pattern (GameManager, UserData, GameAnalytic, SoundManager, TransectionPanel, etc.) now self-registers in Singletons. The exceptions: `Level.ActiveLevel` was the last static, removed in Phase 7.1 — all 12 call sites migrated to `Singletons.Get<Level>()`.

- **No `GameObject.Find*` in production code paths.** Phase 6 removed 22 of 24 calls. Two remain: `CameraOrtSize.cs` (orphaned in production, only attached in `GameTest.unity` which is preserved as a UI prototype scene), and `transform.Find(childName)` in `PoolingObject.cs` (child lookup, not a scene-wide search — different beast). New code should reach for `[SerializeField]` or `Singletons.Get<T>()`, not Find.

- **No Zenject.** Migrated to Cygnus + Singletons in Phase 4, vendored Zenject deleted in Phase 5. `[Inject]` attributes removed. ModestTree (Zenject's bundled util) `IsEmpty()` calls replaced with `.Count == 0` / `.Count > 0`.

- **No per-level subclass pattern.** Level1.cs–Level100.cs deleted in Phase 1. The Level class is now concrete and data-driven. Adding a new level = creating a new `LevelData` SO + level prefab; no new C# class.

- **No IMGUI in production game UI.** `SpaceBlastRuntimeSpawner.OnGUI` is the last IMGUI in a runtime path; it's flagged for retirement in 7.7. `LevelTestLoader.OnGUI` is also IMGUI, but it's explicit test scaffolding and will be replaced by the real menu/transection flow. Production UI uses uGUI Canvas + Buttons.

- **No 100-level enum lock-in for levels.** `LevelTypes` enum (Level1..Level100) is kept only for `GameAnalytic` compat (`LevelData.legacyLevelType`). Real level identification uses `LevelData.levelId` (int), which scales to the planned 5000-level catalog.

- **No mocks in tests.** There are no tests yet; when there are, integration tests against real Unity (no DI mocking) per the user's stance.

---

## 7. Known limitations and sharp edges

The architecture works but has known rough spots. These are tracked in the relevant phase plans; reproducing them here so they're discoverable from the architecture doc.

### 7a. `async void` everywhere

`async void` is used throughout (Bubble.Explode, Booster.OnMouseDown, OnTriggerStay2D, MakeBubbleConstraint.MakeBubble, etc.). Each `Task.Delay` continuation allocates a state machine; exceptions inside `async void` are silently swallowed (or rethrown into the SynchronizationContext, depending on Unity version). Replace with `Awaitable` (Unity 2023+) or proper Task-returning methods — Phase 8 territory. Listed in `feedback_optimization_stance.md` memory.

### 7b. `OnTriggerStay2D` in Booster

`Booster.OnTriggerStay2D` fires every fixed step on every overlap during the merge window. Cheap per call but high frequency. Phase 8 will likely replace with explicit polling during merge.

### 7c. `LevelTypes` enum doesn't scale to 5000

The `LevelTypes` enum has 100 values. `GameAnalytic.SaveAnalytic(LevelTypes type, ...)` consumes it. With a 5000-level catalog, this enum becomes useless. Plan: add `int levelId` to GameAnalytic API, retire the `LevelData.legacyLevelType` field. Tracked separately from Phase 7; analytics modernization concern.

### 7d. MiniGame system overlaps with PopBubbles

`BubbleMiniGame` is the only concrete MiniGame, and it semantically duplicates `PopBubblesMission` (both count pops of a specific color). The MiniGame system has its own static list (`Level.MiniGames`), its own event flow, and is hard-coded to `BubbleTypes.Blue, 50` in `GameManager.BlueBubbleMiniGame()`. Deprecation candidate. The 9-value `MiniGameTypes` enum has aspirational placeholders (DyeJarMiniGame, LanderMiniGame, etc.) that were never implemented.

### 7e. Mission subscription in constructor + base finalizer

`Mission.SetLevel` subscribes to `Level.MoveCompleted`; the finalizer unsubscribes. This means missions can outlive their Level if not explicitly cleaned up — `Level.OnDestroy` now calls `Cleanup()` on each Mission to handle this synchronously. The finalizer is still there as a backstop but isn't reliable (GC timing).

### 7f. SampleScene is a development scene, not a production scene

SampleScene was the testbed throughout the refactor and lacks dependencies that production play would have (InGameTextController, possibly more UI canvases). `Level.Initialize` guards `InGameTextController` access with `IsRegistered`; other UI paths are gameplay-event-triggered and may surface as `Singleton not found` errors mid-play in SampleScene if their UI consumer fires. Production scene (TBD) will have everything wired.

### 7g. No formal test coverage

No play-mode tests, no unit tests, no integration tests. The `Axiom` editor framework has a `TestRunner` diagnostic but no tests are authored. Smoke tests are manual + ad-hoc Unity_RunCommand scripts.

### 7h. `MakeBubbleConstraint` reads `SceneRefs` in its constructor

Constructor side-effects: `MakeBubbleConstraint` looks up `SceneRefs.Spawn` and `SceneRefs.BubbleParent` immediately on construction. This means it can't be instantiated outside Play Mode (or in any context where SceneRefs isn't registered). Annoying for tests; tolerable for now.

### 7i. The `_levelTypes` field remains as a legacy bridge

`Level._levelTypes` (LevelTypes enum) is set from `LevelData.legacyLevelType` in Initialize, then read by `_levelName`, `CurrentLevel()`, and `GameAnalytic.SaveAnalytic`. It's a duplicate of `_data.legacyLevelType`. Removing requires touching analytics + level naming display. Defer until analytics modernization.

---

## 8. Glossary

Acronyms and project-specific terms that appear in the codebase.

- **Asmdef** — Assembly Definition file (`.asmdef`). Unity's mechanism for splitting code into compile units. Each asmdef compiles to its own DLL.
- **BlastObject** — the project's MonoBehaviour base class. Provides Initialize/Registration/UnRegistration lifecycle hooks. (Renamed from `MyObject` in Phase 1 to disambiguate from Cygnus's similarly-named type.)
- **Booster** — power-up entity created by 4+ same-color pops or by merging two boosters.
- **Booster Tier** — Level1/Level2/Level3 within the BoosterTypes 3×3 grid. Booster1Level3, Booster2Level3, Booster3Level3 are all Tier 3.
- **Bubble** — the core poppable entity. Has color (BubbleTypes enum: Blue/Green/Orange/Pink/Purple/Powder).
- **Constraint** — a level-completion gate. MoveConstraint, SecondConstraint (time), MakeBubbleConstraint (spawn rate).
- **Cygnus** — the DI/utility library at `Assets/Lib/Cygnus/`. Provides `Global.Singletons` and `Cygnus.Common.MyObject` (unused).
- **Explodable** — abstract base for entities that can be exploded (Bubble, Booster, LockColor, DyeJar, etc.).
- **Generator** — a per-level entity spawner (DyeJarGenerate, LanderGenerate, etc.). Lives as a component on the level prefab.
- **LevelData** — ScriptableObject describing a level's gameplay config (missions, constraints, bubble rates, prefab ref).
- **LevelCatalog** — ScriptableObject holding the ordered list of LevelData. One asset at Resources/LevelCatalog.asset.
- **LevelFactory** — static class that converts LevelData → runtime Constraint/Mission/Generator invocations.
- **LevelRegistry** — runtime tracker of currently-active entities. Replaces `FindObjectsOfType<T>` mid-game queries.
- **Mission** — a level objective. PopBubbles, PopBoosters, CreateBoosters, ClearObstacles (with type filter).
- **Obstacle** — an in-level target entity (Lock, Jar, Lander, TNT, Ore, ToolBox, Kitten). Each has a corresponding Mission class.
- **Pool / PoolingObject** — Bubble/Booster instance reuse. Pool entries are inactive until borrowed; Pool returns SetActive(false).
- **SceneRefs** — scene-resident Transform references (BubbleParent, BoosterParent, Spawn, LevelParent) registered into Singletons.
- **Singletons** — `Global.Singletons` registry (Cygnus's SingletonManager). Service-locator pattern.
- **Stage** — sub-section of a multi-stage level. Each stage has its own bubble spawn position.

---

## How to keep this doc useful

This is a reference doc — it goes stale fast. Suggestions for keeping it true:

- When a major class is added, removed, or renamed, update sections 2 and 8.
- When a new design pattern is introduced (or an old one retired), update section 3.
- When an invariant changes (e.g. a new script gets `[DefaultExecutionOrder]`), update section 4.
- When a known limitation is fixed, remove it from section 7. When a new one is found, add it.
- The phase-specific implementation plans (`01_Refactor_Phases.md`, etc.) are roadmap documents; this is reference. Don't conflate them.
- Don't append to this with dated entries. Edit in place to keep it current.
