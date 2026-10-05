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
    foreach ($check in @(@('--formats', (Join-Path $root 'tests/fixtures/audio'), 'formats'), @('--engine', $fixture, 'engine'), @('--waveform', $fixture, 'waveform'), @('--mixer', $fixture, 'mixer'))) {
        $result = & dotnet $tool $check[0] $check[1]
        $code = $LASTEXITCODE
        $result | Set-Content (Join-Path $directory "$($check[2]).json") -Encoding utf8
        if ($code -ne 0) { throw "$($check[0]) failed with exit $code. See artifacts/smoke." }
        Write-Host $result
    }
    Remove-Item (Join-Path $directory 'stage-e-library') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $directory 'stage-c-data') -Recurse -Force -ErrorAction SilentlyContinue
    $app = Join-Path $root 'src/Player.App/bin/Release/net10.0-windows/win-x64/Player.App.exe'
    Remove-Item (Join-Path $directory 'ui.json'), (Join-Path $directory 'stage-c-window.png') -ErrorAction SilentlyContinue
    $taggedFixture = Join-Path $root 'tests/fixtures/audio/flac16.flac'
    $process = Start-Process -FilePath $app -ArgumentList @('--ui-smoke', ('"' + $fixture + '"'), ('"' + $taggedFixture + '"')) -PassThru
    if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'WPF UI smoke timed out.' }
    if ($process.ExitCode -ne 0) { throw "WPF UI smoke failed with exit $($process.ExitCode). See artifacts/smoke/ui.json." }
    $ui = Get-Content (Join-Path $directory 'ui.json') -Raw | ConvertFrom-Json
    if ($ui.Status -ne 'ui-smoke-passed') { throw 'WPF UI evidence is not a current successful result.' }
    Write-Host ($ui | ConvertTo-Json -Depth 5)
    if ($Play) {
        $result = & dotnet $tool --engine-play $fixture
        $code = $LASTEXITCODE
        $result | Set-Content (Join-Path $directory 'output.json') -Encoding utf8
        if ($code -ne 0) { throw "Shared-output smoke failed with exit $code." }
        Write-Host $result
    } else { Write-Host 'Output-device and audible tests NOT RUN. Use -Play on an interactive Windows machine.' }
} finally { Pop-Location }
