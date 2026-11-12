#!/usr/bin/env bash
# Builds, signs and checks a release APK of the Vulkan Android client (docs/android.md, "Continuing on another machine").
# Bump ApplicationVersion and ApplicationDisplayVersion in PristonTale.VulkanAndroid.csproj first. The keystore is kept
# outside the repository and passed in; an upgrade in place needs the same keystore as the installed version.
# Usage: package-release.sh <keystore> <android-sdk> <java-sdk> <server-address> [device-serial]
set -euo pipefail
keystore=$1
sdk=$2
jdk=$3
server=$4
device=${5:-}
alias=androiddebugkey
build_tools=36.0.0
here=$(cd "$(dirname "$0")" && pwd)
project="$here/PristonTale.VulkanAndroid.csproj"
version=$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$project" | head -n 1)
log="$here/obj/vulkan-$version.log"
mkdir -p "$here/obj"
if ! JAVA_HOME="$jdk" JAVA_TOOL_OPTIONS='-Xms32m -Xmx768m -XX:+UseSerialGC' dotnet build "$project" --no-restore -c Release \
    -warnaserror -m:1 -nr:false "-p:EtervaleServerAddress=$server" -p:UseSharedCompilation=false -p:PublishTrimmed=false \
    -p:RunAOTCompilation=false -p:AndroidEnableMarshalMethods=false '-p:IntermediateOutputPath=obj/verify-vulkan/' \
    "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk" -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$keystore" \
    -p:AndroidSigningStorePass=android -p:AndroidSigningKeyPass=android "-p:AndroidSigningKeyAlias=$alias" > "$log" 2>&1; then
    tail -n 20 "$log"
    exit 1
fi

apk="$here/bin/Release/net10.0-android/Etervale-$version.apk"
"$sdk/build-tools/$build_tools/apksigner" verify --print-certs "$apk" | grep 'SHA-256'
"$sdk/build-tools/$build_tools/aapt" dump badging "$apk" | grep '^package:'
if [ -n "$device" ]; then
    "$sdk/platform-tools/adb" -s "$device" install -r --no-streaming --no-incremental "$apk"
fi

echo "$apk"
