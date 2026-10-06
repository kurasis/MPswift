using System.Diagnostics;
using System.Security.Cryptography;
using Player.App.Services.Audio;
using Player.Core.Playback;

namespace Player.AudioSmoke;

public static class StressValidation
{
    private sealed record Sample(int Changes, int Handles, long PrivateBytes, long WorkingSet);
    public static async Task<object> RunAsync(string source)
    {
        var file = BassSmokeSession.ValidateSourcePath(Path.GetFullPath(source));
        static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var hash = Hash(file); var samples = new List<Sample>(); var timings = new List<double>();
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        using var process = Process.GetCurrentProcess();
        await using (var player = new SerializedAudioPlayer(() => new BassAudioBackend()))
        {
            for (var i = 0; i < 1050; i++)
            {
                var watch = Stopwatch.StartNew();
                Check(await player.LoadAsync(new AudioRequest(Guid.NewGuid(), file), false), "Stress load failed: " + player.Snapshot.Error?.Detail);
                Check(await player.PrepareNextAsync(new AudioRequest(Guid.NewGuid(), file)), "Stress prepare-next failed.");
                Check(await player.SeekAsync(TimeSpan.FromSeconds((i % 20) / 10d)), "Stress seek failed.");
                Check(await player.StopAsync(), "Stress stop failed.");
                Check(player.Snapshot.State == PlaybackState.Stopped && player.Snapshot.Position == TimeSpan.Zero, "Stress left stale playback state.");
                if (i >= 50) timings.Add(watch.Elapsed.TotalMilliseconds);
                if (i == 49 || (i + 1 - 50) % 100 == 0 && i >= 50)
                { process.Refresh(); samples.Add(new(i + 1 - 50, process.HandleCount, process.PrivateMemorySize64, process.WorkingSet64)); }
            }
        }
        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Check(Hash(file) == hash, "Stress changed source audio.");
        var handleGrowth = samples[^1].Handles - samples[0].Handles;
        var privateGrowth = samples[^1].PrivateBytes - samples[0].PrivateBytes;
        Check(handleGrowth <= 32, "Prepared-source stress retained too many process handles.");
        Check(privateGrowth <= 64L * 1024 * 1024, "Prepared-source stress exceeded its 64 MiB growth guard.");
        timings.Sort();
        return new { Status = "native-preparation-stress-passed", Environment = Environment.OSVersion.VersionString,
            Method = "50 warmup + 1000 serialized real load/prepare-next/seek/stop cycles; process samples every 100 cycles; no forced GC",
            Changes = 1000, Samples = samples, HandleGrowth = handleGrowth, PrivateByteGrowth = privateGrowth,
            CycleP95Milliseconds = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1], SourceUnchanged = true, SourceHandleReleased = true,
            TwoHourDevicePlayback = "not-run", EndpointTransitions = "not-run", Listening = "not-run" };
    }
}
