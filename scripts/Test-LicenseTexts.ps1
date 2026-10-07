#requires -Version 7.4
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$original = Join-Path $root 'docs/licenses'
$records = @(& "$PSScriptRoot/Verify-LicenseTexts.ps1" -Directory $original)
if ($records.Count -ne 6) { throw 'Expected all six independently pinned license texts.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('mpswift-license-test-' + [guid]::NewGuid().ToString('N'))
try {
    Copy-Item $original $temporary -Recurse
    $file = Join-Path $temporary $records[0].path
    $bytes = [IO.File]::ReadAllBytes($file)
    foreach ($case in @('modified', 'missing', 'extra', 'traversal')) {
        switch ($case) {
            'modified' { [IO.File]::WriteAllText($file, 'invalid notice') }
            'missing' { Remove-Item $file }
            'extra' { [IO.File]::WriteAllText((Join-Path $temporary 'extra.txt'), 'unexpected') }
            'traversal' {
                $manifest = Get-Content (Join-Path $temporary 'manifest.json') -Raw | ConvertFrom-Json
                $manifest.files[0].path = '../outside.txt'
                $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $temporary 'manifest.json')
            }
        }
        $rejected = $false
        try { $null = & "$PSScriptRoot/Verify-LicenseTexts.ps1" -Directory $temporary } catch { $rejected = $true }
        if (-not $rejected) { throw "License verification accepted $case input." }
        [IO.File]::WriteAllBytes($file, $bytes)
        if (Test-Path (Join-Path $temporary 'extra.txt')) { Remove-Item (Join-Path $temporary 'extra.txt') }
        Copy-Item (Join-Path $original 'manifest.json') (Join-Path $temporary 'manifest.json') -Force
    }
    $null = & "$PSScriptRoot/Verify-LicenseTexts.ps1" -Directory $temporary
    Write-Host 'License texts: pinned originals pass; modified, missing, extra and traversal inputs rejected.'
} finally {
    if (Test-Path $temporary) { Remove-Item $temporary -Recurse -Force }
}
