#requires -Version 7.4
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Version-Helpers.ps1"
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Player.App/Player.App.csproj'
foreach ($release in @('', '1.0.0')) {
    $raw = & dotnet msbuild $project -getProperty:Version,FileVersion,AssemblyVersion -p:BuildSequence=123 -p:BuildAttempt=2 "-p:ReleaseVersion=$release"
    if ($LASTEXITCODE -ne 0) { throw 'Version-property evaluation failed.' }
    $properties = ($raw -join "`n" | ConvertFrom-Json).Properties
    $expected = Get-ExpectedPlayerVersion -Sequence 123 -Attempt 2 -ReleaseVersion $release
    $info = Get-PlayerVersionInfo $expected
    if ($properties.Version -ne $expected -or $properties.FileVersion -ne $info.FileVersion -or $properties.AssemblyVersion -ne '1.0.0.0') { throw 'Compiled build properties and package/publication version policy disagree.' }
}
foreach ($invalid in @('1.0.0','1.0.0+build.1','1.0.0+build.1.1/escape','2.0.0+build.1.1','1.0.1-dev.x','1.0.1-dev.1;command')) {
    $rejected = $false
    try { Get-PlayerVersionInfo $invalid | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid product version was accepted.' }
}
if ((Get-PlayerVersionInfo '0.2.91-dev.1').FileVersion -ne '0.2.91.1') { throw 'Legacy candidate verification compatibility regressed.' }
Write-Host 'Development/release MSBuild version agreement and malformed-version rejection passed.'
