#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Version-Helpers.ps1"
$Directory = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content (Join-Path $Directory 'tagrepair-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.product -ne 'MPswift.TagRepair' -or $manifest.platform -ne 'win-x64' -or $manifest.distributionApproved -ne $false -or $manifest.sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid CLI package identity.' }
$version = Get-PlayerVersionInfo $manifest.productVersion
$files = @{}
foreach ($item in $manifest.files) {
    if ($item.path -match '(^|/)\.\.?(/|$)|:|\\|^/' -or $item.path -match '(?i)\.(bak|tmp|db|mp3|flac|cue|pdb)$' -or $files.ContainsKey($item.path) -or $item.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Unsafe/duplicate/private CLI package entry.' }
    $path = Join-Path $Directory $item.path
    if (-not (Test-Path $path -PathType Leaf) -or (Get-Item $path).Length -ne $item.bytes -or (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw "CLI package file mismatch: $($item.path)" }
    $files[$item.path] = $item.sha256
}
foreach ($file in Get-ChildItem $Directory -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\','/')
    if ($relative -cnotin @('tagrepair-manifest.json','SHA256SUMS.txt') -and -not $files.ContainsKey($relative)) { throw "Unexpected CLI package file: $relative" }
}
foreach ($required in @('MPswift.TagRepair.exe','MPswift.TagRepair.dll','Player.Core.dll','TagLibSharp.dll','coreclr.dll','README.md','notices/TagLibSharp/2.3.0/COPYING','notices/TagLibSharp/2.3.0/AUTHORS','notices/runtime/LICENSE.TXT','notices/runtime/THIRD-PARTY-NOTICES.TXT')) {
    if (-not $files.ContainsKey($required)) { throw "Missing CLI runtime/notice: $required" }
}
if (@($manifest.licenseTextSources).Count -ne 2 -or $manifest.tagLibVersion -ne '2.3.0') { throw 'Pinned TagLib metadata/notice provenance is incomplete.' }
foreach ($text in $manifest.licenseTextSources) {
    $name = 'notices/TagLibSharp/2.3.0/' + $text.fileName
    if ($text.fileName -cnotin @('COPYING','AUTHORS') -or $text.sha256 -notmatch '^[a-f0-9]{64}$' -or $text.sourceCommit -notmatch '^[a-f0-9]{40}$' -or
        $text.sourceUrl -ne "https://raw.githubusercontent.com/mono/taglib-sharp/$($text.sourceCommit)/$($text.fileName)" -or $files[$name] -ne $text.sha256) { throw 'CLI TagLib license text/provenance mismatch.' }
}
$sums = @{}
foreach ($line in Get-Content (Join-Path $Directory 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw 'Malformed CLI checksum row.' }
    $hash = $Matches[1]; $name = $Matches[2]
    if ($sums.ContainsKey($name) -or ($name -ne 'tagrepair-manifest.json' -and -not $files.ContainsKey($name))) { throw 'Unexpected/duplicate CLI checksum row.' }
    if ((Get-FileHash (Join-Path $Directory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'CLI checksum mismatch.' }
    $sums[$name] = $hash
}
if ($sums.Count -ne $files.Count + 1) { throw 'Incomplete CLI checksum inventory.' }
foreach ($assembly in @('MPswift.TagRepair.dll','Player.Core.dll')) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Directory $assembly))
    $suffix = if ($version.IsRelease) { '.' } else { '+' }
    if ($info.ProductVersion -notin @($manifest.productVersion, ($manifest.productVersion + $suffix + $manifest.sourceCommit)) -or $info.FileVersion -ne $version.FileVersion) { throw 'CLI compiled version mismatch.' }
}
foreach ($native in @('MPswift.TagRepair.exe','coreclr.dll')) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $Directory $native)); $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($offset -lt 0 -or $offset + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $offset) -ne 0x4550 -or [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 0x8664) { throw 'CLI native host must be Windows x64.' }
}
Write-Host "CLI package verified: $($files.Count) files and complete SHA-256 inventory."
