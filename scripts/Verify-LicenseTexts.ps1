#requires -Version 7.4
[CmdletBinding()]
param([string]$Directory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs/licenses'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$directory = [IO.Path]::GetFullPath($Directory)
$manifest = Get-Content (Join-Path $directory 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.distributionApproved -ne $false) { throw 'Unsupported license-text manifest.' }
$paths = @{}
foreach ($file in $manifest.files) {
    if ($file.path -notmatch '^[a-z0-9-]+/[A-Za-z0-9._-]+$' -or $file.fileName -ne $file.path.Split('/')[-1] -or $paths.ContainsKey($file.path)) { throw 'Unsafe/duplicate license-text path.' }
    if ($file.sha256 -notmatch '^[a-f0-9]{64}$' -or $file.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $file.sourceUrl -notmatch '^https://raw\.githubusercontent\.com/[^/]+/[^/]+/[a-f0-9]{40}/' -or -not $file.sourceUrl.Contains('/' + $file.sourceCommit + '/')) { throw 'License-text provenance must be pinned to an HTTPS upstream commit.' }
    if ($file.packages.Count -eq 0 -or @($file.packages | Where-Object { $_ -notmatch '^[A-Za-z0-9._-]+/[0-9]+(\.[0-9]+){2,3}$' }).Count -ne 0) { throw 'Invalid license-text package identity.' }
    $path = Join-Path $directory $file.path
    if (-not (Test-Path $path -PathType Leaf) -or (Get-Item $path).Length -ne $file.bytes -or (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Missing/modified upstream license text: $($file.path)" }
    $paths[$file.path] = $true
}
foreach ($item in Get-ChildItem $directory -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($directory, $item.FullName).Replace('\','/')
    if ($relative -ne 'manifest.json' -and -not $paths.ContainsKey($relative)) { throw "Untracked license text: $relative" }
}
# Return only verified records; packaging copies original bytes without another network fetch.
$manifest.files
