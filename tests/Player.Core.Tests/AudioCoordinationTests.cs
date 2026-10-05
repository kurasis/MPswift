using System.Text;
using Player.Core.Library;
using Player.Core.Media;
using Player.Core.Playback;
using Player.Core.Waveforms;
using Player.App.Services.Storage;

namespace Player.Core.Tests;

public sealed class AudioCoordinationTests
{
    private static PlaylistEntry E(string title, bool enabled = true) => new(Guid.NewGuid(), new(Guid.NewGuid(), "C:\\Music\\" + title + ".wav", title), enabled);
    [Fact] public void QueueBatchPrecedenceDoesNotMovePlaylistCursorAndDetachedSnapshotsRemain()
    {
        var a = E("a"); var b = E("b"); var c = E("c"); var order = new PlaybackOrder(new Random(2));
        order.SetSource([a, b, c]); order.Started(a, false); order.Enqueue([c, c], false); order.Enqueue([b, a], true);
        Assert.Equal(new[] { "b", "a", "c", "c" }, order.Queue.Select(q => q.Entry.Track.Title));
        Assert.Equal(4, order.Queue.Select(q => q.Entry.Id).Distinct().Count());
        var queued = order.Candidates(a, true)[0]; order.Started(queued, true);
        Assert.Equal(a.Id, order.Capture().PlaylistCursorId);
        order.SetSource([a]); Assert.True(order.IsDetached(order.Queue.Last()));
        foreach (var item in order.Queue) order.Started(item.Entry, true);
        order.SetSource([a, b, c]); Assert.Equal(b.Id, order.Candidates(queued, true)[0].Id);
    }
    [Fact] public void RepeatOneNaturalEndYieldsToQueueAndManualNextEscapes()
    {
        var a = E("a"); var b = E("b"); var order = new PlaybackOrder { Repeat = RepeatMode.One };
        order.SetSource([a, b]); order.Started(a, false);
        Assert.Equal(a.Id, order.Candidates(a, true)[0].Id); Assert.Equal(b.Id, order.Candidates(a, false)[0].Id);
        order.Enqueue([b], false); var q = order.Candidates(a, true)[0]; Assert.Equal(b.Track.Id, q.Track.Id); order.Started(q, true);
        Assert.Equal(q.Id, order.Candidates(q, true)[0].Id); Assert.Equal(b.Id, order.Candidates(q, false)[0].Id);
    }
    [Fact] public void ShuffleNoRepeatsPerCycleAndExactBagHistoryRestore()
    {
        var entries = Enumerable.Range(0, 6).Select(i => E(i.ToString())).ToArray(); var order = new PlaybackOrder(new Random(7)); order.SetSource(entries); order.Started(entries[0], false); order.Shuffle = true;
        var selected = new List<Guid> { entries[0].Id };
        while (order.Candidates(null, true).FirstOrDefault() is { } next) { selected.Add(next.Id); order.Started(next, false); }
        Assert.Equal(6, selected.Distinct().Count()); Assert.Equal(6, selected.Count);
        order.Repeat = RepeatMode.All; var secondCycle = order.Candidates(entries.First(e => e.Id == selected[^1]), true); Assert.NotEqual(selected[^1], secondCycle[0].Id);
        var saved = order.Capture(); var reopened = new PlaybackOrder(new Random(999)); reopened.SetSource(entries); reopened.Restore(saved);
        Assert.Equal(order.Candidates(null, true).Select(e => e.Id), reopened.Candidates(null, true).Select(e => e.Id));
        Assert.Equal(order.Previous(entries[^1], TimeSpan.Zero)?.Id, reopened.Previous(entries[^1], TimeSpan.Zero)?.Id);
    }
    [Fact] public void QueueReorderClearAndDeletedSourceAreBounded()
    {
        var a = E("a"); var b = E("b"); var order = new PlaybackOrder { Repeat = RepeatMode.All };
        order.SetSource([a, b]); order.Started(a, false); order.Enqueue([a, b], false); var second = order.Queue[1]; order.Move(second.Id, -1); Assert.Equal(second.Id, order.Queue[0].Id);
        order.SetSource([]); foreach (var q in order.Queue) order.Started(q.Entry, true);
        Assert.Empty(order.Candidates(a, false)); order.Enqueue([a], false); order.Clear(); Assert.Empty(order.Queue);
        order.SetSource([a with { Enabled = false }, b with { Track = b.Track with { Available = false } }]); Assert.Empty(order.Candidates(a, false));
    }
    [Fact] public void CueQuotedUnicodeMultiFilePregapsAndLogicalIdentity()
    {
        var cue = CueSheet.Parse("TITLE \"Альбом\"\nPERFORMER \"Артист\"\nFILE \"one album.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"Первый\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nINDEX 00 00:02:00\nINDEX 01 00:03:00\nFILE \"sub\\two.wav\" WAVE\nTRACK 03 AUDIO\nINDEX 01 00:00:00", "C:\\Music\\album.cue");
        Assert.Empty(cue.Diagnostics); Assert.Equal(3, cue.Songs.Length); Assert.Equal(225, cue.Songs[0].EndFrame); Assert.Null(cue.Songs[1].EndFrame); Assert.Null(cue.Songs[2].EndFrame);
        Assert.Equal("C:\\Music\\one album.wav", cue.Songs[0].Path); Assert.Equal("Артист", cue.Songs[1].Performer);
        Assert.NotEqual(CueSheet.TrackId("C:\\Music\\album.cue", cue.Songs[0]), CueSheet.TrackId("C:\\Music\\album.cue", cue.Songs[1]));
    }
    [Theory] [InlineData("INDEX 01 00:00:75")] [InlineData("INDEX 01 -1:00:00")] [InlineData("INDEX 01 00:60:00")] [InlineData("TITLE \"unclosed")]
    public void CueInvalidTrackIsRejected(string bad)
    { var cue = CueSheet.Parse("FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\n" + bad, "C:\\Music\\a.cue"); Assert.Empty(cue.Songs); Assert.NotEmpty(cue.Diagnostics); }
    [Fact] public void CueDecreasingAndNetworkSourcesNeverBecomeFullAlbumTracks()
    {
        var cue = CueSheet.Parse("FILE \"a.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:03:00\nTRACK 02 AUDIO\nINDEX 01 00:02:00\nFILE \"https://bad/a.wav\" WAVE\nTRACK 03 AUDIO\nINDEX 01 00:00:00", "C:\\Music\\a.cue");
        Assert.DoesNotContain(cue.Songs, s => s.Number == 1 || s.Number == 3); Assert.NotEmpty(cue.Diagnostics);
        Assert.Throws<DecoderFallbackException>(() => CueSheet.Decode([0xff, 0x80]));
    }
    [Fact] public void WaveformSegmentUsesSourceCacheWithRelativeCoordinates()
    {
        var accumulator = new WaveformAccumulator(1000, 1, 1000); accumulator.Add(Enumerable.Range(0, 1000).Select(i => i / 1000f).ToArray());
        var slice = accumulator.Complete().Slice(new(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.8))); slice.Validate();
        Assert.Equal(0.3, slice.DurationSeconds, 5); Assert.Equal(0.5f, slice.Minimum[0]); Assert.Equal(0.799f, slice.Maximum[^1]);
    }
    [Fact] public void ReplayGainInvariantClampsKnownPeakAndMissingTagsStayUnity()
    {
        Assert.Equal(-6.5, ReplayGainTags.Parse(" -6.5 dB ")); Assert.Null(ReplayGainTags.Parse("5,5 dB")); Assert.Null(ReplayGainTags.Parse("NaN")); Assert.Null(ReplayGainTags.Parse("1000"));
        Assert.Equal(1, new ReplayGainTags().Gain(ReplayGainMode.Album)); Assert.Equal(0.5, new ReplayGainTags(12, null, 2).Gain(ReplayGainMode.Track)); Assert.Equal(1, new ReplayGainTags(12).Gain(ReplayGainMode.Off));
    }
    [Fact] public void EqMeasuredToneAttenuationBypassNyquistAndSaturation()
    {
        var tone = Enumerable.Range(0, 48000).Select(i => (float)(0.1 * Math.Sin(2 * Math.PI * 1000 * i / 48000))).ToArray();
        var bypass = tone.ToArray(); new PcmProcessor(48000, 1, new()).Process(bypass); Assert.Equal(tone, bypass);
        var bands = new double[10]; bands[5] = -12; var attenuated = tone.ToArray(); new PcmProcessor(48000, 1, new(true, 0, bands)).Process(attenuated);
        static double Rms(float[] x) => Math.Sqrt(x.Skip(2400).Average(v => v * v)); Assert.InRange(Rms(attenuated) / Rms(tone), 0.24, 0.27);
        var lowRate = new float[] { 2, -2, float.NaN }; var processor = new PcmProcessor(8000, 1, new(true, 0, [0,0,0,0,0,0,0,0,0,12])); processor.Process(lowRate); Assert.All(lowRate, x => Assert.True(float.IsFinite(x) && Math.Abs(x) <= 1)); Assert.True(processor.ProtectedSamples > 0);
        Assert.Equal(0, AudioProcessingSettings.Overlap(3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), true, false)); Assert.Equal(0.5, AudioProcessingSettings.Overlap(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), false, false));
    }
    [Fact] public async Task QueueCueAndProcessingSessionRoundTripThroughActualSqlite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "stage-d-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var tab = Guid.NewGuid(); var a = E("a") with { Track = E("cue").Track with { Segment = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)), CueDocument = "C:\\Music\\a.cue", CueNumber = 1 } };
            var order = new PlaybackOrder(); order.SetSource([a]); order.Enqueue([a, a], false); order.Shuffle = true; var state = new LibraryState([new(tab, "D", [a])], new(tab, tab, a, 1, order.Capture()));
            var path = Path.Combine(directory, "player.db"); await using (var store = new SqlitePlayerStore(path)) await store.SaveAsync(state, true);
            await using var reopened = new SqlitePlayerStore(path); var loaded = await reopened.LoadAsync(); Assert.Equal(2, loaded.Session.Order!.Queue.Length); Assert.Equal(a.Track.Segment, loaded.Session.Order.Queue[0].Entry.Track.Segment); Assert.Equal(a.Id, loaded.Session.Order.Queue[0].OriginEntryId);
        }
        finally { Directory.Delete(directory, true); }
    }
}
