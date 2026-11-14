# Builds the Android asset packs and packs.json (docs/packs.md).
# Usage: tools/pack-build.ps1 -Assets <assets> -Database <world.db> -Output <dir>
param(
    [Parameter(Mandatory)][string]$Assets,
    [Parameter(Mandatory)][string]$Database,
    [Parameter(Mandatory)][string]$Output,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
& $Dotnet run --project "$PSScriptRoot/PristonTale.PackBuild" -c Release -- $Assets $Database $Output
exit $LASTEXITCODE
