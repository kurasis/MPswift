using System.IO;
using System.Security.Cryptography;
using Player.App.Services.Audio;
using Player.App.Services.Storage;
using Player.App.Services.Waveforms;
using Player.Core.Playback;
using Player.Core.Waveforms;

namespace Player.AudioSmoke;

public static class WaveformValidation
{
    public static async Task<object> RunAsync(string source)
    {
        source = LocalFileAccess.ValidateFile(source);
        var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", "waveform-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var hash = Hash(source);
        await using var player = new SerializedAudioPlayer(() => new BassAudioBackend());
        Check(await player.LoadAsync(new(Guid.NewGuid(), source), false), "Production source preparation failed.");
        await player.SeekAsync(TimeSpan.FromSeconds(1)); await player.SetVolumeAsync(0, true);
        WaveformData shortData;
        var cache = new WaveformCache(Path.Combine(output, "cache"));
        await using (var analyzer = new BassWaveformService(cache))
        {
            var requests = new[] { analyzer.AnalyzeAsync(source, null, CancellationToken.None), analyzer.AnalyzeAsync(source, null, CancellationToken.None) };
            shortData = (await Task.WhenAll(requests))[0];
            Check(shortData.Minimum.Min() < -0.045 && shortData.Maximum.Max() > 0.045, "Opposite-phase waveform was canceled or affected by mute/gain.");
            Check(shortData.Minimum.Length == 300 && Math.Abs(shortData.DurationSeconds - 3) < 0.001, "Short waveform dimensions differ.");
            var cached = await analyzer.AnalyzeAsync(source, null, CancellationToken.None);
            Check(shortData.Minimum.SequenceEqual(cached.Minimum) && shortData.Maximum.SequenceEqual(cached.Maximum), "Cached waveform differs.");
            var cacheFile = Directory.GetFiles(Path.Combine(output, "cache"), "*.peaks").Single();
            File.WriteAllBytes(cacheFile, [1, 2, 3]);
            var repaired = await analyzer.AnalyzeAsync(source, null, CancellationToken.None);
            Check(repaired.Minimum.SequenceEqual(shortData.Minimum), "Corrupt cache was not regenerated.");
        }
        Check(player.Snapshot.Position == TimeSpan.FromSeconds(1) && await player.SeekAsync(TimeSpan.FromSeconds(1.5)), "Waveform analysis changed or freed the production decoder.");
        var longPath = Path.Combine(output, "owned-two-hour.wav"); GenerateLong(longPath); var longHash = Hash(longPath);
        WaveformData longData;
        await using (var analyzer = new BassWaveformService(cache)) longData = await analyzer.AnalyzeAsync(longPath, null, CancellationToken.None);
        Check(longData.DurationSeconds == 7200 && longData.Minimum.Length <= WaveformData.MaximumBuckets && longData.Maximum[0] > 0.24f && longData.Minimum[^1] < -0.24f, "Long-file waveform failed actual decode or boundaries.");
        using var cancel = new CancellationTokenSource();
        var canceledAnalyzer = new BassWaveformService(cache);
        var canceled = canceledAnalyzer.AnalyzeAsync(longPath, new InlineProgress(_ => cancel.Cancel()), cancel.Token, true);
        try { await canceled; throw new InvalidDataException("Long-file cancellation did not interrupt the caller."); }
        catch (OperationCanceledException) { }
        await canceledAnalyzer.DisposeAsync();
        using (File.Open(longPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Check(await player.SeekAsync(TimeSpan.FromSeconds(1)), "Canceling analysis freed the production context.");
        Check(hash.AsSpan().SequenceEqual(Hash(source)) && longHash.AsSpan().SequenceEqual(Hash(longPath)), "Waveform analysis modified media.");
        return new { Status = "waveform-passed", SourceUnchanged = true, IndependentDecoder = true, GainAndMuteIndependent = true,
            OppositePhasePeaksPreserved = true, CacheRoundTrip = true, CorruptCacheRegenerated = true,
            ShortBuckets = shortData.Minimum.Length, LongDurationSeconds = longData.DurationSeconds, LongBuckets = longData.Minimum.Length,
            LongFramesPerBucket = longData.FramesPerBucket, PeakPayloadBytes = longData.Minimum.Length * 8,
            CancellationAndHandleRelease = true, DeviceOutput = "not-run", FixtureLicense = "CC0-1.0" };
    }
    private static void GenerateLong(string path)
    {
        const int frames = 8000 * 7200; const int size = frames * 2;
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8); writer.Write(size + 36); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(size);
        writer.Write((short)8192); writer.Flush(); file.SetLength(44L + size); file.Position = 44L + size - 2; writer.Write((short)-8192);
    }
    private static byte[] Hash(string path) { using var file = File.OpenRead(path); return SHA256.HashData(file); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private sealed class InlineProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
