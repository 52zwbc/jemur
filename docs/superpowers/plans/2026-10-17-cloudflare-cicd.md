# Cloudflare CI/CD Implementation Plan

> **For agentic workers:** execute inline with the executing-plans skill.

**Goal:** Replace GitHub Actions with Cloudflare builds and downloadable Windows packages.

**Architecture:** Keep WPF and Windows tests intact. Link the existing portable
production sources and storage/localization tests into a Linux test project.
Cloudflare builds packages and atomically deploys a Worker with static ZIP chunks.

**Tech Stack:** .NET 10, xUnit, Python standard library, Node test runner, Wrangler.

## Global Constraints

- MOC Cloudflare account `18e1caedb6e3a167e1702932a4ecf232`.
- Windows x64 and ARM64, framework-dependent and standalone ZIPs.
- Do not claim WPF tests ran on Linux; no additional Windows runner.
- No R2 activation, billing changes, GitHub Actions, or embedded credentials.

## Tasks

- [ ] Add `tests/portable/Jemur.Portable.Tests.csproj`, reusing the existing tests
  and production source. Use plain file creation for fake screenshots in storage tests.
  Run `dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release` and
  `dotnet test Jemur.sln -c Release`; expect 11 portable and 25 Windows tests.
- [ ] Add `.cloudflare/build.sh`, `package.py`, the streaming download Worker,
  and `wrangler.jsonc`. Test using `node --test .cloudflare/download.test.mjs`,
  publish all four variants, package them, and check ZIPs and checksums.
- [ ] Replace the old workflow documentation with `docs/CI-CD.md`; remove the
  disabled GitHub workflow files and update download links.
- [ ] Connect `main` to Cloudflare Workers Builds with build command
  `bash .cloudflare/build.sh` and deploy command `bash .cloudflare/deploy.sh`.
  Inspect the actual build logs, live `/health`, manifest and downloaded checksums.
