#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

commit="$(git rev-parse HEAD)"
printf 'Checkout commit: %s; Cloudflare commit: %s\n' "$commit" "${WORKERS_CI_COMMIT_SHA:-unset}"
if [[ -n "${WORKERS_CI_COMMIT_SHA:-}" && "$commit" != "$WORKERS_CI_COMMIT_SHA" ]]; then
  echo 'Checkout does not match the Cloudflare build commit.' >&2
  exit 1
fi

if ! dotnet --version >/dev/null 2>&1; then
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh -o /tmp/jemur-dotnet-install.sh
  bash /tmp/jemur-dotnet-install.sh --version 10.0.402 --install-dir "$PWD/.dotnet" --no-path
  export DOTNET_ROOT="$PWD/.dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

node --test .cloudflare/download.test.mjs
dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release --logger 'trx;LogFileName=portable.trx'
dotnet build Jemur.sln -c Release -p:EnableWindowsTargeting=true
echo 'WPF tests are compiled above; running them requires Windows.'

for rid in win-x64 win-arm64; do
  dotnet publish src/Jemur.csproj -c Release -r "$rid" --self-contained false \
    -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:PublishReadyToRun=false -o "artifacts/publish/$rid"
  dotnet publish src/Jemur.csproj -c Release -r "$rid" --self-contained true \
    -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:PublishReadyToRun=false \
    -p:IncludeNativeLibrariesForSelfExtract=true -o "artifacts/publish/$rid-standalone"
done

python3 .cloudflare/package.py "$commit"
