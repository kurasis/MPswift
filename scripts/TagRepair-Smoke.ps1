#requires -Version 7.4
[CmdletBinding()]
param([switch]$IsolatedRunner, [string]$WorkerDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or -not $IsolatedRunner -or $env:CI -ne 'true') { throw 'Packaged CLI writes run only in explicitly isolated Windows CI with owned copies.' }
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/portable'
$audit = Get-Content (Join-Path $output 'tagrepair-audit.json') -Raw | ConvertFrom-Json
$zip = Join-Path $output $audit.Zip
if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.ZipSha256) { throw 'CLI ZIP changed before smoke.' }
if (-not $WorkerDirectory) {
    $workspace = Join-Path $root ('artifacts/tagrepair-smoke-' + [guid]::NewGuid().ToString('N'))
    New-Item $workspace -ItemType Directory | Out-Null
    try {
        # A separate process releases the independently loaded TagLib DLL before cleanup.
        & pwsh -NoProfile -File $PSCommandPath -IsolatedRunner -WorkerDirectory $workspace
        if ($LASTEXITCODE -ne 0) { throw 'Packaged CLI worker failed; see its preceding error.' }
    } finally { Remove-Item $workspace -Recurse -Force }
    return
}
$workspace = [IO.Path]::GetFullPath($WorkerDirectory)
if ([IO.Path]::GetDirectoryName($workspace) -ne (Join-Path $root 'artifacts') -or [IO.Path]::GetFileName($workspace) -notmatch '^tagrepair-smoke-[a-f0-9]{32}$' -or -not (Test-Path $workspace -PathType Container)) {
    throw 'Worker directory must be the fresh workspace created by this CI wrapper.'
}
$previousRuntime = $env:DOTNET_ROOT
try {
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
            $stream = [IO.File]::OpenRead($path)
            $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
            try {
                if ([IO.Path]::GetExtension($path) -eq '.flac') {
                    # TagLib's FLAC invariant range includes mutable metadata; parse the actual frame offset independently.
                    $header = [byte[]]::new(4); $stream.ReadExactly($header, 0, 4)
                    if ([Text.Encoding]::ASCII.GetString($header) -ne 'fLaC') { throw 'Owned FLAC signature is missing.' }
                    $last = $false; $blocks = 0
                    while (-not $last) {
                        if (++$blocks -gt 4096) { throw 'Owned FLAC metadata bound exceeded.' }
                        $stream.ReadExactly($header, 0, 4)
                        $last = ($header[0] -band 0x80) -ne 0
                        $size = ([int]$header[1] -shl 16) -bor ([int]$header[2] -shl 8) -bor [int]$header[3]
                        if ($size -gt $stream.Length - $stream.Position) { throw 'Truncated owned FLAC metadata.' }
                        $stream.Position += $size
                    }
                    $begin = $stream.Position; $end = $stream.Length
                }
                if ($begin -lt 0 -or $end -le $begin) { throw 'Cannot independently identify real audio range.' }
                $stream.Position = $begin; $remaining = $end - $begin; $buffer = [byte[]]::new(81920)
                while ($remaining -gt 0) { $count = $stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining)); if ($count -eq 0) { throw 'Truncated owned source.' }; $hash.AppendData($buffer, 0, $count); $remaining -= $count }
                [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            } finally { $hash.Dispose(); $stream.Dispose() }
        } finally { $file.Dispose() }
    }
    function AddOwnedMpegPrefix([string]$path, [int]$count) {
        $file = [TagLib.File]::Create($path)
        try { $offset = [int]$file.InvariantStartPosition } finally { $file.Dispose() }
        $bytes = [IO.File]::ReadAllBytes($path)
        if ($offset -lt 0 -or $offset -gt $bytes.Length - 4 -or $count -lt 14) { throw 'Invalid owned MPEG prefix fixture.' }
        $padded = [byte[]]::new($bytes.Length + $count)
        [Array]::Copy($bytes, 0, $padded, 0, $offset)
        $marker = [Text.Encoding]::ASCII.GetBytes('Legacy padding'); [Array]::Copy($marker, 0, $padded, $offset, $marker.Length)
        [Array]::Copy($bytes, $offset, $padded, $offset + $count, $bytes.Length - $offset)
        [IO.File]::WriteAllBytes($path, $padded)
    }
    $originals = @{}; $audioHashes = @{}; $fixtureHashes = @{}
    foreach ($format in @('mp3','flac')) {
        $fixture = Join-Path $root "tests/fixtures/audio/$(if ($format -eq 'mp3') { 'mp3-cbr.mp3' } else { 'flac16.flac' })"
        $fixtureHashes[$fixture] = (Get-FileHash $fixture -Algorithm SHA256).Hash
        $path = Join-Path $album "Owned Беларускае.$format"; Copy-Item $fixture $path
        $file = [TagLib.File]::Create($path)
        try { $file.Tag.Title = [Text.Encoding]::Latin1.GetString($encoding.GetBytes('Людзі і сонца')); $file.Tag.Performers = @([Text.Encoding]::Latin1.GetString($encoding.GetBytes('Індыга'))); $file.Save() } finally { $file.Dispose() }
        if ($format -eq 'mp3') { AddOwnedMpegPrefix $path 257 }
        $originals[$path] = (Get-FileHash $path -Algorithm SHA256).Hash; $audioHashes[$path] = AudioHash $path
    }
    $cue = Join-Path $album 'Owned.cue'
    [IO.File]::WriteAllBytes($cue, $encoding.GetBytes("TITLE `"Людзі`"`r`nFILE `"Owned Беларускае.flac`" WAVE`r`nTRACK 01 AUDIO`r`nINDEX 01 00:00:00`r`n"))
    $originals[$cue] = (Get-FileHash $cue -Algorithm SHA256).Hash
    $private = Join-Path $album 'private.txt'; [IO.File]::WriteAllText($private, 'unknown data retained')
    $log = Join-Path $root 'artifacts/smoke/tagrepair-cli.log'
    $preview = @(& $exe $album)
    $preview | Set-Content $log -Encoding utf8
    if ($LASTEXITCODE -ne 0 -or -not ($preview -match 'planned=3;')) { throw 'Actual CLI recursive preview did not inspect all three owned files.' }
    foreach ($path in $originals.Keys) { if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $originals[$path]) { throw 'CLI preview changed original bytes.' } }
    if (@(Get-ChildItem $album -Filter '*.bak').Count -ne 0) { throw 'Preview created backups.' }
    $applied = @(& $exe $album --apply)
    $applied | Add-Content $log -Encoding utf8
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
    $again | Add-Content $log -Encoding utf8
    if ($LASTEXITCODE -ne 0 -or -not ($again -match 'repaired=0; unchanged=3;')) { throw 'Physical repairs are not idempotent.' }
    if (@(Get-ChildItem $album -Filter '*.bak').Count -ne 3 -or @(Get-ChildItem $album -Filter '*.tmp').Count -ne 0 -or [IO.File]::ReadAllText($private) -ne 'unknown data retained') { throw 'Unexpected backups/temp files or private-file modification.' }
    $invalidAlbum = Join-Path $workspace 'Owned unsupported before next album'; New-Item $invalidAlbum -ItemType Directory | Out-Null
    $bad = Join-Path $invalidAlbum 'Unsupported prefix.mp3'; Copy-Item (Join-Path $root 'tests/fixtures/audio/mp3-cbr.mp3') $bad
    $file = [TagLib.File]::Create($bad)
    try { $file.Tag.Title = [Text.Encoding]::Latin1.GetString($encoding.GetBytes('Людзі')); $file.Save() } finally { $file.Dispose() }
    AddOwnedMpegPrefix $bad 8193; $badHash = (Get-FileHash $bad -Algorithm SHA256).Hash
    $next = Join-Path $invalidAlbum 'next-album'; New-Item $next -ItemType Directory | Out-Null
    $valid = Join-Path $next 'valid.cue'; [IO.File]::WriteAllBytes($valid, $encoding.GetBytes("TITLE `"Людзі`"`nFILE `"owned.flac`" WAVE`nTRACK 01 AUDIO`nINDEX 01 00:00:00`n"))
    $validHash = (Get-FileHash $valid -Algorithm SHA256).Hash
    $invalidPreview = @(& $exe $invalidAlbum 2>&1); $invalidPreview | Add-Content $log -Encoding utf8
    if ($LASTEXITCODE -ne 1 -or -not ($invalidPreview -match 'planned=1; unchanged=0; errors=1') -or
        -not ($invalidPreview -match 'Unsupported prefix.mp3') -or -not ($invalidPreview -match 'Unrecognized MPEG audio start') -or ($invalidPreview -match 'Unhandled exception')) { throw 'Unsupported-file preview did not report and continue.' }
    if ((Get-FileHash $bad -Algorithm SHA256).Hash -ne $badHash -or (Get-FileHash $valid -Algorithm SHA256).Hash -ne $validHash -or @(Get-ChildItem $invalidAlbum -Filter '*.bak' -Recurse).Count -ne 0) { throw 'Mixed invalid-file preview wrote data.' }
    $invalidApply = @(& $exe $invalidAlbum --apply 2>&1); $invalidApply | Add-Content $log -Encoding utf8
    if ($LASTEXITCODE -ne 1 -or -not ($invalidApply -match 'repaired=1; unchanged=0; errors=1') -or ($invalidApply -match 'Unhandled exception') -or
        (Get-FileHash $bad -Algorithm SHA256).Hash -ne $badHash) { throw 'Unsupported-file apply crashed, stopped or changed rejected bytes.' }
    $validBackup = @(Get-ChildItem $invalidAlbum -Filter '*.bak' -Recurse)
    if ($validBackup.Count -ne 1 -or (Get-FileHash $validBackup[0].FullName -Algorithm SHA256).Hash -ne $validHash -or @(Get-ChildItem $invalidAlbum -Filter '*.tmp' -Recurse).Count -ne 0) { throw 'Continued CUE apply did not preserve its original and clean staging.' }
    foreach ($path in $fixtureHashes.Keys) { if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $fixtureHashes[$path]) { throw 'Original CC0 fixture was changed.' } }
    $trx = Get-ChildItem (Join-Path $root 'artifacts/tagrepair-test-results') -Filter '*.trx' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    [xml]$tests = Get-Content $trx.FullName -Raw
    $counters = $tests.SelectSingleNode("//*[local-name()='Counters']")
    if ([int]$counters.total -lt 50 -or $counters.passed -ne $counters.total -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) { throw 'CLI Windows tests must all execute and pass.' }
    [ordered]@{ Status = 'tagrepair-packaged-cli-passed'; SourceCommit = $audit.SourceCommit; ProductVersion = $audit.ProductVersion; ZipSha256 = $audit.ZipSha256;
        RealExe = $true; RuntimeIncluded = $true; PreviewNoWrites = $true; PhysicalMp3FlacCue = $true; ExactOriginalBackups = $true; IndependentAudioHashes = $true;
        SourceFixturesUnchanged = $true; PrivateFilesUnchanged = $true; Idempotent = $true; PaddedMp3PrefixAndAudio = $true; InvalidFileContinuation = $true;
        WindowsTests = [int]$counters.total; WindowsTestsFailed = 0; WindowsTestsSkipped = 0 } |
        ConvertTo-Json | Set-Content (Join-Path $output 'tagrepair-smoke.json') -Encoding utf8
} finally { $env:DOTNET_ROOT = $previousRuntime }
