param([string]$GamePath, [string]$UpdateFeed, [switch]$CheckOnly, [switch]$UpdateOnly, [switch]$PlayAfterUpdate)

$ErrorActionPreference = 'Stop'
$feedPath = Join-Path $PSScriptRoot 'update-feed.txt'
if (!$UpdateFeed -and (Test-Path -LiteralPath $feedPath -PathType Leaf)) {
    $UpdateFeed = (Get-Content -LiteralPath $feedPath -Raw).Trim()
}
$install = Join-Path $PSScriptRoot 'versions'
$progressFile = Join-Path ([IO.Path]::GetTempPath()) ("etervale-$PID.txt")

function Write-DownloadProgress([int]$percent) {
    try { [IO.File]::WriteAllText($progressFile, [string]$percent) } catch { }
}

function Save-Package([uri]$uri, [string]$destination) {
    $request = [Net.HttpWebRequest]::Create($uri)
    $request.AllowAutoRedirect = $false
    $request.Timeout = 30000
    $request.ReadWriteTimeout = 30000
    $response = $request.GetResponse()
    try {
        if ([int]$response.StatusCode -ne 200) { throw 'Resposta inválida ao baixar o pacote.' }
        $source = $response.GetResponseStream()
        try {
            $output = [IO.File]::Create($destination)
            try {
                $buffer = New-Object byte[] (256KB)
                $received = 0L
                $previous = -1
                while (($count = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $output.Write($buffer, 0, $count)
                    $received += $count
                    if ($response.ContentLength -gt 0) {
                        $percent = [int][Math]::Min(100, [Math]::Floor(100.0 * $received / $response.ContentLength))
                        if ($percent -ne $previous) {
                            Write-DownloadProgress $percent
                            $previous = $percent
                        }
                    }
                }
            }
            finally { $output.Dispose() }
        }
        finally { $source.Dispose() }
    }
    finally { $response.Dispose() }
}

function Get-Release {
    $feed = [uri]$UpdateFeed
    if ($feed.Scheme -ne 'https') { throw 'O manifesto precisa usar HTTPS.' }
    $release = Invoke-RestMethod -Uri $feed -TimeoutSec 30
    if ($release.version -notmatch '^\d{1,5}(\.\d{1,5}){1,3}$' -or
        $release.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or ([uri]$release.url).Scheme -ne 'https') {
        throw 'Manifesto de atualização inválido.'
    }
    $release
}

function Get-InstalledGame {
    if (Test-Path -LiteralPath $install) {
        $latest = Get-ChildItem -LiteralPath $install -Directory |
            Where-Object { $_.Name -match '^\d{1,5}(\.\d{1,5}){1,3}$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'PristonTale.exe') -PathType Leaf) } |
            Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if ($latest) { return (Join-Path $latest.FullName 'PristonTale.exe') }
    }
    if ($GamePath -and (Test-Path -LiteralPath $GamePath -PathType Leaf)) { return (Resolve-Path -LiteralPath $GamePath).Path }
}

function Install-Release($release) {
    New-Item -ItemType Directory -Path $install -Force | Out-Null
    $target = Join-Path $install $release.version
    if (Test-Path -LiteralPath (Join-Path $target 'PristonTale.exe') -PathType Leaf) { return }
    $scratch = Join-Path $install ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $zip = Join-Path $scratch 'client.zip'
    $content = Join-Path $scratch 'client'
    try {
        Save-Package $release.url $zip
        Write-DownloadProgress -2
        $sha = [Security.Cryptography.SHA256]::Create()
        $stream = [IO.File]::OpenRead($zip)
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($digest -ne $release.sha256) {
            throw 'O hash SHA-256 do pacote não confere.'
        }
        Write-DownloadProgress -3
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $content)
        if (!(Test-Path -LiteralPath (Join-Path $content 'PristonTale.exe') -PathType Leaf)) {
            throw 'O pacote precisa conter PristonTale.exe na raiz.'
        }
        [IO.Directory]::Move($content, $target)
    }
    finally {
        $resolved = (Resolve-Path -LiteralPath $scratch).Path
        if ([IO.Path]::GetDirectoryName($resolved) -eq (Resolve-Path -LiteralPath $install).Path) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}

function Start-Game {
    $executable = Get-InstalledGame
    if (!$executable) { throw 'Cliente Windows não encontrado.' }
    $directory = Split-Path -Parent $executable
    $runtime = Join-Path $directory 'dotnet'
    $oldRoot = $env:DOTNET_ROOT
    try {
        if (Test-Path -LiteralPath $runtime) { $env:DOTNET_ROOT = $runtime }
        $working = if ($GamePath) { Split-Path -Parent (Resolve-Path -LiteralPath $GamePath).Path } else { $directory }
        Start-Process -FilePath $executable -WorkingDirectory $working -PassThru
    }
    finally { $env:DOTNET_ROOT = $oldRoot }
}

if ($UpdateOnly) {
    Install-Release (Get-Release)
    if ($PlayAfterUpdate) { Start-Game }
    return
}

if ($CheckOnly) {
    if ($UpdateFeed) { Get-Release }
    Get-InstalledGame
    exit
}

Add-Type -AssemblyName PresentationFramework
$layout = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Launcher.xaml') -Raw -Encoding UTF8)
$window = [Windows.Markup.XamlReader]::Load([Xml.XmlNodeReader]::new($layout))
foreach ($art in @{ Scene = 'hero-900.jpg'; Emblem = 'emblem-256.png' }.GetEnumerator()) {
    $window.FindName($art.Key).Source = [Windows.Media.Imaging.BitmapImage]::new([uri](Join-Path $PSScriptRoot $art.Value))
}
$status = $window.FindName('Status')
$progress = $window.FindName('Progress')
$shine = $window.FindName('DownloadShine')
$update = $window.FindName('Update')
$play = $window.FindName('Play')
$version = $window.FindName('Version')
function Update-VersionLabel {
    $game = Get-InstalledGame
    if ($game) {
        $folder = Split-Path (Split-Path $game -Parent) -Leaf
        if ($folder -match '^\d+(\.\d+)+$') { $version.Text = "Windows · versão $folder" }
    }
}
Update-VersionLabel
foreach ($link in @{ News = '/noticias'; Events = '/eventos' }.GetEnumerator()) {
    $button = $window.FindName($link.Key)
    $button.IsEnabled = [bool]$UpdateFeed
    if ($UpdateFeed) {
        $destination = [uri]::new([uri]$UpdateFeed, $link.Value)
        $button.Tag = $destination.AbsoluteUri
        $button.Add_Click({ param($sender, $eventArgs) Start-Process $sender.Tag })
    }
}
$play.IsEnabled = [bool](Get-InstalledGame)
$update.IsEnabled = [bool]$UpdateFeed
$timer = [Windows.Threading.DispatcherTimer]::new()
$timer.Interval = [TimeSpan]::FromMilliseconds(250)
$worker = @{ Process = $null }
$timer.Add_Tick({
    $workerProgress = Join-Path ([IO.Path]::GetTempPath()) ("etervale-" + $worker.Process.Id + ".txt")
    if (!$worker.Process.HasExited) {
        $value = 0
        $raw = Get-Content -LiteralPath $workerProgress -Raw -ErrorAction SilentlyContinue
        if ([int]::TryParse($raw, [ref]$value)) {
            $progress.IsIndeterminate = $value -lt 0
            if ($value -ge 0) {
                $status.Text = "Baixando pacote · $value%"
                $progress.Value = $value
            }
            elseif ($value -eq -2) { $status.Text = 'Verificando SHA-256…' }
            else { $status.Text = 'Instalando o cliente…' }
        }
        return
    }
    Remove-Item -LiteralPath $workerProgress -ErrorAction SilentlyContinue
    $timer.Stop()
    $shine.Visibility = 'Collapsed'
    Update-VersionLabel
    $progress.IsIndeterminate = $false
    $progress.Value = if ($worker.Process.ExitCode -eq 0) { 100 } else { 0 }
    $status.Text = if (Get-InstalledGame) { 'Cliente atualizado. Pronto para jogar.' } else { 'A atualização falhou. Consulte o log de atualização.' }
    $worker.Process.Dispose()
    $update.IsEnabled = $true
    $play.IsEnabled = [bool](Get-InstalledGame)
})
$status.Text = if ($UpdateFeed) { 'Verifique se há uma nova versão.' } else { 'Canal de atualização ainda não configurado.' }
$play.Add_Click({
    try {
        $executable = Get-InstalledGame
        if (!$executable) { throw 'Cliente Windows não encontrado.' }
        Start-Game | Out-Null
        $status.Text = 'Cliente iniciado.'
    }
    catch { $status.Text = $_.Exception.Message }
})
$update.Add_Click({
    $update.IsEnabled = $false
    $play.IsEnabled = $false
    try {
        $status.Text = 'Baixando e verificando o pacote…'
        $hostPath = (Get-Process -Id $PID).Path
        $feed = ([uri]$UpdateFeed).AbsoluteUri
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -UpdateFeed "{1}" -UpdateOnly' -f $PSCommandPath, $feed
        $worker.Process = Start-Process -FilePath $hostPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $PSScriptRoot 'update-error.log')
        $progress.IsIndeterminate = $true
        $shine.Visibility = 'Visible'
        $timer.Start()
    }
    catch {
        $status.Text = $_.Exception.Message
        $update.IsEnabled = $true
        $play.IsEnabled = [bool](Get-InstalledGame)
    }
})
[void]$window.ShowDialog()











