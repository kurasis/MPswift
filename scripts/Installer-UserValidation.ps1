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
    $userProfileImage = Get-ItemPropertyValue ("HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + $identity.User.Value) ProfileImagePath
    # CreateProcessWithLogonW may inherit the caller's environment, even with a loaded profile.
    # Initialize only this disposable child's profile variables from its actual token/SID.
    $env:USERPROFILE = [Environment]::ExpandEnvironmentVariables($userProfileImage)
    $env:APPDATA = Join-Path $env:USERPROFILE 'AppData/Roaming'
    $env:LOCALAPPDATA = Join-Path $env:USERPROFILE 'AppData/Local'
    $env:TEMP = Join-Path $env:LOCALAPPDATA 'Temp'
    $env:TMP = $env:TEMP
    New-Item $env:TEMP -ItemType Directory -Force | Out-Null
    $audit = Get-Content (Join-Path $PSScriptRoot 'installer-audit.json') -Raw | ConvertFrom-Json
    $setup = Join-Path $PSScriptRoot $audit.Installer
    if ((Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.InstallerSha256) { throw 'Installer changed before standard-user execution.' }
    # Credential-launched processes can inherit the caller's environment block.
    # Resolve known folders for this token/profile, as the installer and player do.
    $localData = [Environment]::GetFolderPath('LocalApplicationData', 'Create')
    if (-not $localData) { throw 'The isolated standard-user profile has no LocalAppData known folder.' }
    $app = Join-Path $localData 'Programs/MPswift'
    $data = Join-Path $localData 'MPswift/LocalAudioPlayer'
    $group = 'MPswift'
    $shortcuts = Join-Path ([Environment]::GetFolderPath('Programs', 'Create')) $group
    $registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EC91F463-A93D-4DBE-94B7-2199F2F64FA6}_is1'
    function Run-Setup([string]$Target, [string]$Language) {
        $log = Join-Path $PSScriptRoot ($Phase + '-' + $Language + '-' + [guid]::NewGuid().ToString('N') + '.log')
        $start = [Diagnostics.ProcessStartInfo]::new($setup); $start.UseShellExecute = $false
        foreach ($argument in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=$Target",'/TASKS=',"/LANG=$Language",("/LOG=" + $log))) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Owned installer timed out.' }
            return [pscustomobject]@{ ExitCode = $process.ExitCode; Log = $(if (Test-Path $log) { Get-Content $log -Raw } else { 'Installer did not create a log.' }) }
        } finally { $process.Dispose() }
    }
    if ($Phase -eq 'Install') {
        New-Item $data -ItemType Directory -Force | Out-Null
        'Owned playlist data - preserve byte-for-byte' | Set-Content (Join-Path $data 'library.db') -Encoding utf8
        '{"SchemaVersion":1,"Language":"ru"}' | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
        Copy-Item (Join-Path $PSScriptRoot 'pcm16.wav') (Join-Path $data 'owned-music.wav')
        $hashes = @(Get-ChildItem $data -File | ForEach-Object { @{ Name = $_.Name; Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
        $hashes | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'data-hashes.json') -Encoding utf8
        $foreign = Join-Path $localData 'foreign validation folder'
        New-Item $foreign -ItemType Directory | Out-Null
        $sentinel = Join-Path $foreign 'keep.txt'; 'Owned unrelated content' | Set-Content $sentinel
        $before = (Get-FileHash $sentinel).Hash
        $result = Run-Setup $foreign 'english'
        if ($result.ExitCode -ne 7 -or -not $result.Log.Contains('Choose an empty folder') -or (Get-FileHash $sentinel).Hash -ne $before -or (Test-Path (Join-Path $foreign 'MPswift.exe'))) { throw "Unrelated-folder protection did not reject at PrepareToInstall: $($result | ConvertTo-Json -Compress)" }
        $portable = Join-Path $localData 'portable validation folder'
        New-Item (Join-Path $portable 'Data') -ItemType Directory -Force | Out-Null
        'owned portable marker' | Set-Content (Join-Path $portable 'portable.marker')
        $result = Run-Setup $portable 'english'
        if ($result.ExitCode -ne 7 -or -not $result.Log.Contains('Choose a separate installation folder') -or -not (Test-Path (Join-Path $portable 'portable.marker'))) { throw "Portable-folder protection did not reject at PrepareToInstall: $($result | ConvertTo-Json -Compress)" }
        $result = Run-Setup $app 'english'
        if ($result.ExitCode -ne 0) { throw "Fresh standard-user install failed: $($result | ConvertTo-Json -Compress)" }
    } elseif ($Phase -eq 'Upgrade') {
        # Unknown user content in a recognized installation must survive reinstallation and uninstall.
        'Owned extra content' | Set-Content (Join-Path $app 'keep-user-file.txt')
        $result = Run-Setup $app 'russian'
        if ($result.ExitCode -ne 0) { throw "Standard-user reinstall failed: $($result | ConvertTo-Json -Compress)" }
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
    [ordered]@{ Status = 'standard-user-installer-phase-failed'; Phase = $Phase; Message = $_.Exception.Message; Details = $_.ToString(); ScriptStackTrace = $_.ScriptStackTrace } | ConvertTo-Json | Set-Content $report -Encoding utf8
    exit 1
}
