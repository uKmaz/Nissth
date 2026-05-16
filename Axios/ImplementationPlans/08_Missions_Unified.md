# Phase 10 — Unified Missions (in-game + meta) + Persistence + Menu UI

**Date:** 2026-05-03
**Status:** Decisions captured. Ready to execute Phase A on green light.
**Pre-req:** Phase 9 (LevelCatalog + EOL chain + two-scene model) verified.

> Numbered "08" in the plan folder, but conceptually "Phase 10" — Phase 8 is reserved for the mobile perf pass, Phase 9 is the menu/catalog work just shipped.

---

## Why this is one combined plan, not three

Three things have to land together:

1. **Unified Mission abstraction** — collapse the existing 14 `Mission` subclasses into one runtime class + `MissionGoal` data, so adding new mission types is a 5-minute change instead of a new class.
2. **Meta missions** (daily/weekly/monthly) — a parallel runtime that consumes the same `MissionGoal` shape and listens to global game events, with persistence and time-based reset.
3. **Persistence + UI** — JSON-via-PlayerPrefs now, MessagePack-to-server later. New `MissionsPanel` in main menu showing closest-to-completion + expand-to-full-list.

Each needs the others to be useful. Bundling keeps the scope honest.

---

## Decisions captured (from 2026-05-03 conversation)

### Architecture

- **YES** to collapsing the 14 `Mission` subclasses into one runtime class + `MissionGoal` data. The per-type subclasses (`BubbleMission`, `KittenMission`, `DyeJarMission`, `LockColorMission`, `LockKeyMission`, `LanderMission`, `OreMission`, `TntMission`, `ToolBoxMission`, `CreateBoostersMission`, `PopBoostersMission`, etc.) are historical artifacts pre-dating LevelData. New "things to track" should be data + an event-handler entry, not new files.
- "Kittens / dye jars / ice are placeholders — anything can change." The unified shape must make swapping them in/out trivial.

### Persistence

- **MessagePack is the production target.** Server transport uses MessagePack; Cygnus already includes `MessagePack.dll`, `MessagePack.Annotations.dll`, `CygnusNetworking.dll` precompiled in its asmdef. Unity formatters are at `Assets/MessagePack/Unity/`.
- **Start with PlayerPrefs JSON for dev** (matches existing `UserData.SaveAllDataToPrefs` pattern using Newtonsoft). Conversion to MessagePack is mechanical: same DTOs, swap serializer.
- **Mark all persisted DTOs with both `[Serializable]` and `[MessagePackObject]` from day one.** This avoids a later annotation pass.

### Reset cadence

- **Local-device midnight for now**, server-anchored UTC later. Local is exploitable by clock-changing — accepted risk during dev.

### Rewards

- **Use existing systems only**: coins (UserDataManager.EarnCoin), boosters (Buy*.cs), abilities (UserData.AbilityCounts). No new reward types yet.
- Cosmetics / loot deferred until economy is decided.

### UI density

- **3 daily / 2 weekly / 1 monthly** active at a time.
- In-level missions display unchanged (existing top-bar `ViewMissions`).
- New `MissionsPanel` in main menu: closest-to-completion mission shown as "next up" card on top + bubble icon to expand the full list.

---

## The unifying architecture

```
                     ┌─────────────────────────────────┐
                     │  MissionGoal  (data)            │
                     │  - goalType: enum               │
                     │  - targetCount: int             │
                     │  - filter: subtype enum/string  │
                     │  - reward: RewardData           │
                     └─────────────────────────────────┘
                          ▲                       ▲
                          │ consumes              │ consumes
                          │                       │
       ┌──────────────────┴──┐           ┌────────┴───────────────┐
       │ LevelMission         │           │ MetaMission             │
       │  scope: one Level    │           │  scope: day/week/month  │
       │  in-memory only      │           │  persisted              │
       └──────────┬───────────┘           └────────┬────────────────┘
                  │                                 │
                  │ subscribes                      │ subscribes
                  ▼                                 ▼
                 ┌──────────────────────────────────────────┐
                 │  GameEvents  (static pub-sub)            │
                 │  - BubblePopped(color)                    │
                 │  - BoosterUsed(tier)                      │
                 │  - BoosterCreated(tier)                   │
                 │  - ObstacleDestroyed(type)                │
                 │  - LevelCompleted(id)                     │
                 │  - CoinGained(amount)                     │
                 │  - AbilityUsed(type)                      │
                 │  - ...                                    │
                 └──────────────────────────────────────────┘
                          ▲
                          │ raised by
                          │
       ┌──────────────────┴──────────────────────────────┐
       │  Bubble.Explode → GameEvents.BubblePopped(color)│
       │  Booster.Explode → GameEvents.BoosterUsed(tier) │
       │  Kitten.OnSaved → GameEvents.ObstacleDestroyed(Kitten)
       │  UserDataManager.EarnCoin → GameEvents.CoinGained
       │  ...                                            │
       └─────────────────────────────────────────────────┘
```

**The key insight:** in-game and meta missions are the SAME class shape; they just bind to events with different lifetimes and persistence rules.

---

## File shapes (sketches, not final code)

### Shared data

```csharp
// Assets/Scripts/Missions/MissionGoal.cs
[Serializable, MessagePackObject]
public struct MissionGoal
{
    [Key(0)] public MissionGoalType goalType;
    [Key(1)] public int targetCount;
    [Key(2)] public int filter;       // cast to BubbleColor / BoosterTier / ObstacleType etc.
    [Key(3)] public RewardData reward;
}

public enum MissionGoalType
{
    PopBubbles,           // filter = BubbleColor (or 0 for any)
    PopBoosters,          // filter = BoosterTier (or 0 for any)
    CreateBoosters,       // filter = BoosterTier
    DestroyObstacle,      // filter = ObstacleType (Kitten, Lock, DyeJar, ...)
    GainCoin,             // filter unused; targetCount = amount
    UseAbility,           // filter = AbilityType
    CompleteLevel,        // filter unused; targetCount = N
    WinSeries,            // filter unused; targetCount = consecutive wins
    // future additions go here
}

[Serializable, MessagePackObject]
public struct RewardData
{
    [Key(0)] public RewardKind kind;
    [Key(1)] public int amount;
    [Key(2)] public int subtype;     // booster tier / ability type / etc.
}

public enum RewardKind { Coin, Booster, Ability }
```

### Global event bus

```csharp
// Assets/Scripts/Missions/GameEvents.cs
public static class GameEvents
{
    public static event Action<BubbleColor> BubblePopped;
    public static event Action<BoosterTier> BoosterUsed;
    public static event Action<BoosterTier> BoosterCreated;
    public static event Action<ObstacleType> ObstacleDestroyed;
    public static event Action<int> CoinGained;
    public static event Action<AbilityType> AbilityUsed;
    public static event Action<int /*levelId*/> LevelCompleted;
    public static event Action<int /*streak*/> WinSeriesUpdated;

    public static void RaiseBubblePopped(BubbleColor c)        { BubblePopped?.Invoke(c); }
    public static void RaiseBoosterUsed(BoosterTier t)         { BoosterUsed?.Invoke(t); }
    public static void RaiseBoosterCreated(BoosterTier t)      { BoosterCreated?.Invoke(t); }
    public static void RaiseObstacleDestroyed(ObstacleType o)  { ObstacleDestroyed?.Invoke(o); }
    public static void RaiseCoinGained(int n)                  { CoinGained?.Invoke(n); }
    public static void RaiseAbilityUsed(AbilityType a)         { AbilityUsed?.Invoke(a); }
    public static void RaiseLevelCompleted(int id)             { LevelCompleted?.Invoke(id); }
    public static void RaiseWinSeriesUpdated(int n)            { WinSeriesUpdated?.Invoke(n); }
}
```

Static class. No MonoBehaviour. Lifetime = AppDomain. **Subscribers must remember to unsub** — `LevelMission.Cleanup()` already does this via the existing `Mission.Cleanup` pattern; `MetaMission` unsubs in `OnDestroy`/`OnPeriodReset`.

### Unified Mission runtime

```csharp
// Assets/Scripts/Missions/Mission.cs (refactored from existing base)
public class Mission
{
    public MissionGoal Goal { get; }
    public int Progress { get; private set; }
    public bool Completed => Progress >= Goal.targetCount;
    public event Action<Mission> ProgressChanged;
    public event Action<Mission> Completed_;

    private Action _unsubscribe;

    public Mission(MissionGoal goal) { Goal = goal; }

    public void Bind()
    {
        // dispatch on goal.goalType to the right GameEvents subscription
        switch (Goal.goalType)
        {
            case MissionGoalType.PopBubbles:
                Action<BubbleColor> h = c => { if (Goal.filter == 0 || (int)c == Goal.filter) Increment(1); };
                GameEvents.BubblePopped += h;
                _unsubscribe = () => GameEvents.BubblePopped -= h;
                break;
            case MissionGoalType.GainCoin:
                Action<int> hc = n => Increment(n);
                GameEvents.CoinGained += hc;
                _unsubscribe = () => GameEvents.CoinGained -= hc;
                break;
            // ... one case per MissionGoalType
        }
    }

    public void Cleanup() => _unsubscribe?.Invoke();

    private void Increment(int delta)
    {
        if (Completed) return;
        Progress = Math.Min(Progress + delta, Goal.targetCount);
        ProgressChanged?.Invoke(this);
        if (Completed) Completed_?.Invoke(this);
    }
}
```

One class. New mission type = add enum value + a `case` in `Bind()` + raise the corresponding event somewhere. ~5 minute change.

### LevelMission vs MetaMission distinction

```csharp
// Assets/Scripts/Missions/LevelMission.cs
// Trivial wrapper — just a Mission tied to a Level lifetime. Marker class for clarity.
public sealed class LevelMission : Mission
{
    public LevelMission(MissionGoal goal) : base(goal) { }
}

// Assets/Scripts/Missions/MetaMission.cs
[Serializable, MessagePackObject]
public sealed class MetaMission : Mission
{
    [Key(0)] public string Id;
    [Key(1)] public MissionPeriod Period;          // Daily / Weekly / Monthly
    [Key(2)] public DateTime AcquiredAtUtc;
    [Key(3)] public DateTime ExpiresAtUtc;
    [Key(4)] public bool RewardClaimed;
    // Progress is inherited; serialize too via setter.

    public MetaMission(string id, MissionGoal goal, MissionPeriod period)
        : base(goal) { Id = id; Period = period; AcquiredAtUtc = DateTime.UtcNow; ... }
}

public enum MissionPeriod { Daily, Weekly, Monthly }
```

### MetaMissionService — the meta runtime

```csharp
// Assets/Scripts/Missions/MetaMissionService.cs
public class MetaMissionService
{
    public List<MetaMission> Active { get; } = new();
    public DateTime LastDailyResetUtc;
    public DateTime LastWeeklyResetUtc;
    public DateTime LastMonthlyResetUtc;

    public void Initialize()
    {
        // Called from GameBootstrap after Singletons.Add<UserData> + Load
        CheckRollovers();
        foreach (var m in Active) m.Bind();
    }

    public void CheckRollovers()
    {
        var now = DateTime.UtcNow;
        if (now.Date > LastDailyResetUtc.Date)   { RegenerateDaily();   LastDailyResetUtc = now; }
        if (WeekStart(now) > LastWeeklyResetUtc) { RegenerateWeekly();  LastWeeklyResetUtc = WeekStart(now); }
        if (MonthStart(now) > LastMonthlyResetUtc) { RegenerateMonthly(); LastMonthlyResetUtc = MonthStart(now); }
    }

    private void RegenerateDaily() { /* clear old daily, pick 3 from MissionTemplates pool */ }
    // ... weekly, monthly

    public void Save() { UserData.SaveAllDataToPrefs(); /* MetaMissionService is part of UserData blob */ }
}
```

Cygnus singleton. Registered by `GameBootstrap`. Update tick (or Awake-only) calls `CheckRollovers` on app start (and optionally on resume from background).

### Mission templates — the data pool

```csharp
// Assets/Resources/MissionTemplateLibrary.asset (ScriptableObject)
public class MissionTemplateLibrary : ScriptableObject
{
    [SerializeField] private List<MissionGoal> dailyPool;
    [SerializeField] private List<MissionGoal> weeklyPool;
    [SerializeField] private List<MissionGoal> monthlyPool;

    public List<MissionGoal> PickDaily(int n)    { /* random / weighted from dailyPool */ }
    public List<MissionGoal> PickWeekly(int n)   { ... }
    public List<MissionGoal> PickMonthly(int n)  { ... }
}
```

Editable in Inspector. Lets you tune the mission pool per period without touching code.

### Persistence shape

`UserData` gets a new field:

```csharp
[Serializable, MessagePackObject]
public class UserData
{
    // ... existing fields ...
    [Key(40)] public List<MetaMission> MetaMissionsActive = new();
    [Key(41)] public DateTime LastDailyResetUtc;
    [Key(42)] public DateTime LastWeeklyResetUtc;
    [Key(43)] public DateTime LastMonthlyResetUtc;
}
```

Existing JSON-via-PlayerPrefs path (`SaveAllDataToPrefs`) handles serialization. **No new persistence machinery needed** — the existing blob just gets bigger. MessagePack annotations are present but inert until we swap serializer.

Mid-flight serializer swap (when backend lands):

```csharp
// Future replacement in UserData.Load / SaveAllDataToPrefs:
// JSON path:        JsonConvert.SerializeObject(this) → PlayerPrefs.SetString
// MessagePack path: MessagePackSerializer.Serialize(this) → PlayerPrefs.SetString(Convert.ToBase64String(bytes))
//                                                        OR backend POST raw bytes
```

Same DTOs. Same field IDs. Backwards-compatible.

---

## Migration phases (in dependency order)

### Phase A — Foundation: GameEvents bus + unified Mission class

**Goal:** New code path exists alongside the old. No behavior change yet.

1. Create `Assets/Scripts/Missions/GameEvents.cs` (static pub-sub).
2. Create `Assets/Scripts/Missions/MissionGoal.cs` (data + enums + `RewardData`).
3. Create new `Mission.cs` (unified class with `Bind()`/`Cleanup()` pattern). **Keep old `Mission` base + subclasses temporarily — rename old to `MissionLegacy`** so nothing breaks.
4. Wire raises in 8-10 spots: `Bubble.Explode` → `GameEvents.RaiseBubblePopped`, `Booster.Explode` → `RaiseBoosterUsed`, etc. Walk every existing concrete `MissionLegacy` subclass and identify its trigger point — that's where the raise goes.

**Verification:** Compile clean. New `Mission` class can be instantiated and bound; old chain still works untouched.

### Phase B — Migrate LevelMission

**Goal:** Per-level missions now use the unified class.

1. `LevelFactory.BuildMissions(LevelData)` constructs `LevelMission` (= `Mission`) from `LevelData.MissionConfig[]` (which already maps to `MissionGoal` shape).
2. `Level.Initialize` subscribes to each Mission's `Completed_` event for win-condition aggregation. (Same `OnMissionCompleted → IsLevelDone(true)` flow.)
3. `Level.OnDestroy` calls `Cleanup()` on each.
4. **Delete the 14 legacy Mission subclasses.** Update `LevelData.MissionConfig` if its tagged-union shape doesn't already match `MissionGoal` exactly.
5. Update existing `ViewMissions` UI to read `Mission.Progress` / `Mission.Goal.targetCount` via the new shape.

**Verification:** Run all existing LevelData (Smoke01, Smoke02). Mission counts in top-bar match. Win condition still fires.

### Phase C — MetaMissionService + persistence

**Goal:** Daily/weekly/monthly missions exist, persist, and reset on rollover.

1. `Assets/Scripts/Missions/MetaMission.cs` + `MetaMissionService.cs` + `MissionTemplateLibrary.cs` ScriptableObject + asset.
2. `UserData` extended with `MetaMissionsActive` + reset timestamps. Serialize via existing `SaveAllDataToPrefs`. Annotate fields with both `[Serializable]` and `[MessagePackObject]/[Key(N)]` for the future swap.
3. `GameBootstrap` registers `MetaMissionService`, calls `Initialize()` after `UserData.Load()`.
4. On `OnApplicationPause(false)` (resume): `MetaMissionService.CheckRollovers()`. On scene transitions: same.
5. Reward dispatch on Completed: `RewardData.kind` switch → `UserDataManager.EarnCoin` / `EarnBooster` / `EarnAbility`. Set `RewardClaimed=true`. Save.

**Verification:** Set device clock forward 24h → daily missions regenerate. Pop bubbles → daily progress increments. Restart app → progress persists.

### Phase D — MissionsPanel UI in main menu

**Goal:** Player sees and claims meta missions.

1. New `MissionsPanel.prefab` + `MissionsPanel.cs` in `Assets/Resources/UI/Panel/`. Add to MainPanel as a button-opened panel.
2. Top card: "next up" mission (highest progress %, not yet complete).
3. Expand bubble (icon button): full list of active missions, sorted by progress%.
4. Per-mission row: icon, title (`MissionGoal.goalType.ToString()` + filter description), progress bar `Progress/Target`, reward icon+amount, "Claim" button when complete.
5. `MainPanel` adds a Missions button + a small badge showing claimable count.

**Verification:** Open menu → MissionsPanel appears. Mission progress updates live (subscribe to `Mission.ProgressChanged`). Claim button increments UserData → button greys out.

### Phase E — MessagePack swap (when backend lands)

**Goal:** Replace JSON serialization with MessagePack for server transport.

1. Generate MessagePack resolver via Cygnus tooling (or whatever generator you use — flag this with backend team).
2. Swap `UserData.Load` body: `JsonConvert.DeserializeObject<UserData>(string)` → `MessagePackSerializer.Deserialize<UserData>(byte[])`.
3. Swap `SaveAllDataToPrefs` body similarly. Either base64-encode bytes for PlayerPrefs OR POST raw to server.
4. Migration shim: read JSON if present, write MessagePack going forward. One-time.

No DTO changes if we annotate correctly in Phase C.

---

## Files added / modified summary

### New files (Phase A-D)

```
Assets/Scripts/Missions/GameEvents.cs                       (static pub-sub)
Assets/Scripts/Missions/MissionGoal.cs                       (data + enums)
Assets/Scripts/Missions/Mission.cs                           (NEW unified — old becomes MissionLegacy temporarily)
Assets/Scripts/Missions/LevelMission.cs                      (marker class)
Assets/Scripts/Missions/MetaMission.cs                       (persistent variant)
Assets/Scripts/Missions/MetaMissionService.cs                (singleton)
Assets/Scripts/Missions/MissionTemplateLibrary.cs            (SO)
Assets/Resources/MissionTemplateLibrary.asset                (data asset)
Assets/Scripts/UI/Panels/MissionsPanel.cs                    (UI controller)
Assets/Resources/UI/Panel/MissionsPanel.prefab               (UI asset)
```

### Modified

```
Assets/Scripts/Bootstrap/GameBootstrap.cs                    (register MetaMissionService)
Assets/Scripts/MainReSource/UserData.cs                      (4 new fields + MessagePack annotations)
Assets/Scripts/Levels/LevelFactory.cs                        (build LevelMission instead of legacy subclass)
Assets/Scripts/Entities/Objects/Bubble.cs                    (raise GameEvents.BubblePopped)
Assets/Scripts/Entities/Objects/Booster.cs                   (raise GameEvents.BoosterUsed/Created)
Assets/Scripts/Entities/Objects/<Obstacle>.cs                (raise GameEvents.ObstacleDestroyed)
Assets/Scripts/Managers/User/UserDataManager.cs              (raise GameEvents.CoinGained on EarnCoin)
Assets/Scripts/Levels/Level.cs                               (subscribe to new Mission.Completed_)
Assets/Scripts/UI/View/ViewMissions.cs                       (read new Mission shape)
Assets/Scripts/UI/Panels/MainPanel.cs                        (add Missions button + badge)
```

### Deleted (Phase B end)

```
Assets/Scripts/Missions/BubbleMission.cs
Assets/Scripts/Missions/PopBoostersMission.cs
Assets/Scripts/Missions/CreateBoostersMission.cs
Assets/Scripts/Missions/DyeJarMission.cs
Assets/Scripts/Missions/LockColorMission.cs
Assets/Scripts/Missions/LockKeyMission.cs
Assets/Scripts/Missions/LanderMission.cs
Assets/Scripts/Missions/OreMission.cs
Assets/Scripts/Missions/TntMission.cs
Assets/Scripts/Missions/ToolBoxMission.cs
Assets/Scripts/Missions/KittenMission.cs
Assets/Scripts/Missions/MissionLegacy.cs (the renamed old base, deleted last)
```

~12 files removed. Net delta probably +400 lines (new infra), -800 lines (deleted subclasses) ≈ -400 LoC.

---

## Risks & open questions

1. **`MissionGoal.filter` is `int` for serialization simplicity.** Casts to enum at use site. Slight type-safety loss vs. polymorphic shape. Justified by being data-driven + persistable.
2. **Subscription leaks.** `Mission.Cleanup()` MUST be called on every Mission. `LevelMission` cleanup is wired through `Level.OnDestroy`. `MetaMission` cleanup must fire on rollover/regenerate. **Add a `Mission` static counter in dev builds** to assert no leaks.
3. **GameEvents is static — no Cygnus DI.** Acceptable trade for simplicity (events shouldn't depend on lifetime; the bus is conceptually app-global). Alternative: a `IGameEventBus` registered in Singletons. Decided against; static is fine for pub-sub.
4. **What raises `LevelCompleted`?** `Level.IsLevelDone(true)` in the win path. Easy.
5. **Backwards compatibility on `UserData` JSON blob.** Adding 4 fields with default values is safe — Newtonsoft tolerates missing fields. But the dev `PlayerLevel = 1` force-reset in `UserData.Load` might want to extend to "if any meta mission timestamps are 1970, regenerate". Document the dev shortcut for the reset.
6. **MissionGoal pool authoring UX.** `MissionTemplateLibrary` Inspector UX may be cumbersome with many entries. Consider per-period sub-assets (`DailyTemplates.asset`, `WeeklyTemplates.asset`) if it gets unwieldy.
7. **Reward stack overflow if Claim spams.** Add idempotency: `RewardClaimed` flag on `MetaMission` (already in shape).
8. **Existing `Mission.Cleanup()` virtual signature.** Walk every subclass; the rename + collapse must not strand event subscriptions. Phase A's "rename old to MissionLegacy" is the safety net.

---

## Cross-cutting rules per CLAUDE.md

- `Axiom_Verify compilation` after every chunk. Don't batch.
- `SceneDiff snapshot` before Phase B (where prefabs/Level wiring changes).
- Temp scripts in `Assets/Axiom/Editor/AgentBridge/Temp/`, deleted after.
- Update `AgentReports/StatusUpdate.md` per phase (chronological).
- Update `99_Architecture_Reference.md` in-place when each phase lands (mission system is load-bearing — must stay current in arch doc).
- Update `98_File_Map.md` Mission section in Phase B.

---

## Suggested execution order

1. **Phase A** (foundation) — 1 session, low risk. Compile + smoke-test.
2. **Phase B** (LevelMission migration) — 1 session, medium risk (touches every existing mission). Snapshot first.
3. **Phase C** (MetaMissionService + persistence) — 1-2 sessions. Most new code.
4. **Phase D** (UI) — 1 session. Builds on Phase C.
5. **Phase E** (MessagePack swap) — defer until backend integration. Could be its own phase.

Total: 4-5 focused sessions. Phases A and E are isolatable; B/C/D are tightly coupled.

---

## What I'd like to do right now

**Option A (recommended):** Start Phase A immediately. Lowest risk, validates the data shape and the GameEvents bus before any deletions. Existing missions keep working untouched.

**Option B:** Author `MissionTemplateLibrary` data first (decide which mission types you want in the daily/weekly/monthly pool) so Phase A has concrete examples to test against.

**Option C:** Something else.

Tell me which.
