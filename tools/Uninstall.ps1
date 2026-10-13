[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$lock = $null; $ownsLock = $false
try {
    $lock = New-Object Threading.Mutex($false, 'Local\TF2VR_ProVolver_Setup')
    try { $ownsLock = $lock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsLock = $true }
    if (!$ownsLock) { throw 'Another ProVolver setup is already open.' }
    Assert-PVStopped
    $app = Split-Path -Parent $PSScriptRoot
    $expectedApp = Join-Path $env:LOCALAPPDATA 'Titanfall2VR-ProVolver'
    if ($app -ne $expectedApp) { throw 'Run UNINSTALL.cmd from the installed folder in LocalAppData, not the downloaded package.' }
    $record = Get-Content -LiteralPath (Join-Path $app 'install.json') -Raw | ConvertFrom-Json
    if ($record.Product -ne 'Titanfall2VR-ProVolver') { throw 'Installation record is not recognized.' }
    $mod = Join-Path $record.Game 'TF2VR/mods/Titanfall2VR.ProVolver'
    if ((Test-Path -LiteralPath $mod) -and (Get-PVTreeStamp $mod) -ne $record.ModStamp) {
        throw "The game mod has been edited since installation. Back it up and move it out of TF2VR\mods yourself, then retry."
    }
    $original = $record.OriginalModBackup
    if ($original -and !(Test-Path -LiteralPath $original)) { throw 'The original mod backup is missing. Restore it before uninstalling.' }
    Write-Host 'This removes this installed bridge and restores any ProVolver mod present before setup.'
    Write-Host 'Other mods are untouched. Removed files and diagnostics are kept in backup folders.'
    if ((Read-Host 'Type REMOVE to continue') -cne 'REMOVE') { Write-Host 'Cancelled.'; return }
    $id = 'uninstall-' + [Guid]::NewGuid().ToString('N')
    $appBackup = Join-Path $env:LOCALAPPDATA ('Titanfall2VR-ProVolver-backups/' + $id + '/app')
    $modBackup = Join-Path $record.Game ('TF2VR/ProVolver-backups/' + $id + '/mod')
    New-Item -ItemType Directory -Path (Split-Path -Parent $appBackup) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path -Parent $modBackup) -Force | Out-Null
    $modMoved = $false; $restored = $false
    try {
        if (Test-Path -LiteralPath $mod) { Move-Item -LiteralPath $mod -Destination $modBackup; $modMoved = $true }
        if ($original) { Move-Item -LiteralPath $original -Destination $mod; $restored = $true }
        Move-Item -LiteralPath $app -Destination $appBackup
    } catch {
        if ($restored) { Move-Item -LiteralPath $mod -Destination $original }
        if ($modMoved) { Move-Item -LiteralPath $modBackup -Destination $mod }
        throw
    }
    try {
        $link = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Titanfall 2 VR - ProVolver.lnk'
        if (Test-Path -LiteralPath $link) {
            $shell = New-Object -ComObject WScript.Shell
            if ($shell.CreateShortcut($link).TargetPath -eq (Join-Path $app 'PLAY.cmd')) { Remove-Item -LiteralPath $link }
        }
    } catch { Write-Host 'You can remove the desktop shortcut manually.' }
    Write-Host "Uninstalled. Files and diagnostics were preserved in $appBackup"
} catch {
    Write-Host ('UNINSTALL FAILED: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    if ($lock) { if ($ownsLock) { $lock.ReleaseMutex() }; $lock.Dispose() }
}
