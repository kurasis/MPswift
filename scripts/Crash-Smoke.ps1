#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Real process crash smoke is NOT RUN: Windows x64 required.' }
$root = Split-Path $PSScriptRoot -Parent
$owned = Join-Path $root ('artifacts/crash-check-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'src/Player.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Player.App.exe'
$fixture = Join-Path $root 'tests/fixtures/audio/pcm16.wav'
$process = $null
$out = Join-Path $root 'artifacts/smoke'
try {
    New-Item $out -ItemType Directory -Force | Out-Null
    Remove-Item (Join-Path $out 'crash-restart.json'), (Join-Path $out 'crash-failure.json') -ErrorAction SilentlyContinue
    New-Item $owned -ItemType Directory -Force | Out-Null
    'Owned development validation only.' | Set-Content (Join-Path $owned '.player-crash-validation')
    $evidence = Join-Path $owned 'artifacts/smoke'
    function Start-Validation([string]$Phase) {
        $start = [Diagnostics.ProcessStartInfo]::new($app)
        $start.UseShellExecute = $false; $start.WorkingDirectory = $owned
        foreach ($argument in @('--crash-smoke', $Phase, $fixture)) { $start.ArgumentList.Add($argument) }
        [Diagnostics.Process]::Start($start)
    }
    $process = Start-Validation 'checkpoint'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $signal = Join-Path $evidence 'checkpoint.json'
    while (-not (Test-Path $signal)) {
        if ($process.HasExited) {
            $details = if (Test-Path (Join-Path $evidence 'crash.json')) { Get-Content (Join-Path $evidence 'crash.json') -Raw } elseif (Test-Path (Join-Path $evidence 'ui.json')) { Get-Content (Join-Path $evidence 'ui.json') -Raw } else { 'No startup report.' }
            throw "Checkpoint WPF process exited early: $($process.ExitCode). $details"
        }
        if ($clock.Elapsed.TotalSeconds -gt 60) { throw 'WPF crash checkpoint timed out.' }
        Start-Sleep -Milliseconds 100
    }
    $wal = Join-Path $evidence 'stage-g-crash-data/library.db-wal'
    if (-not (Test-Path $wal) -or (Get-Item $wal).Length -le 0) { throw 'Actual live WAL was not present before termination.' }
    $process.Kill(); $process.WaitForExit(); $killedExitCode = $process.ExitCode; $process.Dispose(); $process = $null
    if ($killedExitCode -eq 0) { throw 'Checkpoint process did not report forced termination.' }
    $process = Start-Validation 'verify'
    if (-not $process.WaitForExit(60000)) { throw 'Actual WPF restart validation timed out.' }
    $report = Get-Content (Join-Path $evidence 'crash.json') -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or $report.Status -ne 'crash-restart-model-passed') { throw "WPF crash/restart failed: $($report | ConvertTo-Json -Depth 5)" }
    $report | Add-Member Windows ([Environment]::OSVersion.VersionString)
    $report | Add-Member ForcedWpfTermination $true
    $report | Add-Member KilledExitCode $killedExitCode
    $report | Add-Member Method 'Actual apphost killed after a production checkpoint, while a diagnostic SQLite write transaction was open; actual WPF/native restart validates the last committed state.'
    $report | Add-Member SourceCommit ((& git -C $root rev-parse HEAD).Trim())
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $out 'crash-restart.json') -Encoding utf8
    Write-Host ($report | ConvertTo-Json -Depth 6)
} catch {
    [ordered]@{ Status = 'crash-restart-failed'; Message = $_.Exception.Message; Windows = [Environment]::OSVersion.VersionString } | ConvertTo-Json | Set-Content (Join-Path $out 'crash-failure.json') -Encoding utf8
    throw
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    if (Test-Path $owned) { Remove-Item $owned -Recurse -Force }
}
