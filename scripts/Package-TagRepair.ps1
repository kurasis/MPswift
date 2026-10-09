#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root ('artifacts/tagrepair-package-' + [guid]::NewGuid().ToString('N'))
Push-Location $root
try {
    $app = Join-Path $stage 'MPswift.TagRepair'
    & dotnet publish tools/MPswift.TagRepair/MPswift.TagRepair.csproj -c Release -r win-x64 --self-contained true --no-restore -p:CopyOutputSymbolsToPublishDirectory=false -o $app
    if ($LASTEXITCODE -ne 0) { throw 'CLI self-contained publish failed.' }
    Get-ChildItem $app -Filter '*.pdb' -File -Recurse | Remove-Item
    Copy-Item docs/TAG_REPAIR_CLI.md (Join-Path $app 'README.md')
    $assets = Get-Content tools/MPswift.TagRepair/obj/project.assets.json -Raw | ConvertFrom-Json -AsHashtable
    $tagFolder = $null
    foreach ($cache in $assets.packageFolders.Keys) { $candidate = Join-Path $cache 'taglibsharp/2.3.0'; if (Test-Path $candidate) { $tagFolder = $candidate; break } }
    if (-not $tagFolder) { throw 'Pinned TagLib package is unavailable.' }
    $notice = Join-Path $app 'notices/TagLibSharp/2.3.0'; New-Item $notice -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $tagFolder 'taglibsharp.nuspec') $notice
    $texts = @(& "$PSScriptRoot/Verify-LicenseTexts.ps1" | Where-Object { 'TagLibSharp/2.3.0' -cin $_.packages })
    if ($texts.Count -ne 2) { throw 'Pinned TagLib COPYING/AUTHORS are required.' }
    foreach ($text in $texts) { Copy-Item (Join-Path $root ('docs/licenses/' + $text.path)) (Join-Path $notice $text.fileName) }
    $runtime = Join-Path $app 'notices/runtime'; New-Item $runtime -ItemType Directory -Force | Out-Null
    $runtimeVersion = (& dotnet msbuild tools/MPswift.TagRepair/MPswift.TagRepair.csproj -getProperty:BundledNETCoreAppPackageVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or $runtimeVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Pinned SDK runtime version is unavailable.' }
    $runtimeFolder = $null
    foreach ($cache in $assets.packageFolders.Keys) { $candidate = Join-Path $cache "microsoft.netcore.app.runtime.win-x64/$runtimeVersion"; if (Test-Path $candidate) { $runtimeFolder = $candidate; break } }
    if (-not $runtimeFolder) { throw 'Restored self-contained runtime declaration/notices are required.' }
    foreach ($text in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT','microsoft.netcore.app.runtime.win-x64.nuspec')) { Copy-Item (Join-Path $runtimeFolder $text) $runtime }
    $source = (& git rev-parse HEAD).Trim(); $dirty = [bool](& git status --porcelain --untracked-files=no)
    . "$PSScriptRoot/Version-Helpers.ps1"
    $version = (& dotnet msbuild tools/MPswift.TagRepair/MPswift.TagRepair.csproj -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'CLI build version is unavailable.' }; $null = Get-PlayerVersionInfo $version
    $files = @(Get-ChildItem $app -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($app, $_.FullName).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [ordered]@{ schemaVersion = 1; product = 'MPswift.TagRepair'; productVersion = $version; sourceCommit = $source; sourceTreeDirty = $dirty; platform = 'win-x64'; distributionApproved = $false;
        tagLibVersion = '2.3.0'; licenseTextSources = $texts; files = $files } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $app 'tagrepair-manifest.json') -Encoding utf8
    @($files | ForEach-Object { $_.sha256 + '  ' + $_.path }) + @((Get-FileHash (Join-Path $app 'tagrepair-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() + '  tagrepair-manifest.json') | Set-Content (Join-Path $app 'SHA256SUMS.txt') -Encoding utf8
    & "$PSScriptRoot/Verify-TagRepairPackage.ps1" -Directory $app
    $output = Join-Path $root 'artifacts/portable'; New-Item $output -ItemType Directory -Force | Out-Null
    $name = "MPswift.TagRepair-$version-$($source.Substring(0,12))" + $(if ($dirty) { '-dirty' } else { '' }) + '-win-x64.zip'
    $zip = Join-Path $output $name
    if (Test-Path $zip) { throw 'CLI output already exists; use a fresh source/build attempt.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant(); ($hash + '  ' + $name) | Set-Content ($zip + '.sha256') -Encoding utf8
    [ordered]@{ Status = 'tagrepair-packaged'; ProductVersion = $version; SourceCommit = $source; SourceTreeDirty = $dirty; Zip = $name; ZipSha256 = $hash; Bytes = (Get-Item $zip).Length; Files = $files.Count;
        RuntimeIncluded = $true; SourceAudioNotIncluded = $true; DistributionApproved = $false } | ConvertTo-Json | Set-Content (Join-Path $output 'tagrepair-audit.json') -Encoding utf8
} finally { if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }; Pop-Location }
