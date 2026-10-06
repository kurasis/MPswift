#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory, [string]$OutputDirectory = ([IO.Path]::GetTempPath()),
    [ValidateSet('en','ru')][string]$Language = 'en', [switch]$RequireCleanBaseline, [switch]$SkipTraffic)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitProcess) { throw 'Desktop acceptance requires Windows x64.' }
. (Join-Path $PSScriptRoot 'Acceptance-Helpers.ps1')
$workspace = New-PlayerAcceptanceWorkspace $CandidateDirectory $OutputDirectory
$data = Join-Path $workspace.App 'Data'; New-Item $data -ItemType Directory | Out-Null
@{ SchemaVersion = 1; Language = $Language } | ConvertTo-Json | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
$trace = $null; $traceStopped = $false; $began = [DateTimeOffset]::UtcNow; $runs = @{}; $failure = $null
try {
    if (-not $SkipTraffic) { $trace = Start-PlayerNetworkTrace $workspace; Send-PlayerNetworkControl }
    foreach ($phase in @('first','restart')) {
        $runs[$phase] = Start-PlayerAcceptanceProcess $workspace @('--desktop-acceptance', $phase)
        if ($runs[$phase].ExitCode -ne 0) { throw "Owned desktop $phase failed. Inspect $($workspace.Root)/desktop-$phase.json." }
    }
    if ($null -ne $trace) { Send-PlayerNetworkControl; $loss = Stop-PlayerNetworkTrace $trace; $traceStopped = $true }
} catch { $failure = $_.Exception.Message }
finally {
    if ($null -ne $trace -and -not $traceStopped) {
        try { $loss = Stop-PlayerNetworkTrace $trace; $traceStopped = $true }
        catch { & logman.exe stop $trace.Name -ets 2>&1 | Out-Null }
    }
}
$ended = [DateTimeOffset]::UtcNow
$traffic = @{}
if ($null -eq $failure -and $null -ne $trace -and $traceStopped) {
    $xml = Join-Path $workspace.Root 'network.xml'
    $conversion = & tracerpt.exe $trace.Etl -o $xml -of XML -y 2>&1
    if ($LASTEXITCODE -ne 0) { $failure = 'Owned ETW conversion failed: ' + ($conversion -join ' ') }
    else {
        foreach ($phase in @('first','restart')) {
            $context = Join-Path $workspace.Root ('trace-context-' + $phase + '.json')
            [ordered]@{ RootProcessId = $runs[$phase].ProcessId; ControlProcessId = $PID; StartedUtc = $began.ToString('O'); EndedUtc = $ended.ToString('O'); EventsLost = $loss.EventsLost; LogBuffersLost = $loss.LogBuffersLost; RealTimeBuffersLost = $loss.RealTimeBuffersLost } | ConvertTo-Json | Set-Content $context -Encoding utf8
            $name = 'network-report-' + $phase + '.json'
            $analysis = Start-PlayerAcceptanceProcess $workspace @('--network-report', $xml, $context, $name)
            if (Test-Path (Join-Path $workspace.Root $name)) { $traffic[$phase] = Get-Content (Join-Path $workspace.Root $name) -Raw | ConvertFrom-Json }
            if ($analysis.ExitCode -ne 0) { $failure = "ETW $phase was failed or insufficient evidence."; break }
        }
    }
}
$os = Get-CimInstance Win32_OperatingSystem
$upAdapters = @(Get-NetAdapter -ErrorAction Stop | Where-Object Status -eq 'Up')
$runtimeFound = @()
foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:USERPROFILE '.dotnet'))) {
    if ($base) { foreach ($relative in @('dotnet/sdk','dotnet/shared/Microsoft.NETCore.App','dotnet/shared/Microsoft.WindowsDesktop.App','sdk','shared/Microsoft.NETCore.App')) { if (Test-Path (Join-Path $base $relative)) { $runtimeFound += $relative } } }
}
$dotnetOnPath = $null -ne (Get-Command dotnet.exe -ErrorAction SilentlyContinue)
$first = if (Test-Path (Join-Path $workspace.Root 'desktop-first.json')) { Get-Content (Join-Path $workspace.Root 'desktop-first.json') -Raw | ConvertFrom-Json } else { $null }
$restart = if (Test-Path (Join-Path $workspace.Root 'desktop-restart.json')) { Get-Content (Join-Path $workspace.Root 'desktop-restart.json') -Raw | ConvertFrom-Json } else { $null }
$windows11 = $os.ProductType -eq 1 -and [int]$os.BuildNumber -ge 22000
$standard = $null -ne $first -and -not $first.Desktop.ElevatedAdministrator
$baseline = $null -eq $failure -and $windows11 -and $standard -and $upAdapters.Count -eq 0 -and $runtimeFound.Count -eq 0 -and -not $dotnetOnPath -and -not $SkipTraffic
$report = [ordered]@{ Status = $(if ($null -ne $failure) { 'failed' } else { 'g10-owned-desktop-workflow-passed' }); Failure = $failure;
    SourceCommit = $workspace.SourceCommit; SourceTreeDirty = $workspace.SourceTreeDirty; PackageManifestSha256 = $workspace.ManifestSha256; StartedUtc = $began.ToString('O'); EndedUtc = $ended.ToString('O'); PowerShellVersion = $PSVersionTable.PSVersion.ToString();
    Windows = [ordered]@{ Caption = $os.Caption; Build = $os.BuildNumber; ProductType = $os.ProductType; Windows11Client = $windows11 };
    StandardUserApp = $standard; UpNetworkAdapters = $upAdapters.Count; InstalledRuntimeKnownLocations = $runtimeFound; DotnetOnPath = $dotnetOnPath;
    RuntimeDetectionBoundary = 'Known global/user locations and PATH, not an exhaustive drive scan'; InvalidExternalDotnetRoot = $true;
    FirstRun = $first; ActualApphostRestart = $restart; Network = $traffic; TrafficRequested = -not $SkipTraffic;
    CleanOfflineBaselinePrerequisitesMet = $baseline; FullWindows11Acceptance = 'not-complete: MP3/FLAC baseline, Narrator, physical DPI/monitor and listening records remain separate';
    NetworkMethod = 'Unique owned Kernel-Network/Kernel-Process ETW session; stop-time native loss counters; local TCP control before/after; PID and descendants. Other OS traffic is not attributed to the app.';
    TraceTools = @('logman.exe','tracerpt.exe') | ForEach-Object { $path = Join-Path $env:SystemRoot ('System32/' + $_); [ordered]@{ Tool = $_; Version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion } };
    TraceSha256 = $(if ($null -ne $trace -and (Test-Path $trace.Etl)) { (Get-FileHash $trace.Etl -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }) }
$report | ConvertTo-Json -Depth 18 | Set-Content (Join-Path $workspace.Root 'g10-desktop.json') -Encoding utf8
Write-Host ('G10 report: ' + (Join-Path $workspace.Root 'g10-desktop.json'))
if ($null -ne $failure) { throw $failure }
if ($RequireCleanBaseline -and -not $baseline) { throw 'Clean baseline prerequisites were not met. The report preserves each observed blocker; no network/privilege settings were changed.' }
[pscustomobject]@{ Workspace = $workspace.Root; Report = (Join-Path $workspace.Root 'g10-desktop.json') }
