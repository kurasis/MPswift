using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Player.AudioSmoke;

internal static class SecurityChildProcess
{
    public const long MemoryBytes = 512L * 1024 * 1024;
    public const int WallSeconds = 15;
    public const int CpuSeconds = 8;
    internal sealed record Result(int ExitCode, string Output, string Errors, long PeakCommittedBytes, double WallMilliseconds);

    public static async Task<Result> RunAsync(string tool, string root, string request, TimeSpan? wallLimit = null)
    {
        using var job = CreateJobObject(0, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var limits = new ExtendedLimits
        {
            Basic = new() { Flags = 0x2000 | 0x100 | 0x8 | 0x2, ActiveProcesses = 1, ProcessTime = CpuSeconds * 10_000_000L },
            ProcessMemory = (nuint)MemoryBytes
        };
        if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // The runner always enters through dotnet, so ProcessPath is the provisioned absolute host.
        start.ArgumentList.Add(tool); start.ArgumentList.Add("--security-worker"); start.ArgumentList.Add(request);
        using var process = Process.Start(start) ?? throw new IOException("Security worker did not start.");
        var clock = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(wallLimit ?? TimeSpan.FromSeconds(WallSeconds));
        try
        {
            if (!AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (!IsProcessInJob(process.Handle, job, out var assigned) || !assigned) throw new IOException("Worker resource assignment was not confirmed.");
            var observed = new ExtendedLimits();
            if (!QueryInformationJobObject(job, 9, ref observed, Marshal.SizeOf<ExtendedLimits>(), out _) ||
                observed.Basic.Flags != limits.Basic.Flags || observed.ProcessMemory != limits.ProcessMemory || observed.Basic.ProcessTime != limits.Basic.ProcessTime)
                throw new IOException("Worker resource limits were not confirmed.");
            var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            // Worker cannot load/decode until the parent assigns resource limits.
            await process.StandardInput.WriteLineAsync("bounded-security-worker"); process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            if (!QueryInformationJobObject(job, 9, ref observed, Marshal.SizeOf<ExtendedLimits>(), out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return new(process.ExitCode, await stdout, await stderr, (long)observed.PeakProcessMemory, clock.Elapsed.TotalMilliseconds);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            // Closing the job kills any remaining assigned process; no generic process-name cleanup.
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellation)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        { if (result.Length + read > 65536) throw new InvalidDataException("Worker output exceeds its bound."); result.Append(buffer, 0, read); }
        return result.ToString();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime; public uint Flags; public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits limits, int length);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(nint process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool assigned);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits limits, int length, out int returned);
}
