# Jemur: ropes, desktop pins, groups and the lift toggle

## Intent

Jemur exists because small references (an error screenshot, a colour, an
address) used to end up scattered across the desktop. The clothesline keeps
them in one place. This design adds four things without breaking that promise:

1. **Ropes (tali).** Several clotheslines, one per context ("Kerja",
   "Klien A", "Pribadi"). Exactly one rope is on screen at a time.
2. **Groups (kelompok).** Inside one rope, cards can be gathered under a
   small label so related cards stay together, move together, and can be
   folded into a single stack.
3. **Desktop pins (jepit di desktop).** A card can leave the rope and be
   clipped anywhere on the desktop, next to the work it supports, for
   example an error screenshot beside the code editor. At most three per rope.
4. **Lift toggle (angkat jemuran / jemur lagi).** One control hides
   everything that belongs to the active rope (the rope, its cards and its
   pins) and brings it all back in place.

Everything is persisted. Quitting Jemur or restarting Windows never loses a
card, a pin position, a group, the active rope or the lifted state. When Jemur
starts again (directly or via *Start with Windows*), the screen looks exactly
as it did before.

The owner also asked that the features are polished and animated. Motion is
part of the brand: cards already swing on a pendulum and drop onto the rope.
New motion must feel like the same physical world.

## Goals

- Separate work contexts (ropes) with instant switching.
- Group related cards within a rope, with fold/unfold.
- Pin up to 3 cards per rope freely on the desktop, positions remembered.
- One-step hide/show of the active rope, its cards and its pins, state remembered.
- Full persistence across quit and reboot; old data loads unchanged.
- Animations for every new state change (see *Motion*).

## Non-goals

- A separate app or "version" of Jemur. This all lives in the existing app.
- Two ropes visible at the same time.
- Pinning a whole group, resizing cards, cards visible while Jemur is not running.
- Sync, cloud, accounts, sharing ropes.

## Anti-goals (failure even if it works)

- The desktop becoming messy again: no unlimited pins, no floating toolbars.
- Breaking existing gestures on the rope: drag along rope = move, drag off
  rope = share, drop on nothing = discard, hold = mark up.
- Losing or corrupting data written by 0.1.x.
- Idle cost: no timers or render loops while nothing moves (see
  `CONTRIBUTING.md` and `docs/MEMORY.md`). New windows must stay as cheap as
  the line (layered shadows instead of `DropShadowEffect`, thumbnails only).
- A new dependency.

## Constraints

- WPF on .NET 10, Windows 10 1809+ and 11. Line stays on the primary screen.
- Every user-facing string goes in `locales/en.json` **and every other file in
  `locales/`** (es, id, ja, ms, pt-BR); `LocalizationTests` fails otherwise.
  Indonesian strings use the names below verbatim; other locales get proper
  translations (English as a placeholder only if unavoidable).
- One change per pull request is the project rule; this work may land as
  several PRs (see *Suggested slicing*).

## Vocabulary (UI strings)

| Concept | English | Indonesian |
|---|---|---|
| Rope | Line | Tali |
| Default rope name | Line | Jemuran |
| Group | Group | Kelompok |
| Pin to desktop | Pin to desktop | Jepit di desktop |
| Unpin | Back on the line | Kembalikan ke jemuran |
| Hide all | Take everything in | Angkat jemuran |
| Show all | Hang it back out | Jemur lagi |

## Data model

`items.json` moves to `Version = 2`:

```jsonc
{
  "Version": 2,
  "Ropes":  [{ "Id": "default", "Name": "Jemuran", "Created": "…" }],
  "Groups": [{ "Id": "…", "RopeId": "default", "Name": "Bug login", "Position": 0.32, "Folded": false }],
  "Items":     [ /* HangItem */ ],
  "Discarded": [ /* HangItem */ ]
}
```

`HangItem` gains:

- `RopeId` (string, null means `"default"`).
- `GroupId` (string?).
- `Pinned` (bool), `PinLeft`, `PinTop` (double, DIPs in virtual-screen
  coordinates of the card's top-left).

`Settings` gains `ActiveRope` (string, default `"default"`) and `Lifted`
(bool). `Normalize()` repairs an unknown `ActiveRope` to `"default"`.

Rules:

- Loading v1 (and the pre-0.1 array format) puts every item on `"default"`,
  which is created if missing. The default rope can be renamed but never deleted.
- Deleting a rope moves its items (live and discarded) to `"default"`. Its
  groups are dissolved and its pins are un-pinned onto the default rope.
- Restoring a discarded item returns it to its rope if that rope still
  exists, otherwise to `"default"`, never into a group, never pinned.
- A group with no cards left is removed.
- Pins are limited to 3 per rope. Pinning a card removes it from its group.
- On load, a pin whose rectangle is no longer at least 50% inside any current
  monitor's work area is moved to the nearest work area (monitor unplugged,
  resolution changed).
- Older Jemur reading a v2 file must not crash. System.Text.Json ignores the
  unknown fields, so a pinned card just shows on the line there. That is acceptable.

## Behaviour

### Ropes

- The line shows only the active rope's unpinned cards; pin windows exist
  only for the active rope's pinned cards.
- **Rope tag:** a small fabric tag at the left end of the rope shows the
  active rope's name. It is visible whenever the rope is expanded and peeks
  with the cards when collapsed. Clicking it opens a menu listing the ropes
  (active one checked), then *New line…*, then *Manage lines…* (opens the
  Control Panel).
- **Switching:** from the tag menu, the tray menu (a *Lines* submenu) or the
  new shortcut **Alt+Shift+G** (`Actions.NextRope = "rope"`, rebindable like
  the others) which cycles to the next rope. Switching while lifted also
  un-lifts.
- New screenshots, notes and pastes go to the active rope.
- **Control Panel:** the Line page gets a rope selector above the tiles (the
  existing search stays). Create, rename and delete ropes there. Tile context
  menus get *Move to line ▸* and *Pin to desktop* / *Back on the line* so every
  action is possible without dragging.
- Tray tooltip counts items on the active rope and includes its name.

### Groups on a rope

- **Create:** right-click a card, then *Group ▸ New group…* (prompts for a name;
  empty means "Group n"), or *Group ▸ <existing group on this rope>*.
- **Join or leave by dragging:** dragging a card along the rope into a group's
  span joins it at that slot. Dragging it more than about half a card past the
  group's edge leaves the group. Drag off the rope still means share, unchanged.
- **Layout:** cards in a group sit side by side with a fixed small gap,
  starting at the group's `Position`. Their own `Position` is ignored while
  grouped and recomputed when they leave. The group's label tag hangs on the
  rope just left of its first card.
- **Move the group:** drag its label along the rope; all its cards follow.
- **Fold:** click the label (or the menu *Fold*). Cards collapse into one
  stack at the group position (slight alternating tilt, top card visible, count
  badge). Clicking the stack unfolds. The folded state is persisted.
- **Label menu:** Rename, Fold/Unfold, Ungroup (cards stay where they are),
  Discard group's cards (to Discarded, recoverable as usual).
- No nesting, and no group spans ropes.

### Desktop pins

- **Pin:** item menu, then *Pin to desktop*. The card unclips from the rope and
  settles about 200 DIP below its rope position (first time), in its own small
  transparent, topmost, no-activate tool window, clothespin on top. It can
  then be dragged anywhere on any monitor.
- **On a pin:** click opens the spotlight; hold marks up an image, as on the
  rope. Drag moves it, with pendulum swing from the clothespin. There is no
  drag-off-to-share and no drop-to-discard. Share, copy, save and discard come
  from the right-click menu, which also has *Back on the line* and
  *Move to line ▸*.
- **Limit:** if three are already pinned on this rope, *Pin to desktop* is
  disabled with the tooltip "Up to 3 pins per line".
- **Back on the line:** the card flies back to a free spot on the rope and swings.
- Pins are not affected by the line's auto-hide ("out of sight"). They stay
  fully visible until lifted.

### Lift toggle

- Replaces the current *Show or hide the line* action, same shortcut
  (**Alt+Shift+J**). **Left-clicking the tray icon now toggles lift**; the
  Control Panel stays in the tray menu and on Alt+Shift+P. (This changes the
  current left-click behaviour, which opens the panel; that change is intended.)
- Lifted hides the line window and all pin windows of the active rope;
  `Settings.Lifted` is saved immediately. Menu labels read *Angkat jemuran* or
  *Jemur lagi* according to state.
- Taking a screenshot, making a note, pasting or switching ropes while lifted
  un-lifts first, so new things are never hidden silently.
- At startup, `Lifted` is honoured: nothing appears, and the tray tooltip says
  it is lifted.
- The old "keep the line expanded" meaning of the toggle goes away; hovering
  the top edge still expands the line as today.

## Motion

All motion uses the existing vocabulary: `BackEase` drops, damped pendulum
(`CardView.Step`), `GhostWindow` falls. Durations are 200–450 ms. Physics runs
only while something moves. Honour Windows "Show animations" (Client Area
Animation off means instant state changes).

| Event | Motion |
|---|---|
| Lift | Cards rise up off the top edge, staggered 30 ms left to right; rope fades; pins lift 24 DIP and fade |
| Hang back out | Rope fades in; cards drop in with the existing new-card drop and swing, staggered; pins drop 24 DIP into place and swing |
| Switch rope | Old cards slide off along the rope to the left (stagger); tag flips to the new name; new cards slide in from the right and swing; old pins fade, new pins fade/drop in |
| Pin | Card lifts off the rope with a small tilt, travels on a short arc to its pin spot, lands with a swing |
| Back on the line | Reverse arc to its rope slot, swing |
| Join or leave group | Neighbours ease (200 ms) to their new slots; label tag slides to the new start |
| Fold / unfold | Cards fan in or out from the stack point (250 ms), slight tilt; badge pops in |
| Group created | Label tag drops onto the rope and swings |

## Polish items in scope

Only what these features touch:

- every new action reachable by keyboard from the Control Panel;
- Narrator names on the rope tag, group labels, folded stacks and pins;
- light/dark and accent correct on all new chrome;
- per-monitor DPI correct for pins;
- the empty states read naturally when a rope is empty but others are not.

## Tests (xUnit)

- v1 and array-format `items.json` load onto `"default"`; v2 round-trips.
- Deleting a rope moves live and discarded items to default, dissolves groups,
  and un-pins.
- Restore lands on the original rope or default.
- Pin limit enforced; pinning removes the card from its group; an empty group is removed.
- Group slot layout is a pure function (positions from group start, card
  widths, gap) and is tested.
- Off-screen pin rectangles are clamped into a work area (pure function).
- `Settings.Normalize` repairs `ActiveRope` and keeps `Lifted`.

## Suggested slicing

1. Data model v2 + migration + ropes (switch, tag, panel, shortcut) + tests.
2. Lift toggle + tray left-click + persistence of `Lifted`.
3. Desktop pins.
4. Groups on a rope.
5. Motion pass (anything not landed with its feature).

## Decisions made by the designer (not discussed with the owner)

These came out of the builder review. Each is the designer's call and can be changed.

1. **Empty rope.** If there is more than one rope, the active rope stays
   on screen even when it has no cards on the line. It shows as the collapsed
   strip with its tag peeking, so the tag is always reachable for switching.
   With only the default rope and no cards, the line hides as it does today.
2. **Panel selector switches.** The rope selector in the Control Panel
   changes the active rope on screen; there is no separate "viewed rope". The
   panel's New note, Paste and Screenshot buttons go to the active rope.
3. **Move to line.** A card moved to another rope is always un-pinned and
   leaves its group. It lands at a free spot on the target rope's line. If the
   target rope is not active, the card shows a toast "Moved to <rope>" before it
   leaves the screen. Jemur does not switch ropes.
4. **Discarded stays global.** The Discarded page and tray *Restore last*
   cover all ropes, and each Discarded tile shows its rope name. *Discard all*
   only affects the active rope's line (not its pins). Restore to a rope that is
   not active works silently, except that the panel tile shows where it went. Restore
   while lifted un-lifts only if the item goes to the active rope.
5. **Pins hide during capture.** Pins are hidden before a screenshot, like
   the line, and come back afterwards.
6. **Pin coordinates** are stored in physical pixels of the virtual
   screen (`PinLeft`, `PinTop` of the card's top-left). They are converted with
   the target monitor's DPI when the pin window is placed. The clamp-to-work-area
   function works in physical pixels.
7. **Downgrade.** Before the first v2 write, Jemur copies the v1 file to
   `items.v1-backup.json`. Running an older Jemur after upgrading is not
   supported. An edit there flattens ropes into one. This is noted in
   `CHANGELOG.md`.

## Left to the builder

Exact window and class structure (e.g. a `PinWindow`, a group layout
helper), the rename prompt's UI as long as it matches existing Fluent styling,
exact easing curves inside the stated ranges, tag and badge artwork matching
the clothespin and paper look, and the wording of the translations.
