# CI/CD

Jemur builds and publishes downloads using Cloudflare Workers Builds in the
**Syeana** account (`c595078d172b45beb8df900b7cbb24cb`). GitHub hosts the source;
GitHub Actions is not used. Pushing a version tag alone does not publish a build.

## Cloudflare settings

- Worker: `jemur-downloads`
- Repository: `revanapriyandi/jemur`
- Production branch: `main`; disable non-production branch builds
- Root directory: `/`
- Build command: `bash .cloudflare/build.sh`
- Deploy command: `bash .cloudflare/deploy.sh`
- Dependency installation: `npm ci` from the committed lockfile
- No R2 binding, GitHub token or application data is needed

The Worker is configured in `wrangler.jsonc`. Build/deploy permissions are supplied
by Cloudflare's build integration; do not commit tokens. The `.NET 10.0.402` SDK is
installed from Microsoft's official installer when it is not available.

## Checks and packages

1. Node tests exercise streamed EXE downloads, checksum headers, missing chunks,
   HEAD/conditional requests, old standalone links and public routes. Chunks are
   fetched one at a time and piped through Cloudflare's native FixedLengthStream
   to keep asset connections bounded and enforce the response Content-Length.
2. `dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release` runs the
   existing library and localization tests against the actual linked production
   sources. No WPF types are replaced with stubs.
3. `dotnet build Jemur.sln -c Release -p:EnableWindowsTargeting=true` compiles
   the application and the Windows test assembly. WPF tests cannot run on Linux.
4. `dotnet publish` creates self-contained single-file EXEs for x64 and ARM64.
   Native libraries are bundled for extraction by .NET. Embedded assemblies are
   compressed, and ReadyToRun is disabled for cross-platform compilation.
5. `.cloudflare/nsis.sh` downloads NSIS `3.08-3+deb12u1` from Debian's official
   HTTPS mirror, checks pinned SHA-256 hashes and extracts it without root.
   Sources: [compiler](https://packages.debian.org/bookworm/amd64/nsis/download)
   and [compiler data](https://packages.debian.org/bookworm/all/nsis-common/download).
6. Python checks that each publish directory contains exactly one EXE with the
   expected PE architecture, copies the portable EXEs, and compiles
   `packaging/installer.nsi` into setup EXEs. Every EXE includes the runtime.
   Files are split into 20 MiB static assets; the Worker streams one EXE back.
   The manifest records architecture, package type, source commit and SHA-256.
7. The deploy script rejects a checkout that no longer matches `origin/main`,
   deploys the Worker, verifies the live source commit, and downloads all four
   public EXEs to check byte counts, SHA-256 and Windows executable magic. It
   retries health and manifest checks while the new deployment propagates,
   requiring both endpoints to report the expected commit before downloading.

Run the complete suite on Windows before publishing:

```powershell
dotnet test Jemur.sln -c Release
npm ci
npm run test:downloads
```

The portable project does not replace the Windows suite. In particular, hotkey
parsing and WPF runtime behavior still require Windows validation.

Before publishing a GitHub release, verify the Cloudflare artifacts. On Windows,
run the portable EXE, install the setup EXE, launch the installed application,
check its shortcuts and installed-apps entry, then uninstall it. Confirm that
user data is retained and repeat installation to test upgrades. ARM64 execution
requires an ARM64 Windows device.

`Jemur.exe --render-docs <folder>` provides an off-screen WPF smoke check. Set
`JEMUR_DATA` to an isolated folder with an existing `data` subfolder. This does
not replace interactive screenshot, shortcut and drag-and-drop checks.

## Installer behavior

The installer uses the current user's account and defaults to
`%LOCALAPPDATA%\Programs\Jemur`. It adds Start menu shortcuts, an optional desktop
shortcut and an entry in Windows' installed-apps list. Close Jemur from its tray
menu before upgrading or uninstalling. ARM64 setup rejects non-ARM64 Windows.
Uninstall removes the installed application and its registration while retaining
the library and settings in `%APPDATA%\Jemur`.

## Downloads and verification

- [x64 portable EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-x64)
- [x64 setup EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-x64-setup)
- [ARM64 portable EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-arm64)
- [ARM64 setup EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-arm64-setup)
- [Checksums](https://jemur-downloads.syeana-co.workers.dev/SHA256SUMS.txt)
- [Manifest](https://jemur-downloads.syeana-co.workers.dev/manifest.json)
- [Deployed commit](https://jemur-downloads.syeana-co.workers.dev/health)

`/latest/<variant>` redirects to the current package. The public manifest records
its version, full source commit, filename, size and SHA-256. Match the live commit
to the Cloudflare build and verify downloaded EXEs with `Get-FileHash -Algorithm
SHA256`. Old `-standalone` links redirect to the corresponding portable EXE.
Cloudflare tracks the latest successful `main` build. Failed builds leave the
previous deployment active.

A maintainer publishes versioned GitHub Releases separately using the verified
four EXEs, manifest and checksums, targeting the exact manifest commit. This uses
GitHub's release API and does not require GitHub Actions. Version tags alone do
not create builds or releases. Keep published version tags immutable.

Workers requests and build minutes use the account's existing plan and limits.
No paid plan or additional storage subscription is enabled by this setup.
