#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Packaged executable smoke is NOT RUN: Windows x64 required.' }
$root = Split-Path $PSScriptRoot -Parent
$audit = Get-Content (Join-Path $root 'artifacts/portable/package-audit.json') -Raw | ConvertFrom-Json
$archive = Join-Path $root "artifacts/portable/$($audit.Zip)"
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.ZipSha256) { throw 'Outer ZIP checksum mismatch.' }
$owned = Join-Path $root ('artifacts/package-check-' + [guid]::NewGuid().ToString('N') + ' Unicode Музыка 🎵')
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $owned)
    $app = Join-Path $owned 'LocalAudioPlayer'
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    # Negative verification affects only this freshly extracted owned copy.
    $help = Join-Path $app 'docs/USER_HELP.md'
    $original = [IO.File]::ReadAllBytes($help)
    [IO.File]::AppendAllText($help, 'tamper-probe')
    $rejected = $false
    try { & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app } catch { $rejected = $true }
    if (-not $rejected) { throw 'Changed candidate file was accepted.' }
    [IO.File]::WriteAllBytes($help, $original)
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    $working = Join-Path $owned 'different working directory'
    New-Item $working -ItemType Directory -Force | Out-Null
    $evidence = Join-Path $working 'artifacts/smoke'
    New-Item (Join-Path $evidence 'stage-c-data') -ItemType Directory -Force | Out-Null
    @{ SchemaVersion = 1; Language = 'ru' } | ConvertTo-Json | Set-Content (Join-Path $evidence 'stage-c-data/settings.json') -Encoding utf8
    $fixture = Join-Path $root 'tests/fixtures/audio/pcm16.wav'
    $tagged = Join-Path $root 'tests/fixtures/audio/flac16.flac'
    # Start the apphost, never dotnet run. Its private self-contained runtime must resolve with an invalid DOTNET_ROOT.
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'Player.App.exe'))
    $start.UseShellExecute = $false; $start.WorkingDirectory = $working
    foreach ($argument in @('--ui-smoke', $fixture, $tagged)) { $start.ArgumentList.Add($argument) }
    $start.Environment['DOTNET_ROOT'] = Join-Path $owned 'no external dotnet installation'
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Extracted packaged WPF smoke timed out.' }
    if ($process.ExitCode -ne 0) { throw "Packaged executable smoke failed with $($process.ExitCode). See $evidence/ui.json." }
    $ui = Get-Content (Join-Path $evidence 'ui.json') -Raw | ConvertFrom-Json
    if ($ui.Status -ne 'ui-smoke-passed') { throw 'Packaged UI report did not pass.' }
    $out = Join-Path $root 'artifacts/smoke'
    New-Item $out -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $evidence 'ui.json') (Join-Path $out 'packaged-ui.json')
    Copy-Item (Join-Path $evidence 'stage-c-window.png') (Join-Path $out 'packaged-window-ru.png')
    foreach ($language in @('en','ru')) {
        $before = @(Get-ChildItem $owned -Directory -Filter 'player-acceptance-*' | ForEach-Object Name)
        $legacy = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
        try {
            & $legacy -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $app 'acceptance/Desktop-Acceptance.ps1') -CandidateDirectory $app -OutputDirectory $owned -Language $language
            if ($LASTEXITCODE -ne 0) { throw 'Packaged desktop runner failed under Windows PowerShell 5.1.' }
        }
        finally {
            Get-ChildItem $owned -Directory -Filter 'player-acceptance-*' | Where-Object Name -notin $before | ForEach-Object {
                foreach ($name in @('g10-desktop.json','desktop-first.json','desktop-restart.json','network-report-first.json','network-report-restart.json','network-report-failure.json')) {
                    $file = Join-Path $_.FullName $name
                    if (Test-Path $file) { Copy-Item $file (Join-Path $out "g10-$language-$name") }
                }
                $startup = Join-Path $_.FullName 'artifacts/smoke/ui.json'
                if (Test-Path $startup) { Copy-Item $startup (Join-Path $out "g10-$language-startup.json") }
            }
        }
        $created = @(Get-ChildItem $owned -Directory -Filter 'player-acceptance-*' | Where-Object Name -notin $before)
        if ($created.Count -ne 1) { throw 'Desktop runner did not create exactly one owned workspace.' }
        $desktop = [pscustomobject]@{ Workspace = $created[0].FullName; Report = (Join-Path $created[0].FullName 'g10-desktop.json') }
        $g10 = Get-Content $desktop.Report -Raw | ConvertFrom-Json
        if ($g10.Status -ne 'g10-owned-desktop-workflow-passed' -or $g10.Network.first.Status -ne 'no-app-network-events-observed' -or
            $g10.Network.restart.Status -ne 'no-app-network-events-observed' -or -not $g10.ActualApphostRestart.ActualProcessRestart -or
            -not $g10.PowerShellVersion.StartsWith('5.1.')) { throw 'G10 desktop/ETW evidence failed or is incomplete.' }
        Copy-Item $desktop.Report (Join-Path $out "g10-desktop-$language.json")
        Get-ChildItem $desktop.Workspace -Filter 'g10-*.png' | ForEach-Object { Copy-Item $_.FullName (Join-Path $out $_.Name) }
    }
    $before = @(Get-ChildItem $owned -Directory -Filter 'player-acceptance-*' | ForEach-Object Name)
    try {
        & $legacy -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $app 'acceptance/Audio-Acceptance.ps1') -CandidateDirectory $app -OutputDirectory $owned -Mode Probe
        if ($LASTEXITCODE -ne 0) { throw 'Packaged audio runner failed under Windows PowerShell 5.1.' }
    } finally {
        Get-ChildItem $owned -Directory -Filter 'player-acceptance-*' | Where-Object Name -notin $before | ForEach-Object {
            foreach ($name in @('g11-audio.json','audio-probe.json','audio-failure.json')) {
                $file = Join-Path $_.FullName $name
                $destination = if ($name -eq 'g11-audio.json') { 'g11-device-probe.json' } else { "g11-$name" }
                if (Test-Path $file) { Copy-Item $file (Join-Path $out $destination) }
            }
        }
    }
    $audio = Get-Content (Join-Path $out 'g11-device-probe.json') -Raw | ConvertFrom-Json
    if ($audio.Status -ne 'g11-device-probe-complete' -or -not $audio.PowerShellVersion.StartsWith('5.1.')) { throw 'Actual native device enumeration did not complete.' }
    [ordered]@{ Status = 'packaged-executable-smoke-passed'; SourceCommit = $audit.SourceCommit; ZipSha256 = $audit.ZipSha256; UnicodeExtractionPath = $true; ArbitraryWorkingDirectory = $true; InvalidExternalDotnetRoot = $true; TamperRejected = $rejected; Windows = [Environment]::OSVersion.VersionString; CleanWindows11WithoutSdk = 'not-run'; NetworkDisconnected = 'not-run'; DeviceOutput = 'not-run'; DistributionApproved = $false } | ConvertTo-Json | Set-Content (Join-Path $out 'package-smoke.json') -Encoding utf8
} finally {
    if (Test-Path $owned) { Remove-Item $owned -Recurse -Force }
}
