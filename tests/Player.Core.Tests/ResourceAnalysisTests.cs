using Player.Core.Diagnostics;
namespace Player.Core.Tests;

public sealed class ResourceAnalysisTests
{
    private static ResourceSample S(double seconds, double cpu, int handles = 100, long memory = 20 * 1024 * 1024) => new(seconds, cpu, handles, memory, 30 * 1024 * 1024, 5);
    [Fact] public void TotalCpuUsesWallTimeAndAllLogicalCoresWithoutSubtractingOtherProcessThreads()
    {
        var r = ResourceAnalysis.Analyze([S(10, 20), S(20, 25), S(30, 30)], 4);
        Assert.Equal(20, r.ElapsedSeconds); Assert.Equal(50, r.OneCoreCpuPercent); Assert.Equal(12.5, r.TotalCpuPercent); Assert.True(r.GrowthGuardPassed);
    }
    [Fact] public void LeakTrendUsesMeasuredUnevenIntervalsAndPrivateBytes()
    {
        var r = ResourceAnalysis.Analyze([S(0, 0, 100, 0), S(30, 1, 103, 3 * 1024 * 1024), S(90, 2, 109, 9 * 1024 * 1024)], 8);
        Assert.Equal(6, r.HandlesPerMinute, 6); Assert.Equal(6, r.PrivateMiBPerMinute, 6);
    }
    [Fact] public void MultipleBusyThreadsMayExceedOneCoreWithoutExceedingTotalCapacity()
    { var r = ResourceAnalysis.Analyze([S(0, 0), S(1, 2), S(2, 4)], 4); Assert.Equal(200, r.OneCoreCpuPercent); Assert.Equal(50, r.TotalCpuPercent); }
    [Fact] public void IntermediateRetentionCannotBeHiddenByCleanupAtTheFinalSample()
    {
        Assert.False(ResourceAnalysis.Analyze([S(0, 0), S(1, .1, 150), S(2, .2)], 4).GrowthGuardPassed);
        Assert.False(ResourceAnalysis.Analyze([S(0, 0), S(1, .1, memory: 100 * 1024 * 1024), S(2, .2)], 4).GrowthGuardPassed);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void ResetCpuOrDuplicateWallTimeInvalidateMeasurement(bool cpuReset)
    { Assert.Throws<ArgumentException>(() => ResourceAnalysis.Analyze([S(0, 1), S(1, 2), cpuReset ? S(2, 0) : S(1, 3)], 4)); }
    [Fact] public void MissingSamplesAndNonfiniteCountersCannotPass()
    {
        Assert.Throws<ArgumentException>(() => ResourceAnalysis.Analyze([S(0, 0), S(1, 1)], 4));
        Assert.Throws<ArgumentException>(() => ResourceAnalysis.Analyze([S(0, 0), S(1, double.NaN), S(2, 2)], 4));
    }
}
