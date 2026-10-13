# Hardware-free bridge tests using Roslyn. TEST.cmd remains the Windows Framework test.
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'Bridge.cs') -Raw
Add-Type -TypeDefinition $source -CompilerOptions '/langversion:5'
[TF2Provolver.Program]::SelfTest()
