namespace Player.Core.Diagnostics;

public sealed record ResourceSample(double Seconds, double CpuSeconds, int Handles, long PrivateBytes, long WorkingSetBytes, int Threads);
public sealed record ResourceResult(double ElapsedSeconds, double CpuSeconds, double OneCoreCpuPercent, double TotalCpuPercent,
    int LogicalProcessors, int HandleGrowth, int HandleRange, long PrivateGrowthBytes, long PrivateRangeBytes,
    long MaximumWorkingSetBytes, double HandlesPerMinute, double PrivateMiBPerMinute, bool GrowthGuardPassed,
    string Boundary = "Measured process samples; growth guard and regression slope do not establish an indefinite plateau or reference-PC acceptance");

public static class ResourceAnalysis
{
    public static ResourceResult Analyze(IReadOnlyList<ResourceSample> samples, int logicalProcessors)
    {
        if (logicalProcessors < 1 || samples.Count is < 3 or > 4096) throw new ArgumentException("Invalid resource sample/core bounds.");
        for (var i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            if (!double.IsFinite(s.Seconds) || !double.IsFinite(s.CpuSeconds) || s.Seconds < 0 || s.CpuSeconds < 0 ||
                s.Handles < 0 || s.PrivateBytes < 0 || s.WorkingSetBytes < 0 || s.Threads < 1 ||
                i > 0 && (s.Seconds <= samples[i - 1].Seconds || s.CpuSeconds < samples[i - 1].CpuSeconds))
                throw new ArgumentException("Resource counters must be finite, nonnegative and monotonically timed.");
        }
        var first = samples[0]; var last = samples[^1]; var elapsed = last.Seconds - first.Seconds; var cpu = last.CpuSeconds - first.CpuSeconds;
        var handles = last.Handles - first.Handles; var memory = last.PrivateBytes - first.PrivateBytes;
        var handleRange = samples.Max(s => s.Handles) - samples.Min(s => s.Handles);
        var memoryRange = samples.Max(s => s.PrivateBytes) - samples.Min(s => s.PrivateBytes);
        var meanTime = samples.Average(s => s.Seconds); var variance = samples.Sum(s => Math.Pow(s.Seconds - meanTime, 2));
        double Slope(Func<ResourceSample, double> counter)
        { var mean = samples.Average(counter); return samples.Sum(s => (s.Seconds - meanTime) * (counter(s) - mean)) / variance; }
        // Check the observed range too: recovery at the final sample must not hide large intermediate retention.
        var guard = handles <= 32 && handleRange <= 32 && memory <= 64L * 1024 * 1024 && memoryRange <= 64L * 1024 * 1024;
        return new(elapsed, cpu, cpu / elapsed * 100, cpu / elapsed * 100 / logicalProcessors, logicalProcessors,
            handles, handleRange, memory, memoryRange, samples.Max(s => s.WorkingSetBytes), Slope(s => s.Handles) * 60,
            Slope(s => s.PrivateBytes) * 60 / (1024 * 1024), guard);
    }
}
