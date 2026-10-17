#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
commit="$(git rev-parse HEAD)"
: "${JEMUR_RELEASE_TOKEN:?Configure the Jemur GitHub release token as a Cloudflare build secret}"
remote_commit="$(git ls-remote origin refs/heads/main | cut -f1)"
if [[ "$commit" != "$remote_commit" ]]; then
  echo 'Refusing to replace downloads with an outdated build.' >&2
  exit 1
fi
npx wrangler deploy
node .cloudflare/verify-live.mjs "$commit"
node .cloudflare/release.mjs "$commit"
