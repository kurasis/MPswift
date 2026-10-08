#requires -Version 7.4
[CmdletBinding()]
param([switch]$IsolatedRunner)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or -not $IsolatedRunner -or $env:CI -ne 'true') { throw 'Installer lifecycle tests require an explicit disposable Windows CI runner.' }
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/portable'
$audit = Get-Content (Join-Path $output 'installer-audit.json') -Raw | ConvertFrom-Json
$owned = Join-Path $env:TEMP ('mpswift-installer-' + [guid]::NewGuid().ToString('N'))
$userName = 'mpswiftqa' + [guid]::NewGuid().ToString('N').Substring(0,8)
$password = ConvertTo-SecureString ([guid]::NewGuid().ToString('N') + 'aA1!') -AsPlainText -Force
$account = $null
$report = [ordered]@{ Status = 'installer-lifecycle-failed'; SourceCommit = $audit.SourceCommit; ProductVersion = $audit.ProductVersion; InstallerSha256 = $audit.InstallerSha256; StandardUser = $false; Phases = @(); Wpf = @(); Host = [Environment]::OSVersion.VersionString; CleanWindows11 = 'not-run'; PublisherSignature = 'not-configured'; DistributionApproved = $false }
$evidence = Join-Path $root 'artifacts/smoke'
New-Item $evidence -ItemType Directory -Force | Out-Null
try {
    New-Item $owned -ItemType Directory | Out-Null
    $account = New-LocalUser -Name $userName -Password $password -AccountNeverExpires -PasswordNeverExpires
    $users = Get-LocalGroup -SID 'S-1-5-32-545'
    Add-LocalGroupMember -Group $users.Name -Member $account.Name
    $acl = Get-Acl $owned
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($account.SID, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl $owned $acl
    foreach ($script in @('Installer-UserValidation.ps1','Verify-Candidate.ps1','Version-Helpers.ps1')) { Copy-Item (Join-Path $PSScriptRoot $script) $owned }
    Copy-Item (Join-Path $output $audit.Installer) $owned
    Copy-Item (Join-Path $output 'installer-audit.json') $owned
    foreach ($fixture in @('pcm16.wav','flac16.flac')) { Copy-Item (Join-Path $root "tests/fixtures/audio/$fixture") $owned }
    $credential = [pscredential]::new("$env:COMPUTERNAME\$userName", $password)
    $machineKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EC91F463-A93D-4DBE-94B7-2199F2F64FA6}_is1'
    if (Test-Path $machineKey) { throw 'Unexpected preexisting machine-wide MPswift registration.' }
    foreach ($phase in @('Install','Upgrade','Uninstall')) {
        $process = Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -Credential $credential -LoadUserProfile -ArgumentList @('-NoProfile','-NonInteractive','-File',('"' + (Join-Path $owned 'Installer-UserValidation.ps1') + '"'),'-Phase',$phase) -PassThru
        try {
            # Keep a handle before exit so credential-launched Process objects retain their exit code.
            $null = $process.Handle
            if (-not $process.WaitForExit(360000)) { $process.Kill(); throw 'Standard-user phase timed out.' }
            $phaseReport = Get-Content (Join-Path $owned ($phase.ToLowerInvariant() + '.json')) -Raw | ConvertFrom-Json
            if ($process.ExitCode -ne 0 -or $phaseReport.Status -ne 'standard-user-installer-phase-passed') { throw "Standard-user $phase failed: $($phaseReport | ConvertTo-Json -Compress)" }
            $report.Phases += $phaseReport
        } finally { $process.Dispose() }
        if (Test-Path $machineKey) { throw 'Installer wrote machine-wide application registration.' }
        if ($phase -eq 'Uninstall') { continue }
        # UI/native checks use the runner's existing desktop, from the actually installed executable.
        # Installation/registration/data preservation above run under a separate genuine standard user.
        $token = [guid]::NewGuid().ToString('N')
        $working = Join-Path $owned ('mpswift-ui-smoke-' + $token)
        New-Item $working -ItemType Directory | Out-Null
        $token | Set-Content (Join-Path $working '.player-ui-validation') -Encoding utf8
        $language = if ($phase -eq 'Install') { 'en' } else { 'ru' }
        $data = Join-Path $working 'artifacts/smoke/stage-c-data'
        New-Item $data -ItemType Directory -Force | Out-Null
        @{ SchemaVersion = 1; Language = $language } | ConvertTo-Json | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $phaseReport.AppDirectory 'MPswift.exe'))
        $start.UseShellExecute = $false; $start.WorkingDirectory = $working
        foreach ($argument in @('--ui-smoke',(Join-Path $owned 'pcm16.wav'),(Join-Path $owned 'flac16.flac'))) { $start.ArgumentList.Add($argument) }
        $start.Environment['DOTNET_ROOT'] = Join-Path $owned 'no external runtime'
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Installed WPF timed out.' }
            $ui = Get-Content (Join-Path $working 'artifacts/smoke/ui.json') -Raw | ConvertFrom-Json
            Copy-Item (Join-Path $working 'artifacts/smoke/ui.json') (Join-Path $evidence "installed-ui-$language.json")
            if ($process.ExitCode -ne 0 -or $ui.Status -ne 'ui-smoke-passed' -or $ui.BindingErrors -ne 0) { throw 'Actually installed WPF/native workflow failed.' }
            $report.Wpf += @{ Language = $language; Status = $ui.Status; BindingErrors = $ui.BindingErrors; Runtime = 'bundled self-contained'; Identity = 'existing hosted desktop user, not the installer standard-user identity' }
        } finally { $process.Dispose() }
    }
    $report.StandardUser = $true
    $report.Status = 'installer-lifecycle-passed'
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'installer-smoke.json') -Encoding utf8
} finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'installer-smoke.json') -Encoding utf8
    if (Test-Path $owned) { Get-ChildItem $owned -File | Where-Object Extension -in @('.json','.log') | ForEach-Object { Copy-Item $_.FullName (Join-Path $evidence ('installer-' + $_.Name)) } }
    if ($account) {
        # The SID belongs only to the user created above on this disposable CI machine.
        Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $account.SID.Value -and -not $_.Loaded } | Remove-CimInstance
        Remove-LocalUser -SID $account.SID
    }
    $password.Dispose()
    if (Test-Path $owned) { Remove-Item $owned -Recurse -Force }
}
