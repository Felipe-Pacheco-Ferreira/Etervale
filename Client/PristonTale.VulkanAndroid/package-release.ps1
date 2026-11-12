# Builds, signs and checks a release APK of the Vulkan Android client (docs/android.md, "Continuing on another machine").
# Bump ApplicationVersion and ApplicationDisplayVersion in PristonTale.VulkanAndroid.csproj first. The keystore is kept
# outside the repository and passed in; an upgrade in place needs the same keystore as the installed version.
param(
    [Parameter(Mandatory = $true)][string]$Keystore,
    [Parameter(Mandatory = $true)][string]$AndroidSdk,
    [Parameter(Mandatory = $true)][string]$JavaSdk,
    [Parameter(Mandatory = $true)][string]$ServerAddress,
    [string]$KeyAlias = 'androiddebugkey',
    [string]$StorePassword = 'android',
    [string]$KeyPassword = 'android',
    [string]$BuildTools = '36.0.0',
    [string]$Device
)
$ErrorActionPreference = 'Stop'
$env:JAVA_HOME = $JavaSdk
$env:JAVA_TOOL_OPTIONS = '-Xms32m -Xmx768m -XX:+UseSerialGC'
$project = Join-Path $PSScriptRoot 'PristonTale.VulkanAndroid.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
$log = Join-Path $PSScriptRoot "obj/vulkan-$version.log"
dotnet build $project --no-restore -c Release -warnaserror -m:1 -nr:false `
    "-p:EtervaleServerAddress=$ServerAddress" -p:UseSharedCompilation=false -p:PublishTrimmed=false -p:RunAOTCompilation=false `
    -p:AndroidEnableMarshalMethods=false '-p:IntermediateOutputPath=obj/verify-vulkan/' "-p:AndroidSdkDirectory=$AndroidSdk" `
    "-p:JavaSdkDirectory=$JavaSdk" -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$Keystore" "-p:AndroidSigningStorePass=$StorePassword" `
    "-p:AndroidSigningKeyPass=$KeyPassword" "-p:AndroidSigningKeyAlias=$KeyAlias" *> $log
if ($LASTEXITCODE) {
    Get-Content $log -Tail 20
    exit $LASTEXITCODE
}

$apk = Join-Path $PSScriptRoot "bin/Release/net10.0-android/Etervale-$version.apk"
& "$AndroidSdk/build-tools/$BuildTools/apksigner.bat" verify --print-certs $apk | Select-String 'SHA-256'
& "$AndroidSdk/build-tools/$BuildTools/aapt.exe" dump badging $apk | Select-String '^package:'
if ($Device) {
    & "$AndroidSdk/platform-tools/adb.exe" -s $Device install -r --no-streaming --no-incremental $apk
}

Write-Output $apk
