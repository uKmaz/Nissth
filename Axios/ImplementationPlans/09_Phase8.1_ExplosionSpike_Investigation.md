# Phase 8.1 — Explosion-Spike Investigation (profile-first)

**Date:** 2026-05-04
**Status:** Plan — awaiting your green light to start
**Trigger:** User-confirmed frame spikes during big booster cascades / multi-bubble pops (2026-05-04)
**Pre-req:** Phase 10 missions wiring complete (08a) so testing isn't blocked on UI

> **Phase 8.1, not full Phase 8.** Phase 8 is the broader mobile-performance pass (build profiles, render audits, draw-call budget, etc.). 8.1 is **just the explosion-spike hunt** — narrow scope, fast turnaround, fixes the most visible degradation today. The rest of Phase 8 still waits for full content + real-device profiling.

---

## The discipline (from CLAUDE.md + the project's optimization stance)

1. **Profile first. Always.** No code changes before there's a number. The "obvious" hotspot is wrong about half the time.
2. **One hotspot at a time.** Fix → re-profile → confirm the spike moved. Don't bundle fixes; you'll lose attribution.
3. **DOP discipline within MonoBehaviour.** No ECS migration. Surgical fixes only.
4. **The candidates list below is a pre-flight ranking, NOT a fix list.** If profiling surfaces something else, fix that instead.

---

## Step 1 — Set up the worst-case test scenario

**Goal:** A reproducible, repeatable cascade that triggers the spike consistently.

1. Author or pick a level that maximizes spike likelihood:
   - High `maxBubbleCount` (40+)
   - High `bubbleRates` for one or two colors (so big single-color clusters form)
   - Optional: a `Booster` spawn pre-placed in the scene OR easily creatable via 4+ pop
2. **The trigger to measure**: tap a bubble in a 6+ cluster that triggers a cascade, then have at least 2 boosters chain-explode after.
3. Record the trigger sequence so you can re-run identically. (Note the bubble position, the moves leading to it, etc.)

**Verification:** can you reproduce the spike on demand 3 times in a row?

---

## Step 2 — Capture the profile

Three data sources, captured in this order:

### 2a. Unity Profiler — CPU + GC

1. **Window → Analysis → Profiler.** Enable: CPU Usage, Memory, Rendering, Audio.
2. **Switch CPU Usage to "Hierarchy" view, sort by Total ms descending.**
3. **Enable Deep Profile.** (Toolbar button — slows the editor down but gives per-method attribution. CRITICAL for finding the culprit.)
4. Hit **Record**.
5. Trigger the cascade in the editor.
6. Stop recording.
7. **Find the spike frame** — should be a tall yellow bar in the timeline. Click it.
8. **Capture these numbers** in a notepad:
   - Total frame time (ms)
   - Top 5 contributors by Total ms
   - GC.Alloc total for the frame (KB)
   - Top 3 GC alloc sources (PlayerLoop > whatever)
   - Whether `Physics2D.Step` or `Spine.Animator` shows up unusually high

### 2b. Profiler — Memory module

1. Switch to the **Memory** tab.
2. Take a snapshot before the cascade.
3. Trigger cascade.
4. Take snapshot after.
5. Diff the two — look for sudden List<>, string, AsyncStateMachine, or Closure allocations.

### 2c. Build a representative log

Run with:
- `Application.targetFrameRate = 60`
- `QualitySettings.vSyncCount = 0` (so frame time isn't quantized to vsync intervals)

---

## Step 3 — Read the data and pick ONE hotspot

Match what you see to the candidates below (ranked by my prior of likelihood, NOT certainty).

### Candidate A — `async void` allocation per bubble pop

**Code location:** `Bubble.cs:264` `public override async void Explode(bool isSilent = false)`

**Profile signature:**
- High `AsyncTaskMethodBuilder.Start` cost
- High `AsyncStateMachineBox` allocations per cascade frame
- Total GC alloc roughly proportional to bubble count in cascade

**Cost model:** ~50 bytes per `async void` invocation for the state machine box, plus ~150-300 bytes for the closure if it captures `this`. 50 bubbles in cascade = ~10-17 KB GC pressure for what should be a one-frame mass-pop. GC kicks in mid-frame → spike.

**Surgical fix:** Convert `Bubble.Explode` to plain `void`. The single `await Task.Delay(1000)` at the end (for VFX duration) becomes a `StartCoroutine` with `yield return new WaitForSeconds(1f)` (caching the WaitForSeconds singleton). Or move the deactivate-after-VFX into a Unity scheduling primitive (DOVirtual.DelayedCall + cached tween already-imported via DOTween).

**Risk:** Low. Existing callers don't await Bubble.Explode anywhere we depend on; the async signature is decorative.

### Candidate B — LINQ + List allocation in cascade walk

**Code location:** `Bubble.GetBubblesSequenceList` / `Bubble.TakeThemAll` (the recursive sequence-finder)

**Profile signature:**
- High `Enumerable.Where`, `List`, `Linq.OfType` time
- Many `List<Bubble>.ctor` allocations
- High recursion depth in CPU sample

**Cost model:** Each recursive step allocates a new filtered List. 50-bubble cascade = 50 list allocations. Compounds with the async void cost.

**Surgical fix:** Pre-allocate one `List<Bubble>` as a static reusable buffer, clear it at the start of each cascade walk, reuse for the walk. Replace `.Where(...).ToList()` with index-loop `for (int i=0; i<list.Count; i++) if (predicate) buffer.Add(...)`.

**Risk:** Medium — the recursive walk is touchy; verify cascade behavior unchanged after refactor.

### Candidate C — `GetComponent<Bubble>` per neighbor per step

**Code location:** Wherever the cascade walk dereferences neighbors (likely `Bubble.cs` around `ClosestBubbleList` iteration)

**Profile signature:**
- High `GameObject.GetComponent` time
- Repeated component lookups in CPU sample

**Cost model:** Each `GetComponent<T>` is O(1) but ~200ns. With 50 bubbles × ~6 neighbors each × multiple walk steps = thousands of GetComponent calls per cascade.

**Surgical fix:** Cache the `Bubble` reference on each neighbor at `OnTriggerEnter2D`. `ClosestBubbleList` already stores `Explodable` — change to store both the Explodable AND its cached Bubble component. Or just cache `Bubble this` once on `Awake` and use that.

**Risk:** Low.

### Candidate D — `OnTriggerStay2D` in Booster

**Code location:** `Booster.cs` (the merge-detection trigger)

**Profile signature:**
- High `Physics2D.OnTriggerStay` time
- Many `OnTriggerStay2D` callbacks per fixed step
- Worse with multiple boosters on screen mid-cascade

**Cost model:** `OnTriggerStay2D` fires every FixedUpdate (50 Hz default) for every overlap. With 3 boosters × 4 nearby objects = 12 callbacks per fixed step = 600/sec.

**Surgical fix:** Switch to `OnTriggerEnter2D` + `OnTriggerExit2D` with a bool latch ("am I currently overlapping mergeable booster?"). Only check the actual merge condition when the overlap STATE changes, not every step.

**Risk:** Medium — merge detection is a core mechanic; verify merge still works in all corner cases.

### Candidate E — VFX `Instantiate` per use

**Code location:** `BombSpawnVFX.cs`, `BombMergeVFX.cs`, `ChainBreakVFX.cs`, `HintVFXBubbles.cs`

**Profile signature:**
- High `Object.Instantiate` time during cascade
- High `GameObject.SetActive` time during cascade
- Spike correlated with booster explosions specifically (not generic bubble pops, since Bubble VFX was already pooled in the Phase 9-era polish)

**Cost model:** Each Instantiate is ~0.1-0.5 ms on mid-tier mobile + GC alloc for the GameObject + components. 5+ booster VFX per cascade = 0.5-2.5ms just for instantiation.

**Surgical fix:** Pre-pool each VFX type via `PoolingObject`. Same pattern as Bubble VFX (renderer/collider toggle, SetActive false on completion).

**Risk:** Low — pooling pattern already established in the codebase.

### Candidate F — `ReMakeBubble` flood after cascade

**Code location:** `MakeBubbleConstraint.ReMakeBubble` (called per bubble destruction)

**Profile signature:**
- High `PoolingObject.GetObjectFromPool` time
- Spike right at the END of the cascade, not during it

**Cost model:** 50 bubbles destroyed → 50 ReMakeBubble calls in the same frame → 50 pool fetches + 50 SetActive calls + 50 physics body initializations.

**Surgical fix:** Batch spawns over 2-4 frames. Push to a queue, drain N/frame instead of all-at-once.

**Risk:** Medium — visual gameplay change (bubbles trickle in instead of refilling instantly). User may want to keep the instant refill, in which case this candidate stays.

---

## Step 4 — Apply the fix

Pick the **single highest-impact hotspot** from the profile. Don't speculate; fix what the data shows.

For each fix:
1. **Snapshot scene** before via SceneDiff (CLAUDE.md rule 13).
2. Make the change.
3. **Verify compile** via `Axiom_Verify`.
4. **Re-profile** the same trigger sequence from Step 1.
5. Compare before/after numbers (record both in `AgentReports/StatusUpdate.md` so we have a paper trail).
6. Ship the fix only if the spike measurably improved. If not, revert and pick the next candidate.

---

## Step 5 — Stop when 60fps holds during the cascade

You're done with 8.1 when:
- Spike frame time ≤ 16.6 ms (60fps budget) on the editor profile of the worst-case cascade.
- GC alloc per cascade frame is below ~5 KB (above that, future GC sweeps cause pauses elsewhere).

Anything beyond that → Phase 8.2 (full mobile pass) when you're ready to deploy to a device.

---

## What you should NOT do in 8.1

- **Don't pre-jobify.** Burst/Jobs are step 3 of the Phase 8 staircase, not 8.1. They require specific data shapes (NativeArray<T>) and surface bugs that take days to debug. Wait for measured evidence that DOP-in-MonoBehaviour isn't enough.
- **Don't migrate to ECS.** Decided 2026-05-02. See `01_Refactor_Phases.md` Phase 8 stance.
- **Don't refactor for elegance during the perf pass.** Surgical only. The "right" architectural fix takes weeks; the "right" perf fix takes hours and you can refactor later when you're not under measurement.
- **Don't trust IDE / Unity console / your gut.** Trust the Profiler timeline.

---

## Files this will likely touch (best-guess scope)

If candidates A + B + C are the actual culprits (most likely), the diff is roughly:

- `Assets/Scripts/Entities/Objects/Bubble.cs` — Explode signature, cascade walk LINQ removal, cached neighbor refs
- Potentially `Assets/Scripts/Entities/Explodable.cs` — add cached Bubble ref if helpful
- Maybe 1 helper file — a static `CascadeBuffer` class with reusable List<Bubble>

Total LoC change: ~50-150. NOT a refactor; targeted edits.

---

## Tooling cheatsheet

- **Unity Profiler:** Window → Analysis → Profiler. Deep Profile toggle is critical.
- **Frame Debugger:** Window → Analysis → Frame Debugger. Shows draw call breakdown if Render is the spike (less likely for cascade spikes).
- **Memory Profiler package:** if installed, gives detailed alloc traces.
- **Axiom `physics_reporter`** mode A — collider census on the cascade scene, in case Physics is the cost.
- **Axiom `log_mirror`** mode E — profiler spikes log (catches EditorOnly profile markers).

---

## Suggested start

1. Wire 08a missions UI → confirm the regression hasn't crept in.
2. Build the worst-case test scene (Step 1).
3. Take **one** profile pass — paste the top-5 by Total ms here so we can identify the culprit together before any code changes.
4. We'll pick the candidate together based on what the data shows.

Tell me when you're ready to profile.
