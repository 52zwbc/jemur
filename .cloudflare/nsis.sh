#!/usr/bin/env bash
set -euo pipefail

nsis_root="$PWD/.nsis"
mkdir -p "$nsis_root/downloads"
for package in nsis_3.08-3+deb12u1_amd64.deb nsis-common_3.08-3+deb12u1_all.deb; do
  curl --fail --silent --show-error --location --retry 3 \
    "https://deb.debian.org/debian/pool/main/n/nsis/$package" -o "$nsis_root/downloads/$package"
done
(
  cd "$nsis_root/downloads"
  printf '%s\n' \
    'b9ca8de84341753dd1c071a3f65453dc06bd7b6f2230140a36755d7e402827c2  nsis_3.08-3+deb12u1_amd64.deb' \
    'f1c9e63389c947442fddd5ca446e22966e8f03fc10663f998cf7df58642f9b52  nsis-common_3.08-3+deb12u1_all.deb' \
    | sha256sum --check -
)
for package in "$nsis_root"/downloads/*.deb; do
  dpkg-deb --extract "$package" "$nsis_root"
done
export NSISDIR="$nsis_root/usr/share/nsis"
export PATH="$nsis_root/usr/bin:$PATH"
makensis -VERSION
