#!/usr/bin/env sh
# Builds the Android asset packs and packs.json (docs/packs.md).
# Usage: tools/pack-build.sh <assets> <world.db> <output-dir>
set -e
if [ "$#" -ne 3 ]; then
  echo "usage: $0 <assets> <world.db> <output-dir>" >&2
  exit 2
fi
here=$(cd "$(dirname "$0")" && pwd)
exec dotnet run --project "$here/PristonTale.PackBuild" -c Release -- "$1" "$2" "$3"