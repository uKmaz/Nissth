# Missions UI — Inspector Wiring Instructions

**Date written:** 2026-05-04
**Context:** Phase D code is complete and compile-clean. This doc tells you how to wire the prefabs + scene instances in the editor. Test once everything's connected.

---

## The end state you're building

```
MainPanel (in Menu scene)
  └─ TopBar (existing — Coin/Health/Trophy displays)
  └─ MissionsNextUpCard ← always visible inline (replaces the old ContentEvent)
  │   └─ tap → opens MissionsPanel
  └─ Play / Settings / etc. (existing)

MissionsPanel (popup overlay, hidden by default)
  └─ ScrollView with one MissionRow per active mission
  └─ Exit button (X)
```

When the player has no active missions, `MissionsNextUpCard` hides itself silently — clean fallback if `MissionTemplateLibrary.asset` isn't authored yet or all missions expired.

---

## Step 1 — Build the `MissionRow` prefab

Save at `Assets/Resources/UI/Panel/MissionRow.prefab`.

Hierarchy (one root with children — the names are suggestions, only what's wired matters):

```
MissionRow (RectTransform + MissionRow.cs + LayoutElement)
  ├─ Background (Image)
  ├─ TitleText (TextMeshProUGUI)             → wire to `titleText`
  ├─ ProgressText (TextMeshProUGUI, "0 / 20") → wire to `progressText`
  ├─ ProgressBar
  │   ├─ Background (Image)
  │   └─ Fill (Image, type=Filled, FillMethod=Horizontal) → wire to `progressBarFill`
  ├─ RewardText (TextMeshProUGUI, "+100 Coins") → wire to `rewardText`
  ├─ ClaimButton (Button + Image)           → wire to `claimButton`
  │   └─ Text (TextMeshProUGUI, "CLAIM")
  └─ ClaimedBadge (GameObject with checkmark, INACTIVE by default) → wire to `claimedBadge`
```

The script auto-greys out the claim button when the mission isn't completed (or is already claimed) — you don't have to wire that logic yourself.

---

## Step 2 — Build the `MissionsPanel` prefab

Save at `Assets/Resources/UI/Panel/MissionsPanel.prefab`.

```
MissionsPanel (RectTransform + MissionsPanel.cs)
  ├─ Background (Image, full-screen, semi-transparent black)
  ├─ Frame (the scaling card — DOTween animates this)  → wire to `_missionsPanelDG`
  │   ├─ HeaderText (TextMeshProUGUI, "Missions")
  │   ├─ BtnExit (Button, X icon)                       → wire to `_btnExit`
  │   └─ ScrollView (Unity ScrollView component)
  │       └─ Viewport
  │           └─ Content (with VerticalLayoutGroup + ContentSizeFitter) → wire to `_listContent`
  ├─ (drag MissionRow.prefab from Project into) → `_rowPrefab`
```

**Important:** the `_rowPrefab` field MUST be a project prefab reference (drag from the Project window), NOT a scene instance. The panel instantiates copies of it at runtime.

---

## Step 3 — Build the `MissionsNextUpCard` (inline in MainPanel)

This replaces the old `ContentEvent` placeholder GameObject inside the MainPanel.

In the MainPanel prefab (or scene instance):

1. **Delete the `ContentEvent` GameObject** (it had `MainMenuTopCanvasEvents` script — empty, unused).
2. **Add a new GameObject in the same position** named `MissionsNextUpCard`. It should be a horizontal banner below the top bar.
3. **Add components**:
   - `RectTransform` (sized like a banner — wide and short)
   - `Image` (background sprite — the card visual)
   - `Button` (so the entire card is tappable) → wire to `_openPanelButton`
   - `MissionsNextUpCard.cs` script
4. **Children**:

```
MissionsNextUpCard (Image + Button + MissionsNextUpCard.cs)
  ├─ Icon (Image — optional decorative icon for the period or goal type)
  ├─ TitleText (TextMeshProUGUI, "Pop 20 blue bubbles") → wire `_titleText`
  ├─ ProgressText (TextMeshProUGUI, "13 / 20")          → wire `_progressText`
  ├─ ProgressBar
  │   └─ Fill (Image, type=Filled, FillMethod=Horizontal) → wire `_progressBarFill`
  ├─ RewardText (TextMeshProUGUI, "+100 Coins")          → wire `_rewardText`
  ├─ PeriodText (TextMeshProUGUI, "DAILY")               → wire `_periodText`
  └─ ClaimableBadge (GameObject with red dot/checkmark, INACTIVE by default) → wire `_claimableBadge`
```

5. **Wire `_missionsPanel`** to your `MissionsPanel` scene instance (added in step 4 below).

---

## Step 4 — Place `MissionsPanel` in the Menu scene

1. Drag `MissionsPanel.prefab` into the Menu scene's UI canvas (alongside `SettingsPanel`).
2. **Set the GameObject inactive** in the inspector (top-left checkbox unchecked) — popup state.
3. Wire its inspector fields if not already wired in the prefab itself.
4. Go back to your `MissionsNextUpCard` and drag this scene instance into the `_missionsPanel` field.

---

## Step 5 — Sanity test

Hit Play in editor.

**Check #1 — boot logs.** Console should show (from Phase C diagnostics):
- `[GameBootstrap] MissionTemplateLibrary load result: ok`
- `[MetaMissionService] Initialize. UTC now=... Active count BEFORE rollover check=N`
- `[MetaMissionService]   [0] Daily PopBubbles target=20 progress=20 completed=True ...`
- `[GameBootstrap] MetaMissionService registered + initialized.`

**Check #2 — visual.** MainPanel should show the inline `MissionsNextUpCard` with the closest-to-completion mission. If you have a claimable one, the `ClaimableBadge` should be visible.

**Check #3 — interaction.**
- Tap the card → `MissionsPanel` opens with all 3 active missions in the scroll list.
- Tap Claim on any completed mission → reward dispatches via `UserDataManager.EarnCoin/EarnBooster/EarnAbility`. The button should grey out.
- Tap Exit (X) → panel closes with scale-down animation.

**Check #4 — persistence.** Stop play, restart. Progress should persist (UserData JSON blob in PlayerPrefs).

---

## Common issues

- **Inline card doesn't appear at all** → `MissionsNextUpCard.gameObject` auto-hides if no missions active. Check console for `Active count=0` — means MissionTemplateLibrary pools are empty or asset isn't registered.
- **Click the card → nothing happens** → `_missionsPanel` field unwired on the card script. Check console for the warning "MissionsPanel field is not wired".
- **Panel opens but list is empty** → `_listContent` or `_rowPrefab` unwired. Or `_rowPrefab` is a scene reference instead of a project prefab.
- **Claim button doesn't grey out after click** → `MissionRow.cs` calls `Refresh()` on click; if button stays interactive, the Bind chain didn't reach the row. Verify the panel is calling `row.Bind(mission, OnClaimRequested)` (Phase D logic in `MissionsPanel.BindList`).
- **Reward doesn't apply** → The `RewardData.kind` might be `Coin` (default 0) but `subtype` for Booster/Ability might be wrong. Check the `MissionTemplateLibrary` asset entries.

---

## Tweaking the visuals later

- **Period color coding** — change `_periodText.color` based on `_topMission.Period` in `MissionsNextUpCard.Refresh()`. Daily=green, Weekly=blue, Monthly=gold or whatever you like.
- **Icon per goal type** — add a `Sprite[] _goalIcons` array on `MissionsNextUpCard` indexed by `(int)_topMission.Goal.goalType` and assign in Refresh().
- **Animation polish** — DOTween is already imported. Add a punch-scale on the claim button when reward dispatches.

---

## What you DON'T have to wire

- The button + badge that I had on MainPanel earlier (`_btnMissions`, `_missionsBadge`, `_missionsBadgeCount`) — those fields are deleted. If your prefab still has lingering references, they're harmless.
- `MainPanel._missionsPanel` (also deleted — the panel reference now lives on `MissionsNextUpCard`).
