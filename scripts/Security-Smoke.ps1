#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') { throw 'Security native checks are NOT RUN: Windows x64 required.' }
if ($env:GITHUB_ACTIONS -ne 'true' -and $env:MPSWIFT_ISOLATED_SECURITY_LAB -ne '1') { throw 'Use only disposable CI or an explicitly configured isolated lab; do not fuzz on a working PC.' }
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $tool = Join-Path $root 'tools/Player.AudioSmoke/bin/Release/net10.0/Player.AudioSmoke.dll'
    & dotnet $tool --security-suite (Join-Path $root 'tests/fixtures')
    if ($LASTEXITCODE -ne 0) { throw 'Bounded native/file security checks failed. See artifacts/smoke/security-parsers.json.' }
} finally { Pop-Location }
