#requires -Version 7.4
[CmdletBinding()]
param([switch]$IsolatedRunner)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or -not $IsolatedRunner -or $env:CI -ne 'true') { throw 'Packaged CLI writes run only in explicitly isolated Windows CI with owned copies.' }
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/portable'
$audit = Get-Content (Join-Path $output 'tagrepair-audit.json') -Raw | ConvertFrom-Json
$zip = Join-Path $output $audit.Zip
if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.ZipSha256) { throw 'CLI ZIP changed before smoke.' }
$workspace = Join-Path $root ('artifacts/tagrepair-smoke-' + [guid]::NewGuid().ToString('N'))
$previousRuntime = $env:DOTNET_ROOT
try {
    New-Item $workspace -ItemType Directory | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $workspace)
    $app = Join-Path $workspace 'MPswift.TagRepair'
    & "$PSScriptRoot/Verify-TagRepairPackage.ps1" -Directory $app
    $exe = Join-Path $app 'MPswift.TagRepair.exe'
    $env:DOTNET_ROOT = Join-Path $workspace 'nonexistent-runtime'
    $version = & $exe --version
    if ($LASTEXITCODE -ne 0 -or $version -ne $audit.ProductVersion.Split('+')[0]) { throw 'Self-contained CLI did not report the matching version.' }
    [void][Reflection.Assembly]::LoadFrom((Join-Path $app 'TagLibSharp.dll'))
    [Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
    $encoding = [Text.Encoding]::GetEncoding(1251)
    $album = Join-Path $workspace 'Owned albums Беларускае'
    New-Item $album -ItemType Directory | Out-Null
    function AudioHash([string]$path) {
        $file = [TagLib.File]::Create($path)
        try {
            $begin = $file.InvariantStartPosition; $end = $file.InvariantEndPosition
            if ($begin -lt 0 -or $end -le $begin) { throw 'Cannot independently identify real audio range.' }
            $stream = [IO.File]::OpenRead($path)
            $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
            try {
                $stream.Position = $begin; $remaining = $end - $begin; $buffer = [byte[]]::new(81920)
                while ($remaining -gt 0) { $count = $stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining)); if ($count -eq 0) { throw 'Truncated owned source.' }; $hash.AppendData($buffer, 0, $count); $remaining -= $count }
                [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            } finally { $hash.Dispose(); $stream.Dispose() }
        } finally { $file.Dispose() }
    }
    $originals = @{}; $audioHashes = @{}; $fixtureHashes = @{}
    foreach ($format in @('mp3','flac')) {
        $fixture = Join-Path $root "tests/fixtures/audio/$(if ($format -eq 'mp3') { 'mp3-cbr.mp3' } else { 'flac16.flac' })"
        $fixtureHashes[$fixture] = (Get-FileHash $fixture -Algorithm SHA256).Hash
        $path = Join-Path $album "Owned Беларускае.$format"; Copy-Item $fixture $path
        $file = [TagLib.File]::Create($path)
        try { $file.Tag.Title = [Text.Encoding]::Latin1.GetString($encoding.GetBytes('Людзі і сонца')); $file.Tag.Performers = @([Text.Encoding]::Latin1.GetString($encoding.GetBytes('Індыга'))); $file.Save() } finally { $file.Dispose() }
        $originals[$path] = (Get-FileHash $path -Algorithm SHA256).Hash; $audioHashes[$path] = AudioHash $path
    }
    $cue = Join-Path $album 'Owned.cue'
    [IO.File]::WriteAllBytes($cue, $encoding.GetBytes("TITLE `"Людзі`"`r`nFILE `"Owned Беларускае.flac`" WAVE`r`nTRACK 01 AUDIO`r`nINDEX 01 00:00:00`r`n"))
    $originals[$cue] = (Get-FileHash $cue -Algorithm SHA256).Hash
    $private = Join-Path $album 'private.txt'; [IO.File]::WriteAllText($private, 'unknown data retained')
    $preview = @(& $exe $album)
    if ($LASTEXITCODE -ne 0 -or -not ($preview -match 'planned=3;')) { throw 'Actual CLI recursive preview did not inspect all three owned files.' }
    foreach ($path in $originals.Keys) { if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $originals[$path]) { throw 'CLI preview changed original bytes.' } }
    if (@(Get-ChildItem $album -Filter '*.bak').Count -ne 0) { throw 'Preview created backups.' }
    $applied = @(& $exe $album --apply)
    if ($LASTEXITCODE -ne 0 -or -not ($applied -match 'repaired=3;')) { throw 'Actual CLI did not apply all three owned repairs.' }
    foreach ($path in $originals.Keys) {
        $backup = @(Get-ChildItem $album -File | Where-Object { $_.Name.StartsWith([IO.Path]::GetFileName($path) + '.mpswift-', [StringComparison]::Ordinal) -and $_.Extension -eq '.bak' })
        if ($backup.Count -ne 1 -or (Get-FileHash $backup[0].FullName -Algorithm SHA256).Hash -ne $originals[$path]) { throw 'CLI retained backup differs from its exact original.' }
        if ($audioHashes.ContainsKey($path)) {
            if ((AudioHash $path) -ne $audioHashes[$path]) { throw 'Independent MPEG/FLAC audio-range hash changed.' }
            $file = [TagLib.File]::Create($path)
            try { if ($file.Tag.Title -ne 'Людзі і сонца' -or ($file.Tag.Performers -join ',') -ne 'Індыга') { throw 'Physical saved tags are still garbled.' } } finally { $file.Dispose() }
        }
    }
    $text = [Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($cue))
    if (-not $text.Contains('TITLE "Людзі"') -or -not $text.Contains('FILE "Owned Беларускае.flac" WAVE') -or -not $text.Contains('INDEX 01 00:00:00')) { throw 'Physical CUE UTF-8 or references/timings failed.' }
    $again = @(& $exe $album --apply)
    if ($LASTEXITCODE -ne 0 -or -not ($again -match 'repaired=0; unchanged=3;')) { throw 'Physical repairs are not idempotent.' }
    if (@(Get-ChildItem $album -Filter '*.bak').Count -ne 3 -or @(Get-ChildItem $album -Filter '*.tmp').Count -ne 0 -or [IO.File]::ReadAllText($private) -ne 'unknown data retained') { throw 'Unexpected backups/temp files or private-file modification.' }
    foreach ($path in $fixtureHashes.Keys) { if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $fixtureHashes[$path]) { throw 'Original CC0 fixture was changed.' } }
    $trx = Get-ChildItem (Join-Path $root 'artifacts/tagrepair-test-results') -Filter '*.trx' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    [xml]$tests = Get-Content $trx.FullName -Raw
    $counters = $tests.SelectSingleNode("//*[local-name()='Counters']")
    if ([int]$counters.total -lt 36 -or $counters.passed -ne $counters.total -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) { throw 'CLI Windows tests must all execute and pass.' }
    [ordered]@{ Status = 'tagrepair-packaged-cli-passed'; SourceCommit = $audit.SourceCommit; ProductVersion = $audit.ProductVersion; ZipSha256 = $audit.ZipSha256;
        RealExe = $true; RuntimeIncluded = $true; PreviewNoWrites = $true; PhysicalMp3FlacCue = $true; ExactOriginalBackups = $true; IndependentAudioHashes = $true;
        SourceFixturesUnchanged = $true; PrivateFilesUnchanged = $true; Idempotent = $true; WindowsTests = [int]$counters.total; WindowsTestsFailed = 0; WindowsTestsSkipped = 0 } |
        ConvertTo-Json | Set-Content (Join-Path $output 'tagrepair-smoke.json') -Encoding utf8
    $preview + $applied + $again | Set-Content (Join-Path $root 'artifacts/smoke/tagrepair-cli.log') -Encoding utf8
} finally { $env:DOTNET_ROOT = $previousRuntime; if (Test-Path $workspace) { Remove-Item $workspace -Recurse -Force } }
