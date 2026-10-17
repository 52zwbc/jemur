# Control panel accessibility check

Checked on Windows 11, 18 October 2026, using an isolated library.

## Findings and fixes in 0.1.3

| Area | Finding | Change |
| --- | --- | --- |
| Header actions | Buttons containing icons and text exposed an empty UI Automation name. | Explicit localized action names. |
| Settings | Dropdowns had no name; switches exposed only On or Off. | Names include the setting title and dropdown value; descriptions are exposed as help text. Switches retain the standard toggle pattern. |
| Shortcuts | Key-cap content had no accessible action or shortcut name. | Names include both the action and current shortcut, including recording state. |
| Shortcut recording | Tab was consumed as an invalid shortcut; Esc reached the window first and closed it. | Tab exits recording and moves focus. Esc cancels recording while keeping the panel open. |
| Search | A populated search field had help text but no stable name. | Search retains its localized name regardless of its contents. |
| Library cards | Opening a card required a mouse; action buttons were invisible during keyboard focus. | Cards are buttons supporting Enter/Space. Focus reveals the action bar. Shift+F10 or the menu key opens the item menu. |
| Note colors | Color swatches responded only to mouse clicks. | Named, checked menu items expose every color to keyboard and screen-reader users. |
| Navigation | Rebuilding the side navigation removed the focused button. | Restore focus to the selected navigation item. |

## Verification

- WPF regression tests cover button names, setting labels/help text, shortcut
  names/recording state, Tab/Esc cancellation, and a populated search field.
- Native Windows inspection confirmed the localized action names, setting/value
  names and shortcut values in the UI Automation tree.
- Keyboard inspection confirmed visible focus, Tab navigation through Settings,
  Enter opening a dropdown, arrow-key navigation, Esc returning to its button,
  Enter starting recording, Esc cancelling it without closing the panel, and Tab
  leaving recording without modifying the shortcut.
- Native library inspection confirmed Ctrl+F focuses search, Tab reaches a card
  with a visible focus outline and visible action buttons, and Shift+F10 opens
  the item menu.
- Six languages were rendered in light and dark themes. Translation key and
  placeholder checks pass; native-speaker review remains welcome.

Narrator speech output was not verified. Narrator did not remain running when
launched in the test session, and the maintainer opted to skip that check.
This report covers the control panel and Settings; it is not a full accessibility
certification of capture, markup, overlay cards, or every screen-reader mode.

## Recheck with Narrator

1. Enable Narrator, then open Jemur and move through navigation using Tab,
   arrow keys and Space.
2. On Settings, verify the setting title, selected value, toggle state and help
   text are announced. Change a dropdown and confirm its name updates.
3. Start recording a shortcut with Enter. Check the recording announcement,
   cancel with Esc, then start again and leave with Tab.
4. On the line, use Ctrl+F, Tab to a card, open it with Enter/Space, and use
   Shift+F10 followed by arrow keys to reach the named note colors.
