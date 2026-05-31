# File Map — Who Does What & When

> Written 2026-05-03. Companion to `99_Architecture_Reference.md` — that doc explains the *system* in prose; this one is a flat *file index* you can ctrl-F. Keep both updated when major changes land (per the standing arch-doc-maintenance memory).

---

## How to read this doc

Each row tells you:
- **What it is** — class name and one-line responsibility
- **When it runs** — which lifecycle event triggers it (Awake, OnEnable, on click, on event)
- **Notable** — fields, events, dependencies that matter

If a file isn't here, it's either trivial (a struct, an enum, an interface marker) or vestigial (mentioned in the arch doc's deferred-cleanup list).

---

## A. Boot & DI

These are the things that wake up the game.

### `Assets/Lib/Cygnus/Global.cs`
- **What:** `Global.Singletons` — service locator. The `SingletonManager` instance that holds all registered services.
- **When:** Static, alive for the whole AppDomain.
- **Notable:** `Add<T>(instance)`, `Get<T>()` (throws on missing), `IsRegistered<T>()`, `Remove<T>()`. **`Get<T>` THROWS** — guard with `IsRegistered` for optional dependencies.

### `Assets/Scripts/Bootstrap/GameBootstrap.cs`
- **What:** Top-of-app wireup. Registers all the plain-class services into `Global.Singletons`.
- **When:** Awake (`[DefaultExecutionOrder(-1000)]` — fires before everyone else). Lives in **Menu scene**, persists via `DontDestroyOnLoad`.
- **Notable:** Has an idempotency guard — if `UserData` already registered, the duplicate copy in GamePlay self-destroys. Registers: `UserData`, `Factory`, `GameAnalytic`, `BuyAbility/Health/Booster/Move`, `LevelRegistry`, `LevelCatalog`.

### `Assets/Scripts/Bootstrap/SceneRefs.cs`
- **What:** Holds inspector-wired Transform refs to scene-resident parents (`BubbleParent`, `BoosterParent`, `Spawn`, `LevelParent`). Self-registers on Awake.
- **When:** Awake (`[DefaultExecutionOrder(-1000)]`). Lives in **GamePlay scene only** (Menu has no game-scene transforms).
- **Notable:** This is the replacement for all the deleted `FindGameObjectWithTag` calls.

### `Assets/Scripts/Bootstrap/LevelRegistry.cs`
- **What:** Plain class. Tracks currently-active `MonoBehaviour` entities (Bubble, Booster, etc.) via `HashSet`.
- **When:** Lifetime-of-session (registered by GameBootstrap once). Entities self-register/unregister via `OnEnable`/`OnDisable`.
- **Notable:** Replaces `FindObjectsOfType<T>()` mid-game queries. APIs: `Register(mb)`, `Unregister(mb)`, `Active<T>()` (LINQ filter).

### `Assets/Scripts/Bootstrap/GamePlayAutoLoader.cs`
- **What:** Drops on `_GamePlayAutoLoader` GO in GamePlay scene. On Start, calls `GameManager.MakeLevel()` (no-arg).
- **When:** Start (after every Awake/OnEnable in GamePlay scene).
- **Notable:** This is what actually triggers level loading after the scene transitions in.

---

## B. Persistence & Data

### `Assets/Scripts/MainReSource/UserData.cs`
- **What:** Plain class holding all persistent player state — Coin, Health, Trophy, PlayerLevel, Star, BoosterCounts, AbilityCounts, etc.
- **When:** Loaded once via `UserData.Load()` in GameBootstrap. Stays alive for session.
- **Notable:** **PlayerPrefs persistence is COMMENTED OUT.** Each editor restart resets to field defaults. Critical bug for shipping.

### `Assets/Scripts/Managers/User/UserDataManager.cs`
- **What:** Static helper functions on UserData. `EarnHealth`, `LoseHealth`, `LevelUp`, `EarnCoin`, `LoseCoin`, `EarnBooster`, `EarnAbility`, `CanPlayLevel`.
- **When:** Called from gameplay code wherever stats change.

### `Assets/Scripts/Managers/Analytics/GameAnalytic.cs`
- **What:** Plain class. Per-level metrics tracker — explosions per type, ability uses, end state, time elapsed.
- **When:** Bootstrap-registered. `SaveAnalytic` called from `Level.OnDestroy` at end of each level.
- **Notable:** Currently writes to memory only — no backend wiring.

### `Assets/Scripts/Data/EventProgression.cs` + `Assets/Resources/EventProgression.asset`
- **What:** ScriptableObject holding the event-list progression sequence (per-level event count thresholds).
- **When:** `Resources.Load`'d once in `GameManager.Initialize`, copied into `UserData.EventList`.

---

## C. The Level System (the big one)

### `Assets/Scripts/Levels/LevelData.cs`
- **What:** ScriptableObject — the data side of a level. Holds `levelPrefab` ref, `bubbleRates`, `maxBubbleCount`, `moveConstraint`, `timeConstraint`, `missions[]`, `generators[]`. Plus tagged-union config structs.
- **When:** Loaded by `LevelCatalog` from disk. Read by `LevelFactory` to build runtime objects.
- **Notable:** Generator-friendly shape — no polymorphic SO subclasses. Adding a level = creating a new asset.

### `Assets/Scripts/Levels/LevelCatalog.cs` + `Assets/Resources/LevelCatalog.asset`
- **What:** ScriptableObject holding `List<LevelData>`. Single asset, the playable level catalog.
- **When:** `Resources.Load`'d in GameBootstrap, registered in Singletons.
- **Notable:** APIs: `GetByPlayerLevel(int 1-based)`, `GetByIndex(int 0-based)`, `GetByLevelId(int)`. Today: 2 entries (Smoke01, Smoke02).

### `Assets/Scripts/Levels/LevelFactory.cs`
- **What:** Static class. Converts `LevelData` → runtime `Constraint`/`Mission`/`Generator` instances.
- **When:** Called by `Level.Initialize` (BuildConstraints + BuildMissions) and `Level.Start` (RunGenerators).
- **Notable:** Switch-on-tagged-union dispatch. New mission types = add enum value + switch case.

### `Assets/Scripts/Levels/Level.cs`
- **What:** The runtime level controller — the game state machine. Exposes `EndLevelType` (Playing/NextLevel/LoseLevel/LevelCleaning), holds `Constraints[]`/`Missions[]`/`MiniGames[]`. Aggregates mission completion → fires `IsLevelDone`.
- **When:** Created by `GameManager.MakeLevel(LevelData)`. Awake → Initialize → fires Constraints/Missions setup. Start → resolves generators + reads LevelRegistry. OnDestroy → cleanup, save analytics.
- **Notable:** Concrete class (not abstract anymore). Singletons-registered as `Level` (only one at a time). End-of-level chain orchestrator.

---

## D. Constraints (per-level rules that can fail)

All extend `Constraint` base which extends `BlastObject` (MonoBehaviour subclass — Unity warns on `new`, but works).

### `Assets/Scripts/Constraint/Constraint.cs`
- **What:** Abstract base. Owns `Level` ref, `ConstraintFailed` event, `SetLevel(level)` (registers for MoveCompleted).

### `Assets/Scripts/Constraint/MoveConstraint.cs`
- **What:** Move counter. Fires `ConstraintFailed` when moves run out.
- **Constructor:** `(int maxMoves)`.

### `Assets/Scripts/Constraint/SecondConstraint.cs`
- **What:** Time limit. Polls every 1s, fires `ConstraintFailed` when expired.
- **Constructor:** Multiple overloads (DateTime, TimeSpan).

### `Assets/Scripts/Constraint/MakeBubbleConstraint.cs`
- **What:** Manages bubble spawning. Fills the bubble pool, spawns initial wave (async), handles re-spawn on bubble destruction.
- **Constructor:** `(int maxBubbleCount, BubbleRates)`. **Reads `SceneRefs.Spawn` in ctor** — only constructible at runtime.
- **Never fails** — it's a spawner, not a fail condition.

---

## E. Missions (unified system, in-level + meta)

Phase 10 collapsed the 14 legacy mission subclasses into one goal-driven runtime. Both per-level and persistent (daily/weekly/monthly) missions share the same data shape and event-bus dispatch.

### `Assets/Scripts/Missions/MissionGoal.cs`
- **What:** Flat data shape — `goalType` enum + `targetCount` + `filter` int + `RewardData`. Cast `filter` at use site to the relevant enum (BubbleTypes / BoosterTier / ObstacleType / AbilityTypes).

### `Assets/Scripts/Missions/GameEvents.cs`
- **What:** Static pub-sub bus. 8 events. Producers (Bubble.Explode, Booster, UserDataManager.EarnCoin, Level.NextLevelEventInvoke) raise; missions subscribe via MissionTracker.

### `Assets/Scripts/Missions/MissionTracker.cs`
- **What:** Internal helper. One switch over `MissionGoalType` → returns the correct GameEvents subscription + unsubscribe lambda. Both Mission and MetaMission delegate to this.

### `Assets/Scripts/Missions/Mission.cs`
- **What:** In-level mission runtime. Lives one Level instance. `LevelFactory.BuildMissions` constructs from `MissionConfig`; `Level.Initialize` calls `Bind()` to activate event subs; `Level.OnDestroy` calls `Cleanup()` to unsub.

### `Assets/Scripts/Missions/MetaMission.cs`
- **What:** Persistent mission. Daily/Weekly/Monthly scope. JSON-serializable (Action fields with `[JsonIgnore]`, parameterless constructor). Stored in `UserData.MetaMissionsActive`.

### `Assets/Scripts/Missions/MissionPeriod.cs`
- **What:** Daily/Weekly/Monthly enum.

### `Assets/Scripts/Missions/MissionTemplateLibrary.cs` + `Assets/Resources/MissionTemplateLibrary.asset`
- **What:** ScriptableObject — designer-editable pool of MissionGoals per period + per-period pick count. `Pick(period, n)` randomly selects n entries on rollover.

### `Assets/Scripts/Missions/MetaMissionService.cs`
- **What:** Cygnus singleton. Owns lifetime of MetaMissions. `Initialize` on boot + `OnApplicationPause(false)` re-checks rollovers. `ClaimReward` dispatches via UserDataManager. Auto-saves UserData on every progress + completion + claim.

### `Assets/Scripts/Missions/MissionDescriber.cs`
- **What:** Static formatter. Turns a MissionGoal into "Pop 20 blue bubbles" / "+100 Coins" display strings. Used by both UI components.

### Deleted in Phase B (kept here for grep-reference)
- 11 legacy mission subclasses: `BubbleMission`, `KittenMission`, `DyeJarMission`, `LockColorMission`, `LockKeyMission`, `LanderMission`, `OreMission`, `TntMission`, `ToolBoxMission`, `PopBoostersMission`, `CreateBoostersMission`
- Legacy `Mission` abstract base class
- `IMission` interface

---

## F. Generators (in-level entity spawners)

All `MonoBehaviour` (extend `GenerateObject : BlastObject`). Live as components on level prefabs. Invoked by `LevelFactory.RunGenerators`.

`DyeJarGenerate.cs`, `LanderGenerate.cs`, `TNTGenerate.cs`, `OreGenerate.cs`, `ToolBoxGenerate.cs`, `KittenGenerate.cs` — each spawns its target entity into the level via `Factory.CreateObject<T>`.

---

## G. Game-scene managers (each registered via Singletons)

### `Assets/Scripts/Managers/Core/GameManager.cs`
- **What:** Top-level game state. Holds `canClick`, static `PanelActive`/`UsingSkill`. `MakeLevel(LevelData)` is the level-load entry point.
- **When:** Self-registers in Initialize. Update tick handles click cooldown + health timer.

### `Assets/Scripts/Managers/Core/BoosterManager.cs`
- **What:** Listens to `Bubble.DestroySequenceBubbleEvent` → spawns boosters on 4+ pops. Also handles merge spawns + queueing booster explosions in chains.
- **When:** Awake self-registers. In GamePlay scene only.

### `Assets/Scripts/Managers/Audio/SoundManager.cs`
- **What:** Two AudioSources + click sound. APIs: `MainSoundAction(TrackStatus)`, `SfxSoundAction(SfxSound)`, `PlayButtonClick()`.
- **When:** Self-registers + `DontDestroyOnLoad`. Lives in **Menu scene**, persists into GamePlay so audio doesn't restart.

### `Assets/Scripts/Managers/Camera/CameraMovement.cs`
- **What:** Camera follow + shake on stage advance + booster events.
- **When:** Self-registers. In GamePlay scene only.

### `Assets/Scripts/Managers/Camera/CinemachineShakeController.cs`
- **What:** Cinemachine shake helper.

### `Assets/Scripts/Managers/Auth/*`
- **What:** Auth scaffolding — `IAuthenticationManager`, `AppleAuthManagers`, `MockAuthManager`. **Backend deferred indefinitely.** Stubs.

### `Assets/Scripts/Managers/Network/*`
- **What:** `HttpsClientManager`, `IClient`. Backend stubs. Deferred.

### `Assets/Scripts/Managers/Levels/*`
- **What:** `LevelManager`, `ILevelService`. Backend stubs.

### `Assets/Scripts/Managers/User/*`
- **What:** `UserManager`, `UserDataManager`, `IUserService`. Backend + UserData helpers.

### `Assets/Scripts/Managers/Events/*`
- **What:** `EventManager`, `IEventService`, `WinSeriesManager`. Event-progression helpers.

---

## H. Entity Layer

### `Assets/Scripts/Entities/BlastObject.cs`
- **What:** Base MonoBehaviour for game entities. Awake → Initialize → Registration. Static `Destroyed` event. Virtual `Activate`/`DeActivate`.

### `Assets/Scripts/Entities/Explodable.cs`
- **What:** Abstract base for things that can explode. **Self-registers in `LevelRegistry` via OnEnable/OnDisable** — every Bubble/Booster/LockColor/etc. is tracked here.

### `Assets/Scripts/Entities/Objects/Bubble.cs`
- **What:** The core poppable. Tracks neighbors via `OnTriggerEnter2D`. Click handler in `OnMouseDown`. Cascade detection in `GetBubblesSequenceList`/`TakeThemAll`.
- **Pooled** via `PoolingObject`.

### `Assets/Scripts/Entities/Objects/Booster.cs`
- **What:** Power-up. Spine-animated. Created by 4+ pops or merge. `OnTriggerStay2D` handles merge detection (perf concern flagged for Phase 8).
- **Static events:** `BoosterMakeEvent`, `BoosterSequenceListEvent`, `BoosterExploded`, `BoosterCreated`, `ShakeBooster`.

### Other entities under `Assets/Scripts/Entities/Objects/`
- `LockColor`, `DyeJar`, `Lander`, `TNT`, `Ore`, `ToolBox`, `Kitten`, `Bird`, `Cannon`, `Chain`, `LockKey`, `KeyFromLock`, `PlasmaBall`, `PlasmaProjectile`, `BubbleProjectile`, `Wall`, etc. — gameplay obstacles, mostly Explodable subclasses.
- `Ability/*` — `AbilityTornado`, `AbilityMeteor`, `AbilityUfo`, `Ability` base. Player abilities.

### `Assets/Scripts/Entities/PoolingObject.cs`
- **What:** Pool manager. `FillPool<T>(variant, count)`, `GetObjectFromPool<T>(variant, position)`. Pool returns happen via `gameObject.SetActive(false)`.

### `Assets/Scripts/Entities/Factory.cs`
- **What:** Plain class. Loads all prefabs from any `Resources/` folder, indexes by type+variant. `CreateObject<T>(variantId, parent)` instantiates.

---

## I. UI Layer

### `Assets/Scripts/UI/MyUI.cs`
- **What:** Empty marker class extending BlastObject. Used as a base for UI panels.

### `Assets/Scripts/UI/Panels/MainPanel.cs`
- **What:** The main menu panel. Top-bar resource displays (Coin/Health/Trophy/Star), Play button, Settings button, Comic buttons.
- **When:** Lives in Menu scene's MenuCanvas. Awake fires when scene loads.
- **Notable:** `OnBtnPlayGame` → `SceneManager.LoadScene("GamePlay")`. The user's tap-to-play entry point.

### `Assets/Scripts/UI/Panels/SettingsPanel.cs`
- **What:** Sound/Music/Vibration toggles via PlayerPrefs.
- **When:** Activated via MainPanel.Settings button. In Menu scene.

### `Assets/Scripts/UI/Panels/TransectionPanel.cs`
- **What:** Animated panel transitions. Self-registers in Singletons. Listens to Level events (NextLevelEvent, MovesOutPanelEvent, RestartComsumablePanelEvent, LevelCreated).
- **When:** Lives in GamePlay scene's GameCanvas. Active by default (so it can receive events). Visual content is gated by sub-children + animator.
- **Notable:** Animation events trigger `MakeLevel` via `MakeLevelStartAnimaitonEvent`.

### `Assets/Scripts/UI/Panels/NextLevelInfoPanel.cs`
- **What:** "Level N complete — tap Play to continue" UI. Shows gained coins/trophy.
- **When:** Activated by `TransectionPanel.DialogPanelNextLevel` after end-of-level chain. Inactive in prefab by default.
- **Notable:** Tap Play → `OnButtonPlay` → `CallTransection` → `TransectionPanel.CloseTransectionOnGame` → animation event → `MakeLevel()` (next level loads).

### `Assets/Scripts/UI/Panels/MovesOutPanel.cs`
- **What:** "You ran out of moves — buy more?" panel. Lose-state UI.

### `Assets/Scripts/UI/Panels/RestartConsumablePanel.cs`
- **What:** "Restart with consumables?" panel after lose.

### `Assets/Scripts/UI/Panels/ConsumableItemPanel.cs`
- **What:** Pre-game booster picker. **Currently bypassed** — MainPanel.Play goes straight to GamePlay; can be re-wired later if you want the picker flow.

### `Assets/Scripts/UI/Panels/BuyAbilityPanel.cs` + `BuyBoosterPanel.cs`
- **What:** Purchase UIs.

### `Assets/Scripts/UI/Panels/ComicPanel.cs` + `Comic1Panel.prefab`
- **What:** Story content viewer. Out of scope; Comic button on MainPanel will NRE if tapped (refs unwired).

### `Assets/Scripts/UI/Panels/NotHealthPanel.cs`
- **What:** "Out of lives" panel.

### `Assets/Scripts/UI/Panels/MissionsNextUpCard.cs`
- **What:** Always-visible inline card living below the MainPanel top bar (replaced the unused `ContentEvent`). Shows the closest-to-completion meta mission. Whole card is a button → opens `MissionsPanel`. Auto-hides if no missions are active. Subscribes to MetaMissionService events for live refresh.

### `Assets/Scripts/UI/Panels/MissionsPanel.cs`
- **What:** Popup overlay (hidden by default). Scroll list of every active MetaMission, sorted (claimable first → progress % → period). Instantiates `MissionRow` prefab per mission.

### `Assets/Scripts/UI/Panels/MissionRow.cs`
- **What:** One row inside the MissionsPanel scroll list. Title + progress bar + reward label + claim button. Auto-greys claim button when not claimable.

### `Assets/Scripts/UI/Panels/MainPanel.cs` (already covered above)

### `Assets/Scripts/UI/Panels/CoverPanel.cs`, `AbilityPanel.cs`, `AbilityBGPanel.cs`, `PowerUpPanel.cs`, `GamePanelComicControl.cs`, `TutorialPanel.cs`
- **What:** Various in-game UI overlays.

### `Assets/Scripts/UI/Panels/BaloonCollectCanvas.cs` (sic)
- **What:** Self-registered Canvas. Holds the "balloon collected" trail VFX.

### `Assets/Scripts/UI/UICanvas/BaseCanvas.cs`
- **What:** Self-registered Canvas. Common UI parent.

### `Assets/Scripts/UI/UICanvas/FiveMovesRemain.cs`
- **What:** "5 moves remaining" warning popup. Empty marker class.
- **When:** Activated by `MoveConstraint.MoveDecrease` when count hits 5.

### `Assets/Scripts/UI/UICanvas/Dialog/*`
- **What:** `Dialog`, `DialogYesNo`, `DialogOkay`, `DialogExit`. Modal dialogs.

### `Assets/Scripts/UI/InGameTexts/*`
- **What:** `InGameTextController` self-registers, manages floating text overlays. `AmazingText`, `ExcelentText`, `PerfectText`, `IGameText` are pooled text instances.

### `Assets/Scripts/UI/View/*`
- **What:** `ViewBoosters`, `ViewMissions`, `ViewMove`, `ViewMiniGames`, `ViewAbility*`, `ViewComic`, etc. Per-instance UI representations of game elements.

### `Assets/Scripts/UI/ClickSoundButton.cs`
- **What:** `[RequireComponent(Button)]` — adds click sound to every Button it's on. Backfilled across 26 prefabs in Phase 6.

### `Assets/Scripts/UI/ScrollSwipe.cs`
- **What:** Scroll gesture handler.

### `Assets/Scripts/Util/GameTopCanvasMissions.cs` + `GameTopCanvasMove.cs`
- **What:** Self-registered Canvases. Hold mission-display + move-counter UI in-game.

---

## J. Economy

### `Assets/Scripts/Economy/Buy.cs` (base) + `BuyAbility.cs`, `BuyHealth.cs`, `BuyBooster.cs`, `BuyMove.cs`
- **What:** Plain classes. Purchase logic for each consumable type. Bootstrap-registered.

### `Assets/Scripts/Economy/EventReward.cs`
- **What:** Self-registered MonoBehaviour. Event reward distribution.

---

## K. Mini-game

### `Assets/Scripts/Interfaces/Events_and_MiniGames/MiniGame.cs`
- **What:** Abstract base for mini-games. Held in `Level.MiniGames` static list.

### `Assets/Scripts/Interfaces/Events_and_MiniGames/BubbleMiniGame.cs`
- **What:** "Pop N bubbles of color" bonus tracker. Only concrete MiniGame. Hard-coded by `GameManager.BlueBubbleMiniGame()`.

---

## L. VFX

### `Assets/Scripts/VFX/*`
- `BombMergeVFX`, `BombSpawnVFX`, `ChainBreakVFX`, `HintVFXBubbles` — visual effect MonoBehaviours triggered during gameplay.

---

## M. Util & misc

### `Assets/Scripts/Util/*`
- `CameraOrtSize.cs` — orphaned in production (only attached in `GameTest.unity`). Don't refactor.
- `CameraOrtSize`, `MillControl`, `Timeout` — small utility classes.
- `GameTopCanvasMissions`, `GameTopCanvasMove` (covered above).

### `Assets/Scripts/Mapping/*`
- Mapping helpers (small).

### `Assets/Scripts/Interfaces/*`
- `IFactory`, `IExplodable`, `IConstraint`, `IMission`, `IMiniGame`, `IGenerateObject` — interface contracts.

### `Assets/Scripts/Runtime/*`
- Empty after the 2026-05-03 cleanup pass (SpaceBlastRuntimeSpawner + EntitySpawner deleted). Folder may stay empty.

---

## N. Scenes

- `Assets/Scenes/Menu.unity` — main menu. Boots first (Build Settings index 0). Has GameBootstrap (DontDestroyOnLoad), SoundManager (DontDestroyOnLoad), MenuCanvas (MainPanel + SettingsPanel), Camera, EventSystem.
- `Assets/Scenes/GamePlay.unity` — gameplay scene. Boots when MainPanel.Play tapped. Has SceneRefs, GameManager, BoosterManager, PoolingObject, all the gameplay parents (BubbleParent/BoosterParent/Spawn/LevelParent), CameraMovement, Chains, GameWalls, GameSafeArea, GamePlayAutoLoader, GameCanvas (GamePanel — bundled in-game UI).
- `Assets/Scenes/GameTest.unity` — preserved UI prototype (you wanted to steal from later). Doesn't run post-refactor.
- `Assets/Scenes/MechanicTest.unity` — old mechanic playground. Unused.

---

## Common questions answered by file

### "What starts the game on menu?"
`MainPanel.cs` — its `OnBtnPlayGame` (wired to the Play button) calls `SceneManager.LoadScene("GamePlay")`. That's the trigger.

### "What collects the data?"
- **Persistent player state:** `UserData.cs` (Coin/Health/Trophy/Stars/PlayerLevel/Booster counts/Ability counts).
- **Per-level analytics:** `GameAnalytic.cs` — explosions per type, ability uses, end state. Saved at end of each level via `Level.OnDestroy → SaveGameAnalytic`.

### "What passes the data to in-game?"
1. `MainPanel.OnBtnPlayGame` → `SceneManager.LoadScene("GamePlay")`
2. GamePlay scene loads. Bootstrap survives via `DontDestroyOnLoad` so `UserData`/`Factory`/`LevelCatalog` are still in Singletons.
3. `_GamePlayAutoLoader.Start` → `GameManager.MakeLevel()` (no-arg)
4. `GameManager.MakeLevel()` reads `UserData.PlayerLevel`, asks `LevelCatalog.GetByPlayerLevel(N)` for the data, calls `MakeLevel(LevelData data)` overload
5. `MakeLevel(data)` instantiates `data.levelPrefab`, attaches a `Level` component, calls `Level.SetData(data)`, then `SetActive(true)` so `Level.Awake → Initialize` reads the data

### "What holds the state machine in the game?"
- `Level.cs` — primary state owner. `EndLevelType` enum (`Playing`/`NextLevel`/`LoseLevel`/`LevelCleaning`) is the master state.
- `GameManager.cs` — global game flags. Static `PanelActive` (blocks input), `UsingSkill` (blocks clicks), instance `canClick` (cooldown gate).
- Mission/Constraint completion via per-instance `MissionCompleted`/`ConstraintFailed` events. Aggregated by `Level.OnMissionCompleted` (fires `IsLevelDone(true)` when all missions done) and `Level.OnConstraintFailed` (fires `IsLevelDone(false)` on first failure).

### "What handles end-of-level?"
1. `IsLevelDone(true/false)` sets `EndLevelType`, waits 2s, queues `WaitForAllBoostersInList → PlayEndLevelAnimation`
2. `PlayEndLevelAnimation` → `TransectionPanel.StartLevelCompleteAnimation(EndLevelProcesses)` (win path) or `CanCheckMoveOut` (lose path)
3. After 2s win animation → `EndLevelProcesses` → `EndLevelBoostersGetInQueue` → `WaitForAllBoostersInList(ClearAllExplodables)`
4. `ClearAllExplodables` runs through leftover entities (~5ms each) → callback `CanCheckMoveOut`
5. `CanCheckMoveOut` reads `EndLevelType`. If `NextLevel` → `NextLevelEventInvoke()` → `UserDataManager.LevelUp()` + raises `Level.NextLevelEvent`
6. `TransectionPanel.DialogPanelNextLevel` (subscribed to `NextLevelEvent`) → `_nextLevelInfoPanel.Activate()`
7. Player taps Play on NextLevelInfoPanel → `OnButtonPlay` → `CallTransection` → `TransectionPanel.CloseTransectionOnGame` → animation event → `CloseTransectionOnGameEnd` → `MakeLevel()` → next level loads. **(This step 7 is the currently-broken bug.)**

---

## Deleted in the 2026-05-03 cleanup pass

- `Assets/Scripts/Bootstrap/LevelTestLoader.cs` (replaced by GamePlayAutoLoader)
- `Assets/Scripts/Runtime/SpaceBlastRuntimeSpawner.cs` (Phase 7.7 retirement)
- `Assets/Scripts/Runtime/EntitySpawner.cs` (orphaned — attached nowhere)
- `Assets/Editor/SpaceBlastSceneSetupTool.cs` (editor tool that referenced the deleted spawner)
- `Assets/Resources/Levels/Level-1.prefab` … `Level-100.prefab` (100 throwaway content corpses)
- `Assets/Resources/Levels/` folder (empty after prefab removal)
- Disabled `_LevelTestLoader` and `_SpaceBlastRuntimeSpawner` GameObjects from GamePlay scene

---

## How to keep this doc useful

- When you add a new MonoBehaviour or plain-class service, add a row.
- When you delete a file, move it to the "Deleted" section.
- Don't append dated entries (that's StatusUpdate's job) — edit in place.
- Keep the "Common questions" section honest. If the wiring changes, fix the answer.
