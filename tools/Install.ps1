[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$stage = $null; $modStage = $null; $lock = $null; $ownsLock = $false
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or ![Environment]::Is64BitOperatingSystem) {
        throw 'This installer requires 64-bit Windows.'
    }
    $lock = New-Object Threading.Mutex($false, 'Local\TF2VR_ProVolver_Setup')
    try { $ownsLock = $lock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsLock = $true }
    if (!$ownsLock) { throw 'Another ProVolver setup is already open.' }
    Assert-PVStopped
    $source = Split-Path -Parent $PSScriptRoot
    $app = Join-Path $env:LOCALAPPDATA 'Titanfall2VR-ProVolver'
    $previous = $null
    if (Test-Path -LiteralPath $app) {
        $record = Join-Path $app 'install.json'
        if (!(Test-Path -LiteralPath $record)) { throw "An unrecognized folder already exists at $app. Move it aside first; setup has not changed it." }
        $previous = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
        if ($previous.Product -ne 'Titanfall2VR-ProVolver') { throw 'Existing installation record is not recognized.' }
    }
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    Write-Host ''
    Write-Host 'Titanfall 2 VR - ProVolver setup 0.3.3' -ForegroundColor Cyan
    Write-Host 'This installs the standalone recoil mod and creates a desktop shortcut.'
    Write-Host 'The game provider remains experimental.'
    $game = $null
    if ($GamePath) {
        $game = Resolve-PVGameRoot $GamePath
        if (!$game) { throw 'The supplied path does not contain Titanfall 2 and the installed CircuitLord VR mod.' }
    } else {
        $games = @(Find-PVGames)
        if ($previous) {
            $savedGame = Resolve-PVGameRoot $previous.Game
            if ($savedGame) { $games = @($savedGame) + $games }
        }
        $games = @($games | Select-Object -Unique)
        if ($games.Count -eq 1) { $game = $games[0] }
    }
    while ($true) {
        if (!$game) {
            $picker = New-Object Windows.Forms.FolderBrowserDialog
            $picker.Description = 'Select your Titanfall2 game folder (the one containing Titanfall2.exe and TF2VR).'
            $picker.ShowNewFolderButton = $false
            try {
                if ($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { Write-Host 'Setup cancelled.'; return }
                $game = Resolve-PVGameRoot $picker.SelectedPath
            } finally { $picker.Dispose() }
            if (!$game) {
                [Windows.Forms.MessageBox]::Show('That folder does not contain Titanfall 2 with CircuitLord VR installed. Please select the game folder.', 'Choose the game folder') | Out-Null
                continue
            }
        }
        Write-Host "`nGame: $game"
        $answer = Read-Host 'Press Enter to install here, B to browse, or Q to cancel'
        if ($answer -match '^[Qq]$') { Write-Host 'Setup cancelled.'; return }
        if ($answer -match '^[Bb]$') { $game = $null; continue }
        if ([string]::IsNullOrWhiteSpace($answer)) { break }
    }
    if ($previous -and $previous.Game -ne $game) { throw 'Uninstall the existing bridge before choosing a different game installation.' }
    $mod = Join-Path $game 'TF2VR/mods/Titanfall2VR.ProVolver'
    Assert-PVPlainTree $mod
    Assert-PVPlainTree $app
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 with the x64 C# compiler is required. Install/repair it and retry.' }
    $id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $stage = Join-Path $env:LOCALAPPDATA ('Titanfall2VR-ProVolver-stage-' + $id)
    $modStage = Join-Path $game ('TF2VR/ProVolver-stage-' + $id)
    New-Item -ItemType Directory -Path $stage | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stage 'tools') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stage 'docs') | Out-Null
    foreach ($name in @('Bridge.cs','BUILD.cmd','START.cmd','TEST.cmd','PLAY.cmd','UNINSTALL.cmd',
        'GET_PROTUBE_DLL.cmd','README.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md','VERSION')) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $stage
    }
    foreach ($name in @('Get-ProTubeDll.ps1','Setup.Common.ps1','Uninstall.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage 'tools')
    }
    Copy-Item -LiteralPath (Join-Path $source 'docs/VALIDATION.md') -Destination (Join-Path $stage 'docs')
    Write-Host 'Preparing the bridge...'
    & $compiler /nologo /target:exe /platform:x64 /optimize+ ('/out:' + (Join-Path $stage 'ProvolverBridge.exe')) (Join-Path $stage 'Bridge.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Bridge compilation failed; the existing installation has not been changed.' }
    & (Join-Path $stage 'ProvolverBridge.exe') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Bridge self-tests failed; installation stopped.' }
    # Reuse only the exact tested DLL. No personal SDK files are uploaded or bundled.
    $expected = '1f5acd7e815ad4f6289f254f8452c86732d46baefc5e975b2202d569007c5b75'
    foreach ($folder in @($app,$source)) {
        $dll = Join-Path $folder 'ForceTubeVR_API_x64.dll'
        if ((Test-Path -LiteralPath $dll) -and (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -eq $expected) {
            Copy-Item -LiteralPath $dll -Destination $stage
            break
        }
    }
    Write-Host 'Checking/downloading the official ProTube driver...'
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -ExecutionPolicy Bypass -File (Join-Path $stage 'tools/Get-ProTubeDll.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'ProTube download failed. Check the internet connection and retry; existing files are unchanged.' }
    # Stop if the game was started while setup was preparing files.
    Assert-PVStopped
    Copy-Item -LiteralPath (Join-Path $source 'game-mod/Titanfall2VR.ProVolver') -Destination $modStage -Recurse
    $logs = Join-Path $game 'TF2VR/logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $stage 'logs-path.txt') -Value $logs -Encoding UTF8
    $appBackup = Join-Path $env:LOCALAPPDATA ('Titanfall2VR-ProVolver-backups/' + $id + '/app')
    $modBackup = Join-Path $game ('TF2VR/ProVolver-backups/' + $id + '/mod')
    $originalModBackup = $null
    if ($previous) { $originalModBackup = $previous.OriginalModBackup }
    elseif (Test-Path -LiteralPath $mod) { $originalModBackup = $modBackup }
    $record = [ordered]@{ Product='Titanfall2VR-ProVolver'; Version='0.3.3'; Game=$game;
        OriginalModBackup=$originalModBackup; ModStamp=(Get-PVTreeStamp $modStage) }
    $record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'install.json') -Encoding UTF8
    Write-Host 'Installing files (existing ProVolver files are backed up)...'
    Install-PVTransaction -PreparedApp $stage -PreparedMod $modStage -App $app -Mod $mod -AppBackup $appBackup -ModBackup $modBackup
    $stage = $null; $modStage = $null
    try {
        $desktop = [Environment]::GetFolderPath('DesktopDirectory')
        $linkPath = Join-Path $desktop 'Titanfall 2 VR - ProVolver.lnk'
        $shell = New-Object -ComObject WScript.Shell
        if (Test-Path -LiteralPath $linkPath) {
            $existing = $shell.CreateShortcut($linkPath)
            if ($existing.TargetPath -ne (Join-Path $app 'PLAY.cmd')) { throw 'A different shortcut already has that name.' }
        }
        $shortcut = $shell.CreateShortcut($linkPath)
        $shortcut.TargetPath = Join-Path $app 'PLAY.cmd'
        $shortcut.WorkingDirectory = $app
        $shortcut.Description = 'Start ProVolver recoil, then launch Titanfall 2 VR normally.'
        $shortcut.Save()
        Write-Host "`nInstalled. Use the desktop shortcut: Titanfall 2 VR - ProVolver" -ForegroundColor Green
    } catch {
        Write-Host "Installed, but the desktop shortcut could not be created: $($_.Exception.Message)"
        Write-Host "To play, open: $app\PLAY.cmd"
    }
    Write-Host 'Turn on the ProVolver and close ProTube Companion, then open the shortcut.'
    Write-Host 'Launch Titanfall 2 VR normally and load your campaign. No menu or log-path entry is needed.'
    Write-Host 'Keep the bridge window open while playing. Close it or press Ctrl+C when finished.'
    Write-Host "For tests use $app\START.cmd. To remove it use UNINSTALL.cmd in that folder."
} catch {
    Write-Host "`nSETUP FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    foreach ($temporary in @($stage,$modStage)) {
        if ($temporary -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue }
    }
    if ($lock) { if ($ownsLock) { $lock.ReleaseMutex() }; $lock.Dispose() }
}
