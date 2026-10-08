#requires -Version 7.4
[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Version-Helpers.ps1"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$staging = Join-Path $root ('artifacts/candidate-stage-' + [guid]::NewGuid().ToString('N'))
try {
    if (-not $SkipBuild) { & "$PSScriptRoot/Build.ps1" }
    & "$PSScriptRoot/Setup-Native.ps1"
    $app = Join-Path $staging 'MPswift'
    & dotnet publish src/Player.App/Player.App.csproj -c Release --no-restore -p:CopyOutputSymbolsToPublishDirectory=false -o $app
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained candidate publish failed.' }
    # Reference-project symbols can survive incremental publish; remove only symbols in this fresh owned stage.
    Get-ChildItem $app -Recurse -Filter '*.pdb' -File | Remove-Item
    foreach ($required in @('MPswift.exe','MPswift.dll','coreclr.dll','PresentationFramework.dll','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','ru/MPswift.resources.dll','e_sqlite3.dll')) {
        if (-not (Test-Path (Join-Path $app $required))) { throw "Missing packaged runtime/application file: $required" }
    }
    New-Item (Join-Path $app 'docs') -ItemType Directory -Force | Out-Null
    foreach ($document in @('DESIGN','ARCHITECTURE','FORMAT_SUPPORT','TEST_RESULTS','REQUIREMENTS_STATUS','KNOWN_LIMITATIONS','THIRD_PARTY_NOTICES','RELEASE_ACCEPTANCE','RELEASE_1_0','SPEC_COMPLETION','SECURITY_AUDIT_2026-10-07','SECURITY_FOLLOWUP_2026-10-07','SECURITY_DEEPENING_2026-10-07','USER_HELP','USER_HELP.ru')) {
        Copy-Item "docs/$document.md" (Join-Path $app "docs/$document.md")
    }
    New-Item (Join-Path $app 'docs/evidence') -ItemType Directory -Force | Out-Null
    Copy-Item docs/evidence/security-dependencies-2026-10-07.json (Join-Path $app 'docs/evidence/security-dependencies-2026-10-07.json')
    Copy-Item docs/evidence/security-ogg-reproduction-2026-10-07.json (Join-Path $app 'docs/evidence/security-ogg-reproduction-2026-10-07.json')
    Copy-Item README.md (Join-Path $app 'README.md')
    $installerPin = Get-Content installer/toolchain.json -Raw | ConvertFrom-Json
    if ((Get-Item installer/LICENSE.txt).Length -ne $installerPin.licenseBytes -or (Get-FileHash installer/LICENSE.txt -Algorithm SHA256).Hash.ToLowerInvariant() -ne $installerPin.licenseSha256) { throw 'Pinned Inno Setup license text changed.' }
    Copy-Item installer/LICENSE.txt (Join-Path $app 'docs/INNO_SETUP_LICENSE.txt')
    New-Item (Join-Path $app 'acceptance') -ItemType Directory -Force | Out-Null
    foreach ($script in @('Acceptance-Helpers.ps1','Desktop-Acceptance.ps1','Audio-Acceptance.ps1','Stress-Acceptance.ps1')) { Copy-Item (Join-Path $PSScriptRoot $script) (Join-Path $app "acceptance/$script") }
    Copy-Item docs/DESKTOP_ACCEPTANCE.md (Join-Path $app 'docs/DESKTOP_ACCEPTANCE.md')
    Copy-Item docs/AUDIO_ACCEPTANCE.md (Join-Path $app 'docs/AUDIO_ACCEPTANCE.md')
    Copy-Item docs/STRESS_ACCEPTANCE.md (Join-Path $app 'docs/STRESS_ACCEPTANCE.md')
    Copy-Item docs/DISTRIBUTION_REVIEW.md (Join-Path $app 'docs/DISTRIBUTION_REVIEW.md')
    'Explicit portable data location; created only on first normal launch.' | Set-Content (Join-Path $app 'portable.marker') -Encoding utf8
    $assets = Get-Content src/Player.App/obj/project.assets.json -Raw | ConvertFrom-Json -AsHashtable
    $packages = @{}
    foreach ($identity in $assets.libraries.Keys) {
        if ($assets.libraries[$identity].type -eq 'package') { $packages[$identity] = $assets.libraries[$identity].path }
    }
    # Framework packs are not NuGet lock dependencies; include the actual restored runtime/projection metadata.
    foreach ($framework in $assets.project.frameworks.Values) {
        foreach ($download in $framework.downloadDependencies) {
            if ($download.name -in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64','Microsoft.Windows.SDK.NET.Ref')) {
                $version = $download.version.Trim('[',']').Split(',')[0].Trim()
                $packages[($download.name + '/' + $version)] = $download.name.ToLowerInvariant() + '/' + $version
            }
        }
    }
    $inventory = @()
    $licenseTexts = @(& "$PSScriptRoot/Verify-LicenseTexts.ps1")
    foreach ($identity in ($packages.Keys | Sort-Object)) {
        $folder = $null
        foreach ($cache in $assets.packageFolders.Keys) {
            $candidate = Join-Path $cache $packages[$identity]
            if (Test-Path $candidate) { $folder = $candidate; break }
        }
        if (-not $folder) { throw "Missing restored package metadata: $identity" }
        $nuspec = Get-ChildItem $folder -Filter '*.nuspec' | Select-Object -First 1
        if (-not $nuspec) { throw "Missing package declaration: $identity" }
        [xml]$xml = Get-Content $nuspec.FullName -Raw
        $metadata = $xml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $license = $metadata.SelectSingleNode('*[local-name()="license"]')
        $url = $metadata.SelectSingleNode('*[local-name()="licenseUrl"]')
        $copyright = $metadata.SelectSingleNode('*[local-name()="copyright"]')
        $repository = $metadata.SelectSingleNode('*[local-name()="repository"]')
        $noticeFolder = Join-Path $app ('notices/' + $identity)
        New-Item $noticeFolder -ItemType Directory -Force | Out-Null
        Copy-Item $nuspec.FullName $noticeFolder
        $texts = @(Get-ChildItem $folder -File | Where-Object { $_.Name -match '(?i)license|notice|copying' })
        foreach ($text in $texts) { Copy-Item $text.FullName $noticeFolder }
        $supplements = @($licenseTexts | Where-Object { $identity -cin $_.packages })
        foreach ($text in $supplements) { Copy-Item (Join-Path $root ('docs/licenses/' + $text.path)) (Join-Path $noticeFolder $text.fileName) }
        $inventory += [ordered]@{ identity = $identity; declaration = ('notices/' + $identity + '/' + $nuspec.Name); license = $(if ($license) { $license.InnerText } else { $null }); licenseType = $(if ($license) { $license.GetAttribute('type') } else { $null }); licenseUrl = $(if ($url) { $url.InnerText } else { $null }); copyright = $(if ($copyright) { $copyright.InnerText } else { $null }); repositoryUrl = $(if ($repository) { $repository.GetAttribute('url') } else { $null }); repositoryCommit = $(if ($repository) { $repository.GetAttribute('commit') } else { $null }); accompanyingTexts = @(@($texts | ForEach-Object { $_.Name }) + @($supplements | ForEach-Object { $_.fileName }) | Sort-Object -Unique); supplementalTextSources = @($supplements); distributionReview = 'pending; metadata is not legal clearance' }
    }
    $inventory | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $app 'dependency-inventory.json') -Encoding utf8
    $native = Get-Content native/manifest.json -Raw | ConvertFrom-Json
    foreach ($library in $native.libraries) {
        $file = Join-Path $app "native/win-x64/$($library.fileName)"
        if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $library.sha256) { throw "Native hash mismatch: $($library.name)" }
        foreach ($companion in $library.requiredCompanionFiles) { if (-not (Test-Path (Join-Path $app "native/win-x64/$companion"))) { throw "Missing native companion: $companion" } }
    }
    $source = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Source commit unavailable.' }
    $dirty = @(& git status --porcelain).Count -ne 0
    $version = (& dotnet msbuild src/Player.App/Player.App.csproj -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Build version unavailable.' }
    $versionInfo = Get-PlayerVersionInfo $version
    if ($versionInfo.IsRelease) {
        'MPswift 1.0. Owner-authorized versioned release. Hardware/manual acceptance and third-party rights review remain open; see docs/RELEASE_1_0.md and docs/DISTRIBUTION_REVIEW.md.' | Set-Content (Join-Path $app 'RELEASE-NOTES.txt') -Encoding utf8
    } else {
        'DEVELOPMENT BUILD. FULL STAGE G ACCEPTANCE IS INCOMPLETE. See docs/RELEASE_ACCEPTANCE.md and docs/THIRD_PARTY_NOTICES.md.' | Set-Content (Join-Path $app 'DEVELOPMENT-ONLY.txt') -Encoding utf8
    }
    $files = @(Get-ChildItem $app -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($app, $_.FullName).Replace('\','/')
        if ($relative -match '(^|/)(Data|Cache|Logs|reference|test-results|\.git)(/|$)|\.(db(-wal|-shm)?|peaks|jsonl|wav|flac|mp3|pdb)$') { throw "Private/development input found in candidate: $relative" }
        [ordered]@{ path = $relative; bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ schemaVersion = 1; product = 'MPswift'; productVersion = $version; packageKind = 'portable'; platform = 'win-x64'; sourceCommit = $source; sourceTreeDirty = $dirty; sdk = (& dotnet --version).Trim(); distributionApproved = $false; evidenceLevel = 'automated integration; Windows 11 clean/offline/hardware/license gates remain'; files = $files }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $app 'package-manifest.json') -Encoding utf8
    $sums = @($files | ForEach-Object { $_.sha256 + '  ' + $_.path })
    $sums += (Get-FileHash (Join-Path $app 'package-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() + '  package-manifest.json'
    $sums | Set-Content (Join-Path $app 'SHA256SUMS.txt') -Encoding utf8
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    $output = Join-Path $root 'artifacts/portable'
    New-Item $output -ItemType Directory -Force | Out-Null
    $name = 'MPswift-' + $version + '-' + $source.Substring(0,12) + $(if ($dirty) { '-dirty' } else { '' }) + '-win-x64.zip'
    $zip = Join-Path $output $name
    if (Test-Path $zip) { Remove-Item $zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    ($hash + '  ' + $name) | Set-Content ($zip + '.sha256') -Encoding utf8
    [ordered]@{ Status = 'local-candidate-packaged'; ProductVersion = $version; SourceCommit = $source; SourceTreeDirty = $dirty; Zip = $name; ZipSha256 = $hash; Bytes = (Get-Item $zip).Length; Files = $files.Count; Dependencies = $inventory.Count; NativeLibraries = $native.libraries.Count; DistributionApproved = $false } | ConvertTo-Json | Set-Content (Join-Path $output 'package-audit.json') -Encoding utf8
    Write-Host "Local development candidate: $zip"
    Write-Host 'Full Stage G acceptance remains open. Packaging itself does not upload; successful main CI publishes a separate development prerelease.'
} finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Pop-Location
}
