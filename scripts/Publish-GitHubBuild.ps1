#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$RunId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$Attempt,
    [string]$Directory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/portable')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$audit = Get-Content (Join-Path $Directory 'package-audit.json') -Raw | ConvertFrom-Json
$integrity = Get-Content (Join-Path $Directory 'integrity-audit.json') -Raw | ConvertFrom-Json
if ($audit.SourceTreeDirty -or $audit.SourceCommit -notmatch '^[a-f0-9]{40}$' -or $audit.ProductVersion -notmatch '^0\.2\.[0-9]+-dev\.[0-9]+$' -or
    $audit.Zip -ne "MPswift-$($audit.ProductVersion)-$($audit.SourceCommit.Substring(0,12))-win-x64.zip") { throw 'Only clean versioned committed-source Windows candidates can be published.' }
if ($env:GITHUB_SHA -ne $audit.SourceCommit) { throw 'Candidate source does not match the publishing workflow commit.' }
if ($audit.ProductVersion -ne "0.2.$($env:GITHUB_RUN_NUMBER)-dev.$Attempt") { throw 'Candidate version does not match this workflow build and attempt.' }
if ($env:GH_REPO -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'GitHub repository is required.' }
if ($audit.Status -ne 'local-candidate-packaged' -or $integrity.Status -ne 'candidate-integrity-negative-checks-passed' -or -not $integrity.RestoredCopyPassed -or $integrity.SourceTreeDirty -or $integrity.SourceCommit -ne $audit.SourceCommit -or $integrity.ZipSha256 -ne $audit.ZipSha256 -or $integrity.ProductVersion -ne $audit.ProductVersion) { throw 'Candidate audit and integrity evidence do not agree.' }
$zip = Join-Path $Directory $audit.Zip
$checksum = $zip + '.sha256'
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $audit.ZipSha256 -or (Get-Item $zip).Length -ne $audit.Bytes -or (Get-Content $checksum -Raw).Trim() -ne ($actual + '  ' + $audit.Zip)) { throw 'Downloaded ZIP/checksum differs from the verified Windows candidate.' }
$tag = "build-$RunId-attempt-$Attempt"
$runUrl = "https://github.com/$($env:GH_REPO)/actions/runs/$RunId/attempts/$Attempt"
$notes = Join-Path $Directory 'github-release-notes.md'
@"
MPswift **$($audit.ProductVersion)**, Windows x64 portable development build from source ``$($audit.SourceCommit)``.

Extract the ZIP and launch ``MPswift/MPswift.exe``. The .NET desktop runtime is included.

Linux/Windows build tests, Windows native/WPF smoke, candidate integrity checks and extracted executable smoke passed in [this workflow]($runUrl). ZIP SHA-256: ``$actual``. Package and integrity reports are attached.

Stage G manual Windows 11/offline/device/accessibility/profile and licensing acceptance remains open; this prerelease is not version 1.0. See the packaged release acceptance and third-party inventory documents.
"@ | Set-Content $notes -Encoding utf8
& gh release create $tag --repo $env:GH_REPO --target $audit.SourceCommit --title "MPswift $($audit.ProductVersion) · Windows x64 ($($audit.SourceCommit.Substring(0,12)))" --notes-file $notes --draft --prerelease --latest=false
if ($LASTEXITCODE -ne 0) { throw 'GitHub draft creation failed.' }
$assets = @($zip, $checksum, (Join-Path $Directory 'package-audit.json'), (Join-Path $Directory 'integrity-audit.json'))
& gh release upload $tag @assets --repo $env:GH_REPO
if ($LASTEXITCODE -ne 0) { throw 'GitHub asset upload failed; release remains a draft.' }
$releaseJson = & gh api "repos/$($env:GH_REPO)/releases?per_page=100"
if ($LASTEXITCODE -ne 0) { throw 'GitHub upload verification failed; release remains a draft.' }
$matching = @(($releaseJson -join "`n") | ConvertFrom-Json | Where-Object tag_name -eq $tag)
if ($matching.Count -ne 1 -or -not $matching[0].draft -or $matching[0].target_commitish -ne $audit.SourceCommit) { throw 'GitHub draft identity mismatch.' }
$release = $matching[0]
foreach ($asset in $assets) {
    $name = [IO.Path]::GetFileName($asset)
    $remote = @($release.assets | Where-Object name -eq $name)
    $digest = 'sha256:' + (Get-FileHash $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($remote.Count -ne 1 -or $remote[0].size -ne (Get-Item $asset).Length -or $remote[0].digest -ne $digest -or $remote[0].state -ne 'uploaded') { throw "Uploaded asset mismatch: $name; release remains a draft." }
}
& gh release edit $tag --repo $env:GH_REPO --draft=false --latest=false
if ($LASTEXITCODE -ne 0) { throw 'GitHub prerelease publication failed.' }
$url = "https://github.com/$($env:GH_REPO)/releases/download/$tag/$($audit.Zip)"
Write-Host "Published Windows ZIP: $url"
if ($env:GITHUB_STEP_SUMMARY) { "[Download Windows x64 ZIP]($url)`n`nSHA-256: ``$actual``" | Add-Content $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
