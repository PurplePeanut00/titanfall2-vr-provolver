# Download the independently hosted vendor binary. Never add it to this repository.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $projectRoot 'ForceTubeVR_API_x64.dll'
$expected = '1f5acd7e815ad4f6289f254f8452c86732d46baefc5e975b2202d569007c5b75'
$uri = 'https://raw.githubusercontent.com/ProTubeVR/ForceTubeVR-Unity-Package/2873daf436296d9ea84013fef41c7ff8a4b379e5/new/Assets/Plugins/Windows/ForceTubeVR_API_x64.dll'
$temporary = Join-Path $projectRoot ('protube-' + [Guid]::NewGuid().ToString('N') + '.download')
try {
    if (Test-Path -LiteralPath $destination) {
        $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -eq $expected) {
            Write-Host 'The tested ProTube DLL is already present. Run START.cmd.'
            exit 0
        }
        throw 'A different DLL already exists. Move it out of this folder before running setup; it has not been overwritten.'
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $temporary
    $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw 'Download hash did not match the tested DLL. The download will be removed.' }
    Move-Item -LiteralPath $temporary -Destination $destination
    Write-Host 'ProTube DLL downloaded and SHA-256 verified. Run START.cmd.'
} catch {
    Write-Host ('ERROR: ' + $_.Exception.Message)
    exit 1
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
