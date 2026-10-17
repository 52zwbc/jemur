#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
commit="$(git rev-parse HEAD)"
remote_commit="$(git ls-remote origin refs/heads/main | cut -f1)"
if [[ "$commit" != "$remote_commit" ]]; then
  echo 'Refusing to replace downloads with an outdated build.' >&2
  exit 1
fi
npx wrangler deploy
curl --retry 5 --retry-delay 3 --retry-all-errors --fail --silent --show-error \
  "https://jemur-downloads.syeana-co.workers.dev/health?commit=$commit" \
  | node --input-type=module -e '
    let input = "";
    for await (const chunk of process.stdin) input += chunk;
    const health = JSON.parse(input);
    if (health.commit !== process.argv[1]) throw new Error("Deployed commit does not match build");
    console.log("Verified deployed commit: " + health.commit);
  ' "$commit"
node .cloudflare/verify-live.mjs "$commit"
