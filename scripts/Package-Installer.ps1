#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Installer compilation requires an isolated Windows build environment.' }
. "$PSScriptRoot/Version-Helpers.ps1"
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/portable'
$audit = Get-Content (Join-Path $output 'package-audit.json') -Raw | ConvertFrom-Json
$zip = Join-Path $output $audit.Zip
if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.ZipSha256) { throw 'Portable source archive changed.' }
$version = Get-PlayerVersionInfo $audit.ProductVersion
$owned = Join-Path $root ('artifacts/installer-stage-' + [guid]::NewGuid().ToString('N'))
New-Item $owned -ItemType Directory | Out-Null
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $owned)
    $app = Join-Path $owned 'MPswift'
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    Remove-Item (Join-Path $app 'portable.marker')
    'MPswift EC91F463-A93D-4DBE-94B7-2199F2F64FA6' | Set-Content (Join-Path $app '.mpswift-installation') -Encoding utf8
    $manifest = Get-Content (Join-Path $app 'package-manifest.json') -Raw | ConvertFrom-Json
    $manifest.packageKind = 'installed'
    $manifest.files = @(Get-ChildItem $app -Recurse -File | Where-Object { [IO.Path]::GetRelativePath($app, $_.FullName) -notin @('package-manifest.json','SHA256SUMS.txt') } | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($app, $_.FullName).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $app 'package-manifest.json') -Encoding utf8
    $sums = @($manifest.files | ForEach-Object { $_.sha256 + '  ' + $_.path })
    $sums += (Get-FileHash (Join-Path $app 'package-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() + '  package-manifest.json'
    $sums | Set-Content (Join-Path $app 'SHA256SUMS.txt') -Encoding utf8
    & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app
    $pin = Get-Content (Join-Path $root 'installer/toolchain.json') -Raw | ConvertFrom-Json
    $toolSetup = Join-Path $owned 'inno-toolchain.exe'
    Invoke-WebRequest $pin.url -OutFile $toolSetup
    if ((Get-Item $toolSetup).Length -ne $pin.bytes -or (Get-FileHash $toolSetup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256) { throw 'Official Inno Setup compiler hash/size mismatch.' }
    if ((Get-AuthenticodeSignature $toolSetup).Status -ne 'Valid') { throw 'Official Inno Setup compiler Authenticode validation failed.' }
    $compilerDirectory = Join-Path $owned 'Compiler'
    $start = [Diagnostics.ProcessStartInfo]::new($toolSetup)
    $start.UseShellExecute = $false
    foreach ($argument in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS',"/DIR=$compilerDirectory")) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Compiler provisioning timed out.' }
        if ($process.ExitCode -ne 0) { throw 'Compiler provisioning failed.' }
    } finally { $process.Dispose() }
    $compiler = Join-Path $compilerDirectory 'ISCC.exe'
    $name = "MPswift-$($audit.ProductVersion)-setup-win-x64"
    Copy-Item (Join-Path $root 'installer/MPswift.iss') (Join-Path $owned 'MPswift.iss')
    $fileRows = @(Get-ChildItem $app -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($app, $_.FullName)
        if ($relative.Contains('"') -or $relative.Contains(';') -or $relative.Contains('{') -or $relative.Contains('}')) { throw 'Unsafe installer source filename.' }
        $destination = [IO.Path]::GetDirectoryName($relative)
        'Source: "' + $_.FullName + '"; DestDir: "{app}' + $(if ($destination) { '\' + $destination } else { '' }) + '"; Flags: ignoreversion'
    })
    $fileRows | Set-Content (Join-Path $owned 'payload-files.iss') -Encoding utf8
    & $compiler "/DPayloadDirectory=$app" "/DOutputDirectory=$output" "/DOutputName=$name" "/DProductVersion=$($audit.ProductVersion)" "/DFileVersion=$($version.FileVersion)" "/DAppIcon=$root/src/Player.App/Assets/MPswift.ico" (Join-Path $owned 'MPswift.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $setup = Join-Path $output ($name + '.exe')
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    ($hash + '  ' + [IO.Path]::GetFileName($setup)) | Set-Content ($setup + '.sha256') -Encoding utf8
    [ordered]@{ Status = 'installer-compiled'; ProductVersion = $audit.ProductVersion; SourceCommit = $audit.SourceCommit; SourceTreeDirty = $audit.SourceTreeDirty; PortableZipSha256 = $audit.ZipSha256; Installer = [IO.Path]::GetFileName($setup); InstallerSha256 = $hash; Bytes = (Get-Item $setup).Length; CompilerVersion = $pin.version; CompilerInstallerSha256 = $pin.sha256; Privileges = 'current user; no elevation requested'; DataDirectory = '%LOCALAPPDATA%/MPswift/LocalAudioPlayer'; Signed = $false; DistributionApproved = $false } | ConvertTo-Json | Set-Content (Join-Path $output 'installer-audit.json') -Encoding utf8
} finally {
    # This temporary tree contains only our extracted payload and compiler files.
    if (Test-Path $owned) { Remove-Item $owned -Recurse -Force }
}
