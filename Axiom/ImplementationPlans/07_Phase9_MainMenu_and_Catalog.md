# Phase 9 — Main Menu + Level Catalog + End-of-Level Flow

**Date:** 2026-05-02
**Status:** Awaiting your sign-off on a few decisions, then I execute
**Pre-req:** Phase 7.4 verified — data-driven level loading works end-to-end

> **Why "Phase 9":** Phase 8 is reserved for the mobile perf pass. This work is its own thing — the boot/menu/catalog/progression layer that turns a runnable test into a playable game.

---

## Why this is one combined plan, not three

Three things have to land together for "playable game":

1. **A level catalog** — without it, "Start" can only load one hardcoded level and "Levels" has nothing to display.
2. **A main menu** — without it, the game can only be entered via dev scaffolding.
3. **Level-end → next-level wiring** — without it, completing level 1 doesn't trigger level 2.

Each needs the other two to be useful. Bundling them into one plan keeps the scope honest.

---

## What's already there (good news)

A pre-existing UI layer is sitting almost-intact in the codebase:

- **`Assets/Resources/UI/Panel/MainPanel.prefab`** — full menu prefab with art, buttons, top-bar resource displays. `MainPanel.cs` controller has Health/Coin/Trophy displays, Play button, Settings button, Comic-story buttons, resource-collect animations. **Most of what you want is already authored.**
- **`Assets/Resources/UI/Panel/SettingsPanel.prefab`** + `SettingsPanel.cs` — sound/music/vibration toggles wired through `SoundManager`. Works as-is.
- **`Assets/Resources/UI/Panel/ConsumableItemPanel.prefab`** — pre-game "select boosters before starting" panel. The current Play button opens this.
- **`Assets/Resources/UI/Panel/TransectionPanel.prefab`** + `TransectionPanel.cs` — animated level→level transition. 4 sites in TransectionPanel call `GameManager.MakeLevel()` (currently empty no-op).
- **`Assets/Scenes/GameTest.unity`** — has level-grid UI ("Levels" GameObject with numbered children 1-15) you flagged for stealing. Currently broken post-refactor; can salvage assets/layout.

The MainPanel buttons currently are: **Play, Settings, Comic, Comic Book**. We need to **add: Levels, Missions, IAP, Ad-for-coins.**

---

## What needs building / re-wiring

### A. Level catalog (foundation)
- New ScriptableObject `LevelCatalog` (`Assets/Scripts/Levels/LevelCatalog.cs`): holds `List<LevelData> levels`. Lookup by `levelId` or by index.
- Single instance asset: `Assets/Resources/LevelCatalog.asset`. Loaded via `Resources.Load<LevelCatalog>("LevelCatalog")`.
- Seed with `LevelData_Smoke01` as level 1.
- Bootstrap registers it: `Global.Singletons.Add(catalog)`.

### B. Wire `GameManager.MakeLevel()` (no-arg) to actually load
- Currently empty stub. Make it: `MakeLevel(catalog.GetByIndex(UserData.PlayerLevel - 1))`.
- This automatically wires the 4 TransectionPanel callers that have been silent since Phase 1.

### C. Wire end-of-level → next-level
- When all `Mission.MissionCompleted` events have fired (and any `MoveConstraint.ConstraintFailed` hasn't), trigger Level.IsLevelDone(true).
- Existing `EndLevelProcesses` chain → eventually `UserDataManager.LevelUp()` → `TransectionPanel` animation → `MakeLevel()` (now wired) → next level loads.
- Validate the chain works end-to-end by completing the test level and seeing level 2 (= same level for now since catalog has only 1).

### D. Salvage + extend the main menu
- Place `MainPanel.prefab` into SampleScene as a top-level Canvas overlay (or use BaseCanvas).
- Add 4 new buttons next to existing Play/Settings: **Levels, Missions, IAP, Ad**.
- Wire:
  - **Play** — already opens ConsumableItemPanel which already handles the "start playing" flow. Verify chain still works after our wiring.
  - **Settings** — already opens SettingsPanel. Verify.
  - **Levels** — new. Opens a new `LevelSelectPanel` showing a scrollable grid of buttons by level id; click loads via MakeLevel.
  - **Missions, IAP, Ad** — stub. Each opens a "Coming soon" toast (uses existing `DialogOkay` pattern — there's a `DialogOkay.cs` controller already).
- Replace `LevelTestLoader` (test scaffolding) with the real menu flow. Disable or remove its GameObject.

### E. Onboarding (first-launch welcome)
- `UserData.PlayerLevel == 1` AND a "hasOnboarded" PlayerPref isn't set → show onboarding splash (one screen: "Welcome to DragonBlast" + "Tap to play"). On tap → set the pref, hide splash, show MainPanel.
- Skip this for now if it's complex; defer to its own pass.

---

## Decisions to make

I'll execute autonomously after your sign-off. My recommendation in **bold** for each.

### D1. Scene model

- **A) Single scene (SampleScene → rebrand "Game"). MainMenu is a Canvas overlay panel. Activate when not in a level, deactivate during play.**
- B) Two scenes: MainMenu.unity + Game.unity. SceneManager.LoadScene between them.

**My pick: A.** Mobile-friendly (no scene-load hitch), simpler data passing (no DontDestroyOnLoad), reuses the existing Bootstrap/SceneRefs. The existing MainPanel.prefab is already designed to be a Canvas overlay, not a standalone scene.

### D2. UI source

- **A) Salvage MainPanel.prefab. Add 4 new buttons (Levels, Missions, IAP, Ad). Salvage SettingsPanel.prefab as-is.**
- B) Build fresh from scratch.
- C) Salvage from GameTest.unity (which already has a Levels grid).

**My pick: A + raid GameTest for the Levels grid layout/assets.** MainPanel has 80% of what we need. GameTest's Levels grid is the missing 20%. Building fresh wastes the existing authored work.

### D3. Onboarding

- A) Single welcome splash on first launch ("Welcome — tap to play"). Set PlayerPref, never show again.
- **B) Skip onboarding for this phase. Add later if needed.**

**My pick: B.** Keeps scope tight. Most casual blast games work fine without it; the value is marginal vs build cost. Add a "first-launch tutorial" pass later if it surfaces as needed.

### D4. Levels panel UX

- **A) Scrollable grid of square buttons labeled by level number. Locked levels (id > UserData.PlayerLevel) shown grayed out.**
- B) Mini-map style (Candy Crush) with paths between levels.
- C) Vertical list.

**My pick: A.** Standard, easy to author, scales to 5000 levels without issue. Mini-map is gameplay-design level work that belongs in a polish pass after content exists.

### D5. Stubbed buttons (Missions, IAP, Ad)

- **A) Visible buttons. Click → DialogOkay popup with "Coming soon."**
- B) Visible but disabled (grayed out).
- C) Hidden until each feature ships.

**My pick: A.** Visible-and-clickable communicates "this is part of the game" without committing to behavior. Hidden buttons require remembering to show them later.

### D6. Catalog discovery

- **A) Single `LevelCatalog.asset` at `Resources/LevelCatalog.asset`. Bootstrap loads + registers it.**
- B) Walk Resources/Levels/ and auto-register all LevelData assets found.
- C) Per-event/world catalog hierarchy (later concern).

**My pick: A.** Explicit, predictable, easy to hand-author. Auto-discovery sounds nice but adds order ambiguity (what's level 1 vs level 2?). With (A), the catalog explicitly orders levels.

---

## Sub-phases (assuming all "my pick" answers above)

Execution breaks down into ~5 sub-phases, each independently verifiable:

### 9.1 — Level catalog foundation
- Author `LevelCatalog.cs` SO + create asset with LevelData_Smoke01 as level 1
- Register in GameBootstrap
- Wire `GameManager.MakeLevel()` (no-arg) to load `catalog[UserData.PlayerLevel - 1]`
- Smoke: verify `GameManager.MakeLevel()` (no args) loads the test level

### 9.2 — End-of-level → next-level wiring
- Confirm `Level.IsLevelDone(true)` fires when missions complete
- Trace the chain: IsLevelDone → EndLevelProcesses → TransectionPanel animation → UserDataManager.LevelUp → MakeLevel() → next level
- Likely needs to wire mission-complete aggregation if not already there
- Smoke: complete the test level → verify it tries to load level 2 (same level since catalog has 1 entry)

### 9.3 — MainPanel into SampleScene
- Place MainPanel.prefab as Canvas overlay
- Verify existing Play / Settings / Comic buttons work (or fix what's broken)
- Disable LevelTestLoader (its job is now done by MainPanel.Play button)
- Smoke: see MainPanel on play, click Play → goes through to ConsumableItemPanel → game starts

### 9.4 — New buttons: Levels + stubs
- Add 4 buttons to MainPanel (or in a sub-panel if MainPanel is too cramped)
- Author LevelSelectPanel.prefab + LevelSelectPanel.cs (scrollable grid of level buttons)
- Wire Levels → opens LevelSelectPanel, click level → MakeLevel(catalog[id])
- Stub Missions/IAP/Ad → DialogOkay "Coming soon"
- Smoke: all 6 buttons work as designed

### 9.5 — Cleanup + handoff
- Verify the loop: menu → play → complete level → return to menu → see incremented PlayerLevel → play next level
- Update StatusUpdate + arch reference doc
- User adds 10-20 LevelData assets to the catalog (their own next step)

---

## Out of scope (deferred)

- Onboarding splash (D3 picked B)
- Rich animations on the new Levels/Missions/IAP/Ad buttons (basic transitions only)
- Mini-map level select UX (D4 picked A)
- Real IAP integration, real ad SDK (the buttons are stubs)
- Mission types beyond what LevelData already supports
- Any backend / leaderboard / cloud save (all deferred indefinitely per game shape memo)
- Phase 7.5 custom inspector — separate concern; can come after this if authoring 10-20 levels in the default inspector is painful

---

## Open questions for you (the 3 that need a real answer)

1. **D1-D6 — confirm "all my picks" or call out specific changes.** If you want to override on any, say which.
2. **Existing MainPanel ConsumableItemPanel flow** — `MainPanel.OnBtnPlayGame` opens `ConsumableItemPanel` (a pre-game booster-select panel). Keep that flow (player picks consumables before each level) or simplify (Play → straight to gameplay)? My recommendation: **keep it**, since it's already authored and gives players a choice point. But if you want a faster flow, say so.
3. **Catalog seed** — for the first release of this menu, the catalog has just `LevelData_Smoke01` as level 1. Do you want me to also create a placeholder Level 2 (a duplicate of Smoke01) so the level-end-to-next-level transition has somewhere to go for verification? My pick: **yes**, makes the smoke test meaningful.

---

## What I'd like to do right now

Get answers to the 3 questions, then execute 9.1 → 9.2 → 9.3 → 9.4 → 9.5 in sequence. Each sub-phase compile-verified, smoke-tested where possible. Updating the arch doc + StatusUpdate as I go.

---

## 9.3.5 — Course correction: two-scene split (Menu + GamePlay)

**Date added:** 2026-05-02 night, after 9.3 wired
**Reason:** User course-corrected D1 (single-scene). Two scenes is the right shape for this game; my D1 pick was the lazy choice. Memory saved as `feedback_two_scene_model.md`.

### What changes

**Scene model (replaces D1):**
- `Assets/Scenes/Menu.unity` — new scene. Contains: Camera, EventSystem, GameBootstrap (DontDestroyOnLoad), SoundManager (DontDestroyOnLoad), MenuCanvas with MainPanel.prefab + SettingsPanel.prefab.
- `Assets/Scenes/GamePlay.unity` — current SampleScene renamed. Contains: all current game stuff minus the MenuCanvas I added in 9.3. Plus a new auto-loader script that calls `MakeLevel()` on Start.
- Build Settings: Menu at index 0, GamePlay at index 1.

**Persistent vs scene-specific:**
- **DontDestroyOnLoad'd:** GameBootstrap (so plain-class services UserData/Factory/etc. persist), SoundManager (so audio continues seamlessly).
- **Menu scene only:** MenuCanvas (MainPanel + SettingsPanel + future LevelSelectPanel).
- **GamePlay scene only:** SceneRefs, GameManager, BoosterManager, PoolingObject, CameraMovement, EventReward, GamePanel (in-game UI panels), BubbleParent / BoosterParent / Spawn / LevelParent / GameWalls / Chains / GameSafeArea.

**Scene transition:**
- Menu → GamePlay: `SceneManager.LoadScene("GamePlay")` from MainPanel.OnBtnPlayGame
- GamePlay → Menu (later, deferred): `SceneManager.LoadScene("Menu")` from a back button or end-of-level path

### Code changes for 9.3.5

1. `GameBootstrap.Awake` — add `DontDestroyOnLoad(gameObject);`
2. `SoundManager.Initialize` — add `DontDestroyOnLoad(gameObject);`
3. `MainPanel.OnBtnPlayGame` — replace `TransectionPanel.CloseTransectionOnOnMenu()` with `SceneManager.LoadScene("GamePlay");`
4. New `Assets/Scripts/Bootstrap/GamePlayAutoLoader.cs` — drops on a GamePlay-scene GO; calls `GameManager.MakeLevel()` on Start (replaces the inline LevelTestLoader for the GamePlay-scene flow).

### Scene work for 9.3.5

I'll do via editor utility:
1. Rename `SampleScene.unity` → `GamePlay.unity` via AssetDatabase.RenameAsset
2. Open GamePlay scene, **remove** the MenuCanvas I added in 9.3 (it'll move to Menu)
3. Create empty `Menu.unity`
4. In Menu: add Camera, EventSystem, MenuCanvas with standalone MainPanel.prefab + SettingsPanel.prefab, GameBootstrap GO, SoundManager GO (copied from GamePlay)
5. In GamePlay: keep GameBootstrap there for cold-load case (someone opens GamePlay scene directly in editor); add GamePlayAutoLoader GO
6. Update Build Settings: Menu (0), GamePlay (1)
7. Set Menu as the default-loaded scene

### Cleanup later (deferred)

- **Singletons.Remove on OnDestroy** for scene-specific MonoBehaviours (SceneRefs, GameManager, BoosterManager, etc.) — needed for clean Menu→GamePlay→Menu→GamePlay re-entry. Without it, the second GamePlay load throws "already registered" on its own self-Add. **Defer until pause-to-menu or end-of-level-to-menu paths are wired.**
- **MainPanel's other dependencies** (NotHealthPanel, ComicPanel sub-references) — same as before, mostly tolerated since user won't trigger those paths in initial testing.

### Open question for the user

The standalone `MainPanel.prefab` (at `Assets/Resources/UI/Panel/MainPanel.prefab`) is what I'll use in Menu scene. It depends on SerializeField references to ConsumableItemPanel, NotHealthPanel, ComicPanel, ViewMiniGames — all of which are inside GamePanel. Used standalone, those refs will be **null in Menu**.

For 9.3.5 minimum scope: leave them null, accept that Comic/MiniGame paths will NRE if tapped. Only Play and Settings need to work. Tell me if I should do something different (e.g., disable those buttons via inspector to prevent tapping).
