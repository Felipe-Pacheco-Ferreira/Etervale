#!/usr/bin/env bash
# Publishes the browser client in Release into the website's Play:ClientPath folder (docs/web-client.md), replacing the
# previous build there; the site serves it under /play without a restart. The world packs (tools/PristonTale.WebAssetPacks)
# are built from the published files and copied last, their index after their chunks.
# Usage: publish.sh <site-play-folder>
set -euo pipefail
target=$1
here=$(cd "$(dirname "$0")" && pwd)
output="$here/obj/publish-web"
packs="$here/obj/publish-packs"
rm -rf "$output" "$packs"
dotnet publish "$here/PristonTale.Browser.csproj" -c Release -warnaserror -m:1 -o "$output"
dotnet run --project "$here/../../tools/PristonTale.WebAssetPacks/PristonTale.WebAssetPacks.csproj" -c Release \
    -- --root "$output/wwwroot" --out "$packs"
mkdir -p "$target"
rm -rf "$target/world-packs.json" "$target/world-packs" "$target/_framework" "$target/main.js"
cp -R "$output/wwwroot/." "$target/"
cp -R "$packs/world-packs" "$target/"
cp "$packs/world-packs.json" "$target/"
echo "$target"
