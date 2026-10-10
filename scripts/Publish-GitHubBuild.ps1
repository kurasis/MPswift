#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$RunId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$Attempt,
    [string]$Directory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/portable')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Version-Helpers.ps1"
$audit = Get-Content (Join-Path $Directory 'package-audit.json') -Raw | ConvertFrom-Json
$integrity = Get-Content (Join-Path $Directory 'integrity-audit.json') -Raw | ConvertFrom-Json
$version = Get-PlayerVersionInfo $audit.ProductVersion
if ($audit.SourceTreeDirty -or $audit.SourceCommit -notmatch '^[a-f0-9]{40}$' -or
    $audit.Zip -ne "MPswift-$($audit.ProductVersion)-$($audit.SourceCommit.Substring(0,12))-win-x64.zip") { throw 'Only clean versioned committed-source Windows candidates can be published.' }
if ($env:GITHUB_SHA -ne $audit.SourceCommit) { throw 'Candidate source does not match the publishing workflow commit.' }
if ($audit.ProductVersion -ne (Get-ExpectedPlayerVersion -Sequence $env:GITHUB_RUN_NUMBER -Attempt $Attempt -ReleaseVersion $env:MPSWIFT_RELEASE_VERSION)) { throw 'Candidate version does not match this workflow build and attempt.' }
if ($env:GH_REPO -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'GitHub repository is required.' }
if ($audit.Status -ne 'local-candidate-packaged' -or $integrity.Status -ne 'candidate-integrity-negative-checks-passed' -or -not $integrity.RestoredCopyPassed -or $integrity.SourceTreeDirty -or $integrity.SourceCommit -ne $audit.SourceCommit -or $integrity.ZipSha256 -ne $audit.ZipSha256 -or $integrity.ProductVersion -ne $audit.ProductVersion) { throw 'Candidate audit and integrity evidence do not agree.' }
$zip = Join-Path $Directory $audit.Zip
$checksum = $zip + '.sha256'
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $audit.ZipSha256 -or (Get-Item $zip).Length -ne $audit.Bytes -or (Get-Content $checksum -Raw).Trim() -ne ($actual + '  ' + $audit.Zip)) { throw 'Downloaded ZIP/checksum differs from the verified Windows candidate.' }
$installer = Get-Content (Join-Path $Directory 'installer-audit.json') -Raw | ConvertFrom-Json
$installed = Get-Content (Join-Path $Directory 'installer-smoke.json') -Raw | ConvertFrom-Json
if ($installer.Status -ne 'installer-compiled' -or $installer.SourceTreeDirty -or $installer.SourceCommit -ne $audit.SourceCommit -or $installer.ProductVersion -ne $audit.ProductVersion -or $installer.PortableZipSha256 -ne $audit.ZipSha256 -or
    $installer.Installer -ne "MPswift-$($audit.ProductVersion)-setup-win-x64.exe" -or $installed.Status -ne 'installer-lifecycle-passed' -or -not $installed.StandardUser -or $installed.SourceCommit -ne $audit.SourceCommit -or
    $installed.ProductVersion -ne $audit.ProductVersion -or $installed.InstallerSha256 -ne $installer.InstallerSha256 -or @($installed.Phases).Count -ne 3 -or @($installed.Wpf).Count -ne 2) { throw 'Installer audit/lifecycle evidence does not agree with the portable build.' }
foreach ($phase in $installed.Phases) { if ($phase.Status -ne 'standard-user-installer-phase-passed' -or $phase.Administrator -or -not $phase.DataPreserved) { throw 'Installer standard-user lifecycle evidence failed.' } }
if (($installed.Phases.Phase -join ',') -ne 'Install,Upgrade,Uninstall' -or (($installed.Wpf.Language | Sort-Object) -join ',') -ne 'en,ru') { throw 'Installer lifecycle phases/languages are incomplete.' }
foreach ($ui in $installed.Wpf) { if ($ui.Status -ne 'ui-smoke-passed' -or $ui.BindingErrors -ne 0 -or $ui.UiAudit.Status -ne 'ui-audit-passed' -or
    -not $ui.DesktopPanel.AccessiblePositionPresets -or -not $ui.DesktopPanel.PositionDraftCancel -or $ui.WindowShutdown.Status -ne 'window-shutdown-passed') { throw 'Installed executable WPF/UI audit evidence failed.' } }
$setup = Join-Path $Directory $installer.Installer
$setupHash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($setupHash -ne $installer.InstallerSha256 -or (Get-Item $setup).Length -ne $installer.Bytes -or (Get-Content ($setup + '.sha256') -Raw).Trim() -ne ($setupHash + '  ' + $installer.Installer)) { throw 'Downloaded installer/checksum differs from the verified Windows build.' }
$cli = Get-Content (Join-Path $Directory 'tagrepair-audit.json') -Raw | ConvertFrom-Json
$cliSmoke = Get-Content (Join-Path $Directory 'tagrepair-smoke.json') -Raw | ConvertFrom-Json
if ($cli.Status -ne 'tagrepair-packaged' -or $cli.SourceTreeDirty -or $cli.SourceCommit -ne $audit.SourceCommit -or $cli.ProductVersion -ne $audit.ProductVersion -or
    $cli.Zip -ne "MPswift.TagRepair-$($audit.ProductVersion)-$($audit.SourceCommit.Substring(0,12))-win-x64.zip" -or -not $cli.RuntimeIncluded -or -not $cli.SourceAudioNotIncluded -or
    $cliSmoke.Status -ne 'tagrepair-packaged-cli-passed' -or $cliSmoke.SourceCommit -ne $audit.SourceCommit -or $cliSmoke.ProductVersion -ne $audit.ProductVersion -or $cliSmoke.ZipSha256 -ne $cli.ZipSha256 -or
    [int]$cliSmoke.WindowsTests -lt 93 -or $cliSmoke.WindowsTestsFailed -ne 0 -or $cliSmoke.WindowsTestsSkipped -ne 0) { throw 'CLI source/version/runtime/Windows evidence mismatch.' }
foreach ($gate in @('RealExe','RuntimeIncluded','PreviewNoWrites','PhysicalMp3FlacCue','ExactOriginalBackups','IndependentAudioHashes','SourceFixturesUnchanged','PrivateFilesUnchanged','Idempotent','PaddedMp3PrefixAndAudio','InvalidFileContinuation','FilenameConfirmedAsciiI','ContextualCollectionCases','MirroredBackupDirectory','BackupSourceMappings')) {
    if ($cliSmoke.$gate -ne $true) { throw "CLI verification gate failed: $gate" }
}
$cliZip = Join-Path $Directory $cli.Zip
$cliHash = (Get-FileHash $cliZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($cliHash -ne $cli.ZipSha256 -or (Get-Item $cliZip).Length -ne $cli.Bytes -or (Get-Content ($cliZip + '.sha256') -Raw).Trim() -ne ($cliHash + '  ' + $cli.Zip)) { throw 'CLI ZIP/checksum changed after Windows smoke.' }
$tag = if ($version.IsRelease) { 'v' + $version.ReleaseVersion } else { "build-$RunId-attempt-$Attempt" }
$runUrl = "https://github.com/$($env:GH_REPO)/actions/runs/$RunId/attempts/$Attempt"
$notes = Join-Path $Directory 'github-release-notes.md'
$kind = if ($version.IsRelease) { 'owner-authorized 1.0 release' } else { 'development prerelease' }
@"
MPswift **$($audit.ProductVersion)**, Windows x64 $kind from source ``$($audit.SourceCommit)``.

Run the setup EXE for a per-user installation without administrator rights. English/Russian setup, Start menu shortcut and optional desktop shortcut are included. Install/reinstall/uninstall preserve playlists, settings and music in ``%LOCALAPPDATA%/MPswift/LocalAudioPlayer``. To migrate an existing portable session, use the player's backup/restore commands; setup refuses a portable Data directory.

Alternatively, extract the portable ZIP and launch ``MPswift/MPswift.exe``. The .NET desktop runtime is included in both packages. There is no automatic updater, startup registration or file-association override.

The separate **MPswift.TagRepair** ZIP is an offline Windows x64 console utility with its runtime included. Run ``MPswift.TagRepair.exe "C:\Music"`` for recursive preview, then add ``--apply`` to physically repair MP3/FLAC tags and CUE text. Verified originals are retained under ``MPswift.TagRepair.Backups`` in the selected root, preserving original subfolders; encoded audio is checked before writing. Read the utility README before applying, especially mixed-language/ambiguous tags and old ID3 device compatibility. CLI ZIP SHA-256: ``$cliHash``. Actual extracted EXE preview/apply/idempotence, exact backups and independent audio-range hashes passed on owned test copies in the Windows runner.

Linux/Windows build tests, Windows native/WPF smoke, candidate integrity checks, extracted executable smoke, genuine standard-user installer lifecycle and actual installed EN/RU WPF passed in [this workflow]($runUrl). ZIP SHA-256: ``$actual``. Installer SHA-256: ``$setupHash``. Audit and lifecycle reports are attached.

The owner authorized this versioned release. Clean Windows 11, physical device/two-hour output/manual acceptance and third-party distribution-rights review remain open; version numbering does not mark them passed. The EXE is unsigned: a publisher certificate is not configured. See the packaged release acceptance, 1.0 notes and distribution review before redistributing or using commercially.
"@ | Set-Content $notes -Encoding utf8
[string[]]$flags = if ($version.IsRelease) { @('--latest=false') } else { @('--prerelease','--latest=false') }
& gh release create $tag --repo $env:GH_REPO --target $audit.SourceCommit --title "MPswift $($version.Version.Split('+')[0]) · Windows x64" --notes-file $notes --draft @flags
if ($LASTEXITCODE -ne 0) { throw 'GitHub draft creation failed.' }
$assets = @($zip, $checksum, $setup, ($setup + '.sha256'), $cliZip, ($cliZip + '.sha256'), (Join-Path $Directory 'package-audit.json'), (Join-Path $Directory 'integrity-audit.json'), (Join-Path $Directory 'installer-audit.json'), (Join-Path $Directory 'installer-smoke.json'), (Join-Path $Directory 'tagrepair-audit.json'), (Join-Path $Directory 'tagrepair-smoke.json'))
& gh release upload $tag @assets --repo $env:GH_REPO
if ($LASTEXITCODE -ne 0) { throw 'GitHub asset upload failed; release remains a draft.' }
$releaseJson = & gh api "repos/$($env:GH_REPO)/releases?per_page=100"
if ($LASTEXITCODE -ne 0) { throw 'GitHub upload verification failed; release remains a draft.' }
$matching = @(($releaseJson -join "`n") | ConvertFrom-Json | Where-Object tag_name -eq $tag)
if ($matching.Count -ne 1 -or -not $matching[0].draft -or $matching[0].target_commitish -ne $audit.SourceCommit) { throw 'GitHub draft identity mismatch.' }
$release = $matching[0]
if ($release.prerelease -eq $version.IsRelease -or @($release.assets).Count -ne $assets.Count) { throw 'GitHub draft release channel/asset count mismatch.' }
foreach ($asset in $assets) {
    $name = [IO.Path]::GetFileName($asset)
    $remote = @($release.assets | Where-Object name -eq $name)
    $digest = 'sha256:' + (Get-FileHash $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($remote.Count -ne 1 -or $remote[0].size -ne (Get-Item $asset).Length -or $remote[0].digest -ne $digest -or $remote[0].state -ne 'uploaded') { throw "Uploaded asset mismatch: $name; release remains a draft." }
}
$latest = if ($version.IsRelease) { '--latest=true' } else { '--latest=false' }
& gh release edit $tag --repo $env:GH_REPO --draft=false $latest
if ($LASTEXITCODE -ne 0) { throw 'GitHub prerelease publication failed.' }
$url = "https://github.com/$($env:GH_REPO)/releases/download/$tag/$($audit.Zip)"
Write-Host "Published Windows ZIP: $url"
$setupUrl = "https://github.com/$($env:GH_REPO)/releases/download/$tag/$($installer.Installer)"
Write-Host "Published Windows installer: $setupUrl"
$cliUrl = "https://github.com/$($env:GH_REPO)/releases/download/$tag/$($cli.Zip)"
Write-Host "Published standalone tag repair CLI: $cliUrl"
if ($env:GITHUB_STEP_SUMMARY) { "[Download Windows x64 installer]($setupUrl)`n`n[Download portable ZIP]($url)`n`nZIP SHA-256: ``$actual```nInstaller SHA-256: ``$setupHash``" | Add-Content $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
