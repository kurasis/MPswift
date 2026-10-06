#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Optional WMA validation requires Windows.' }
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'tools/Player.AudioSmoke/bin/Release/net10.0'
$owned = Join-Path $root ('artifacts/missing-wma-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item $owned -ItemType Directory | Out-Null
    Get-ChildItem $source | Copy-Item -Destination $owned -Recurse
    Remove-Item (Join-Path $owned 'native/win-x64/basswma.dll')
    $result = & dotnet (Join-Path $owned 'Player.AudioSmoke.dll') --missing-wma (Join-Path $root 'tests/fixtures/audio')
    $code = $LASTEXITCODE
    $result | Set-Content (Join-Path $root 'artifacts/smoke/optional-wma.json') -Encoding utf8
    if ($code -ne 0) { throw "Missing-WMA dependency check failed: $result" }
    Write-Host $result
} finally { if (Test-Path $owned) { Remove-Item $owned -Recurse -Force } }
