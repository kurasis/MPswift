#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & "$PSScriptRoot/Setup-Native.ps1"
    & "$PSScriptRoot/Build.ps1"
    # RID and SelfContained are set on Player.App only. Global CLI properties would
    # propagate the Windows RID to the platform-neutral Core and invalidate its lock.
    & dotnet publish src/Player.App/Player.App.csproj -c Release --no-restore -o artifacts/publish/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained development publish failed.' }
    foreach ($file in @('Player.App.exe', 'Player.App.dll', 'coreclr.dll', 'PresentationFramework.dll', 'native/manifest.json',
        'native/win-x64/bass.dll', 'native/win-x64/bassmix.dll', 'native/win-x64/basswasapi.dll',
        'native/win-x64/bass.txt', 'native/win-x64/bassmix.txt', 'native/win-x64/basswasapi.txt')) {
        if (-not (Test-Path (Join-Path $root "artifacts/publish/win-x64/$file"))) { throw "Missing published file: $file" }
    }
    Write-Host 'Local Stage A development output created. This is not a portable release or a distribution approval.'
} finally { Pop-Location }
