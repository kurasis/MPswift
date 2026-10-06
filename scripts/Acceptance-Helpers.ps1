# Shared by explicit, owned acceptance runs. Compatible with Windows PowerShell 5.1.
Set-StrictMode -Version Latest
function New-PlayerAcceptanceWorkspace {
    param([string]$CandidateDirectory, [string]$OutputDirectory)
    $candidate = [IO.Path]::GetFullPath($CandidateDirectory)
    $manifest = Get-Content (Join-Path $candidate 'package-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.platform -ne 'win-x64' -or $manifest.distributionApproved) { throw 'A development win-x64 candidate manifest is required.' }
    $token = [guid]::NewGuid().ToString('N')
    $workspace = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('player-acceptance-' + $token)
    New-Item $workspace -ItemType Directory -ErrorAction Stop | Out-Null
    $token | Set-Content (Join-Path $workspace '.player-acceptance-validation') -Encoding ascii
    $copy = Join-Path $workspace 'App'; New-Item $copy -ItemType Directory | Out-Null
    $expected = @{}
    foreach ($entry in $manifest.files) {
        $name = [string]$entry.path
        if ($name.StartsWith('/') -or $name.Contains('\') -or $name.Contains(':') -or $name.Split('/') -contains '..' -or $expected.ContainsKey($name)) { throw 'Unsafe/duplicate manifest path.' }
        $expected[$name] = $true
        $inputFile = Join-Path $candidate $name
        if ((Get-Item $inputFile -ErrorAction Stop).Length -ne $entry.bytes -or (Get-FileHash $inputFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Changed candidate file: $name" }
        $destination = Join-Path $copy $name
        New-Item (Split-Path $destination -Parent) -ItemType Directory -Force | Out-Null
        Copy-Item $inputFile $destination
        if ((Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Candidate copy changed: $name" }
    }
    foreach ($required in @('Player.App.exe','coreclr.dll','PresentationFramework.dll','portable.marker','native/manifest.json')) {
        if (-not $expected.ContainsKey($required)) { throw "Required candidate file absent: $required" }
    }
    Copy-Item (Join-Path $candidate 'package-manifest.json') (Join-Path $copy 'package-manifest.json')
    Copy-Item (Join-Path $candidate 'SHA256SUMS.txt') (Join-Path $copy 'SHA256SUMS.txt')
    $checksums = @{}
    foreach ($line in Get-Content (Join-Path $copy 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw 'Invalid checksum row.' }
        $hash = $Matches[1]; $name = $Matches[2]
        if ($checksums.ContainsKey($name) -or ($name -ne 'package-manifest.json' -and -not $expected.ContainsKey($name))) { throw 'Unexpected/duplicate checksum path.' }
        $checksums[$name] = $true
        if ((Get-FileHash (Join-Path $copy $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Checksum mismatch: $name" }
    }
    if ($checksums.Count -ne $expected.Count + 1) { throw 'Incomplete candidate checksum list.' }
    [pscustomobject]@{ Root = $workspace; App = $copy; Token = $token; SourceCommit = $manifest.sourceCommit; SourceTreeDirty = $manifest.sourceTreeDirty; ManifestSha256 = (Get-FileHash (Join-Path $copy 'package-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function Start-PlayerAcceptanceProcess {
    param($Workspace, [string[]]$Arguments, [int]$TimeoutSeconds = 90)
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $Workspace.App 'Player.App.exe'; $start.UseShellExecute = $false; $start.WorkingDirectory = $Workspace.Root
    # These supported acceptance arguments are owned absolute filenames/known phase tokens.
    # Escape Windows argv quoting; do not construct shell commands from input paths.
    $encoded = foreach ($argument in $Arguments) { '"' + [regex]::Replace([regex]::Replace($argument, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"' }
    $start.Arguments = $encoded -join ' '
    $start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $Workspace.Root 'no installed runtime'
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = Join-Path $Workspace.Root 'no installed runtime'
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); $process.WaitForExit(); throw 'Owned acceptance apphost timed out.' }
        [pscustomobject]@{ ProcessId = $process.Id; ExitCode = $process.ExitCode }
    } finally { $process.Dispose() }
}
function Send-PlayerNetworkControl {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $client = New-Object Net.Sockets.TcpClient
    $server = $null
    try {
        $listener.Start(); $accept = $listener.AcceptTcpClientAsync()
        $client.Connect([Net.IPAddress]::Loopback, $listener.LocalEndpoint.Port)
        $server = $accept.GetAwaiter().GetResult(); $bytes = New-Object byte[] 32768
        $client.GetStream().Write($bytes, 0, $bytes.Length)
        $received = 0
        while ($received -lt $bytes.Length) { $read = $server.GetStream().Read($bytes, 0, $bytes.Length - $received); if ($read -le 0) { throw 'Local ETW positive control failed.' }; $received += $read }
    } finally { if ($null -ne $server) { $server.Dispose() }; $client.Dispose(); $listener.Stop() }
}
function Start-PlayerNetworkTrace {
    param($Workspace)
    $name = 'PlayerAcceptance-' + $Workspace.Token
    $etl = Join-Path $Workspace.Root 'network.etl'
    $result = & logman.exe start $name -o $etl -f bin -max 64 -bs 128 -nb 16 128 -p '{7dd42a49-5329-4832-8dfd-43d979153a88}' 0xffffffffffffffff 5 -p '{22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716}' 0x10 5 -ets 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('Owned ETW session could not start (often elevation is required): ' + ($result -join ' ')) }
    [pscustomobject]@{ Name = $name; Etl = $etl }
}
function Stop-PlayerNetworkTrace {
    param($Trace)
    if (-not ('PlayerOwnedEtwQuery' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PlayerOwnedEtwQuery {
    [StructLayout(LayoutKind.Sequential)] struct Wnode { public uint BufferSize, ProviderId; public ulong HistoricalContext; public long TimeStamp; public Guid Guid; public uint ClientContext, Flags; }
    [StructLayout(LayoutKind.Sequential)] struct Properties { public Wnode Wnode; public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags; public int AgeLimit; public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost; public IntPtr LoggerThreadId; public uint LogFileNameOffset, LoggerNameOffset; }
    public sealed class Result { public uint EventsLost, LogBuffersLost, RealTimeBuffersLost; }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode)] static extern uint ControlTraceW(ulong handle, string name, IntPtr properties, uint control);
    public static Result Stop(string name) {
        int size = Marshal.SizeOf(typeof(Properties)); IntPtr buffer = Marshal.AllocHGlobal(size + 4096);
        try {
            byte[] zero = new byte[size + 4096]; Marshal.Copy(zero, 0, buffer, zero.Length);
            Properties p = new Properties(); p.Wnode.BufferSize = (uint)zero.Length; p.LoggerNameOffset = (uint)size; p.LogFileNameOffset = (uint)(size + 2048);
            Marshal.StructureToPtr(p, buffer, false); uint result = ControlTraceW(0, name, buffer, 1);
            if (result != 0) throw new System.ComponentModel.Win32Exception((int)result);
            p = (Properties)Marshal.PtrToStructure(buffer, typeof(Properties)); return new Result { EventsLost=p.EventsLost, LogBuffersLost=p.LogBuffersLost, RealTimeBuffersLost=p.RealTimeBuffersLost };
        } finally { Marshal.FreeHGlobal(buffer); }
    }
}
'@
    }
    [PlayerOwnedEtwQuery]::Stop($Trace.Name)
}
