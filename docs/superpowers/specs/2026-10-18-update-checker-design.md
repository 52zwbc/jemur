# Jemur update checker design

## Goal

Add a safe update checker to the Jemur Windows desktop application. Users can
check manually from About, and Jemur can perform a low-frequency background
check while it is running. The feature reports a newer release and lets the
user open the official release page. It never downloads, installs, replaces,
or restarts Jemur automatically.

## Scope

The first version includes:

- a reusable update metadata client;
- semantic version comparison against `JemurApp.Version`;
- a manual check action in the About page;
- a delayed startup check after Jemur has initialized;
- a maximum once-per-24-hour background interval while the process is running;
- a tray notification when a newer version is found;
- an About status row showing the result of the most recent check;
- an explicit link to the GitHub release page for the selected version;
- localized strings in every bundled locale;
- tests for parsing, comparison, failures, scheduling and accessibility.

The feature does not add telemetry, account identifiers, a service, a startup
task, a persistent download cache, silent installer execution, or an automatic
restart. It does not change the existing library, notes, screenshots or
settings format.

## Metadata contract

The client requests:

`https://jemur-downloads.syeana-co.workers.dev/manifest.json`

The existing manifest is the source of truth for the build. The client reads:

- `version`: a strict semantic version, optionally with a prerelease suffix;
- `commit`: a 40-character lowercase Git commit SHA;
- `files`: the existing package list, used only to ensure the response is a
  real Jemur manifest;
- `releaseUrl` when present, otherwise the known GitHub repository release URL
  for the version tag.

The client rejects an HTTP error, timeout, malformed JSON, an invalid version,
an invalid commit, or a response without a usable release URL. The response is
bounded by a small timeout and size limit. A failed check leaves the current
application state untouched.

## Version and result model

An internal result type distinguishes:

- `UpdateAvailable`: remote version is newer, with version, commit and URL;
- `UpToDate`: remote version is equal to or older than the local version;
- `Failed`: a user-facing localized failure message and diagnostic exception
  for the log.

Semantic comparison handles major, minor, patch and prerelease identifiers;
it does not compare display strings or rely on lexicographical ordering.

The client is independent of WPF and the tray. This keeps parsing and network
behavior testable in the portable test project and lets the UI decide how to
present each result.

## Manual flow

The About page contains an update card below the version display:

1. The card shows `Check for updates` until a result exists.
2. Clicking it disables the button while the request is in flight and shows a
   localized checking state.
3. For an equal or older version, it shows the local version and the time of
   the check.
4. For a newer version, it shows the remote version and a `Download installer`
   button that opens the official GitHub release URL with the existing `Open`
   helper.
5. For a failure, it shows a short retryable error and leaves the manual
   button available.

The controls have accessible names, help text where useful, visible keyboard
focus, and remain usable when the network is unavailable.

## Background flow

After the normal application startup has created the tray and loaded the
library, Jemur schedules one check after approximately 30 seconds. A dispatcher
timer then permits another check no sooner than 24 hours later while the
process remains alive. The timer is stopped during shutdown.

Background checks are skipped when a check is already running. They do not
show a failure balloon for ordinary network errors. When a newer version is
found, Jemur sends one tray notification per remote version per process. The
notification points users to the About page through the existing tray click;
the About page exposes the release URL. Background checks never open a browser
or download a file on their own.

The initial delay and interval are constants in the update service, not user
settings, so this feature does not add another preference surface.

## Error handling and security

- Use `HttpClient` with a short timeout and cancellation token.
- Require HTTPS and the exact configured host.
- Reject oversized or malformed responses before deserialization completes.
- Never execute any downloaded content.
- Log technical details locally through `Log.Warn` while exposing a concise
  localized message to the user.
- Treat every remote field as untrusted data; render it as text and use only
  the validated release URL.
- A check failure must never block application startup, hotkeys, the tray, or
  the control panel.

## Test plan

Portable tests cover:

- valid stable and prerelease manifests;
- equal, older and newer semantic versions;
- missing fields, invalid commit/version, invalid release URL and oversized
  response;
- HTTP errors, cancellation and timeout;
- release URL fallback construction.

Windows tests cover:

- About card rendering and accessible names;
- manual check loading, success, update and failure states using a fake client;
- background scheduling with a controllable clock/timer abstraction;
- one notification per version per process;
- clean shutdown of the timer;
- keyboard focus and activation of the download action.

The existing Windows, portable and Cloudflare download/release suites remain
required gates. No live network call is made by unit tests.

## Acceptance criteria

- A user can see and manually check for updates from About.
- A newer verified version produces a clear tray notification during normal
  background operation.
- The user must choose to open the release page and start the installer.
- Offline or invalid metadata cannot break startup or existing Jemur data.
- The behavior is localized and accessible across all bundled languages.
- Existing release packaging continues to publish EXE installers and portable
  EXEs unchanged.
