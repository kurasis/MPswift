#requires -Version 7.4
[CmdletBinding()]
param([string]$SourceArchive)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') { throw 'TagLib replacement is NOT RUN: Windows x64 required.' }
if ($env:GITHUB_ACTIONS -ne 'true' -and $env:MPSWIFT_ISOLATED_SECURITY_LAB -ne '1') { throw 'Use disposable CI or an explicitly configured isolated lab.' }
$root = Split-Path $PSScriptRoot -Parent
$owned = Join-Path $root ('artifacts/taglib-replacement-' + [guid]::NewGuid().ToString('N'))
if (Test-Path $owned) { throw 'Replacement checks require a new owned directory.' }
New-Item $owned -ItemType Directory | Out-Null
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$identity = 'TagLibSharp, Version=2.3.0.0, Culture=neutral, PublicKeyToken=db62eba44689b5b0'
$appOriginal = Join-Path $root 'src/Player.App/bin/Release/net10.0-windows10.0.19041.0/win-x64'
$testsOriginal = Join-Path $root 'tests/Player.Core.Tests/bin/Release/net10.0'
$pinnedDll = Join-Path $appOriginal 'TagLibSharp.dll'
$originalHash = Hash $pinnedDll
if ($originalHash -cne 'dad93b152d55cf6a58b2037bcf9dd34b45d87761fe5ed61a232b3d4641d2a5d3') { throw 'Original TagLib DLL differs from the reviewed NuGet binary.' }
$fixture = Join-Path $root 'tests/fixtures/audio/pcm16.wav'
$tagged = Join-Path $root 'tests/fixtures/audio/flac16.flac'
$fixtureHash = Hash $fixture; $taggedHash = Hash $tagged
$process = $null
$result = [ordered]@{ Status = 'taglib-replacement-failed'; SourceCommit = (& git -C $root rev-parse HEAD); Windows = [Environment]::OSVersion.VersionString; Scope = 'Owned build-output copies; real EN/RU WPF metadata/native workflow; no shipped dependency replacement'; CoreTestsExerciseTagLib = $false; SourceDistributionApproved = $false }
Push-Location $root
try {
    $source = Join-Path $owned 'source'
    $prepareArguments = @((Join-Path $PSScriptRoot 'Prepare-TagLibSource.py'), $source)
    if ($SourceArchive) { $prepareArguments += @('--archive', ([IO.Path]::GetFullPath($SourceArchive))) }
    & python @prepareArguments
    if ($LASTEXITCODE -ne 0) { throw 'Pinned source preparation failed; no source build performed.' }
    $upstream = Join-Path $source 'taglib-sharp-b5ae84f2e84087bf160bb0471420200dd2b5d809'
    $review = Get-Content (Join-Path $root 'docs/evidence/taglib-source-preparation-2026-10-07.json') -Raw | ConvertFrom-Json
    foreach ($material in $review.selectedBuildMaterial) {
        $path = Join-Path $source $material.path
        if ((Get-Item $path).Length -ne $material.bytes -or (Hash $path) -cne $material.sha256) { throw 'Source build material differs from the reviewed snapshot.' }
    }
    $project = Join-Path $upstream 'src/TaglibSharp/TaglibSharp.csproj'
    $lock = Join-Path (Split-Path $project -Parent) 'packages.lock.json'
    if (Test-Path $lock) { throw 'Unexpected upstream restore lock; source retained for inspection.' }
    Copy-Item (Join-Path $PSScriptRoot 'fixtures/taglib-source-packages.lock.json') $lock
    & dotnet restore $project --locked-mode -p:LibTargetFrameworks=netstandard2.0 -p:RestorePackagesWithLockFile=true
    if ($LASTEXITCODE -ne 0) { throw 'Locked source-library restore failed.' }
    & dotnet build $project -c Release --no-restore -p:LibTargetFrameworks=netstandard2.0 -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) { throw 'Reviewed source-library build failed.' }
    $rebuilt = Join-Path $upstream 'src/TaglibSharp/bin/Release/netstandard2.0/TagLibSharp.dll'
    if ([Reflection.AssemblyName]::GetAssemblyName($rebuilt).FullName -cne $identity) { throw 'Rebuilt assembly identity changed.' }
    $rebuiltHash = Hash $rebuilt
    $result.UpstreamCommit = $review.source.commit
    $result.SourceArchiveSha256 = Hash (Join-Path $source 'upstream-source.zip')
    $result.SourceInventorySha256 = Hash (Join-Path $source 'source-receipt.json')
    $result.SourcePackagesLockSha256 = Hash $lock
    $result.Sdk = (& dotnet --version)
    $result.AssemblyIdentity = $identity; $result.OriginalDllSha256 = $originalHash; $result.RebuiltDllSha256 = $rebuiltHash
    $tests = Join-Path $owned 'tests'
    Copy-Item -LiteralPath $testsOriginal -Destination $tests -Recurse
    Copy-Item -LiteralPath $rebuilt -Destination (Join-Path $tests 'TagLibSharp.dll') -Force
    $testResults = Join-Path $owned 'test-results'
    & dotnet vstest (Join-Path $tests 'Player.Core.Tests.dll') --logger:trx ('--ResultsDirectory:' + $testResults)
    if ($LASTEXITCODE -ne 0) { throw 'Copied Core regression suite failed.' }
    $trx = @(Get-ChildItem $testResults -Filter '*.trx')
    if ($trx.Count -ne 1) { throw 'Expected exactly one current Core test report.' }
    [xml]$xml = Get-Content $trx[0].FullName -Raw
    $counters = $xml.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 321 -or $counters.total -ne $counters.executed -or $counters.total -ne $counters.passed -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) { throw 'Core suite was incomplete or skipped.' }
    $result.CoreTests = @{ Total = [int]$counters.total; Passed = [int]$counters.passed; Failed = [int]$counters.failed; Skipped = [int]$counters.notExecuted; TrxSha256 = Hash $trx[0].FullName }
    $app = Join-Path $owned 'app'
    Copy-Item -LiteralPath $appOriginal -Destination $app -Recurse
    Copy-Item -LiteralPath $rebuilt -Destination (Join-Path $app 'TagLibSharp.dll') -Force
    New-Item (Join-Path $root 'artifacts/smoke') -ItemType Directory -Force | Out-Null
    $uiResults = @()
    foreach ($language in @('en', 'ru')) {
        $token = [guid]::NewGuid().ToString('N')
        $working = Join-Path $owned ('mpswift-ui-smoke-' + $token)
        $data = Join-Path $working 'artifacts/smoke/stage-c-data'
        New-Item $data -ItemType Directory -Force | Out-Null
        $token | Set-Content (Join-Path $working '.player-ui-validation') -Encoding utf8
        @{ SchemaVersion = 1; Language = $language } | ConvertTo-Json | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'MPswift.exe'))
        $start.UseShellExecute = $false; $start.WorkingDirectory = $working
        foreach ($argument in @('--ui-smoke', $fixture, $tagged)) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        if (-not $process.WaitForExit(120000)) { $process.Kill(); $process.WaitForExit(); throw 'Rebuilt-library WPF check timed out.' }
        $reportPath = Join-Path $working 'artifacts/smoke/ui.json'
        if (Test-Path -LiteralPath $reportPath) { Copy-Item -LiteralPath $reportPath (Join-Path $root "artifacts/smoke/taglib-replacement-ui-$language.json") }
        if ($process.ExitCode -ne 0) { throw 'Rebuilt-library WPF check failed; retained UI report identifies the actual assertion.' }
        $ui = Get-Content $reportPath -Raw | ConvertFrom-Json
        if ($ui.Status -ne 'ui-smoke-passed' -or $ui.BindingErrors -ne 0 -or -not $ui.UnicodeMetadata -or -not $ui.MetadataHandleReleased -or
            $ui.TagLibAssembly.Identity -cne $identity -or $ui.TagLibAssembly.Sha256 -cne $rebuiltHash -or $ui.TagLibAssembly.RelativePath -cne 'TagLibSharp.dll') { throw 'Real WPF did not confirm the rebuilt TagLib assembly and metadata behavior.' }
        $uiResults += @{ Language = $language; Status = $ui.Status; BindingErrors = $ui.BindingErrors; LoadedTagLib = $ui.TagLibAssembly; ReportSha256 = Hash $reportPath }
        $process.Dispose(); $process = $null
        Copy-Item $reportPath (Join-Path $root "artifacts/smoke/taglib-replacement-ui-$language.json")
    }
    if ((Hash $pinnedDll) -cne $originalHash -or (Hash $fixture) -cne $fixtureHash -or (Hash $tagged) -cne $taggedHash) { throw 'Original application or owned fixtures changed.' }
    $result.Wpf = $uiResults; $result.OriginalDllAndFixturesUnchanged = $true
    $result.Status = 'taglib-windows-replacement-passed'
} catch {
    $result.Failure = $_.Exception.Message
    throw
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    New-Item (Join-Path $root 'artifacts/smoke') -ItemType Directory -Force | Out-Null
    $result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $root 'artifacts/smoke/taglib-replacement.json') -Encoding utf8
    Pop-Location
}
