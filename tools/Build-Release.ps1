[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION.' }
$dist = Join-Path $projectRoot 'dist'
$name = 'Titanfall2VR-ProVolver-Easy-Setup-v' + $version
$stage = Join-Path $dist $name
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stage 'tools') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
# Explicit allowlist: no vendor DLLs, personal settings, logs or uploaded game files.
$files = @('Bridge.cs', 'BUILD.cmd', 'START.cmd', 'TEST.cmd', 'GET_PROTUBE_DLL.cmd',
    'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md', 'VERSION',
    'INSTALL.cmd', 'PLAY.cmd', 'UNINSTALL.cmd', '.gitignore', '.gitattributes')
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stage }
foreach ($script in @('Get-ProTubeDll.ps1','Build-Release.ps1','Install.ps1','Setup.Common.ps1','Uninstall.ps1','Test-Setup.ps1','Test-Bridge.ps1','Test-Provider.py')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('tools/' + $script)) -Destination (Join-Path $stage 'tools')
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/VALIDATION.md') -Destination (Join-Path $stage 'docs')
Copy-Item -LiteralPath (Join-Path $projectRoot 'game-mod') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot '.github') -Destination $stage -Recurse -Force
$zip = Join-Path $dist ($name + '.zip')
# ZipFile includes dotfiles; Compress-Archive may skip them on some platforms.
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host ('Created ' + $zip)
Write-Host 'Easy Setup ZIP: extract and run INSTALL.cmd. Vendor DLL downloads separately.'
