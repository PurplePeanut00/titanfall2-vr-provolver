# Hardware-free setup tests. Run with Windows PowerShell 5.1 or PowerShell 7.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
function Check([bool]$Value,[string]$Name) {
    if (!$Value) { throw "TEST FAILED: $Name" }
    Write-Host "PASS $Name"
}
function Put([string]$Path,[string]$Value) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    Set-Content -LiteralPath $Path -Value $Value -Encoding UTF8
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('pv-setup-test-' + [Guid]::NewGuid().ToString('N'))
try {
    $steam = Join-Path $root 'Steam client'
    $library = Join-Path $root 'Other drive library'
    $game = Join-Path $library 'steamapps/common/Titanfall custom name'
    Put (Join-Path $game 'Titanfall2.exe') 'fixture, not executable'
    Put (Join-Path $game 'TF2VR/mods/Titanfall2VR.Cockpit/mod.json') '{}'
    $escapedLibrary = $library.Replace('\','\\')
    Put (Join-Path $steam 'steamapps/libraryfolders.vdf') ('"libraryfolders" { "0" { "path" "' + $escapedLibrary + '" } }')
    Put (Join-Path $library 'steamapps/appmanifest_1237970.acf') '"AppState" { "installdir" "Titanfall custom name" }'
    $games = @(Get-PVSteamGames @($steam))
    Check ($games.Count -eq 1 -and $games[0] -eq $game) 'discover a second Steam library and custom installation name'
    Check ((Resolve-PVGameRoot (Join-Path $game 'TF2VR/mods')) -eq $game) 'folder picker accepts game, TF2VR or mods folder'
    $vanilla = Join-Path $root 'vanilla'
    Put (Join-Path $vanilla 'Titanfall2.exe') 'fixture'
    Check ($null -eq (Resolve-PVGameRoot $vanilla)) 'reject vanilla installation without CircuitLord VR'
    $paths = @(Get-PVLibraryPaths ('"1" "' + $escapedLibrary + '"'))
    Check ($paths.Count -eq 1 -and $paths[0] -eq $library) 'legacy Steam library format'
    Put (Join-Path $library 'steamapps/appmanifest_1237970.acf') '"installdir" "../../escape"'
    Check (@(Get-PVSteamGames @($steam)).Count -eq 0) 'do not follow manifest path traversal'

    $app = Join-Path $root 'installed app'
    $mod = Join-Path $game 'TF2VR/mods/Titanfall2VR.ProVolver'
    $stagedApp = Join-Path $root 'prepared app'
    $stagedMod = Join-Path $root 'prepared mod'
    Put (Join-Path $stagedApp 'Bridge.cs') 'new app'
    Put (Join-Path $stagedMod 'mod.json') 'new mod'
    $appBackup = Join-Path $root 'backups/first/app'
    $modBackup = Join-Path $root 'backups/first/mod'
    Install-PVTransaction $stagedApp $stagedMod $app $mod $appBackup $modBackup
    Check ((Test-Path -LiteralPath (Join-Path $app 'Bridge.cs')) -and (Test-Path -LiteralPath (Join-Path $mod 'mod.json'))) 'fresh install puts both components in place'
    Check (!(Test-Path -LiteralPath $appBackup) -and !(Test-Path -LiteralPath $modBackup)) 'fresh install has no invented old backups'
    $stamp = Get-PVTreeStamp $mod
    Put (Join-Path $stagedApp 'Bridge.cs') 'updated app'
    Put (Join-Path $stagedMod 'mod.json') 'updated mod'
    Install-PVTransaction $stagedApp $stagedMod $app $mod $appBackup $modBackup
    Check ((Get-Content -LiteralPath (Join-Path $appBackup 'Bridge.cs') -Raw).Trim() -eq 'new app') 'upgrade preserves previous app'
    Check ((Get-Content -LiteralPath (Join-Path $modBackup 'mod.json') -Raw).Trim() -eq 'new mod') 'upgrade preserves previous mod'
    Check ((Get-PVTreeStamp $mod) -ne $stamp) 'changed provider detected for uninstall guard'
    Put (Join-Path $stagedApp 'Bridge.cs') 'broken install app'
    Put (Join-Path $stagedMod 'mod.json') 'broken install mod'
    $threw = $false
    try {
        Install-PVTransaction $stagedApp $stagedMod $app $mod (Join-Path $root 'rollback/app') (Join-Path $root 'rollback/mod') -AfterModInstalled { throw 'simulated disk error' }
    } catch { $threw = $true }
    Check $threw 'installation error is propagated'
    Check ((Get-Content -LiteralPath (Join-Path $app 'Bridge.cs') -Raw).Trim() -eq 'updated app') 'rollback restores previous app'
    Check ((Get-Content -LiteralPath (Join-Path $mod 'mod.json') -Raw).Trim() -eq 'updated mod') 'rollback restores previous mod'
    Check ((Get-Content -LiteralPath (Join-Path $game 'TF2VR/mods/Titanfall2VR.Cockpit/mod.json') -Raw).Trim() -eq '{}') 'unrelated VR mod stays untouched'
    $stamp = Get-PVTreeStamp $mod
    Put (Join-Path $mod 'extra-file.txt') 'user changes'
    Check ((Get-PVTreeStamp $mod) -ne $stamp) 'added file detected for uninstall guard'
    Write-Host 'All 15 setup checks passed. Windows UI, registry, shortcuts and hardware are not covered by these fixture tests.'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
