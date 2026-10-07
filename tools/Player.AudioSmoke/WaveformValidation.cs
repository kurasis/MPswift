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
        var correction = await CorrectionCacheAsync(output);
        return new { Status = "waveform-passed", SourceUnchanged = true, IndependentDecoder = true, GainAndMuteIndependent = true,
            OppositePhasePeaksPreserved = true, CacheRoundTrip = true, CorruptCacheRegenerated = true,
            ShortBuckets = shortData.Minimum.Length, LongDurationSeconds = longData.DurationSeconds, LongBuckets = longData.Minimum.Length,
            LongFramesPerBucket = longData.FramesPerBucket, PeakPayloadBytes = longData.Minimum.Length * 8,
            CancellationAndHandleRelease = true, CorrectionCache = correction, DeviceOutput = "not-run", FixtureLicense = "CC0-1.0" };
    }
    private static async Task<object> CorrectionCacheAsync(string output)
    {
        var fixtures = Path.Combine(Environment.CurrentDirectory, "tests", "fixtures", "audio-extended");
        if (new[] { "hybrid-corrected.wv", "hybrid-corrected.wvc" }.Any(name => !File.Exists(Path.Combine(fixtures, name))))
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") throw new FileNotFoundException("CI correction fixtures are required.");
            return new { Status = "not-run", Reason = "Optional repository correction fixtures are unavailable in this working directory." };
        }
        var main = Path.Combine(output, "owned-hybrid.wv"); var sidecar = Path.ChangeExtension(main, ".wvc");
        File.Copy(Path.Combine(fixtures, "hybrid-corrected.wv"), main);
        var mainHash = Hash(main);
        var cacheDirectory = Path.Combine(output, "correction-cache");
        await using var analyzer = new BassWaveformService(new WaveformCache(cacheDirectory));
        var without = await analyzer.AnalyzeAsync(main, null, CancellationToken.None);
        Check(Directory.GetFiles(cacheDirectory, "*.peaks").Length == 1, "Uncorrected waveform cache missing.");
        File.Copy(Path.Combine(fixtures, "hybrid-corrected.wvc"), sidecar);
        var with = await analyzer.AnalyzeAsync(main, null, CancellationToken.None);
        Check(Directory.GetFiles(cacheDirectory, "*.peaks").Length == 2, "Correction appearance reused the uncorrected cache key.");
        Check(!without.Minimum.SequenceEqual(with.Minimum) || !without.Maximum.SequenceEqual(with.Maximum), "Correction appearance did not change actual decoded peaks.");
        var modified = File.GetLastWriteTimeUtc(sidecar).AddSeconds(2);
        File.SetLastWriteTimeUtc(sidecar, modified);
        var changed = await analyzer.AnalyzeAsync(main, null, CancellationToken.None);
        Check(Directory.GetFiles(cacheDirectory, "*.peaks").Length == 3 && changed.Minimum.SequenceEqual(with.Minimum) && changed.Maximum.SequenceEqual(with.Maximum), "Correction timestamp did not invalidate its cache entry.");
        File.Delete(sidecar);
        var removed = await analyzer.AnalyzeAsync(main, null, CancellationToken.None);
        Check(Directory.GetFiles(cacheDirectory, "*.peaks").Length == 3 && removed.Minimum.SequenceEqual(without.Minimum) && removed.Maximum.SequenceEqual(without.Maximum), "Correction removal did not recover the uncorrected cache entry.");
        using (File.Open(main, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(mainHash.AsSpan().SequenceEqual(Hash(main)), "Correction waveform checks modified the main audio file.");
        return new { AppearanceInvalidates = true, ModificationInvalidates = true, RemovalRecoversAbsentEntry = true, ActualDecodedPeaksDiffer = true, MainSourceUnchanged = true, HandlesReleased = true };
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
