#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content (Join-Path $root 'native/manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.platform -ne 'win-x64') { throw 'Unsupported native manifest.' }
$destination = Join-Path $root 'native/win-x64'
$cache = Join-Path $root 'artifacts/native-cache'
New-Item $destination, $cache -ItemType Directory -Force | Out-Null
foreach ($library in $manifest.libraries) {
    if ($library.name -notin @('bass', 'bassmix', 'basswasapi', 'bassflac', 'bassopus', 'bassalac', 'bass_aac', 'basswma', 'bassape', 'basswv', 'bassdsd', 'bass_mpc', 'bass_tta') -or $library.fileName -ne "$($library.name).dll") {
        throw 'Unexpected native library name.'
    }
    $target = Join-Path $destination $library.fileName
    $valid = (Test-Path $target) -and ((Get-FileHash $target -Algorithm SHA256).Hash -eq $library.sha256)
    foreach ($companion in $library.requiredCompanionFiles) {
        if ($companion -notin @("$($library.name).txt", 'gpl.txt', 'readme.txt', "$($library.name)/readme.txt", "$($library.name)/lgpl.txt")) { throw 'Unexpected companion name.' }
        if (-not (Test-Path (Join-Path $destination $companion))) { $valid = $false }
    }
    if ($valid) { Write-Host "$($library.name): verified retained files"; continue }
    $archivePath = Join-Path $cache "$($library.name).zip"
    if (-not (Test-Path $archivePath) -or (Get-FileHash $archivePath -Algorithm SHA256).Hash -ne $library.archiveSha256) {
        Invoke-WebRequest -Uri $library.sourceUrl -OutFile $archivePath
    }
    if ((Get-FileHash $archivePath -Algorithm SHA256).Hash -ne $library.archiveSha256) {
        throw "Archive checksum mismatch for $($library.name). Do not use the artifact or update hashes blindly."
    }
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.GetEntry($library.archiveEntry)
        if ($null -eq $entry) { throw 'Pinned x64 DLL is missing from archive.' }
        $staging = Join-Path $cache $library.fileName
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $staging, $true)
        if ((Get-FileHash $staging -Algorithm SHA256).Hash -ne $library.sha256) { throw 'DLL checksum mismatch.' }
        $pe = [System.IO.File]::ReadAllBytes($staging)
        if ($pe.Length -lt 64 -or $pe[0] -ne 0x4D -or $pe[1] -ne 0x5A) { throw 'Invalid PE header.' }
        $offset = [BitConverter]::ToInt32($pe, 0x3C)
        if ($offset -lt 64 -or $offset + 6 -gt $pe.Length -or [BitConverter]::ToUInt32($pe, $offset) -ne 0x4550 -or [BitConverter]::ToUInt16($pe, $offset + 4) -ne 0x8664) {
            throw 'Native DLL is not a Windows x64 PE image.'
        }
        foreach ($companion in $library.requiredCompanionFiles) {
            $entry = $archive.GetEntry(($companion -split "/")[-1])
            if ($null -eq $entry) { throw "Missing upstream license/documentation: $companion" }
            New-Item (Split-Path (Join-Path $destination $companion) -Parent) -ItemType Directory -Force | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $companion), $true)
        }
        Copy-Item $staging $target -Force
        Write-Host "$($library.name) $($library.version): archive, DLL SHA-256 and x64 architecture verified"
    } finally { $archive.Dispose() }
}
Write-Host 'Development provisioning complete. Public distribution remains gated by dependency terms and intended use.'
