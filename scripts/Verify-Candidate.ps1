#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$directory = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content (Join-Path $directory 'package-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.platform -ne 'win-x64' -or $manifest.distributionApproved) { throw 'Unsupported candidate manifest or invalid distribution approval.' }
$expected = @{}
foreach ($file in $manifest.files) {
    if ($file.path.Contains('\') -or $file.path.StartsWith('/') -or $file.path.Contains(':') -or $file.path.Split('/') -contains '..' -or $expected.ContainsKey($file.path)) { throw "Unsafe/duplicate manifest path: $($file.path)" }
    $expected[$file.path] = $true
    $full = Join-Path $directory $file.path
    if (-not (Test-Path $full -PathType Leaf)) { throw "Missing candidate file: $($file.path)" }
    if ((Get-Item $full).Length -ne $file.bytes -or (Get-FileHash $full -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Candidate file changed: $($file.path)" }
}
foreach ($item in Get-ChildItem $directory -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($directory, $item.FullName).Replace('\','/')
    if ($relative -notin @('package-manifest.json','SHA256SUMS.txt') -and -not $expected.ContainsKey($relative)) { throw "Unexpected candidate file: $relative" }
}
$checksums = @{}
foreach ($line in Get-Content (Join-Path $directory 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw 'Invalid checksum row.' }
    $hash = $Matches[1]; $relative = $Matches[2]
    if ($checksums.ContainsKey($relative)) { throw 'Duplicate checksum row.' }; $checksums[$relative] = $true
    if ($relative -ne 'package-manifest.json' -and -not $expected.ContainsKey($relative)) { throw 'Unexpected checksum path.' }
    if ((Get-FileHash (Join-Path $directory $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Checksum mismatch: $relative" }
}
if ($checksums.Count -ne $expected.Count + 1) { throw 'Incomplete checksum list.' }
if ($manifest.PSObject.Properties.Name -contains 'productVersion') {
    if ($manifest.productVersion -notmatch '^0\.2\.([0-9]+)-dev\.([0-9]+)$') { throw 'Invalid candidate product version.' }
    $fileVersion = "0.2.$($Matches[1]).$($Matches[2])"
    foreach ($assembly in @('MPswift.dll','Player.Core.dll')) {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $directory $assembly))
        if ($info.ProductVersion.Split('+')[0] -ne $manifest.productVersion -or $info.FileVersion -ne $fileVersion) { throw "Compiled version differs from candidate manifest: $assembly" }
    }
}
foreach ($required in @('MPswift.exe','coreclr.dll','PresentationFramework.dll','e_sqlite3.dll','ru/MPswift.resources.dll','native/manifest.json','portable.marker','docs/USER_HELP.md','docs/USER_HELP.ru.md','dependency-inventory.json','DEVELOPMENT-ONLY.txt')) {
    if (-not $expected.ContainsKey($required)) { throw "Required candidate file absent: $required" }
}
$native = Get-Content (Join-Path $directory 'native/manifest.json') -Raw | ConvertFrom-Json
foreach ($library in $native.libraries) {
    $path = Join-Path $directory "native/win-x64/$($library.fileName)"
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $library.sha256) { throw "Native manifest mismatch: $($library.name)" }
    $bytes = [IO.File]::ReadAllBytes($path); $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($offset -lt 0 -or $offset + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $offset) -ne 0x4550 -or [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 0x8664) { throw "Native library is not x64 PE: $($library.name)" }
}
Write-Host "Candidate audit passed: $($manifest.files.Count) files, complete hashes, $($native.libraries.Count) native x64 libraries, no extra files. Distribution is not approved."
