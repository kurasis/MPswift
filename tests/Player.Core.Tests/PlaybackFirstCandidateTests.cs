using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class PlaybackFirstCandidateTests
{
    private static PlaylistEntry Entry(int index) => new(Guid.NewGuid(), new(Guid.NewGuid(), @"C:\Music\" + index + ".wav", index.ToString()));
    [Theory]
    [InlineData(RepeatMode.Off, false, false)]
    [InlineData(RepeatMode.All, false, false)]
    [InlineData(RepeatMode.One, false, false)]
    [InlineData(RepeatMode.Off, true, false)]
    [InlineData(RepeatMode.All, true, false)]
    [InlineData(RepeatMode.One, true, false)]
    [InlineData(RepeatMode.Off, false, true)]
    [InlineData(RepeatMode.All, false, true)]
    [InlineData(RepeatMode.One, false, true)]
    [InlineData(RepeatMode.Off, true, true)]
    [InlineData(RepeatMode.All, true, true)]
    [InlineData(RepeatMode.One, true, true)]
    public void MatchesFullTraversalIncludingQueueRefillRemovalAndRestoration(RepeatMode repeat, bool shuffle, bool natural)
    {
        var entries = Enumerable.Range(0, 12).Select(Entry).ToArray();
        var reference = new PlaybackOrder(new Random(7)); var fast = new PlaybackOrder(new Random(7));
        foreach (var order in new[] { reference, fast }) { order.SetSource(entries); order.Shuffle = shuffle; order.Repeat = repeat; }
        reference.Enqueue([entries[4], entries[4]], false); fast.Restore(reference.Capture());
        PlaylistEntry? active = null;
        for (var step = 0; step < 40; step++)
        {
            if (step == 3)
            {
                entries = entries.Where(entry => entry.Id != active?.Id).Select((entry, i) => i == 2 ? entry with { Enabled = false } : entry).Reverse().ToArray();
                reference.SetSource(entries); fast.SetSource(entries);
            }
            if (step == 5) { reference.Enqueue([entries[0]], true); fast.Restore(reference.Capture()); }
            var expected = reference.Candidates(active, natural).FirstOrDefault();
            Assert.Equal(expected, fast.FirstCandidate(active, natural));
            Assert.Equal(reference.Capture().Remaining, fast.Capture().Remaining);
            Assert.Equal(reference.Queue, fast.Queue);
            if (expected is null) continue;
            foreach (var order in new[] { reference, fast })
            {
                if (step % 7 == 6) order.Failed(expected);
                else order.Started(expected, order.Queue.Any(item => item.Entry.Id == expected.Id));
            }
            active = expected;
        }
    }
    [Fact]
    public void DuplicateIdsRetainTheFirstSourceMatchAndLargeSelectionAvoidsFullArrays()
    {
        var entries = Enumerable.Range(0, 10000).Select(Entry).ToArray();
        var order = new PlaybackOrder(new Random(7)); order.SetSource(entries); order.Started(entries[0], false); order.Shuffle = true;
        order.SetSource(entries.Append(entries[0] with { Track = entries[0].Track with { Title = "later duplicate" } }).ToArray());
        Assert.Equal(order.Candidates(entries[0], true).FirstOrDefault(), order.FirstCandidate(entries[0], true));
        order.Shuffle = false;
        for (var i = 0; i < 20; i++) order.FirstCandidate(entries[0], true);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) Assert.Same(entries[1], order.FirstCandidate(entries[0], true));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 64000);
    }
    [Fact]
    public async Task PositionTicksDoNotRetraverseLargeSourcesButChangesAndSeeksStillPrepare()
    {
        var entries = Enumerable.Range(0, 10000).Select(Entry).ToArray();
        var player = new TickPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetEntries(entries); Assert.True(await coordinator.LoadAsync(entries[0].Id));
        Assert.Equal(entries[1].Id, player.Prepared.Last()!.EntryId);
        for (var i = 0; i < 20; i++) player.Tick(i);
        var count = player.Prepared.Count; var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) player.Tick(i);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 256000);
        Assert.Equal(count, player.Prepared.Count);
        coordinator.Enqueue([entries[^1]], true);
        Assert.Equal(entries[^1].Track.Path, player.Prepared.Last()!.Path);
        Assert.True(await coordinator.SeekAsync(TimeSpan.FromSeconds(2)));
        Assert.True(player.Prepared.Count > count + 1);
        coordinator.ClearQueue(); Assert.Equal(entries[1].Id, player.Prepared.Last()!.EntryId);
        coordinator.SetEntries(entries.Where(entry => entry.Id != entries[1].Id)); Assert.Equal(entries[2].Id, player.Prepared.Last()!.EntryId);
    }
    [Fact]
    public async Task ObservedNaturalEndArrivesBeforeTheNextSynchronousLoad()
    {
        var entries = new[] { Entry(1), Entry(2) }; var player = new TickPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetEntries(entries); await coordinator.LoadAsync(entries[0].Id);
        var observed = new List<PlaybackSnapshot>(); coordinator.SnapshotObserved += observed.Add;
        player.End();
        Assert.True(observed.Count >= 2); Assert.True(observed[0].Ended); Assert.Equal(entries[0].Id, observed[0].EntryId);
        Assert.Equal(entries[1].Id, observed[1].EntryId); Assert.True(observed[1].Revision > observed[0].Revision);
    }
    private sealed class TickPlayer : IAudioPlayer, IAdvancedAudioPlayer
    {
        public PlaybackSnapshot Snapshot { get; private set; } = PlaybackSnapshot.Empty;
        public event Action<PlaybackSnapshot>? SnapshotChanged;
        public List<AudioRequest?> Prepared { get; } = [];
        public void Tick(int position) { Snapshot = Snapshot with { Revision = Snapshot.Revision + 1, Position = TimeSpan.FromMilliseconds(position) }; SnapshotChanged?.Invoke(Snapshot); }
        public void End() { Snapshot = Snapshot with { State = PlaybackState.Stopped, Ended = true }; Tick(3000); }
        public Task<bool> LoadAsync(AudioRequest request, bool autoPlay, CancellationToken cancellationToken = default)
        { Snapshot = Snapshot with { Generation = Snapshot.Generation + 1, EntryId = request.EntryId, State = autoPlay ? PlaybackState.Playing : PlaybackState.Stopped, Duration = TimeSpan.FromMinutes(3), CanSeek = true, Ended = false }; Tick(0); return Task.FromResult(true); }
        public Task<bool> PrepareNextAsync(AudioRequest? request, bool repeatOne) { Prepared.Add(request); return Task.FromResult(true); }
        public Task<bool> SeekAsync(TimeSpan position) { Tick((int)position.TotalMilliseconds); return Task.FromResult(true); }
        public Task<bool> StopAsync() { Snapshot = Snapshot with { State = PlaybackState.Stopped }; Tick(0); return Task.FromResult(true); }
        public Task<bool> PlayAsync() => Task.FromResult(true);
        public Task<bool> PauseAsync() => Task.FromResult(true);
        public Task<bool> SetVolumeAsync(double volume, bool muted) => Task.FromResult(true);
        public Task<AudioDevice[]> GetDevicesAsync() => Task.FromResult(Array.Empty<AudioDevice>());
        public Task<bool> SetProcessingAsync(AudioProcessingSettings settings) => Task.FromResult(true);
        public Task<bool> SetOutputAsync(AudioOutputSettings settings) => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
