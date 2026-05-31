# Phase 5 Closeout + Phase 6 (Find* Audit & Removal) — Plan

**Date:** 2026-05-02
**Status:** Awaiting your green light
**Pre-req:** Phase 4 verified (smoke test passed this morning — bubble click → pop → audio confirmed)

---

## Why this is one combined plan

Phase 5 was originally "Singleton Elimination," but Phase 4 absorbed essentially all of it (331 `.Instance` rewrites, 30+ singletons routed through `Global.Singletons`). The only loose end is the vendored Zenject at `Assets/Plugins/Zenject/` — a 5-minute task, not a phase. Bundling it into the front of Phase 6 keeps the implementation plan count honest.

Phase 6 is the substantive work: 24 `Find*` call sites across 12 files.

---

## PART A — Phase 5 Closeout

### A.1 — Delete vendored Zenject

**Audit result:**
- `Assets/Plugins/Zenject/` — 2.2 MB
- Compiles to its own `zenject.asmdef` (isolated DLL, was never auto-referenced into Assembly-CSharp once asmdefs landed in Phase 3)
- **Zero references from `Assets/Scripts/`** — `using Zenject;` count = 0, `using ModestTree;` count = 0
- `DragonBlast.Runtime.asmdef` no longer references Zenject (removed in Phase 4 Step 8)
- `Assets/Editor/` — `SetupSampleScene.cs` had its GameInstaller block stubbed out in Phase 4, also clean

**Action:** Delete the entire `Assets/Plugins/Zenject/` folder via `AssetDatabase.MoveAssetsToTrash` (the plural API; the singular triggered an MCP UI-confirm block in Phase 1).

**Verification:**
1. `Axiom_Verify` `compilation` → expect CLEAN
2. `Axiom_Verify` `errors` → expect 0
3. Bubble-click smoke in SampleScene (no expected change, but cheap insurance)

**Risk:** Negligible. No game code references it. The asmdef isolation in Phase 3 means it was already compile-isolated.

### A.2 — Done

That closes Phase 5. No remaining static `Instance` patterns in game code (`Level.ActiveLevel` excepted, deferred to Phase 7).

---

## PART B — Phase 6: Find* Audit & Removal

### B.1 — Audit results (recap)

| File | Line | Call | Tier |
|:---|:---|:---|:---|
| `Managers/Core/GameManager.cs` | 70 | `FindGameObjectWithTag("LevelParent")` | **Dead — delete** |
| `Managers/Core/BoosterManager.cs` | 45 | `FindGameObjectWithTag("BoosterParent")` | Tag — SceneRefs |
| `Constraint/MakeBubbleConstraint.cs` | 36 | `FindGameObjectWithTag("Spawn")` | Tag — SceneRefs |
| `Constraint/MakeBubbleConstraint.cs` | 41 | `FindGameObjectWithTag("BubbleParent")` | Tag — SceneRefs |
| `Runtime/SpaceBlastRuntimeSpawner.cs` | 71 | `FindGameObjectWithTag("Spawn")` | Tag — SceneRefs |
| `Levels/Level.cs` | 188 | `FindGameObjectWithTag("BubbleParent")` | Tag — SceneRefs |
| `Levels/Level.cs` | 189 | `FindGameObjectWithTag("BoosterParent")` | Tag — SceneRefs |
| `Levels/Level.cs` | 534 | `FindGameObjectWithTag("BubbleParent")` (dup) | Tag — SceneRefs |
| `Levels/Level.cs` | 535 | `FindGameObjectWithTag("BoosterParent")` (dup) | Tag — SceneRefs |
| `Levels/Level.cs` | 578 | `FindGameObjectWithTag("Spawn")` | Tag — SceneRefs |
| `Managers/Camera/CameraMovement.cs` | 110 | `FindGameObjectsWithTag("Obstacle")` | Tag (array) — registry |
| `GenerateObjectInLevel/DyeJarGenerate.cs` | 24 | `FindObjectsOfType<DyeJar>()` | Init — registry |
| `Managers/Camera/CameraMovement.cs` | 72 | `FindObjectsOfType<Explodable>()` | Init — registry |
| `Levels/Level.cs` | 193 | `FindObjectsOfType<LockColor>()` | Init — registry |
| `Levels/Level.cs` | 258 | `FindObjectsOfType<Bubble>()` | Mid-game — pool query |
| `Levels/Level.cs` | 511 | `FindObjectsOfType<Bubble>()` | Mid-game — pool query |
| `Levels/Level.cs` | 673 | `FindObjectsOfType<Booster>()` | Mid-game — pool query |
| `Levels/Level.cs` | 695 | `FindObjectsOfType<Explodable>()` | Mid-game — pool/registry |
| `Entities/Objects/Cannon.cs` | 56 | `FindObjectsOfType<Bubble>()` | Mid-game — pool query |
| `Entities/Objects/Ability/AbilityTornado.cs` | 180 | `FindObjectsOfType<Bubble>()` | Mid-game — pool query |
| `Constraint/MoveConstraint.cs` | 68 | `Resources.FindObjectsOfTypeAll<FiveMovesRemain>()` | Resources — registry |
| `Managers/Audio/SoundManager.cs` | 107 | `Resources.FindObjectsOfTypeAll<Button>()` | Resources — see B.5 |
| `Managers/Audio/SoundManager.cs` | 117 | `Resources.FindObjectsOfTypeAll<Button>()` | Resources — see B.5 |
| `Util/CameraOrtSize.cs` | 20 | `GameObject.Find("Sky")` | One-off — SerializeField |

**Excluded from this phase:**
- `Entities/PoolingObject.cs:72` — `transform.Find(childName)` is a child lookup on a specific Transform, not a scene-wide search. Different beast, lower priority. Leave for now.
- `MainReSource/UserData.cs:176`, `Entities/Factory.cs:51,64` — `List<T>.Find(predicate)` false positives.

### B.2 — Replacement strategies

#### Strategy 1 — `SceneRefs` container (for tagged scene-parent lookups)

Most `FindGameObjectWithTag("BubbleParent" / "BoosterParent" / "Spawn")` calls hit a small set of well-known scene-resident parent transforms. Pattern:

1. Create `Assets/Scripts/Bootstrap/SceneRefs.cs`:
   ```csharp
   namespace Bootstrap
   {
       public class SceneRefs : MonoBehaviour
       {
           [SerializeField] private Transform bubbleParent;
           [SerializeField] private Transform boosterParent;
           [SerializeField] private Transform spawn;

           public Transform BubbleParent => bubbleParent;
           public Transform BoosterParent => boosterParent;
           public Transform Spawn => spawn;

           private void Awake() => Global.Singletons.Add(this);
       }
   }
   ```
2. Add the `SceneRefs` component to the `_GameBootstrap` GameObject (or a dedicated `_SceneRefs` GO) in SampleScene. Wire the three Transform fields via inspector (one-time editor utility script).
3. Each consumer becomes:
   ```csharp
   var sceneRefs = Global.Singletons.Get<SceneRefs>();
   _bubbleParent = sceneRefs.BubbleParent;
   ```
4. Tags `BubbleParent`, `BoosterParent`, `Spawn` can stay registered (harmless) — leave for now, optional cleanup later.

**Why this and not direct `[SerializeField]` on each consumer?** Consumer count is non-trivial (8 sites). Centralizing them in one component keeps wiring sane and means one inspector field to update if a parent moves. If you'd rather see direct SerializedFields per consumer, say so and I'll switch.

#### Strategy 2 — Per-level type registry (for `FindObjectsOfType<T>()`)

For Bubble/Booster/Explodable/LockColor/DyeJar — these are spawned per level and accumulated. Two viable options:

**Option 2a — Self-registration via a `LevelRegistry` SO/instance**
- Each spawned entity (Bubble, Booster, Explodable, LockColor, DyeJar) calls `Global.Singletons.Get<LevelRegistry>().Register(this)` in `OnEnable` and `Unregister` in `OnDisable`.
- `LevelRegistry` exposes `IReadOnlyList<Bubble> ActiveBubbles`, etc.
- Bootstrap registers a fresh `LevelRegistry` per level (clears on level transition).

**Option 2b — Extend PoolingObject to expose active enumeration**
- `PoolingObject` already manages Bubble/Booster pools. Add `IEnumerable<T> GetActive<T>()` that walks the pooled instances and yields active ones.
- Consumers do `Global.Singletons.Get<PoolingObject>().GetActive<Bubble>()`.

**Recommendation: 2a.** PoolingObject is per-pooled-type, but Explodable/LockColor/DyeJar may not be pooled. A `LevelRegistry` is a uniform pattern. 2b is also fine if you prefer fewer new types.

#### Strategy 3 — Direct `[SerializeField]` (for one-offs)

- `CameraOrtSize.Sky` — single object, single consumer. `[SerializeField] Transform sky;` and wire in the inspector.

#### Strategy 4 — Special-case (SoundManager + Buttons)

`SoundManager.FindEvertButtonInGame()` and `SoundManager.UnRegistration()` use `Resources.FindObjectsOfTypeAll<Button>()` to subscribe a generic click sound to **every** Button in the project. This is a hack with two problems:

1. `Resources.FindObjectsOfTypeAll<Button>()` returns Buttons in inactive prefabs/assets too — likely subscribing the same listener to prefab assets.
2. It re-subscribes on every Registration call without idempotency check.

**Two options:**
- **Cleanest:** Custom `ClickSoundButton.cs` component. Drop on any button that should play the sound. Subscribes itself in `OnEnable` to its sibling `Button.onClick`. Phase 6 then deletes `FindEvertButtonInGame()` entirely.
- **Pragmatic:** Replace `Resources.FindObjectsOfTypeAll<Button>()` with `FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)` (Unity 2023+) — same broken design but using non-Resources API. Doesn't actually fix the code smell. Skip.

**Recommendation: cleanest option, but split into a sub-task.** This is the only call where the right answer is "redesign the feature," not "replace Find with serialize." If you want the redesign, it adds ~1 hour. If you'd rather defer the SoundManager Buttons refactor and just fix the other 22 call sites, I'll mark this one as a known TODO and move on.

#### Strategy 5 — Delete

- `GameManager.cs:70` — `_levelParent` field is dead. Delete the line + the field. Leave the `LevelParent` tag in TagManager (harmless).

### B.3 — Execution order

Sequenced to make verification cheap (each step is independently testable in SampleScene):

| Step | Action | Files touched | Verify |
|:---|:---|:---|:---|
| 1 | Delete `_levelParent` dead field + Find call | GameManager.cs | Compile + smoke |
| 2 | Create `SceneRefs.cs`, place GO in SampleScene, wire inspector refs | new file + scene | Compile + scene loads |
| 3 | Migrate the 8 tag-Find call sites (BubbleParent/BoosterParent/Spawn) | 5 files | Compile + smoke |
| 4 | Migrate `CameraOrtSize.Sky` to SerializeField | 1 file + scene | Compile + smoke |
| 5 | Create `LevelRegistry.cs`, register in Bootstrap | new file | Compile |
| 6 | Add OnEnable/OnDisable register/unregister to Bubble, Booster, Explodable, LockColor, DyeJar, FiveMovesRemain | 6 files | Compile + smoke (quick play test — bubbles still pop) |
| 7 | Migrate the 9 `FindObjectsOfType<T>()` + `FiveMovesRemain` call sites to LevelRegistry | 5 files | Compile + smoke |
| 8 | Migrate `CameraMovement` `FindGameObjectsWithTag("Obstacle")` (decide if Obstacles go in LevelRegistry too, or stay tagged + lazy) | 1 file | Compile + smoke |
| 9 | (optional) SoundManager Buttons refactor — defer or do now per your call | 1+ files | Compile + smoke + click any button |

After all steps: full play test — start level, pop bubbles, use boosters/abilities, complete level, restart.

### B.4 — Out of scope (deferred)

- `transform.Find(childName)` in `PoolingObject.cs:72` — child-name lookup, low risk, defer.
- `Level.ActiveLevel` static — deferred to Phase 7's level-creator rewrite.
- Removing tags from TagManager that no longer have Find consumers — cosmetic, harmless to leave.

### B.5 — Open questions for you

1. **SceneRefs vs per-consumer SerializedField** — I propose SceneRefs (centralized). OK?
2. **LevelRegistry vs extending PoolingObject** — I propose LevelRegistry (uniform). OK?
3. **SoundManager Buttons** — fix now (Strategy 4 cleanest option) or defer with a TODO?
4. **CameraMovement `FindGameObjectsWithTag("Obstacle")`** — Obstacles into LevelRegistry, or keep as a one-time lookup on Initialize? (Need to know if obstacles spawn dynamically mid-level — I'd bet not.)

---

## Cross-cutting

Per CLAUDE.md:
- Snapshot SampleScene before Step 1: `SceneDiff.Snapshot label: "before_phase6"`
- `Axiom_Verify compilation` after each step
- Update `AgentReports/StatusUpdate.md` at the end (append-only, dated section)

---

## What I'd like to do right now

Get your answers to the four open questions (or a "use your judgment, just go"), and I'll execute Part A + Part B Steps 1–4 in one push (those are the safe ones with no design decisions left). Hold for your call on Steps 5+ if you want to weigh the LevelRegistry shape first.
