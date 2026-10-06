#requires -Version 7.4
[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$staging = Join-Path $root ('artifacts/candidate-stage-' + [guid]::NewGuid().ToString('N'))
try {
    if (-not $SkipBuild) { & "$PSScriptRoot/Build.ps1" }
    & "$PSScriptRoot/Setup-Native.ps1"
    $app = Join-Path $staging 'LocalAudioPlayer'
    & dotnet publish src/Player.App/Player.App.csproj -c Release --no-restore -p:CopyOutputSymbolsToPublishDirectory=false -o $app
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained candidate publish failed.' }
    # Reference-project symbols can survive incremental publish; remove only symbols in this fresh owned stage.
    Get-ChildItem $app -Recurse -Filter '*.pdb' -File | Remove-Item
    foreach ($required in @('Player.App.exe','Player.App.dll','coreclr.dll','PresentationFramework.dll','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','ru/Player.App.resources.dll','e_sqlite3.dll')) {
        if (-not (Test-Path (Join-Path $app $required))) { throw "Missing packaged runtime/application file: $required" }
    }
    New-Item (Join-Path $app 'docs') -ItemType Directory -Force | Out-Null
    foreach ($document in @('ARCHITECTURE','FORMAT_SUPPORT','TEST_RESULTS','REQUIREMENTS_STATUS','KNOWN_LIMITATIONS','THIRD_PARTY_NOTICES','RELEASE_ACCEPTANCE','USER_HELP','USER_HELP.ru')) {
        Copy-Item "docs/$document.md" (Join-Path $app "docs/$document.md")
    }
    Copy-Item README.md (Join-Path $app 'README.md')
    'Explicit portable data location; created only on first normal launch.' | Set-Content (Join-Path $app 'portable.marker') -Encoding utf8
    'LOCAL DEVELOPMENT CANDIDATE. NOT APPROVED FOR PUBLIC DISTRIBUTION. See docs/RELEASE_ACCEPTANCE.md and docs/THIRD_PARTY_NOTICES.md.' | Set-Content (Join-Path $app 'DEVELOPMENT-ONLY.txt') -Encoding utf8
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
        $noticeFolder = Join-Path $app ('notices/' + $identity)
        New-Item $noticeFolder -ItemType Directory -Force | Out-Null
        Copy-Item $nuspec.FullName $noticeFolder
        $texts = @(Get-ChildItem $folder -File | Where-Object { $_.Name -match '(?i)license|notice|copying' })
        foreach ($text in $texts) { Copy-Item $text.FullName $noticeFolder }
        $inventory += [ordered]@{ identity = $identity; declaration = ('notices/' + $identity + '/' + $nuspec.Name); license = $(if ($license) { $license.InnerText } else { $null }); licenseUrl = $(if ($url) { $url.InnerText } else { $null }); accompanyingTexts = @($texts | ForEach-Object { $_.Name }); distributionReview = 'pending; metadata is not legal clearance' }
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
    $files = @(Get-ChildItem $app -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($app, $_.FullName).Replace('\','/')
        if ($relative -match '(^|/)(Data|Cache|Logs|reference|test-results|\.git)(/|$)|\.(db(-wal|-shm)?|peaks|jsonl|wav|flac|mp3|pdb)$') { throw "Private/development input found in candidate: $relative" }
        [ordered]@{ path = $relative; bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ schemaVersion = 1; product = 'Local Audio Player'; platform = 'win-x64'; sourceCommit = $source; sourceTreeDirty = $dirty; sdk = (& dotnet --version).Trim(); distributionApproved = $false; evidenceLevel = 'local development candidate; Windows 11 clean/offline/hardware/license gates remain'; files = $files }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $app 'package-manifest.json') -Encoding utf8
    $sums = @($files | ForEach-Object { $_.sha256 + '  ' + $_.path })
    $sums += (Get-FileHash (Join-Path $app 'package-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() + '  package-manifest.json'
    $sums | Set-Content (Join-Path $app 'SHA256SUMS.txt') -Encoding utf8
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    $output = Join-Path $root 'artifacts/portable'
    New-Item $output -ItemType Directory -Force | Out-Null
    $name = 'LocalAudioPlayer-dev-' + $source.Substring(0,12) + $(if ($dirty) { '-dirty' } else { '' }) + '-win-x64.zip'
    $zip = Join-Path $output $name
    if (Test-Path $zip) { Remove-Item $zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    ($hash + '  ' + $name) | Set-Content ($zip + '.sha256') -Encoding utf8
    [ordered]@{ Status = 'local-candidate-packaged'; SourceCommit = $source; SourceTreeDirty = $dirty; Zip = $name; ZipSha256 = $hash; Bytes = (Get-Item $zip).Length; Files = $files.Count; Dependencies = $inventory.Count; NativeLibraries = $native.libraries.Count; DistributionApproved = $false } | ConvertTo-Json | Set-Content (Join-Path $output 'package-audit.json') -Encoding utf8
    Write-Host "Local development candidate: $zip"
    Write-Host 'Distribution approval remains false. No GitHub release or binary artifact upload was performed.'
} finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Pop-Location
}
