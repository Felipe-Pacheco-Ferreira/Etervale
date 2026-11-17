#!/bin/bash
# Fetches Slang, the compiler of the effect processor's WebGPU profile (monogame/issues/022-webgpu-effects.md), into
# tools/slang (git-ignored): the official release of the pinned version for this Linux or macOS machine, checked
# against its SHA-256. Build the effect compiler (or the client) again afterwards; it copies Slang next to its assembly.
#
#   monogame/pipeline/fetch-slang.sh
set -eu
version=2026.22
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) asset=linux-x86_64; sha=a877b75c6ec515f5d8bf66ea4bae4f0a5ba85050b9540c2046670285808347e6 ;;
  Linux-aarch64) asset=linux-aarch64; sha=b7d03e99de10caff973d45439159300d569d4220d2364eec34ffa7ef2a5baddd ;;
  Darwin-x86_64) asset=macos-x86_64; sha=baad48f7432c1826a84a396d9bd6a2ebc3c9ceb395a187baad49d29066fbcd02 ;;
  Darwin-arm64) asset=macos-aarch64; sha=885b5bd9a79a1ae81fdb92212f5f7c785001f870d45c39f9bb5df01339db3fea ;;
  *) echo "No Slang release for $(uname -s) $(uname -m)." >&2; exit 1 ;;
esac

here="$(cd "$(dirname "$0")" && pwd)"
target="$here/tools/slang"
if [ -f "$target/version.txt" ] && [ "$(cat "$target/version.txt")" = "$version $asset" ]; then
  echo "Slang $version is already in $target"
  exit 0
fi

name="slang-$version-$asset.tar.gz"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
curl -fsSL -o "$tmp/$name" "https://github.com/shader-slang/slang/releases/download/v$version/$name"
if command -v sha256sum >/dev/null; then
  actual="$(sha256sum "$tmp/$name" | cut -d' ' -f1)"
else
  actual="$(shasum -a 256 "$tmp/$name" | cut -d' ' -f1)"
fi
if [ "$actual" != "$sha" ]; then
  echo "$name has SHA-256 $actual, expected $sha." >&2
  exit 1
fi

rm -rf "$target"
mkdir -p "$target"
tar -xzf "$tmp/$name" -C "$target"
echo "$version $asset" > "$target/version.txt"
echo "Slang $version fetched into $target"
