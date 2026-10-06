#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Actual NTFS/ACL/watcher failure checks require Windows.' }
$root = Split-Path $PSScriptRoot -Parent
$token = [guid]::NewGuid().ToString('N')
$owned = Join-Path $root ('artifacts/g9-validation-' + $token)
$vhd = Join-Path $owned 'owned-test-disk.vhd'
$out = Join-Path $root 'artifacts/smoke'
$process = $null
$letter = $null
function Invoke-OwnedDiskPart([string[]]$Commands) {
    $script = Join-Path $owned 'owned-diskpart.txt'
    $Commands | Set-Content $script -Encoding ascii
    $lines = & diskpart.exe /s $script
    if ($LASTEXITCODE -ne 0) { throw "Owned VHD DiskPart failed: $lines" }
    Write-Host ($lines -join "`n")
}
try {
    New-Item $owned, $out -ItemType Directory -Force | Out-Null
    foreach ($candidate in @('R','S','T','U','V','W','X','Y','Z')) {
        if (-not (Test-Path ($candidate + ':\')) -and -not (Get-PSDrive -Name $candidate -ErrorAction SilentlyContinue)) { $letter = $candidate; break }
    }
    if (-not $letter) { throw 'No unused test drive letter available.' }
    # Every destructive DiskPart command follows selection of this newly owned VHD.
    # Physical disks are never selected. The token and VHD path are unique per run.
    Invoke-OwnedDiskPart @("create vdisk file=`"$vhd`" maximum=128 type=expandable", "select vdisk file=`"$vhd`"", 'attach vdisk', 'create partition primary', 'format fs=ntfs quick label=MPswiftOwned', "assign letter=$letter")
    $volume = $letter + ':\'
    if (-not (Test-Path $volume)) { throw 'Owned VHD did not mount; full-disk validation cannot run.' }
    [IO.File]::WriteAllText((Join-Path $volume '.player-volume-token'), $token)
    @{ Directory = $volume; Token = $token } | ConvertTo-Json | Set-Content (Join-Path $owned '.player-owned-volume')
    'Owned development validation.' | Set-Content (Join-Path $owned '.player-crash-validation')
    $app = Join-Path $root 'src/Player.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/MPswift.exe'
    $fixture = Join-Path $root 'tests/fixtures/audio/pcm16.wav'
    $start = [Diagnostics.ProcessStartInfo]::new($app)
    $start.UseShellExecute = $false; $start.WorkingDirectory = $owned
    foreach ($argument in @('--crash-smoke', 'failures', $fixture)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(120000)) { throw 'Owned G9 resilience validation timed out.' }
    $path = Join-Path $owned 'artifacts/smoke/crash.json'
    if (-not (Test-Path $path)) { throw 'Owned G9 resilience report missing.' }
    $report = Get-Content $path -Raw | ConvertFrom-Json
    $report | Add-Member SourceCommit ((& git -C $root rev-parse HEAD).Trim())
    $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $out 'g9-resilience.json') -Encoding utf8
    if ($process.ExitCode -ne 0 -or $report.Status -ne 'g9-resilience-passed') { throw "Actual G9 resilience checks failed: $($report | ConvertTo-Json -Depth 10)" }
    Write-Host ($report | ConvertTo-Json -Depth 10)
} catch {
    [ordered]@{ Status = 'g9-resilience-failed'; Message = $_.Exception.Message; Windows = [Environment]::OSVersion.VersionString } | ConvertTo-Json | Set-Content (Join-Path $out 'g9-resilience-failure.json') -Encoding utf8
    throw
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    if (Test-Path $vhd) { Invoke-OwnedDiskPart @("select vdisk file=`"$vhd`"", 'detach vdisk') }
    if (Test-Path $owned) { Remove-Item $owned -Recurse -Force }
}
