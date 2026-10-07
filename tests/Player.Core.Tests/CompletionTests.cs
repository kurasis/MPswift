using System.Runtime.CompilerServices;
using System.Text.Json;
using Player.App.Services.Storage;
using Player.Core.Integration;
using Player.Core.Library;
using Player.Core.Media;
using Player.Core.Playback;
using Player.Core.Waveforms;

namespace Player.Core.Tests;

public sealed class CompletionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mpswift-completion-" + Guid.NewGuid().ToString("N"));
    public CompletionTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private static PlaylistEntry Entry(string path, TrackSegment? segment = null) => new(Guid.NewGuid(), new(Guid.NewGuid(), path, "Owned", Segment: segment));

    [Fact]
    public void DuplicateIdentityIgnoresCaseAndMetadataButPreservesDifferentCueBoundsAndFirstOccurrence()
    {
        var whole = Entry(@"C:\Music\Album.flac");
        var first = Entry(@"C:\Music\Album.flac", new(TimeSpan.Zero, TimeSpan.FromSeconds(60)));
        var next = Entry(@"C:\Music\Album.flac", new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)));
        var duplicateWhole = Entry(@"c:/music/./ALBUM.flac") with { Enabled = false };
        var duplicateSong = Entry(@"C:\Music\ALBUM.flac", first.Track.Segment);
        var openEnded = Entry(@"C:\Music\Album.flac", new(TimeSpan.Zero, null));
        Assert.Equal(new[] { duplicateWhole.Id, duplicateSong.Id }, PlaylistDuplicates.FindRemovable([whole, first, next, duplicateWhole, duplicateSong, openEnded]));
        Assert.Empty(PlaylistDuplicates.FindRemovable([first, next, openEnded]));
    }
    [Fact]
    public void DuplicateDetectionHandlesTenThousandEntriesWithoutCollapsingCueSongs()
    {
        var entries = Enumerable.Range(0, 10000).Select(i => Entry(@"C:\Music\album.flac", new(TimeSpan.FromSeconds(i % 5000), TimeSpan.FromSeconds(i % 5000 + 1)))).ToArray();
        Assert.Equal(entries.Skip(5000).Select(e => e.Id), PlaylistDuplicates.FindRemovable(entries));
    }
    [Fact]
    public void ClearingCacheInvalidatesOlderWritersAndPreservesDatabaseAndUnrelatedFiles()
    {
        var cache = new WaveformCache(_directory); var key = new string('a', 64); var epoch = cache.Epoch;
        var data = new WaveformData(48000, 2, 480, 480, [-.5f], [.5f], [.5f]);
        File.WriteAllText(Path.Combine(_directory, "library.db"), "owned database");
        File.WriteAllText(Path.Combine(_directory, "sentinel.txt"), "owned sentinel");
        cache.Write(key, data); Assert.Equal(1, cache.GetUsage().Files);
        cache.Clear(); cache.WriteIfCurrent(key, data, epoch);
        Assert.Equal(0, cache.GetUsage().Files); Assert.Null(cache.Read(key));
        Assert.Equal("owned database", File.ReadAllText(Path.Combine(_directory, "library.db")));
        Assert.Equal("owned sentinel", File.ReadAllText(Path.Combine(_directory, "sentinel.txt")));
        cache.WriteIfCurrent(key, data, cache.Epoch); Assert.NotNull(cache.Read(key));
    }
    [Fact]
    public void ChangingCacheBudgetEvictsOldestFilesAndReportsActualUsage()
    {
        var cache = new WaveformCache(_directory);
        var data = new WaveformData(48000, 2, 480, 480L * 300000, new float[300000], new float[300000]);
        for (var i = 0; i < 8; i++)
        {
            var key = i.ToString("x64"); cache.Write(key, data);
            File.SetLastWriteTimeUtc(Path.Combine(_directory, key + ".peaks"), new DateTime(2020, 1, 1, 0, 0, i, DateTimeKind.Utc));
        }
        cache.SetBudget(16L * 1024 * 1024); var usage = cache.GetUsage();
        Assert.Equal(6, usage.Files); Assert.InRange(usage.Bytes, 1, usage.BudgetBytes);
        Assert.Null(cache.Read(0.ToString("x64"))); Assert.Null(cache.Read(1.ToString("x64"))); Assert.NotNull(cache.Read(2.ToString("x64")));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetBudget(1));
        Assert.Equal(16L * 1024 * 1024, cache.GetUsage().BudgetBytes);
    }
    [Fact]
    public async Task ConcurrentCacheReadWriteAndClearUseCompleteFiles()
    {
        var cache = new WaveformCache(_directory); var key = new string('b', 64);
        var data = new WaveformData(48000, 2, 480, 480, [-.5f], [.5f]);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            for (var j = 0; j < 12; j++)
            {
                if (i % 3 == 0) cache.Clear();
                else if (i % 3 == 1) cache.Write(key, data);
                else cache.Read(key)?.Validate();
            }
        })));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
    [Fact]
    public void NewPreferencesRoundTripAndLegacySettingsKeepPreviousDefaults()
    {
        var file = new SettingsFile(_directory);
        var settings = new PlayerSettings(WaveformCacheMiB: 64) { Accent = "violet", WaveformStyle = "peaks", DefaultRepeat = RepeatMode.All, DefaultShuffle = true, RestoreSession = false, RestorePosition = false, LastFileDirectory = @"C:\Music", LastFolderDirectory = @"C:\" };
        file.Save(settings); Assert.Equal(JsonSerializer.Serialize(settings.Validate()), JsonSerializer.Serialize(file.Load()));
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{\"SchemaVersion\":1,\"Volume\":17}");
        var old = file.Load(); Assert.True(old.RestoreSession); Assert.True(old.RestorePosition); Assert.Equal("amber", old.Accent); Assert.Null(old.LastFileDirectory);
    }
    [Theory]
    [InlineData("blue", "blue")]
    [InlineData("green", "green")]
    [InlineData("violet", "violet")]
    [InlineData("invalid", "amber")]
    public void AccentValidationHasSafeFallback(string input, string expected) => Assert.Equal(expected, (new PlayerSettings { Accent = input }).Validate().Accent);
    [Theory]
    [InlineData(@"C:\Music", @"C:\Music\")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"\\server\music", null)]
    [InlineData("https://example.com", null)]
    public void SavedDialogDirectoriesStayLocalAndPreserveDriveRoots(string input, string? expected) => Assert.Equal(expected, (new PlayerSettings { LastFileDirectory = input }).Validate().LastFileDirectory);
    [Fact]
    public void UnknownWaveformStyleAndRepeatDefaultFallBackWithoutChangingRestorePolicy()
    {
        var value = (new PlayerSettings { WaveformStyle = "unknown", DefaultRepeat = (RepeatMode)99, RestoreSession = false }).Validate();
        Assert.Equal("energy", value.WaveformStyle); Assert.Equal(RepeatMode.Off, value.DefaultRepeat); Assert.False(value.RestoreSession);
    }
    [Fact]
    public void DiagnosticRedactionHandlesNestedPrefixesCaseJsonEscapesAndUnicode()
    {
        var path = @"C:\Users\Private\Музыка\song.flac";
        var json = JsonSerializer.Serialize(new { Path = path });
        var encodedPrefix = JsonSerializer.Serialize(@"C:\Users\Private")[1..^1];
        var redacted = DiagnosticReport.Redact(json, [@"C:\Users\Private", encodedPrefix]);
        Assert.DoesNotContain("Private", redacted); using var parsed = JsonDocument.Parse(redacted);
        Assert.StartsWith("<local>", parsed.RootElement.GetProperty("Path").GetString());
        var plain = DiagnosticReport.Redact(@"c:\users\PRIVATE\Music\song C:\Users\Private\Data\logs C:\Users\PrivateOther", [@"C:\Users\Private", @"C:\Users\Private\Data"]);
        Assert.Equal(@"<local>\Music\song <local>\logs C:\Users\PrivateOther", plain);
    }
    [Fact]
    public void DiagnosticLimitDoesNotSplitAnEmoji()
    {
        var result = DiagnosticReport.Redact(new string('x', DiagnosticReport.MaximumCharacters - 1) + "🎵tail", []);
        Assert.Equal(DiagnosticReport.MaximumCharacters - 1, result.Length); Assert.False(char.IsHighSurrogate(result[^1]));
    }
    [Fact]
    public void PausedEqEditsDoNotRetainOneThousandOldProcessors()
    {
        var (current, previous) = MakeProcessors(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(previous, weak => Assert.False(weak.TryGetTarget(out _))); GC.KeepAlive(current);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (PcmProcessor Current, WeakReference<PcmProcessor>[] Previous) MakeProcessors()
    {
        var weak = new List<WeakReference<PcmProcessor>>(); var current = new PcmProcessor(48000, 2, new());
        for (var i = 0; i < 1000; i++) { weak.Add(new(current)); current = new(48000, 2, new(), current); }
        return (current, weak.ToArray());
    }
    [Fact]
    public void EqTransitionMatchesPreviousFilterOutputAndOriginalTwentyMillisecondCurve()
    {
        var settings = new AudioProcessingSettings(true, -3, Enumerable.Repeat(-2d, 10).ToArray());
        var old = new PcmProcessor(48000, 2, settings); var independentOld = new PcmProcessor(48000, 2, settings);
        var warm = Enumerable.Range(0, 1000).Select(i => .1f * (float)Math.Sin(i / 20d)).ToArray(); var warmCopy = warm.ToArray(); old.Process(warm); independentOld.Process(warmCopy);
        var next = new PcmProcessor(48000, 2, new(), old);
        var input = Enumerable.Range(0, 2400).Select(i => .1f * (float)Math.Sin(i / 13d)).ToArray();
        var oldOutput = input.ToArray(); independentOld.Process(oldOutput); var actual = input.ToArray(); next.Process(actual);
        for (var i = 0; i < actual.Length; i++)
        {
            var fraction = Math.Min(1, (i / 2) / 960d);
            Assert.InRange(Math.Abs(actual[i] - (oldOutput[i] * (1 - fraction) + input[i] * fraction)), 0, 2e-8);
        }
    }
}
