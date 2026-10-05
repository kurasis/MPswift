#requires -Version 7.4
[CmdletBinding()]
param([switch]$Play)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Native smoke is NOT RUN: Windows x64 is required.'
}
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & "$PSScriptRoot/Setup-Native.ps1"
    & "$PSScriptRoot/Build.ps1"
    $directory = Join-Path $root 'artifacts/smoke'
    New-Item $directory -ItemType Directory -Force | Out-Null
    $fixture = Join-Path $directory "generated tone Музыка 🎵.wav"
    # Remove only this tool's generated fixture before regeneration.
    Remove-Item $fixture, "$fixture.json" -ErrorAction SilentlyContinue
    $tool = Join-Path $root 'tools/Player.AudioSmoke/bin/Release/net10.0/Player.AudioSmoke.dll'
    & dotnet $tool --generate-fixture $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' }
    foreach ($mode in @('--probe', '--decode')) {
        $arguments = if ($mode -eq '--probe') { @($tool, $mode) } else { @($tool, $mode, $fixture) }
        $result = & dotnet @arguments
        $code = $LASTEXITCODE
        $result | Set-Content (Join-Path $directory "$($mode.TrimStart('-')).json") -Encoding utf8
        if ($code -ne 0) { throw "Native $mode failed with exit $code. See artifacts/smoke." }
        Write-Host $result
    }
    if ($Play) {
        $result = & dotnet $tool --play $fixture
        $code = $LASTEXITCODE
        $result | Set-Content (Join-Path $directory 'output.json') -Encoding utf8
        if ($code -ne 0) { throw "Shared-output smoke failed with exit $code." }
        Write-Host $result
    } else { Write-Host 'Output-device and audible tests NOT RUN. Use -Play on an interactive Windows machine.' }
} finally { Pop-Location }
