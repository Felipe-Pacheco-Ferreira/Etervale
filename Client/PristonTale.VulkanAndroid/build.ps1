param(
    [Parameter(Mandatory)][string]$AndroidSdk,
    [Parameter(Mandatory)][string]$Ndk,
    [Parameter(Mandatory)][string]$JavaSdk,
    [string]$CMake = 'cmake',
    [string]$Ninja,
    [switch]$SkipNativeBuild,
    [string]$AssetRoot,
    [string]$DesktopContent,
    [string]$ServerAddress,
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
if (!$AssetRoot) { $AssetRoot = Join-Path $root 'assets' }
if (!$DesktopContent) { $DesktopContent = Join-Path $root 'Client/PristonTale/bin/Release/net10.0/Content' }
$ninjaOption = if ($Ninja) { @("-DCMAKE_MAKE_PROGRAM=$Ninja") } else { @() }
$build = Join-Path $PSScriptRoot 'obj/vulkan'
New-Item -ItemType Directory -Force $build | Out-Null

if (!$SkipNativeBuild) {
    foreach ($target in @(@('arm64','arm64-v8a'), @('x64','x86_64'))) {
        $output = Join-Path $build $target[0]
        & $CMake -S "$PSScriptRoot/native" -B $output -G Ninja @ninjaOption "-DCMAKE_TOOLCHAIN_FILE=$Ndk/build/cmake/android.toolchain.cmake" "-DANDROID_ABI=$($target[1])" -DANDROID_PLATFORM=android-26 -DCMAKE_BUILD_TYPE=Release
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
        & $CMake --build $output --target main
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
}

$archive = Join-Path $build 'ricarten-desktop.zip'
$content = $DesktopContent
$sources = [ordered]@{
    'Field/Ricarten' = 'assets/Field/Ricarten'
    'cSelect' = 'assets/cSelect'
    'StartImage/login' = 'assets/StartImage/login'
    'StartImage/MessageBox' = 'assets/StartImage/MessageBox'
    'char/monster/Hound' = 'assets/char/monster/Hound'
    'weapons' = 'assets/weapons'
    'image' = 'assets/image'
    'char/tmABCD' = 'assets/char/tmABCD'
    'char/npc' = 'assets/char/npc'
    'char/npcXms' = 'assets/char/npcXms'
    'sky' = 'assets/sky'
    'Effect' = 'assets/Effect'
    'wav' = 'assets/wav'
    'rain' = 'assets/rain'
    'Content/Fonts' = "$content/Fonts"
    'Content/Effects' = "$content/Effects"
}
function Write-AssetArchive($archive, $entries) {
    $temporaryArchive = "$archive.tmp"
    if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
    $zip = [IO.Compression.ZipFile]::Open($temporaryArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries.GetEnumerator()) {
            $sourcePath = if ($entry.Value.StartsWith('assets/')) {
                Join-Path $AssetRoot $entry.Value.Substring('assets/'.Length)
            } else {
                $entry.Value
            }
            $directory = (Resolve-Path -LiteralPath $sourcePath).Path
            Get-ChildItem -LiteralPath $directory -File -Recurse | ForEach-Object {
                $relative = [IO.Path]::GetRelativePath($directory, $_.FullName).Replace('\', '/')
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, "$($entry.Key)/$relative")
            }
        }
    }
    finally {
        $zip.Dispose()
    }
    [IO.File]::Move($temporaryArchive, $archive, $true)
}

Write-AssetArchive $archive $sources
Write-AssetArchive (Join-Path $build 'opening-desktop.zip') ([ordered]@{
    'StartImage/Opening' = 'assets/StartImage/Opening'
    'Hud/Manifests' = 'assets/Hud/Manifests'
    'Hud/Textures' = 'assets/Hud/Textures'
})
& "$PSScriptRoot/package-adventurer-assets.ps1" -AssetRoot $AssetRoot

& $Dotnet build "$PSScriptRoot/PristonTale.VulkanAndroid.csproj" -c Release -warnaserror "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" "-p:EtervaleServerAddress=$ServerAddress"
exit $LASTEXITCODE
