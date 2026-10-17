# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.1.1] - 2026-10-18

### Added
- Per-user EXE installers for x64 and ARM64, with Start menu shortcuts and a Windows uninstall entry.
- An optional desktop shortcut. Uninstalling preserves the library and settings.

### Changed
- Application downloads are self-contained portable EXEs and setup EXEs instead of ZIPs.
- Cloudflare builds both installer architectures on Linux and verifies all four public executable downloads.
- Successful Cloudflare builds automatically create a GitHub Release for each new application version, after checking all uploaded asset hashes.
- Download chunks are fetched on demand to keep streaming connections bounded.

## [0.1.0] — 2026-10-17

First public release.

### Added
- A clothesline across the top of the screen that holds screenshots and notes.
- Screenshot capture: drag an area, click a window, or press Enter for the whole screen.
- Notes that open as a sheet of paper in the middle of the screen.
- Drag a card into any app to share it; drop it on empty space to let it go.
- Mark-up editor with pen, highlighter, box, arrow, eraser and undo/redo.
- Tucks away when idle, leaving only the bottom edges of the cards visible.
- Control panel with a library of everything on the line, a discarded pile kept for 1, 7 or 30 days, and settings.
- Customisable global shortcuts.
- Light and dark theme following Windows, with the system accent colour and Mica.
- English and Indonesian, with drop-in JSON translations.
