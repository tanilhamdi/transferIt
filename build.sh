#!/usr/bin/env bash
# Builds standalone Linux packages of transferit into ./dist/.
# Each package (dist/transferit-<arch>.tar.gz) contains the program + install.sh
# and runs on a machine WITHOUT .NET installed.
#
#   ./build.sh            build x64 and arm64
#   ./build.sh linux-x64  build just one
set -euo pipefail

cd "$(dirname "$0")"
command -v dotnet >/dev/null || { echo "error: dotnet SDK not found" >&2; exit 1; }

rids=("$@")
[ ${#rids[@]} -eq 0 ] && rids=(linux-x64 linux-arm64)

rm -rf dist
mkdir -p dist

for rid in "${rids[@]}"; do
  echo "==> Building $rid"
  out="dist/transferit-$rid"
  dotnet publish -c Release -r "$rid" -o "$out" --nologo -v quiet
  cp install.sh "$out/"
  chmod +x "$out/transferit" "$out/install.sh"
  tar -czf "dist/transferit-$rid.tar.gz" -C dist "transferit-$rid"
  echo "    -> dist/transferit-$rid.tar.gz ($(du -h "dist/transferit-$rid.tar.gz" | cut -f1))"
done

echo
echo "Send the matching .tar.gz to your friend. On their machine:"
echo "  tar -xzf transferit-linux-x64.tar.gz"
echo "  cd transferit-linux-x64 && ./install.sh"
