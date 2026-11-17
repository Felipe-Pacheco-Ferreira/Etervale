# Fetches Slang, the compiler of the effect processor's WebGPU profile (monogame/issues/022-webgpu-effects.md), into
# tools/slang (git-ignored): the official release of the pinned version for this Windows machine, checked against its
# SHA-256. Build the effect compiler (or the client) again afterwards; it copies Slang next to its assembly.
#
#   powershell -ExecutionPolicy Bypass -File monogame/pipeline/fetch-slang.ps1
$ErrorActionPreference = 'Stop'
$version = '2026.22'
$assets = @{
    'X64'   = @('windows-x86_64', 'dd0c4a86bd44cdb7e9cf712af0c8ebf2fff1bba19fa5e3c128ff87384e8ff17b')
    'Arm64' = @('windows-aarch64', '3b74b69e1924d359d3626949b3cde0f6e65068a7b5eb2457e83a351697fe4e71')
}
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
if (-not $assets.ContainsKey($architecture))
{
    throw "No Slang release for Windows $architecture."
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $here 'tools\slang'
$stamp = Join-Path $target 'version.txt'
if ((Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq "$version $($assets[$architecture][0])"))
{
    Write-Host "Slang $version is already in $target"
    exit 0
}

$name = "slang-$version-$($assets[$architecture][0]).zip"
$download = Join-Path ([System.IO.Path]::GetTempPath()) $name
Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/shader-slang/slang/releases/download/v$version/$name" -OutFile $download
$hash = (Get-FileHash -Algorithm SHA256 $download).Hash.ToLowerInvariant()
if ($hash -ne $assets[$architecture][1])
{
    Remove-Item $download
    throw "$name has SHA-256 $hash, expected $($assets[$architecture][1])."
}

if (Test-Path $target)
{
    Remove-Item -Recurse -Force $target
}

Expand-Archive -Path $download -DestinationPath $target
Remove-Item $download
Set-Content -Path $stamp -Value "$version $($assets[$architecture][0])"
Write-Host "Slang $version fetched into $target"
