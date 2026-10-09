#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & dotnet restore Player.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & dotnet build Player.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet test tests/Player.Core.Tests/Player.Core.Tests.csproj -c Release --no-build --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & dotnet test tests/MPswift.TagRepair.Tests/MPswift.TagRepair.Tests.csproj -c Release --no-build --logger trx --results-directory artifacts/tagrepair-test-results
    if ($LASTEXITCODE -ne 0) { throw 'Tag repair CLI tests failed.' }
} finally { Pop-Location }
