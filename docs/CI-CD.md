# CI/CD

Jemur builds and publishes downloads using Cloudflare Workers Builds in the
**MOC** account (`18e1caedb6e3a167e1702932a4ecf232`). GitHub hosts the source;
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

1. Node tests exercise streamed ZIP downloads, checksum headers, missing chunks,
   HEAD/conditional requests and public routes.
2. `dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release` runs the
   existing library and localization tests against the actual linked production
   sources. No WPF types are replaced with stubs.
3. `dotnet build Jemur.sln -c Release -p:EnableWindowsTargeting=true` compiles
   the application and the Windows test assembly. WPF tests cannot run on Linux.
4. `dotnet publish` produces x64 and ARM64 packages, each with and without the
   .NET runtime. ReadyToRun is disabled for these cross-platform builds; the
   application otherwise retains its existing runtime configuration.
5. Python's standard library creates ZIPs and SHA-256 checksums. ZIPs are split
   into 20 MiB static assets; the Worker streams their bytes back as one ZIP.
6. The deploy script rejects a checkout that no longer matches `origin/main`,
   deploys the Worker and verifies the live source commit.

Run the complete suite on Windows before publishing:

```powershell
dotnet test Jemur.sln -c Release
npm ci
npm run test:downloads
```

The portable project does not replace the Windows suite. In particular, hotkey
parsing and WPF runtime behavior still require Windows validation.

## Downloads and verification

- [x64](https://jemur-downloads.moc-18e.workers.dev/latest/win-x64)
- [x64 standalone](https://jemur-downloads.moc-18e.workers.dev/latest/win-x64-standalone)
- [ARM64](https://jemur-downloads.moc-18e.workers.dev/latest/win-arm64)
- [ARM64 standalone](https://jemur-downloads.moc-18e.workers.dev/latest/win-arm64-standalone)
- [Checksums](https://jemur-downloads.moc-18e.workers.dev/SHA256SUMS.txt)
- [Manifest](https://jemur-downloads.moc-18e.workers.dev/manifest.json)
- [Deployed commit](https://jemur-downloads.moc-18e.workers.dev/health)

`/latest/<variant>` redirects to the current package. The public manifest records
its version, full source commit, filename, size and SHA-256. Match the live commit
to the Cloudflare build and verify downloaded ZIPs with `Get-FileHash -Algorithm
SHA256`. Only the latest successful `main` build is linked, not a historical
release archive. Failed builds leave the previous deployment active.

Workers requests and build minutes use the account's existing plan and limits.
No paid plan or additional storage subscription is enabled by this setup.
