# Ropes, Desktop Pins, Groups and Lift Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add multiple ropes (tali), groups on a rope (kelompok), up to 3 desktop pins per rope, and a persisted lift toggle (angkat jemuran / jemur lagi), all animated.

**Architecture:** All rules live in `Library` and two pure helpers in `Core` (no windows), so they are unit-tested. `LineWindow` shows only the active rope and draws groups. A new `PinWindow` per pinned card, owned by a small `PinBoard`, handles desktop pins. `JemurApp` holds the active rope and lift state, persisted in `Settings`.

**Tech Stack:** C# / WPF on .NET 10 (`net10.0-windows`), xUnit 2.9, no new packages.

**Spec:** `docs/superpowers/specs/2026-10-19-ropes-pins-groups-design.md` (read it; this plan does not repeat its behaviour tables).

## Global Constraints

- No new dependencies. No timers or `CompositionTarget.Rendering` while nothing moves. No `DropShadowEffect`; reuse `CardView`'s layered shadows.
- Every new string goes in `locales/en.json` **and** `es.json`, `id.json`, `ja.json`, `ms.json`, `pt-BR.json` (`LocalizationTests.HasSameKeysAsEnglish` fails otherwise). Keep `{0}` placeholders identical.
- Indonesian UI names verbatim: Tali, Jemuran (default rope), Kelompok, Jepit di desktop, Kembalikan ke jemuran, Angkat jemuran, Jemur lagi.
- Default rope id `"default"`; it can be renamed, never deleted. `Library.MaxPinsPerRope = 3`.
- `items.json` `Version = 2`; v1 and the pre-0.1 array format must still load. Before the first v2 write, copy the old file to `items.v1-backup.json` (only if that backup does not exist).
- Pin coordinates: physical pixels of the virtual screen, top-left of the card.
- Motion durations 200–450 ms; respect `SystemParameters.ClientAreaAnimation` (false means no animation, apply final state).
- Shortcut for next rope: `Alt+Shift+G`, action id `"rope"`. Toggle (`Alt+Shift+J`) becomes lift. Tray left-click becomes lift.
- Build and test commands: `dotnet build` and `dotnet test` from the repo root (Windows).

## Review Focus

1. **Inconsistent data on disk** (a group listing an item id that is gone or on another rope, an item whose rope was deleted): load repairs it silently, with no exception and no lost cards. Test: `Load_RepairsDanglingRopeAndGroupReferences` (Task 1/2).
2. **Monitor unplugged or resolution changed:** a pin that is less than 50% visible comes back fully inside the nearest work area. Test: `Clamp_MovesOffscreenPinIntoNearestWorkArea` (Task 2).
3. **A grouped or pinned card leaves through another path** (drag-off-share drop on nothing, Discard, Move to line, rope deleted): it is removed from its group and un-pinned, and empty groups disappear. Tests: `Discard_RemovesFromGroupAndUnpins`, `DeleteRope_*` (Tasks 1–2).
4. **Old `settings.json` without the `rope` shortcut or `ActiveRope`:** gets `Alt+Shift+G` and `"default"`. Test: `Normalize_AddsRopeShortcutAndDefaultRope` (Task 1).
5. **Deleting the active rope, or starting lifted then taking a screenshot:** the app switches to default and un-lifts, and the new card is visible. Manual check in Task 3 and Task 4 verification steps.

---

### Task 1: Data model v2, ropes in Library, migration

**Files:**
- Modify: `src/Core/Models.cs` (HangItem fields, `Rope`, `CardGroup`, `Actions.NextRope`, Settings fields)
- Modify: `src/Core/Library.cs` (FileShape v2, rope API, load repair, v1 backup)
- Test: `tests/LibraryRopeTests.cs` (new, `[Collection("AppPaths")]`, same setup/teardown as `LibraryTests`), `tests/SettingsStoreTests.cs`

**Interfaces (produces):**
- `public sealed class Rope { string Id; string Name; DateTime Created; public const string DefaultId = "default"; }`
- `public sealed class CardGroup { string Id; string RopeId; string Name; double Position; bool Folded; List<string> ItemIds; }`. Order in `ItemIds` is left-to-right order. This replaces the spec's `HangItem.GroupId`: one source of truth for membership and order.
- `HangItem`: `string? RopeId`, `bool Pinned`, `double PinLeft`, `double PinTop`.
- `Actions.NextRope = "rope"`, added to `Actions.All` and `DefaultHotkeys()` as `"Alt+Shift+G"`.
- `Settings`: `string ActiveRope = "default"`, `bool Lifted`. `Normalize()` sets empty or null `ActiveRope` to `"default"`.
- `Library`: `IReadOnlyList<Rope> Ropes`, `IReadOnlyList<CardGroup> Groups`, `IEnumerable<HangItem> ItemsOn(string ropeId)`, `Rope? RopeById(string id)`, `Rope AddRope(string name)`, `void RenameRope(string id, string name)`, `bool DeleteRope(string id)` (false for default/unknown), `void MoveToRope(HangItem item, string ropeId)`, `void DiscardAll(string ropeId)` (replaces the parameterless one: unpinned live items of that rope only), `const int MaxPinsPerRope = 3`.
- New event `public event Action<HangItem>? Relocated;`, raised when an item's rope, pin state or group changes, so views reconcile. Ropes and groups changes raise `Changed`.
- Every item's `RopeId` is non-null after `Load` and `Add` (`Add` keeps a set `RopeId`; null becomes `"default"`).

- [ ] **Step 1: Write failing tests** in `tests/LibraryRopeTests.cs`:
  - `LoadsV1FileOntoDefaultRope`: write a v1 `{"Version":1,"Items":[{note}],"Discarded":[]}` to `AppPaths.ItemsFile`; after `Load(7)`: `Ropes` has one rope `Id=="default"`, `Name=="Jemuran"`, and the item has `RopeId=="default"`.
  - `LoadsArrayFormatOntoDefaultRope`: same with `[ {note} ]`.
  - `V2RoundTrips`: `AddRope("Klien A")`, add a note with that `RopeId`, `SaveNow`, reload; rope and `RopeId` survive.
  - `FirstV2SaveBacksUpV1File`: v1 file, `Load`, `SaveNow`; `items.v1-backup.json` exists with the original bytes. A second `SaveNow` does not overwrite it.
  - `DeleteRope_MovesLiveAndDiscardedItemsToDefault`: item on rope A (one live, one discarded), `DeleteRope(A)` returns true; both have `RopeId=="default"`; `RopeById(A)` is null.
  - `DeleteRope_RefusesDefault`: `DeleteRope("default")` is false.
  - `Restore_ReturnsToDefaultWhenRopeIsGone`: discard on A, delete A, restore: `RopeId=="default"`.
  - `MoveToRope_SetsRopeAndResetsPosition`: `Position` becomes `-1`, `RopeId` is the target, and `Relocated` fires once.
  - `DiscardAll_OnlyTouchesThatRope`.
  - `Load_RepairsDanglingRopeReference`: v2 file with an item `RopeId:"ghost"` → `"default"`.
  - In `SettingsStoreTests`: `Normalize_AddsRopeShortcutAndDefaultRope`: an old settings JSON without `rope` and `ActiveRope` → `Hotkeys["rope"]=="Alt+Shift+G"`, `ActiveRope=="default"`, `Lifted==false`.
- [ ] **Step 2:** `dotnet test --filter "FullyQualifiedName~LibraryRopeTests|FullyQualifiedName~SettingsStoreTests"`. Expected: compile errors / FAIL.
- [ ] **Step 3: Implement** the model and Library API above. The default rope's name comes from the caller: `Load(int keepDiscardedDays, string defaultRopeName = "Jemuran")`. JemurApp passes `L.T("rope.default")`. Load repair: unknown `RopeId` becomes `"default"`, and the default rope is created if missing. The backup happens in `SaveNow` when the file on disk was loaded with `Version < 2`. Update the one existing caller of `DiscardAll()` to pass the active rope (temporarily `"default"`; Task 4 passes the real one).
- [ ] **Step 4:** `dotnet test`. Expected: all tests PASS, including the existing `LibraryTests`.
- [ ] **Step 5: Commit** `feat: store ropes in items.json v2 with v1 migration`.

### Task 2: Pins and groups rules, layout helpers

**Files:**
- Modify: `src/Core/Library.cs`
- Create: `src/Core/LineLayout.cs` (pure functions)
- Test: `tests/LibraryGroupPinTests.cs`, `tests/LineLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces (Library):
  - `int PinCount(string ropeId)`
  - `bool Pin(HangItem item, double left, double top)`: false when the rope already has 3 pins. On success it removes the item from its group, sets the fields, raises `Relocated`.
  - `void MovePin(HangItem item, double left, double top)`: saves, no event.
  - `void Unpin(HangItem item)`: `Position = -1`, raises `Relocated`.
  - `CardGroup? GroupOf(HangItem item)`
  - `CardGroup CreateGroup(HangItem first, string name)`: group at `first.Position`, on its rope.
  - `void JoinGroup(HangItem item, string groupId, int index)`: leaves any old group, un-pins, clamps the index.
  - `void LeaveGroup(HangItem item)`
  - `void RenameGroup(string id, string name)`, `void SetFolded(string id, bool folded)`, `void MoveGroup(string id, double position)`
  - `void Ungroup(string id)`: each card keeps its current on-screen spot. The caller passes the new positions: `void Ungroup(string id, IReadOnlyDictionary<string,double> positions)`.
  - Empty groups are removed automatically. `Discard`, `Drop`, `MoveToRope`, `DeleteRope` and `Pin` all leave the group. `Discard`, `MoveToRope` and `DeleteRope` un-pin.
- Produces (`LineLayout`, static):
  - `double[] GroupSlots(double startX, IReadOnlyList<double> widths, double gap)`: returns card centre x values, with the first card's left edge at `startX`. `gap = 10`.
  - `int SlotIndexAt(double startX, IReadOnlyList<double> widths, double gap, double x)`: the insert index for a dragged card centre at `x`.
  - `bool IsOutsideGroup(double startX, IReadOnlyList<double> widths, double gap, double x, double cardWidth)`: true when `x` is more than `cardWidth / 2` beyond either edge.
  - `System.Windows.Point ClampPin(System.Windows.Rect pin, IReadOnlyList<System.Windows.Rect> workAreas)`: unchanged if at least 50% of the area is inside one work area; otherwise moves the top-left so the rect fits fully inside the work area whose centre is nearest the pin's centre.

- [ ] **Step 1: Write failing tests.**
  - `Pin_RefusesFourthOnSameRope`: three pins succeed; the fourth returns false; `PinCount==3`. A fourth pin on another rope succeeds.
  - `Pin_RemovesFromGroup` and `JoinGroup_UnpinsCard`.
  - `Discard_RemovesFromGroupAndUnpins`: group of 2, discard one: the group keeps 1 id. Discard the other: `Groups` is empty.
  - `DeleteRope_DissolvesGroupsAndUnpins`.
  - `JoinGroup_InsertsAtIndexAndLeavesOldGroup`.
  - `Load_RepairsDanglingGroupReferences`: a group lists a missing id and an id on another rope; both are dropped, and the group is removed if it ends up empty.
  - `GroupsAndPinsRoundTrip`: save/reload keeps `ItemIds` order, `Folded`, `PinLeft`/`PinTop`.
  - `LineLayoutTests`:
    - `GroupSlots(100, [50, 70], 10)` returns `[125, 195]`.
    - `SlotIndexAt` with the same widths: `x=110` gives 0, `x=170` gives 1, `x=400` gives 2.
    - `IsOutsideGroup(100, [50,70], 10, 50, 50)` is true and `(…, 150, 50)` is false.
    - `Clamp_MovesOffscreenPinIntoNearestWorkArea`: work areas `(0,0,1920,1040)` and `(1920,0,1280,984)`; pin `(3500,100,160,140)` gives `(3040,100)`.
    - `Clamp_KeepsMostlyVisiblePin`: pin `(1850,100,160,140)` is unchanged.
- [ ] **Step 2:** run the new test classes. Expected: FAIL.
- [ ] **Step 3: Implement** the Library methods and `LineLayout`.
- [ ] **Step 4:** `dotnet test`. Expected: all PASS.
- [ ] **Step 5: Commit** `feat: add pin and group rules to the library`.

### Task 3: Lift toggle (angkat jemuran / jemur lagi)

**Files:**
- Modify: `src/Windows/LineWindow.cs` (replace `TogglePinned` with lift; add `Motion` check)
- Create: `src/UI/Motion.cs`: `static bool Enabled => SystemParameters.ClientAreaAnimation;`
- Modify: `src/App/JemurApp.cs`, `src/Interop/TrayIcon.cs` (no API change; rewire `Click`), all `locales/*.json`

**Interfaces:**
- Produces (LineWindow): `void SetLifted(bool lifted, bool animate)` and `bool IsLifted`.
  - Lift: cards rise above the top edge (`Slide.Y` to `-(Height+40)`), staggered 30 ms left to right, 280 ms, `CubicEase EaseIn`. The rope fades. Then `Hide()`.
  - Unlift: `Show()`, the rope fades in, and cards replay the existing new-card drop (`Angle=22`, `BackEase`) staggered 30 ms.
  - `Rebuild()` and `HoldOpen()` must not show the window while lifted.
- Produces (JemurApp): `bool Lifted => Settings.Lifted`, `void SetLifted(bool lifted)` (saves settings, updates the line and later pins, refreshes the tray tip), `void ToggleLift()`, `void EnsureVisible()` (un-lifts if lifted).
- Locale keys (en / id): `action.lift` "Take everything in" / "Angkat jemuran", `action.unlift` "Hang it back out" / "Jemur lagi", `tray.lifted` "Jemur · taken in" / "Jemur · jemuran diangkat". Remove `action.toggleLine` from every locale.

- [ ] **Step 1:** Implement `Motion.Enabled` and `LineWindow.SetLifted`, and delete `TogglePinned` and `_pinned`. `Actions.Toggle` maps to `ToggleLift`. Tray `Click` maps to `ToggleLift`. The tray menu item label follows state (`action.lift`/`action.unlift`) with the toggle hotkey as its gesture. The tray tip uses `tray.lifted` while lifted.
- [ ] **Step 2:** `Start()`: if `Settings.Lifted`, do not show the line. `TakeScreenshot`, `NewNote` and `HangClipboard` call `EnsureVisible()` first. `TakeScreenshot` must not re-show the line if it was lifted before `EnsureVisible`. Order: `EnsureVisible()`, then the existing hide/capture/show.
- [ ] **Step 3:** Add the keys to all six locale files. `dotnet build` and `dotnet test`. Expected: PASS (the `LocalizationTests` too).
- [ ] **Step 4: Manual check** with `$env:JEMUR_DATA="$env:TEMP\jemur-dev"; dotnet run --project src`:
  - Alt+Shift+J and tray left-click lift and restore, with the animation.
  - Quit while lifted and start again: nothing shows. Alt+Shift+S un-lifts and the new card drops in.
  - With Windows Settings → Accessibility → Visual effects → Animation effects off, it switches instantly.
- [ ] **Step 5: Commit** `feat: lift and hang the line back out from one toggle`.

### Task 4: Ropes on screen, switching, management

**Files:**
- Create: `src/Windows/RopeTag.cs` (fabric tag `Border` drawn like the clothespin and paper, with a Narrator name of `L.F("rope.tagName", name)`)
- Create: `src/UI/Prompt.cs`: `static string? Ask(string title, string label, string initial, Window? owner)`, a small Fluent dialog using `Ui.TextBox`, `common.save` and `common.cancel`. Returns null on cancel.
- Modify: `src/Windows/LineWindow.cs`, `src/App/JemurApp.cs`, `src/Windows/ControlPanel.cs`, all `locales/*.json`

**Interfaces:**
- Consumes: Library rope API (Task 1), `SetLifted`/`EnsureVisible` (Task 3).
- Produces (LineWindow):
  - `string RopeId { get; }` and `void ShowRope(string ropeId, bool animate)`.
    - Old cards slide left along the rope: `Canvas.Left` minus 220 and fade, 260 ms, stagger 25 ms.
    - The tag flips: `ScaleY` 1, 0, 1, 200 ms, swapping the text at the midpoint.
    - New cards come in from +220 and get `Kick(±40)`.
  - The line only holds cards where `RopeId == ropeId && !Pinned`. It reacts to `Library.Relocated` by adding or removing the card.
  - Visibility rule (spec decision 1): the line shows if the rope has unpinned cards, or `Library.Ropes.Count > 1`. With no cards, it stays collapsed with the tag peeking.
- Produces (JemurApp):
  - `string ActiveRope => Settings.ActiveRope`
  - `void SwitchRope(string ropeId)`: `EnsureVisible`, saves, `Line.ShowRope`, and later `Pins.ShowRope`.
  - `void NextRope()` (bound to `Actions.NextRope`)
  - `void NewRope()`: prompt, `AddRope`, switch.
  - `void RenameRope(string id)`
  - `void DeleteRope(string id)`: confirm, `Library.DeleteRope`; if it was active, switch to `"default"`.
  - `void MoveToRope(HangItem item, string ropeId)`: toast `L.F("toast.movedTo", ropeName)` on the card first when the target is not active.
  - Restoring a discarded item onto the active rope while lifted un-lifts (spec decision 4); restoring onto another rope does not.
  - The Line page empty state with other ropes present reads `rope.emptyTitle` "Nothing on {0} yet" / "Belum ada apa-apa di {0}".
  - New items get `RopeId = ActiveRope`. `Start()` repairs an unknown `Settings.ActiveRope` to `"default"`. The tray tip shows `{rope} · {n items}`.
- UI wiring:
  - Clicking the rope tag opens a menu: the ropes (active one checked), `rope.new` "New line…", `rope.manage` "Manage lines…".
  - Tray menu: a `rope.lines` "Lines" submenu with the same entries.
  - Item menu: `rope.moveTo` "Move to line" submenu listing the other ropes.
  - Control Panel Line page: `Ui.DropDown` of ropes (switches the active rope), plus `rope.rename` "Rename", `rope.delete` "Delete line" (disabled for default) and `rope.new` buttons. Tiles show only the active rope. Discarded tiles show the rope name under the title.
- Locale keys (en / id):

  | key | en | id |
  |---|---|---|
  | `rope.default` | Line | Jemuran |
  | `rope.lines` | Lines | Tali |
  | `rope.new` | New line… | Tali baru… |
  | `rope.manage` | Manage lines… | Atur tali… |
  | `rope.rename` | Rename | Ganti nama |
  | `rope.delete` | Delete line | Hapus tali |
  | `rope.deleteConfirm` | Delete "{0}"? Its cards move to "{1}". | Hapus "{0}"? Card-nya pindah ke "{1}". |
  | `rope.namePrompt` | Line name | Nama tali |
  | `rope.moveTo` | Move to line | Pindah ke tali |
  | `rope.tagName` | Line: {0} | Tali: {0} |
  | `toast.movedTo` | Moved to {0} | Pindah ke {0} |
  | `action.nextRope` | Next line | Tali berikutnya |

  `action.nextRope` also shows up in Settings → Keyboard shortcuts: add `(Actions.NextRope, "")` to the glyph list in `ControlPanel.cs`.

- [ ] **Step 1:** Implement `Prompt.Ask` and `RopeTag`.
- [ ] **Step 2:** LineWindow rope filtering, `ShowRope` with the animation, the visibility rule and the tag menu.
- [ ] **Step 3:** JemurApp rope actions, hotkey, tray submenu and item menu. Pass the active rope to `DiscardAll`.
- [ ] **Step 4:** Control Panel selector, management buttons and the rope name on Discarded tiles.
- [ ] **Step 5:** Locale keys in all six files. `dotnet build` and `dotnet test`. Expected: PASS.
- [ ] **Step 6: Manual check:**
  - Create "Klien A", switch with Alt+Shift+G, the tag, the tray and the panel; the animation plays.
  - A new screenshot lands on the active rope.
  - Move a card to another rope and see the toast.
  - Delete the active rope: you land on Jemuran with the cards there.
  - Restart: the active rope is restored.
- [ ] **Step 7: Commit** `feat: switch between several lines`.

### Task 5: Desktop pins

**Files:**
- Create: `src/Windows/PinWindow.cs`: one transparent, topmost, no-activate tool window (`Native.MakeToolWindow(this, noActivate: true)`) hosting a `CardView`. It handles drag with pendulum swing (reusing `CardView.Step` on `CompositionTarget.Rendering` only while moving), click to spotlight and hold for 500 ms to mark up, the same as `LineWindow`.
- Create: `src/Windows/PinBoard.cs`: owns the `PinWindow`s of the active rope.
- Modify: `src/App/JemurApp.cs`, `src/Windows/LineWindow.cs` (expose card screen rect for the fly animation), all `locales/*.json`

**Interfaces:**
- Consumes: `Library.Pin/MovePin/Unpin/PinCount`, `LineLayout.ClampPin`, `Library.Relocated`.
- Produces (PinBoard):
  - `void ShowRope(string ropeId, bool animate)`
  - `void SetLifted(bool lifted, bool animate)`: lift moves each pin up 24 DIP and fades it, 220 ms; unlift drops it 24 DIP with `Kick(±30)`.
  - `void HideForCapture()` and `void RestoreAfterCapture()`
  - `PinWindow? WindowOf(HangItem item)`
  - Positions are converted between physical pixels and DIPs with the DPI of the monitor under the pin (`MonitorFromPoint`, `GetDpiForWindow` or the `VisualTreeHelper.GetDpi` of the window). On `ShowRope`, each pin is clamped with `ClampPin` against the current work areas (`MonitorFromPoint` + `GetMonitorInfo` on each monitor; add `EnumDisplayMonitors` to `Native` if needed) and saved back if it moved.
- Produces (JemurApp):
  - `void PinToDesktop(HangItem item)`: the first spot is the card's line position + 200 DIP down. Fly animation: a `GhostWindow`-style arc from the rope card to the spot, 380 ms, then the pin window appears and swings.
  - `void UnpinToLine(HangItem item)`: reverse arc, then the card appears on the rope with `Kick(60)`.
  - `Spotlight`, `Copy` and `Markup` toasts and reloads also look up the `PinBoard` window when the item is pinned. `CardCenter` falls back to the pin's centre.
  - `TakeScreenshot` calls `Pins.HideForCapture()` next to `Line.Hide()` and `Pins.RestoreAfterCapture()` after the capture.
  - `SetLifted` and `SwitchRope` also drive `Pins`.
- Item menu: `pin.toDesktop` "Pin to desktop" (disabled with the tooltip `pin.limit` "Up to 3 pins per line" when `PinCount == 3`), shown when not pinned. When pinned: `pin.back` "Back on the line". On a pin, the menu also has Copy, Open, Save as, Mark up, Move to line and Discard. Discard on a pin makes the pin window fall (`GhostWindow.Fall`).
- Locale keys (en / id): `pin.toDesktop` Pin to desktop / Jepit di desktop, `pin.back` Back on the line / Kembalikan ke jemuran, `pin.limit` Up to 3 pins per line / Paling banyak 3 jepitan per tali, `pin.name` Pinned: {0} / Dijepit: {0} (Narrator name on the pin window).
- Control Panel tiles: the same Pin/Back action in the tile's action bar.

- [ ] **Step 1:** `PinWindow` with drag, click, hold and swing. Dragging calls `Library.MovePin` on mouse up only.
- [ ] **Step 2:** `PinBoard` with DPI conversion, clamping on show, lift and capture hooks.
- [ ] **Step 3:** JemurApp pin/unpin actions with the fly animation, menu items, panel tile action and capture hook.
- [ ] **Step 4:** Locale keys in all six files. `dotnet build` and `dotnet test`. Expected: PASS.
- [ ] **Step 5: Manual check:**
  - Pin 3 cards; the fourth menu item is disabled with the tooltip.
  - Drag a pin to a second monitor with different scaling, restart: it is in the same place.
  - Lift and unlift move the pins too. A screenshot does not contain the pins.
  - Back on the line flies the card home. Switching ropes swaps the pins.
- [ ] **Step 6: Commit** `feat: pin up to three cards to the desktop`.

### Task 6: Groups on a rope

**Files:**
- Create: `src/Windows/GroupLabel.cs`: a small label tag on the rope with the name, a fold chevron, and the Narrator name `L.F("group.name", name, count)`.
- Modify: `src/Windows/LineWindow.cs` (layout via `LineLayout.GroupSlots`, drag join/leave, label drag, folded stack), `src/App/JemurApp.cs` (menus), all `locales/*.json`

**Interfaces:**
- Consumes: the Task 2 group API and layout helpers.
- Layout: a grouped card's x comes from `GroupSlots(group.Position * LineWidth, widths, 10)` and ignores `item.Position`. The label sits left of the first card. Free cards keep `FreePosition()` but avoid group spans.
- Drag a free card: when its centre enters a group's span, `JoinGroup(item, group.Id, SlotIndexAt(...))` on mouse up. While hovering, the neighbours ease apart (200 ms) to preview the slot.
- Drag a grouped card: it reorders inside the group, and when `IsOutsideGroup` it becomes free on mouse up (`LeaveGroup`, `Position = x / LineWidth`). Dragging off the rope still means share (unchanged).
- Drag the label: `MoveGroup`. All cards follow and swing.
- Fold: a label click or the menu → `SetFolded`. Cards animate to the stack point (250 ms, alternating ±3° tilt, 4 DIP offset each), with a count badge on the top card. Clicking the stack unfolds.
- Item menu: `group.menu` "Group" submenu with `group.new` "New group…" plus the existing groups on this rope, and `group.leave` "Remove from group" when the card is grouped.
- Label menu: `group.rename` Rename, `group.fold`/`group.unfold`, `group.ungroup` "Ungroup", `group.discardAll` "Discard these cards".
- Locale keys (en / id):
  - `group.menu` Group / Kelompok
  - `group.new` New group… / Kelompok baru…
  - `group.defaultName` Group {0} / Kelompok {0}
  - `group.leave` Remove from group / Keluarkan dari kelompok
  - `group.rename` Rename / Ganti nama
  - `group.fold` Fold / Lipat
  - `group.unfold` Unfold / Buka lipatan
  - `group.ungroup` Ungroup / Bubarkan kelompok
  - `group.discardAll` Discard these cards / Buang card ini
  - `group.name` Group {0}, {1} cards / Kelompok {0}, {1} card
  - `group.namePrompt` Group name / Nama kelompok
- Auto-hide (`UpdateHover`) treats labels and stacks like cards, so peek and expand still work.

- [ ] **Step 1:** Layout and label rendering. Folded stacks render with the badge.
- [ ] **Step 2:** Drag join, leave and reorder with the slot preview. Label drag moves the group.
- [ ] **Step 3:** Fold and unfold animations, plus the menus (item and label).
- [ ] **Step 4:** Locale keys in all six files. `dotnet build` and `dotnet test`. Expected: PASS.
- [ ] **Step 5: Manual check:**
  - Create a group from the menu and drag two cards in and one out.
  - Reorder, move the group by its label, fold and unfold.
  - Restart: the order and the folded state remain.
  - Drag a grouped card off the rope into Explorer: it is shared and stays grouped. Drop it on nothing: it is discarded and the group updates.
- [ ] **Step 6: Commit** `feat: group cards on a line`.

### Task 7: Motion and polish pass

**Files:**
- Modify: the windows touched above, `CHANGELOG.md`, `README.md` (Features and Shortcuts sections)

- [ ] **Step 1:** Walk the spec's Motion table. Every row plays, and every row is instant when `Motion.Enabled` is false. Fix any that are missing. A group created through the menu makes its label drop and swing.
- [ ] **Step 2:** Narrator pass:
  - The rope tag, group labels, stacks and pins have names.
  - Tile actions for pin, move and group are keyboard reachable in the Control Panel.
  - Light and dark mode, plus the accent colour, look right on the new chrome (the prompt dialog, tile actions).
- [ ] **Step 3:** Idle check:
  - After everything settles, `CompositionTarget.Rendering` has no handlers and no new `DispatcherTimer` runs. Inspect by setting a breakpoint or a temporary `Log.Info` while lifted and while idle with 3 pins.
  - Compare the working set before and after with `Memory.Report`.
- [ ] **Step 4:** README Features and Shortcuts (Alt+Shift+J now "Take everything in / hang it back out", Alt+Shift+G "Next line", tray left-click). CHANGELOG "Unreleased" entry including: "Older versions can't read lines, groups or pins; downgrading flattens them (a v1 backup is kept as items.v1-backup.json)."
- [ ] **Step 5:** `dotnet build` and `dotnet test`. Expected: PASS.
- [ ] **Step 6: Commit** `feat: polish motion and accessibility for lines, pins and groups`.
