# Phase 2 — Folder & Namespace Consolidation

**Date:** 2026-05-01
**Status:** Awaiting taxonomy approval
**Goal:** Eliminate the `Manager/` vs `Managers/` split. Group classes by purpose. Unify the `Manager` (singular) namespace into `Managers` (plural). Defer subfolder namespaces and asmdef boundaries to Phase 3.

---

## Current State

```
Assets/Scripts/Manager/   (10 files, namespace = "Manager")
├── AppleAuthManagers.cs           [namespace Manager]
├── BoosterManager.cs              [namespace Manager]
├── CameraMovement.cs              [namespace Manager]
├── CinemachineShakeController.cs  [namespace Manager]
├── GameAnalytic.cs                [no namespace]
├── GameManager.cs                 [namespace Manager]
├── HttpsClientManager.cs          [namespace Manager]
├── MockAuthManager.cs             [namespace Manager]
├── SoundManager.cs                [namespace Manager]
└── UserDatasManager.cs            [no namespace]

Assets/Scripts/Managers/  (10 files, namespace = "Managers" mostly)
├── EventManager.cs                [namespace Managers]
├── GameInstaller.cs               [no namespace]
├── IAuthenticationManager.cs      [no namespace]
├── IClient.cs                     [no namespace]
├── IEventService.cs               [no namespace]
├── ILevelService.cs               [namespace Managers]
├── IUserService.cs                [namespace Managers]
├── LevelManager.cs                [namespace Managers]
├── UserManager.cs                 [namespace Managers]
└── WinSeriesManager.cs            [no namespace]
```

Two separate folders, two separate namespaces (`Manager` and `Managers`), 7 files with no namespace at all. Confusing. Consolidating now.

---

## Proposed Final Layout

All under `Assets/Scripts/Managers/` with purpose-based subfolders:

```
Assets/Scripts/Managers/
├── Auth/
│   ├── IAuthenticationManager.cs
│   ├── AppleAuthManagers.cs
│   └── MockAuthManager.cs
├── Audio/
│   └── SoundManager.cs
├── Camera/
│   ├── CameraMovement.cs
│   └── CinemachineShakeController.cs
├── Analytics/
│   └── GameAnalytic.cs
├── Network/
│   ├── HttpsClientManager.cs
│   └── IClient.cs
├── Core/
│   ├── GameManager.cs
│   ├── BoosterManager.cs
│   └── GameInstaller.cs       # Will be replaced by Cygnus bootstrap in Phase 4
├── Levels/
│   ├── LevelManager.cs
│   └── ILevelService.cs
├── User/
│   ├── UserManager.cs
│   ├── UserDatasManager.cs    # See "Open question" below re: typo
│   └── IUserService.cs
└── Events/
    ├── EventManager.cs
    ├── IEventService.cs
    └── WinSeriesManager.cs
```

**Rationale notes:**
- `WinSeriesManager` lives in `Events/` because it subscribes to `Level.NextLevelEvent` and tracks event progression. Could equally go in `Core/` — your call.
- `BoosterManager` is in `Core/` because it's a runtime gameplay manager (booster spawn/merge/explode queue), not a service. Could move to its own `Boosters/` folder if you prefer.
- `GameInstaller` stays in `Core/` until Phase 4 retires it (replaced by Cygnus bootstrap).
- I considered a `Cinemachine/` folder for the camera shake controller but `Camera/` covers both well enough.

---

## Namespace Strategy

For Phase 2, the **bare minimum** to make things consistent:

| File state today | Phase 2 action |
|:---|:---|
| `namespace Manager` | Rewrite to `namespace Managers` |
| `namespace Managers` | Leave as-is |
| No namespace at all | Add `namespace Managers` |

**Result:** every file in `Assets/Scripts/Managers/**` is under `namespace Managers` (flat). The subfolder structure is purely organizational for now.

**Subfolder namespaces (e.g. `Managers.Auth`)** are deferred to **Phase 3** where they will be aligned with asmdef boundaries. Doing them now means doing them twice.

### Consumer updates

Anywhere in the codebase that has `using Manager;` (singular) gets rewritten to `using Managers;` (plural). I'll grep for it and patch all callsites in one batch.

Files that reference orphan-namespace classes (e.g. `GameAnalytic.Instance` from `Level.cs`) will need `using Managers;` added if they don't already have it. I'll detect compile errors and patch as I go.

---

## Execution Plan

1. **Pre-flight**
   - List of `using Manager;` callsites (chat output, no doc).
   - List of consumers of orphan-namespace classes (`GameAnalytic`, `IAuthenticationManager`, `IClient`, `IEventService`, `GameInstaller`, `WinSeriesManager`, `UserDatasManager`).

2. **File moves** — one `Unity_RunCommand` doing the full taxonomy with `AssetDatabase.MoveAsset` (preserves GUIDs / scene+prefab refs).

3. **Namespace fixes** — same `Unity_RunCommand`, walks each moved file, normalizes namespace per the table above.

4. **Consumer updates** — second `Unity_RunCommand`, walks all `.cs` files under `Assets/Scripts/`, replaces `using Manager;` → `using Managers;`. Adds `using Managers;` to files that reference orphan-namespace classes if missing.

5. **Verify** — `Axiom_Verify compilation` until CLEAN. Iterate on any compile errors.

6. **Append entry to `StatusUpdate.md`** summarizing what moved.

---

## Open Question for You

**`UserDatasManager` — fix the typo to `UserDataManager`?** Same pattern as `GameAnalitic` → `GameAnalytic` (fixed in Phase 1). If yes, I'll batch the rename into Phase 2 since we're touching the file anyway (~5–10 caller sites). If no, leave as `UserDatasManager`.

Anything else in the taxonomy you'd like rearranged before I execute?

---

## Reply with one of:

- **"go"** — execute as proposed, fix `UserDatasManager` typo, leave taxonomy as drafted.
- **"go but keep UserDatas"** — execute as proposed, don't touch the typo.
- **"change X"** — anything you want moved between subfolders, renamed, or restructured.
