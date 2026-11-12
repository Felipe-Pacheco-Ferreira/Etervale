param([Parameter(Mandatory)][string]$AssetRoot)

$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'obj/vulkan/adventurer-journey.zip'
$temporaryArchive = "$destination.tmp"
$directories = @(
    'Field/forest', 'Field/forever-fall',
    'char/monster/Hopy', 'char/monster/Mouse', 'char/monster/Monmushroom',
    'char/monster/Monhobgoblin', 'char/monster/Monrebi',
    'char/monster/Monmushroom-mo', 'char/monster/MonMudy'
)
if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
$zip = [IO.Compression.ZipFile]::Open($temporaryArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($path in $directories) {
        $directory = (Resolve-Path -LiteralPath (Join-Path $AssetRoot $path)).Path
        Get-ChildItem -LiteralPath $directory -File -Recurse | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($directory, $_.FullName).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, "$path/$relative")
        }
    }
}
finally {
    $zip.Dispose()
}
[IO.File]::Move($temporaryArchive, $destination, $true)
