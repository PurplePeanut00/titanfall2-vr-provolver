# Shared setup routines; compatible with Windows PowerShell 5.1.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Resolve-PVGameRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $candidate = [IO.Path]::GetFullPath($Path.Trim().Trim('"'))
    for ($i = 0; $i -lt 3; $i++) {
        if ((Test-Path -LiteralPath (Join-Path $candidate 'Titanfall2.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'TF2VR/mods/Titanfall2VR.Cockpit/mod.json') -PathType Leaf)) {
            return $candidate
        }
        $parent = Split-Path -Parent $candidate
        if (!$parent -or $parent -eq $candidate) { break }
        $candidate = $parent
    }
    return $null
}

function Get-PVLibraryPaths([string]$Text) {
    # Steam VDF uses doubled backslashes. Support current "path" and old numeric entries.
    foreach ($match in [regex]::Matches($Text, '"(?:path|\d+)"\s+"([^"\r\n]+)"')) {
        $value = $match.Groups[1].Value.Replace('\\', '\')
        if ([IO.Path]::IsPathRooted($value)) { $value }
    }
}

function Get-PVSteamGames([string[]]$SteamRoots) {
    $libraries = @($SteamRoots)
    foreach ($steam in $SteamRoots) {
        $vdf = Join-Path $steam 'steamapps/libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) { $libraries += @(Get-PVLibraryPaths (Get-Content -LiteralPath $vdf -Raw)) }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        $name = 'Titanfall2'
        $manifest = Join-Path $library 'steamapps/appmanifest_1237970.acf'
        if (Test-Path -LiteralPath $manifest) {
            $match = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"installdir"\s+"([^"\r\n]+)"')
            if ($match.Success) {
                $value = $match.Groups[1].Value
                # An installdir must be a single folder name, not an arbitrary path.
                if ($value -ne '.' -and $value -ne '..' -and $value -notmatch '[\\/:]') { $name = $value }
            }
        }
        $game = Resolve-PVGameRoot (Join-Path $library ('steamapps/common/' + $name))
        if ($game) { $game }
    }
}

function Find-PVGames {
    $steamRoots = @()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        if (Test-Path $key) {
            $item = Get-ItemProperty $key
            foreach ($field in @('SteamPath','InstallPath')) {
                if ($item.PSObject.Properties[$field] -and $item.$field) { $steamRoots += [string]$item.$field }
            }
        }
    }
    if (${env:ProgramFiles(x86)}) { $steamRoots += (Join-Path ${env:ProgramFiles(x86)} 'Steam') }
    $found = @(Get-PVSteamGames ($steamRoots | Select-Object -Unique))
    foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1237970',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1237970')) {
        if (Test-Path $key) {
            $item = Get-ItemProperty $key
            if ($item.PSObject.Properties['InstallLocation']) {
                $game = Resolve-PVGameRoot $item.InstallLocation
                if ($game) { $found += $game }
            }
        }
    }
    $found | Select-Object -Unique
}

function Assert-PVPlainTree([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    $items = @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force)
    foreach ($item in $items) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Setup will not replace a folder containing a link/junction: $($item.FullName)"
        }
    }
}

function Get-PVTreeStamp([string]$Path) {
    Assert-PVPlainTree $Path
    $root = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $rows = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($root.Length).Replace('\','/')
        $relative + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    } | Sort-Object)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join "`n"))))).Replace('-','') }
    finally { $sha.Dispose() }
}

function Install-PVTransaction {
    param([string]$PreparedApp, [string]$PreparedMod, [string]$App, [string]$Mod,
        [string]$AppBackup, [string]$ModBackup,
        [scriptblock]$AfterModInstalled = {})
    # Backup destinations are unique and outside Northstar's mods directory.
    foreach ($path in @($App,$Mod,$PreparedApp,$PreparedMod)) { Assert-PVPlainTree $path }
    if ((Test-Path -LiteralPath $AppBackup) -or (Test-Path -LiteralPath $ModBackup)) { throw 'Backup destination already exists.' }
    $oldAppMoved = $false; $oldModMoved = $false; $newModMoved = $false; $newAppMoved = $false
    try {
        if (Test-Path -LiteralPath $App) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $AppBackup) -Force | Out-Null
            Move-Item -LiteralPath $App -Destination $AppBackup
            $oldAppMoved = $true
        }
        if (Test-Path -LiteralPath $Mod) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $ModBackup) -Force | Out-Null
            Move-Item -LiteralPath $Mod -Destination $ModBackup
            $oldModMoved = $true
        }
        Move-Item -LiteralPath $PreparedMod -Destination $Mod
        $newModMoved = $true
        & $AfterModInstalled
        Move-Item -LiteralPath $PreparedApp -Destination $App
        $newAppMoved = $true
    } catch {
        $failure = $_
        try {
            if ($newAppMoved) { Remove-Item -LiteralPath $App -Recurse -Force }
            if ($newModMoved) { Remove-Item -LiteralPath $Mod -Recurse -Force }
            if ($oldModMoved) { Move-Item -LiteralPath $ModBackup -Destination $Mod }
            if ($oldAppMoved) { Move-Item -LiteralPath $AppBackup -Destination $App }
        } catch { throw "Setup failed and rollback needs attention. Backups: $AppBackup ; $ModBackup . $($_.Exception.Message)" }
        throw $failure
    }
}

function Assert-PVStopped {
    if (Get-Process -Name Titanfall2,NorthstarLauncher,ProvolverBridge -ErrorAction SilentlyContinue) {
        throw 'Close Titanfall 2 and every ProVolver bridge window, then run setup again.'
    }
}
