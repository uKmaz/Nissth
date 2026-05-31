# DragonBlast — Refactor Phase Plan

**Date:** 2026-05-01
**Status:** Awaiting your green light on Phase 1
**Decisions baked in (from your review of `00_ProjectReview_Findings.md`):**

- Backend URL — **deferred** (will be replaced later)
- DI — **migrate Zenject → "Global"** (your custom framework, faster)
- Levels — **delete all Level1–Level100**, build a level-creator targeting 10–20 distinct outcomes; your sample level on the scene is the only good one
- `Manager/` + `Managers/` — **consolidate to `Managers/`** (or split into purpose-specific folders if a class deserves its own)
- Singletons — **remove all** (not professional)
- Asmdefs on game code — **mandatory** (compile-cycle priority)
- Naming/quality smells — **fix most**, EXCEPT: `MyObject` stays, `Baloon`/`Balloon` typo stays
- `GameObject.Find*` — **eliminate unless 100% necessary**
- Platforms — **Android + iOS, 60fps target on both**
- Solo dev, no deadlines, no shipped users

---

## ⚠ One Blocker Up-Front

I searched the codebase for the "Global" DI framework. Nothing found:
- No folder named `Global`
- No class/namespace `Global*`
- No bindings outside Zenject (only `GameInstaller.cs`, `GameManager.cs`, `Level.cs` reference Zenject)

**Question for you:** Where does Global live? Three possibilities:
1. **In another repo / personal lib** — please point me at it (path, git URL, package). I'll import it before Phase 4.
2. **Not written yet** — we should design it before Phase 4 (or design it into the plan).
3. **It IS in the repo under a name I missed** — give me a hint and I'll wire it up.

Until this is resolved, **Phase 4 (DI migration)** is blocked. Phases 1–3 are not.

---

## Phase Sequence (in dependency order)

### Phase 1 — Cleanup & Demolition (low risk, high value)
**Goal:** Delete throwaway code, fix easy naming/structure smells. Shrinks the surface for everything that follows.

**Actions:**
1. **Delete `Assets/Scripts/Levels/Level1.cs` … `Level100.cs`** (100 files). Keep `Level.cs` (abstract base) and `LevelTypes.cs` enum if referenced elsewhere — verify first.
2. **Move root-level VFX scripts** into `Assets/Scripts/VFX/`:
   - `BombMergeVFX.cs`, `BombSpawnVFX.cs`, `ChainBreakVFX.cs`, `HintVFXBubbles.cs`
   - `EntitySpawner.cs` — moves to `Assets/Scripts/Runtime/` (already a folder there)
3. **Rename `GameAnalitic` → `GameAnalytic`** (file + class + all callers).
4. **Strip dev/Turkish debug logs** from `GameManager.cs:67` and any obvious siblings (audit, not bulk delete — confirm each).
5. **Move hardcoded event progression list** out of `GameManager.cs:74` into a serialized field or asset (cheap, no architectural change).
6. **Add namespaces to orphan files** that currently report `(none)` — e.g. `GameInstaller`, `WinSeriesManager`, `EntitySpawner`, `MillControl`, root VFX. Match the folder convention.

**Won't touch:** `MyObject`, `Baloon`/`Balloon` (per your direction).

**Verification:**
- `Axiom_Verify` compilation = CLEAN after each chunk
- Scene loads without missing-script warnings (use `reference_scanner missing_scripts`)
- Log mirror shows zero new errors

**Estimated impact:** 100+ files deleted, ~10 files renamed/moved, ~150 LoC of dev logs stripped. Cuts `Assembly-CSharp` from 296 → ~190 source files.

---

### Phase 2 — Folder & Naming Consolidation
**Goal:** Settle the `Manager/` vs `Managers/` confusion and namespace it.

**Plan to discuss before executing:**
- Move all `Manager/*` files into `Managers/`
- Subdivide `Managers/` if any class deserves its own folder, e.g.:
  - `Managers/Auth/` → `AppleAuthManagers`, `GoogleAuthManagers`, `MockAuthManager`, `IAuthenticationManager`
  - `Managers/Audio/` → `SoundManager`
  - `Managers/Camera/` → `CameraMovement`, `CinemachineShakeController`
  - `Managers/Analytics/` → `GameAnalytic`
  - `Managers/Network/` → `HttpsClientManager`, `IClient`, `ClientSpaceBlast`
  - `Managers/Game/` → `GameManager`, `BoosterManager`, `WinSeriesManager`, `LevelManager`, `UserManager`, `EventManager`
- Update namespaces to match (`Managers.Auth`, etc.)

**Asks for you:** Approve folder taxonomy before execution. I'll propose it and wait.

**Verification:** Same as Phase 1 (compile + scene load + log mirror).

---

### Phase 3 — Assembly Definitions
**Goal:** Split `Assembly-CSharp` into multiple asmdefs so single-file edits don't recompile the world.

**Proposed split (will refine with `project_cartographer` dependency map first):**

| Asmdef | Folders | Depends on |
|:---|:---|:---|
| `DragonBlast.Core` | Entities, Interfaces, Mapping, Util, Data | (none) |
| `DragonBlast.Constraints` | Constraint, Missions, Economy, MainReSource | Core |
| `DragonBlast.Entities` | Entities/Objects, GenerateObjectInLevel | Core, Constraints |
| `DragonBlast.UI` | UI, Views, BaloonCollected | Core, Constraints, Entities |
| `DragonBlast.Managers` | Managers (post-consolidation) | Core, Constraints, Entities, UI |
| `DragonBlast.Levels` | Levels (slim, post-cleanup) | all of the above |
| `DragonBlast.VFX` | VFX, SpineScripts | Core, Entities |
| `DragonBlast.Runtime` | Runtime | all |

**Risk:** circular dependencies will surface — that's the point. Each cycle = a real architectural problem to fix or break with an interface.

**Verification:**
- `script_analyzer DependencyGraph` (mode B) before splitting → produces dependency map
- Compilation clean after EACH new asmdef (don't write them all at once)
- Iteration time test: edit one Entities file, measure recompile delta

---

### Phase 4 — DI Migration (BLOCKED on Global location)
**Goal:** Replace Zenject with Global.

**Pre-requisite:** Resolve the blocker above. I need the Global package + an example binding.

**Plan once unblocked:**
1. Import Global into the project (or point to existing location)
2. Re-implement `GameInstaller` bindings in Global
3. Replace `[Inject]` from Zenject with Global's equivalent attribute on `GameManager`, `Level`, anything else using it
4. Remove Zenject package from `manifest.json` only after every reference is gone
5. Verify Spine/DOTween/Cinemachine packages don't depend on Zenject

**Verification:** Scene runs with no missing dependency exceptions; `log_mirror` clean.

---

### Phase 5 — Singleton Elimination
**Goal:** Remove `public static T Instance` patterns.

**Known offenders (from initial scan):**
- `GameManager.Instance` (at `GameManager.cs:27`)
- `UserData.Instance` (referenced from `GameManager.cs:66`)
- `Level.ActiveLevel` (static, at `Level.cs:48`) — semantically singleton but typed slightly differently
- More TBD — full audit needed

**Plan:** Replace each callsite with constructor injection via Global. After Phase 4 completes, callers are easier to migrate because the DI plumbing is in place.

---

### Phase 6 — `Find*` Audit & Replacement
**Goal:** Eliminate `GameObject.Find` / `FindGameObjectWithTag` / `FindObjectOfType`.

**Approach:**
- `script_analyzer` ApiUsageAudit (mode E) with attributeFilter for `Find*` — produces a list
- For each callsite: replace with `[SerializeField]` or DI dependency. Walk Editor scripts to wire up references via temp `[MenuItem]` setup utilities (CLAUDE.md rule 14 territory).
- Snapshot scene before, verify after (CLAUDE.md rule 13).

---

### Phase 7 — Level Creator
**Goal:** New tooling to define levels as data (ScriptableObject) and a small set (10–20) of test scenarios that exercise each gameplay outcome.

**To design with you BEFORE executing:**
- ScriptableObject schema for a level (constraints, missions, bubble rates, etc.)
- Inspector/Editor window UX for building levels
- Which 10–20 outcomes to cover — this is a gameplay-design call, not engineering. I'll list the obvious ones (each Mission type × variants) and you pick.

**Pre-req:** Phases 1, 4, 5 complete (clean DI, no singletons, levels removed).

**Deletion list (carried forward from prior phases):**
- `Assets/Resources/Levels/Level-1.prefab` … `Level-100.prefab` — corpse from Level1-100 script deletion in Phase 1; level-creator output replaces them.
- `Assets/Scripts/Runtime/SpaceBlastRuntimeSpawner.cs` — parallel runtime game loop with IMGUI test UI; reimplements spawn/move/game-over logic that belongs in the Level system. Ships in production code path. The right shape is a minimal Level prefab built via the new creator (no missions, just `MakeBubbleConstraint` + `MoveConstraint`) — same code path as production, no parallel loop. Retire this file when the level creator can produce a "test bubbles" level in seconds. (Decision: 2026-05-02. The IMGUI Start button's missing click sound surfaced the question; the deeper answer is the spawner is the wrong shape, not that the click sound is broken.)
- `Assets/Scripts/Util/CameraOrtSize.cs` — orphaned outside `GameTest.unity`; the Sky lookup it does is the only `GameObject.Find` left in the project. The proper level prefab structure under the level creator should expose camera bounds explicitly; this file goes away with that change.
- `Assets/Scripts/Levels/Level.cs` `static Level ActiveLevel` — current-active reference. Replace with proper level lifecycle ownership in the new system.

---

### Phase 8 — Mobile Performance Pass
**Goal:** Hit 60fps on mid-tier Android + iOS.

**⚠ Confirmed user-observed symptom (2026-05-04): frame spikes during big explosion cascades** (booster chain reactions, multi-bubble pops). This elevates Phase 8 priority above "wait for content" — the spikes are reproducible TODAY and affect the editor experience, not just final mobile builds. The most likely culprits are listed in step 2-3 below; profile first to confirm before fixing anything.

**Stance on ECS / DOTS (decided 2026-05-02):** Full ECS migration is rejected for this codebase. Wrong scale (~300 entities peak vs 10k+ where ECS pays off), wrong paradigm fit (event-driven branchy match-3 logic, not uniform per-frame iteration), hybrid coexistence cost with Spine/DOTween/Unity 2D physics/uGUI eats any iteration win, solo-dev migration cost is months. Instead: **profile-driven DOP discipline within MonoBehaviour, with surgical Burst/Jobs on measured hotspots only.**

**Execution staircase (in order — don't skip steps):**

1. **Profile first.** Unity Profiler attached to a real device on a worst-case level (lots of bubbles + active boosters mid-cascade). Capture: CPU per-frame breakdown, GC alloc rate, draw calls, SetPass calls, physics step time, Spine animator cost. *Don't optimize anything before you have numbers.*

2. **DOP discipline within MonoBehaviour** (cheap wins, no framework cost):
   - **`async void` audit.** This codebase uses `async void` extensively (Bubble.Explode, Booster.OnMouseDown, OnTriggerStay2D, etc.). Each continuation allocates; exceptions are silently swallowed. Replace with `Awaitable` (Unity 2023+) or proper Task-returning methods where exception flow matters.
   - **Strip LINQ from hot paths.** `.Where().ToList()` inside `Update`/event handlers/cascade detection allocates per call. Index loops + pre-allocated buffers.
   - **`OnTriggerStay2D` in Booster.** Fires every fixed step on every overlap — for booster merge detection you only need to check during the merge window. Polling beats the callback here.
   - **Pre-cache `WaitForSeconds`, `Vector3.up`, etc.** — common allocation traps.
   - **Pool more aggressively.** Spine animators already pooled; check VFX (`BombSpawnVFX`, `BombMergeVFX`, `ChainBreakVFX`, `HintVFXBubbles`) — `Instantiate`/`Destroy` per use is GC pressure.

3. **Surgical Burst/Jobs** — only on a single measured hotspot, only if the data shape fits. Most likely candidates if profiling flags them:
   - **Cascade flood-fill** (`Bubble.GetBubblesSequenceList`, `TakeThemAll`). Currently graph-walks via `GetComponent<Bubble>()` calls. A struct-of-arrays of bubble positions/types (fed by `LevelRegistry` from Phase 6) into an `IJobParallelFor` is the canonical pattern.
   - **Booster `FindObjectsToShine` / `ExplodeAllInRange`.** Multiple `Physics2D.CircleCastAll` per booster trigger; the geometry math + filtering is jobifiable on top of a `NativeArray<float3>` of positions.
   - Anything else profiling surfaces. *Don't pre-jobify.*

4. **Render pass.** `Application.targetFrameRate = 60`, vSync settings, GPU Resident Drawer, occlusion culling — audit per platform via Build Profiles. Sprite batch breaks (Spine atlases, materials), draw-call budget, shader variant count.

5. **Heavier moves** — full ECS, custom physics, render pipeline customization — only if steps 1-4 don't hold 60fps. **Don't reach here without evidence.** Most casual mobile games at 300 entities hit 60fps on the standard MonoBehaviour stack; if you can't, the diagnosis is usually a few specific hot paths, not the architecture.

**Tooling:** `physics_reporter`, `render_auditor`, Unity Profiler for capture. `Axiom_Verify` `compilation` between iterations.

**Pre-req:** Phases 1-7 complete. Profiling on a near-final game state is more useful than profiling mid-refactor.

---

## Cross-Cutting Rules I'll Follow

Per CLAUDE.md:
- Snapshot before each phase: `SceneDiff.Snapshot label: "before_phase_N"`
- `Axiom_Verify compilation` after every chunk
- Temp scripts → `Assets/Axiom/Editor/AgentBridge/Temp/`, deleted after use
- AssetDatabase for moves/renames (never raw filesystem)
- Update `AgentReports/StatusUpdate.md` at the end of each phase
- New plan file per phase if scope grows beyond what's listed here

---

## What I'd Like to Do Right Now

**Option A (recommended):** Start Phase 1 immediately. Lowest risk, highest visible payoff (delete 100+ files), validates the workflow.

**Option B:** Resolve the Global DI blocker first so Phase 4 isn't gated.

**Option C:** Something else you have in mind.

Tell me which and I'll proceed.
