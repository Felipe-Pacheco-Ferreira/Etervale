# Publishes the browser client in Release into the website's Play:ClientPath folder (docs/web-client.md), replacing the
# previous build there; the site serves it under /play without a restart.
param(
    [Parameter(Mandatory = $true)][string]$Target
)
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'obj/publish-web'
if (Test-Path $output) {
    Remove-Item -Recurse -Force $output
}

dotnet publish (Join-Path $PSScriptRoot 'PristonTale.Browser.csproj') -c Release -warnaserror -m:1 -o $output
if ($LASTEXITCODE) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force $Target | Out-Null
foreach ($old in @('_framework', 'main.js')) {
    $path = Join-Path $Target $old
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
    }
}

Copy-Item -Recurse -Force (Join-Path $output 'wwwroot/*') $Target
Write-Output $Target
