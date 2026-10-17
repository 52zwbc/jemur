# Cloudflare CI/CD

Approved in chat: use the Syeana Cloudflare account, build/release on Cloudflare,
and run Linux-compatible tests there. WPF tests remain a Windows check.

Pushes to `main` build Windows x64 and ARM64 packages, in framework-dependent and
self-contained variants. Cloudflare runs the portable storage/localization tests
and compiles the full Windows solution before publishing. A failed build leaves
the previous download deployment active.

Workers Static Assets stores the packages. ZIP files are divided into 20 MiB
assets and streamed as one normal ZIP by a download Worker, respecting the 25 MiB
per-asset limit without requiring R2 activation. The manifest records the commit,
version, sizes and SHA-256 checksums. Only the current successful build is linked;
this is not a historical release archive. GitHub remains the source host; GitHub
Actions is disabled and is not used to build or deploy.

The target account is Syeana (`c595078d172b45beb8df900b7cbb24cb`), selected by the
user. Payment and changes to billing are outside this task.

Validation: run the original Windows suite, portable tests, download Worker tests,
all four publish targets, and then the actual Cloudflare build. Verify the live
commit marker and downloaded ZIP checksum against the deployed manifest.
