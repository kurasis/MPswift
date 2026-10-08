# Run only by Installer-Smoke.ps1 as its newly created isolated standard user.
#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Install','Upgrade','Uninstall')][string]$Phase)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$report = Join-Path $PSScriptRoot ($Phase.ToLowerInvariant() + '.json')
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $identity.Name -notmatch '\\mpswiftqa[0-9a-f]{8}$') { throw 'Installer validation requires its disposable standard-user identity.' }
    $audit = Get-Content (Join-Path $PSScriptRoot 'installer-audit.json') -Raw | ConvertFrom-Json
    $setup = Join-Path $PSScriptRoot $audit.Installer
    if ((Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.InstallerSha256) { throw 'Installer changed before standard-user execution.' }
    $app = Join-Path $env:LOCALAPPDATA 'Programs/MPswift'
    $data = Join-Path $env:LOCALAPPDATA 'MPswift/LocalAudioPlayer'
    $group = 'MPswift QA ' + $identity.User.Value.Split('-')[-1]
    $shortcuts = Join-Path ([Environment]::GetFolderPath('Programs')) $group
    $registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EC91F463-A93D-4DBE-94B7-2199F2F64FA6}_is1'
    function Run-Setup([string]$Target, [string]$Language) {
        $start = [Diagnostics.ProcessStartInfo]::new($setup); $start.UseShellExecute = $false
        foreach ($argument in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=$Target","/GROUP=$group",'/TASKS=',"/LANG=$Language",("/LOG=" + (Join-Path $PSScriptRoot "$Phase-$Language.log")))) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Owned installer timed out.' }
            return $process.ExitCode
        } finally { $process.Dispose() }
    }
    if ($Phase -eq 'Install') {
        New-Item $data -ItemType Directory -Force | Out-Null
        'Owned playlist data - preserve byte-for-byte' | Set-Content (Join-Path $data 'library.db') -Encoding utf8
        '{"SchemaVersion":1,"Language":"ru"}' | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
        Copy-Item (Join-Path $PSScriptRoot 'pcm16.wav') (Join-Path $data 'owned-music.wav')
        $hashes = @(Get-ChildItem $data -File | ForEach-Object { @{ Name = $_.Name; Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
        $hashes | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'data-hashes.json') -Encoding utf8
        $foreign = Join-Path $env:LOCALAPPDATA 'foreign validation folder'
        New-Item $foreign -ItemType Directory | Out-Null
        $sentinel = Join-Path $foreign 'keep.txt'; 'Owned unrelated content' | Set-Content $sentinel
        $before = (Get-FileHash $sentinel).Hash
        if ((Run-Setup $foreign 'english') -eq 0 -or (Get-FileHash $sentinel).Hash -ne $before -or (Test-Path (Join-Path $foreign 'MPswift.exe'))) { throw 'Installer accepted/changed an unrelated nonempty directory.' }
        $portable = Join-Path $env:LOCALAPPDATA 'portable validation folder'
        New-Item (Join-Path $portable 'Data') -ItemType Directory -Force | Out-Null
        'owned portable marker' | Set-Content (Join-Path $portable 'portable.marker')
        if ((Run-Setup $portable 'english') -eq 0 -or -not (Test-Path (Join-Path $portable 'portable.marker'))) { throw 'Installer accepted/changed a portable data directory.' }
        if ((Run-Setup $app 'english') -ne 0) { throw 'Fresh standard-user install failed.' }
    } elseif ($Phase -eq 'Upgrade') {
        # Unknown user content in a recognized installation must survive reinstallation and uninstall.
        'Owned extra content' | Set-Content (Join-Path $app 'keep-user-file.txt')
        if ((Run-Setup $app 'russian') -ne 0) { throw 'Standard-user reinstall failed.' }
        if ((Get-Content (Join-Path $app 'keep-user-file.txt') -Raw).Trim() -ne 'Owned extra content') { throw 'Reinstall modified unknown user content.' }
        Remove-Item (Join-Path $app 'keep-user-file.txt')
    } else {
        'Owned extra content' | Set-Content (Join-Path $app 'keep-user-file.txt')
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'unins000.exe')); $start.UseShellExecute = $false
        foreach ($argument in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/LOG=" + (Join-Path $PSScriptRoot 'uninstall.log')))) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Owned uninstaller timed out.' }
            if ($process.ExitCode -ne 0) { throw 'Standard-user uninstall failed.' }
        } finally { $process.Dispose() }
        if ((Test-Path (Join-Path $app 'MPswift.exe')) -or (Test-Path $registry) -or (Test-Path (Join-Path $shortcuts 'MPswift.lnk'))) { throw 'Uninstall retained application/registry/shortcut.' }
        if ((Get-Content (Join-Path $app 'keep-user-file.txt') -Raw).Trim() -ne 'Owned extra content') { throw 'Uninstall deleted unknown user content.' }
    }
    foreach ($file in @(Get-Content (Join-Path $PSScriptRoot 'data-hashes.json') -Raw | ConvertFrom-Json)) {
        if ((Get-FileHash (Join-Path $data $file.Name) -Algorithm SHA256).Hash -ne $file.Sha256) { throw 'Installer lifecycle changed playlist/settings/music data.' }
    }
    if ($Phase -ne 'Uninstall') {
        & "$PSScriptRoot/Verify-Candidate.ps1" -Directory $app -Installed
        if (-not (Test-Path $registry) -or -not (Test-Path (Join-Path $shortcuts 'MPswift.lnk')) -or (Test-Path (Join-Path $app 'portable.marker'))) { throw 'Per-user registration, shortcut or data mode is incorrect.' }
        if ((Get-ItemProperty $registry).DisplayVersion -ne $audit.ProductVersion) { throw 'Installed version differs from audited installer.' }
    }
    [ordered]@{ Status = 'standard-user-installer-phase-passed'; Phase = $Phase; Administrator = $false; AppDirectory = $app; DataPreserved = $true; PerUserRegistration = $true; UnknownFilesPreserved = $true; SourceCommit = $audit.SourceCommit; ProductVersion = $audit.ProductVersion } | ConvertTo-Json | Set-Content $report -Encoding utf8
} catch {
    [ordered]@{ Status = 'standard-user-installer-phase-failed'; Phase = $Phase; Message = $_.Exception.Message; Details = $_.ToString() } | ConvertTo-Json | Set-Content $report -Encoding utf8
    exit 1
}
