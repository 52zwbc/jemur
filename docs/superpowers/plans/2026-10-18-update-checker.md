# In-app and background update checker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (&#96;- [ ]&#96;) syntax for tracking.

**Goal:** Add a localized manual and background update checker that reports newer Jemur releases without downloading, installing, or replacing application files automatically.

**Architecture:** Keep HTTP, manifest validation and semantic-version comparison in a WPF-independent core service so portable tests can exercise it without a live network. Add a small application coordinator that owns the 30-second startup delay, 24-hour cadence, deduplicated tray notification, and shutdown cancellation. Let the About page consume coordinator results and open the validated GitHub release URL only after an explicit user action.

**Tech Stack:** C#/.NET 10, WPF, HttpClient, System.Text.Json, existing TrayIcon, existing localization JSON files, xUnit, the local .NET SDK at artifacts/tools/dotnet/dotnet.exe.

## Global Constraints

- Never download, execute, replace, or restart the Jemur executable automatically.
- Request only https://jemur-downloads.syeana-co.workers.dev/manifest.json.
- Require a valid semantic version, a 40-character lowercase commit SHA, a usable release URL, and a bounded response.
- A network or metadata failure must never block startup, hotkeys, the tray, the control panel, or existing user data.
- Background checks run once about 30 seconds after startup and no sooner than once per 24 hours while the process is alive.
- Notify once per remote version per process; ordinary background failures stay quiet.
- Route every visible string through locales/en.json and L.T/L.F; keep all bundled locales key-complete and placeholder-compatible.
- Preserve the existing WPF/Fluent visual language, portable EXE, installer, Cloudflare build and GitHub release flows.
- Unit tests must not call the live network.

---

### Task 1: Define and test the portable manifest/version client

**Files:**
- Create: src/Core/UpdateChecker.cs
- Test: tests/portable/UpdateCheckerTests.cs
- Modify: tests/portable/Jemur.Portable.Tests.csproj

**Interfaces:**
- Produces internal sealed record UpdateManifest(string Version, string Commit, Uri ReleaseUrl).
- Produces internal enum UpdateCheckKind { UpToDate, UpdateAvailable, Failed }.
- Produces internal sealed record UpdateCheckResult(UpdateCheckKind Kind, UpdateManifest? Manifest, string? Error).
- Produces internal interface IUpdateChecker with Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken).
- Produces internal sealed class UpdateChecker with constructor (HttpClient httpClient, string localVersion, Uri manifestUri, string repositoryUrl).
- Produces internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) with TryParse and CompareTo.

- [ ] Step 1: Write failing portable tests for semantic ordering and manifest validation.

Cover equal, newer, older and prerelease ordering; valid JSON; invalid version,
commit, URL and root JSON; HTTP failure; cancellation; and release URL fallback.
Use a local HttpMessageHandler test double that returns HttpResponseMessage
instances. Do not call the Cloudflare URL.

~~~csharp
[Theory]
[InlineData("0.1.3", "0.1.3", 0)]
[InlineData("0.1.4", "0.1.3", 1)]
[InlineData("0.1.3", "0.1.3-beta.1", 1)]
public void SemanticVersionsUseReleaseOrdering(string left, string right, int expected) { }
~~~

- [ ] Step 2: Run the focused portable tests and confirm they fail.

Run:

~~~powershell
$env:DOTNET_ROOT="$PWD/artifacts/tools/dotnet"
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
& "$env:DOTNET_ROOT/dotnet.exe" test tests/portable/Jemur.Portable.Tests.csproj -c Release --filter FullyQualifiedName~UpdateCheckerTests
~~~

Expected: compile/test failure because the update types do not exist yet.

- [ ] Step 3: Implement the minimal client.

Implement numeric major/minor/patch comparison with SemVer prerelease
precedence. CheckAsync must use a linked 5-second timeout, GET with
ResponseHeadersRead, reject non-success status, read at most 32 KiB, deserialize
case-insensitively, validate the version, lowercase 40-character commit and
HTTPS GitHub URL, fall back to repositoryUrl/releases/tag/v{version}, compare
with the local version, and return Failed while logging through Log.Warn rather
than throwing.

- [ ] Step 4: Run the focused tests and commit.

Expected: all UpdateCheckerTests pass.

~~~powershell
git add src/Core/UpdateChecker.cs tests/portable/UpdateCheckerTests.cs tests/portable/Jemur.Portable.Tests.csproj
git commit -m "feat: add validated update manifest client"
~~~

### Task 2: Add application scheduling and tray notification

**Files:**
- Create: src/App/UpdateCoordinator.cs
- Modify: src/App/JemurApp.cs
- Test: tests/portable/UpdateCoordinatorTests.cs
- Modify: tests/portable/Jemur.Portable.Tests.csproj

**Interfaces:**
- Produces internal sealed class UpdateCoordinator : IDisposable.
- Constructor: (IUpdateChecker checker, Action<string,string,bool> notify, Action showPanel, TimeProvider clock, TimeSpan initialDelay, TimeSpan interval).
- Methods: Start(), Task<UpdateCheckResult> CheckNowAsync(CancellationToken), Dispose().
- JemurApp owns one coordinator after the tray is created, calls Start, routes showPanel to ShowPanel, and disposes it in Stop.

- [ ] Step 1: Write failing tests for delayed startup, 24-hour cadence, quiet failures, deduplicated notification and disposal.
- [ ] Step 2: Run the focused tests and confirm they fail because the coordinator does not exist.
- [ ] Step 3: Implement the coordinator with a dispatcher-safe callback supplied by JemurApp. Schedule at 30 seconds and then 24 hours, serialize checks with an in-flight guard, track the last notified remote version in memory only, notify once per newer version, and never open a browser during background work.
- [ ] Step 4: Wire the coordinator after _tray.Show in JemurApp.Start and dispose it before _tray.Dispose in Stop. Catch startup exceptions so Jemur still starts.
- [ ] Step 5: Run focused coordinator and full portable tests, then commit.

~~~powershell
git add src/App/UpdateCoordinator.cs src/App/JemurApp.cs tests/portable/UpdateCoordinatorTests.cs tests/portable/Jemur.Portable.Tests.csproj
git commit -m "feat: check for releases in the background"
~~~

### Task 3: Add localized About update UI and manual action

**Files:**
- Modify: src/Windows/ControlPanel.cs
- Modify: src/App/JemurApp.cs
- Modify: src/UI/Ui.cs only if a small reusable status/action helper is needed
- Modify: locales/en.json, locales/id.json, locales/es.json, locales/pt-BR.json, locales/ja.json, locales/ms.json
- Test: tests/AccessibilityTests.cs

**Interfaces:**
- JemurApp exposes internal IUpdateChecker UpdateChecker and internal Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken).
- ControlPanel.AboutPage owns an update card and refreshes it after the async result returns.

- [ ] Step 1: Add these keys to every locale with matching placeholders: about.updates, about.updatesDesc, about.checkUpdates, about.checkingUpdates, about.upToDate, about.updateAvailable, about.downloadInstaller, about.updateFailed, about.lastChecked.
- [ ] Step 2: Add failing WPF assertions for localized accessible names, help text, keyboard focus and the download button.
- [ ] Step 3: Implement the About card below the version row using existing spacing, icons and button styles. Show the current status, Check for updates, and only for UpdateAvailable a Download installer button that calls the existing Open helper with the validated URL.
- [ ] Step 4: Disable the manual button while checking, update the checking state, marshal the result to the dispatcher, keep failures retryable, and prevent stale rebuilt/closed pages from being updated.
- [ ] Step 5: Run all Windows tests and commit.

~~~powershell
git add src/Windows/ControlPanel.cs src/App/JemurApp.cs src/UI/Ui.cs locales/*.json tests/AccessibilityTests.cs
git commit -m "feat: expose manual update checks in About"
~~~

### Task 4: Update documentation and versioned release metadata

**Files:**
- Modify: README.md
- Modify: CHANGELOG.md
- Modify: docs/CI-CD.md if the current release documentation needs the manifest endpoint described
- Modify: src/Jemur.csproj

- [ ] Step 1: Document the manual About check, quiet delayed/24-hour background check, tray notification, and explicit release-page download.
- [ ] Step 2: Add the changelog entry and bump the next patch version without claiming automatic installation.
- [ ] Step 3: Render About in light and dark themes with isolated JEMUR_DATA, inspect wrapping and focusable controls, then commit.

~~~powershell
git add README.md CHANGELOG.md docs/CI-CD.md src/Jemur.csproj
git commit -m "docs: document Jemur update checks"
~~~

### Task 5: Run complete verification and publish

**Files:** No new production files; inspect the complete diff and release artifacts.

- [ ] Step 1: Run all local gates.

~~~powershell
$env:DOTNET_ROOT="$PWD/artifacts/tools/dotnet"
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
& "$env:DOTNET_ROOT/dotnet.exe" test Jemur.sln -c Release --logger "console;verbosity=minimal"
& "$env:DOTNET_ROOT/dotnet.exe" test tests/portable/Jemur.Portable.Tests.csproj -c Release --logger "console;verbosity=minimal"
npm run test:downloads
git diff --check
git status --short --branch
~~~

Expected: all Windows, portable and Cloudflare tests pass.

- [ ] Step 2: Push after local gates pass and confirm Cloudflare Workers Builds uses the pushed commit, completes its tests, builds both architectures, deploys the Worker and publishes only after manifest and SHA-256 verification.
- [ ] Step 3: Verify the public manifest version and commit, the GitHub release is published and non-draft, and the latest EXE/installer links return expected assets. Launch the new binary with isolated data, open About and run a manual check.
- [ ] Step 4: Record the release URL, Cloudflare manifest version/commit, test totals, and unverified boundaries such as Narrator speech or ARM64 hardware.
