# Contributing

Thanks for wanting to help. Bug reports, translations and small focused pull requests are all welcome.

## Build and run

You need Windows 10 (1809+) or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/revanapriyandi/jemur
cd jemur
dotnet run --project src
```

To keep your real screenshots and notes out of the way while developing, point Jemur at a scratch folder:

```powershell
$env:JEMUR_DATA = "$env:TEMP\jemur-dev"
dotnet run --project src
```

Tests:

```powershell
dotnet test
```

## Project layout

```
src/
  App/        startup, single instance, every user-facing action (JemurApp)
  Core/       data model, storage, localization, hotkey parsing — no windows here
  Interop/    Win32: tray icon, global shortcuts, screen capture, memory trimming
  UI/         theme tokens, Fluent control styles (Controls.xaml), small UI helpers
  Windows/    the line, spotlight, capture overlay, mark-up editor, control panel
  Assets/     icons (regenerate with assets/brand/render-icons.py)
locales/      UI text, one JSON file per language
tests/        xUnit tests
```

## Guidelines

- **Keep it light.** Jemur sits in the tray all day. Avoid new dependencies, timers that tick while idle, `DropShadowEffect` on long-lived elements, and decoding images at full size when a thumbnail will do. See [docs/MEMORY.md](docs/MEMORY.md).
- **Feel native.** Follow Windows 11 conventions: Segoe UI, 4 px corners on controls, theme brushes from `Theme`/`Controls.xaml` (never hard-coded colours in the chrome), sentence-case labels.
- **Every string goes in `locales/en.json`.** Use `L.T("key")`. The tests fail if a key is missing.
- **Don't break people's data.** `items.json` and `settings.json` must keep loading files written by older versions.
- One change per pull request, with a short note on how you tested it (scaling, light/dark, multiple monitors if relevant).

## Screenshots

The control panel and mark-up images in `docs/images` are rendered straight from the app, off screen, so they never contain anything from your desktop:

```powershell
$env:JEMUR_DATA = "C:\path\to\demo-data"   # a folder with items.json + shots\
.\Jemur.exe --render-docs out
```

`docs/images/social-preview.png` is the image to upload under the repository's *Settings → Social preview*.

## Releasing (maintainers)

Bump `<Version>` in `src/Jemur.csproj`, update `CHANGELOG.md`, and run the full Windows suite before pushing to `main`:

```powershell
dotnet test Jemur.sln -c Release
git push origin main
```

Cloudflare Workers Builds runs portable tests, compiles the Windows solution,
builds self-contained portable EXEs and NSIS setup EXEs for x64 and ARM64, and
updates the download service. WPF tests and installer smoke checks run on Windows.
See [CI/CD](docs/CI-CD.md) for setup, commands and verification. After public
download checks pass, Cloudflare automatically publishes a GitHub Release for a
new application version using the exact manifest commit. Existing published
versions are skipped without changing their tag or assets. Increase `<Version>`
and update the changelog to publish another release; tags alone do not trigger it.
