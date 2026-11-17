#!/usr/bin/env bash
# Publishes the browser client in Release into the website's Play:ClientPath folder (docs/web-client.md), replacing the
# previous build there; the site serves it under /play without a restart.
# Usage: publish.sh <site-play-folder>
set -euo pipefail
target=$1
here=$(cd "$(dirname "$0")" && pwd)
output="$here/obj/publish-web"
rm -rf "$output"
dotnet publish "$here/PristonTale.Browser.csproj" -c Release -warnaserror -m:1 -o "$output"
mkdir -p "$target"
rm -rf "$target/_framework" "$target/main.js"
cp -R "$output/wwwroot/." "$target/"
echo "$target"
