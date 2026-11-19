# Publishes the browser client in Release into the website's Play:ClientPath folder (docs/web-client.md), replacing the
# previous build there; the site serves it under /play without a restart. The world packs (tools/PristonTale.WebAssetPacks)
# are built from the published files and copied last, their index after their chunks.
param(
    [Parameter(Mandatory = $true)][string]$Target
)
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'obj/publish-web'
$packs = Join-Path $PSScriptRoot 'obj/publish-packs'
foreach ($old in @($output, $packs)) {
    if (Test-Path $old) {
        Remove-Item -Recurse -Force $old
    }
}

dotnet publish (Join-Path $PSScriptRoot 'PristonTale.Browser.csproj') -c Release -warnaserror -m:1 -o $output
if ($LASTEXITCODE) {
    exit $LASTEXITCODE
}

dotnet run --project (Join-Path $PSScriptRoot '../../tools/PristonTale.WebAssetPacks/PristonTale.WebAssetPacks.csproj') -c Release `
    -- --root (Join-Path $output 'wwwroot') --out $packs
if ($LASTEXITCODE) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force $Target | Out-Null
foreach ($old in @('world-packs.json', 'world-packs', '_framework', 'main.js')) {
    $path = Join-Path $Target $old
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
    }
}

Copy-Item -Recurse -Force (Join-Path $output 'wwwroot/*') $Target
Copy-Item -Recurse -Force (Join-Path $packs 'world-packs') $Target
Copy-Item -Force (Join-Path $packs 'world-packs.json') $Target
Write-Output $Target
