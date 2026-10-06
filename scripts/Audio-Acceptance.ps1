#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory, [string]$OutputDirectory = ([IO.Path]::GetTempPath()),
    [ValidateSet('Probe','Shared','Exclusive','Digital')][string]$Mode = 'Probe', [string]$DeviceId)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitProcess) { throw 'Audio acceptance requires Windows x64.' }
. (Join-Path $PSScriptRoot 'Acceptance-Helpers.ps1')
$workspace = New-PlayerAcceptanceWorkspace $CandidateDirectory $OutputDirectory
$arguments = @('--audio-acceptance', $Mode.ToLowerInvariant())
if ($DeviceId) { $arguments += $DeviceId }
$started = [DateTimeOffset]::UtcNow; $run = $null; $failure = $null; $audio = $null
try {
    $run = Start-PlayerAcceptanceProcess $workspace $arguments
    $nativeReport = Join-Path $workspace.Root ('audio-' + $Mode.ToLowerInvariant() + '.json')
    if (-not (Test-Path $nativeReport)) { throw 'Native audio report is missing. Inspect the owned audio-failure.json.' }
    $audio = Get-Content $nativeReport -Raw | ConvertFrom-Json
    if ($run.ExitCode -ne 0 -and $run.ExitCode -ne 3) { throw 'Native audio validation failed; the actual report and capture remain in the owned workspace.' }
} catch { $failure = $_.Exception.Message }
$drivers = @(Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceClass='MEDIA'" | Select-Object DeviceName, DriverVersion, DriverProviderName, IsSigned)
$report = [ordered]@{ Status = $(if ($failure) { 'failed' } elseif ($run.ExitCode -eq 3) { 'blocked' } else { $audio.Status });
    Failure = $failure; Mode = $Mode; SourceCommit = $workspace.SourceCommit; SourceTreeDirty = $workspace.SourceTreeDirty;
    PackageManifestSha256 = $workspace.ManifestSha256; StartedUtc = $started.ToString('O'); EndedUtc = [DateTimeOffset]::UtcNow.ToString('O');
    InvalidExternalDotnetRoots = $true; ActualApphostProcessId = $(if ($run) { $run.ProcessId } else { $null });
    ActualNativeAudio = $audio; InstalledMediaDrivers = $drivers; ManualListening = 'not-run'; PhysicalHotplugSleep = 'not-run';
    EvidenceBoundary = 'Probe only enumerates actual native devices. Output modes require a real endpoint; Digital additionally requires matching stereo loopback and measured capture. Other codec boundaries and hardware/listening acceptance remain separate.' }
$reportPath = Join-Path $workspace.Root 'g11-audio.json'
$report | ConvertTo-Json -Depth 20 | Set-Content $reportPath -Encoding utf8
Write-Host ('G11 report: ' + $reportPath)
if ($failure) { throw $failure }
[pscustomobject]@{ Workspace = $workspace.Root; Report = $reportPath; Status = $report.Status }
