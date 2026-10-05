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
    foreach ($file in @('Player.App.exe', 'Player.App.dll', 'coreclr.dll', 'PresentationFramework.dll', 'native/manifest.json')) {
        if (-not (Test-Path (Join-Path $root "artifacts/publish/win-x64/$file"))) { throw "Missing published file: $file" }
    }
    $manifest = Get-Content native/manifest.json -Raw | ConvertFrom-Json
    foreach ($library in $manifest.libraries) {
        $binary = Join-Path $root "artifacts/publish/win-x64/native/win-x64/$($library.fileName)"
        if ((Get-FileHash $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $library.sha256) { throw "Published native checksum mismatch: $($library.name)" }
        foreach ($companion in $library.requiredCompanionFiles) {
            if (-not (Test-Path (Join-Path (Split-Path $binary -Parent) $companion))) { throw "Missing published companion: $companion" }
        }
    }
    if (Test-Path artifacts/publish/win-x64/docs/spec/reference) { throw 'Development reference must not enter application output.' }
    Write-Host 'Local Stage B development output created. This is not a portable release or a distribution approval.'
} finally { Pop-Location }
