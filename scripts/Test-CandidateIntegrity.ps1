#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$audit = Get-Content (Join-Path $root 'artifacts/portable/package-audit.json') -Raw | ConvertFrom-Json
$owned = Join-Path $root ('artifacts/integrity-check-' + [guid]::NewGuid().ToString('N'))
try {
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $root "artifacts/portable/$($audit.Zip)"), $owned)
    $app = Join-Path $owned 'LocalAudioPlayer'
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    $help = Join-Path $app 'docs/USER_HELP.md'
    $original = [IO.File]::ReadAllBytes($help)
    $checks = @()
    function Require-Rejection([string]$Name) {
        $rejected = $false
        try { & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app } catch { $rejected = $true }
        if (-not $rejected) { throw "Integrity negative check failed: $Name" }
    }
    [IO.File]::AppendAllText($help, 'owned-tamper-probe'); Require-Rejection 'changed file'; $checks += 'changed file'
    [IO.File]::WriteAllBytes($help, $original)
    [IO.File]::Delete($help); Require-Rejection 'missing file'; $checks += 'missing file'
    [IO.File]::WriteAllBytes($help, $original)
    [IO.File]::WriteAllText((Join-Path $app 'private-user.db'), 'owned-extra-probe'); Require-Rejection 'extra file'; $checks += 'extra file'
    [IO.File]::Delete((Join-Path $app 'private-user.db'))
    $sums = Join-Path $app 'SHA256SUMS.txt'; $originalSums = [IO.File]::ReadAllBytes($sums)
    [IO.File]::WriteAllText($sums, ''); Require-Rejection 'empty checksum list'; $checks += 'empty checksum list'
    [IO.File]::WriteAllBytes($sums, $originalSums)
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    [ordered]@{ Status = 'candidate-integrity-negative-checks-passed'; SourceCommit = $audit.SourceCommit; SourceTreeDirty = $audit.SourceTreeDirty; Checks = $checks; ZipSha256 = $audit.ZipSha256; RestoredCopyPassed = $true } | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts/portable/integrity-audit.json') -Encoding utf8
} finally { if (Test-Path $owned) { Remove-Item $owned -Recurse -Force } }
