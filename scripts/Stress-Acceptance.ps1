#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory, [string]$OutputDirectory = ([IO.Path]::GetTempPath()),
    [ValidateSet('Mixer','Shared','Exclusive')][string]$Mode = 'Mixer', [ValidateRange(60,7200)][int]$DurationSeconds = 7200, [string]$DeviceId)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitProcess) { throw 'Stress acceptance requires Windows x64.' }
. (Join-Path $PSScriptRoot 'Acceptance-Helpers.ps1')
$workspace = New-PlayerAcceptanceWorkspace $CandidateDirectory $OutputDirectory
$arguments = @('--stress-acceptance', $Mode.ToLowerInvariant(), $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($DeviceId) { $arguments += $DeviceId }
$run = $null; $native = $null; $failure = $null; $started = [DateTimeOffset]::UtcNow
try {
    $timeout = $DurationSeconds + $(if ($Mode -eq 'Mixer') { 90 } else { 3600 })
    $run = Start-PlayerAcceptanceProcess $workspace $arguments $timeout
    $path = Join-Path $workspace.Root 'g12-native.json'
    if (-not (Test-Path $path)) { throw 'Native G12 report is missing; inspect g12-failure.json and g12-progress.jsonl in the owned workspace.' }
    $native = Get-Content $path -Raw | ConvertFrom-Json
    if ($run.ExitCode -ne 0 -and $run.ExitCode -ne 3) { throw 'Native G12 validation failed; reports and bounded progress remain in the owned workspace.' }
} catch { $failure = $_.Exception.Message }
$os = Get-CimInstance Win32_OperatingSystem
$report = [ordered]@{ Status = $(if ($failure) { 'failed' } elseif ($run.ExitCode -eq 3) { 'blocked' } else { $native.Status });
    Failure = $failure; Mode = $Mode; RequestedSoakSeconds = $DurationSeconds; SourceCommit = $workspace.SourceCommit; SourceTreeDirty = $workspace.SourceTreeDirty;
    PackageManifestSha256 = $workspace.ManifestSha256; PowerShellVersion = $PSVersionTable.PSVersion.ToString();
    StartedUtc = $started.ToString('O'); EndedUtc = [DateTimeOffset]::UtcNow.ToString('O'); InvalidExternalDotnetRoots = $true;
    ActualNative = $native; Hardware = [ordered]@{ Windows = $os.Caption; Build = $os.BuildNumber; ProductType = $os.ProductType;
        Cpu = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed);
        TotalPhysicalMemoryBytes = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory;
        MediaDrivers = @(Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceClass='MEDIA'" | Select-Object DeviceName, DriverVersion, DriverProviderName, IsSigned) };
    ReferencePcAndSsd = 'not-verified'; WholePlayerUiLibraryWaveform = 'not-run'; ManualListening = 'not-run';
    EvidenceBoundary = 'Mixer is actual native PCM without an endpoint. Shared/Exclusive require real WASAPI. Less than 7200 seconds is a short check, never two-hour output acceptance. Headless telemetry is not full WPF/reference-PC resource acceptance.' }
$reportPath = Join-Path $workspace.Root 'g12-stress.json'
$report | ConvertTo-Json -Depth 20 | Set-Content $reportPath -Encoding utf8
Write-Host ('G12 report: ' + $reportPath)
if ($failure) { throw $failure }
[pscustomobject]@{ Workspace = $workspace.Root; Report = $reportPath; Status = $report.Status }
