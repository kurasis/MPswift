using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class PlaylistContinuationTests
{
    private static PlaylistEntry Entry(string album, int number) => new(Guid.NewGuid(),
        new(Guid.NewGuid(), $"C:/Music/{album}/{number}.flac", $"{album} {number}", Album: album));
    private static PlaybackPlaylistSource Source(params PlaylistEntry[] entries) => new(Guid.NewGuid(), entries);
    [Fact]
    public async Task NaturalEndCrossesAlbumAndPlaylistBoundariesAndStopsAtTheEnd()
    {
        var a = Entry("A", 1); var b = Entry("B", 1); var c = Entry("C", 1); var d = Entry("C", 2);
        var first = Source(a, b); var last = Source(c, d); var player = new ControlledPlayer();
        await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, Source(), Source(Entry("disabled", 1) with { Enabled = false }), last], first.Id, true);
        await coordinator.LoadAsync(a.Id); player.End(); Assert.Equal(b.Id, player.Snapshot.EntryId);
        player.End(); Assert.Equal(c.Id, player.Snapshot.EntryId); Assert.Equal(last.Id, coordinator.SourcePlaylistId);
        var state = coordinator.CaptureSourceState(); Assert.Equal(last.Id, state.SourceId); Assert.Equal(c.Id, state.Active!.Id);
        Assert.Equal(c.Id, state.Order.PlaylistCursorId);
        player.End(); Assert.Equal(d.Id, player.Snapshot.EntryId); player.End();
        Assert.Equal(new[] { a.Id, b.Id, c.Id, d.Id }, player.Loaded);
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
    }
    [Fact]
    public async Task NextPlaylistAlwaysStartsAtItsFirstEligibleTrackEvenAfterALongAlbum()
    {
        var longAlbum = Source(Enumerable.Range(0, 80).Select(i => Entry("long", i)).ToArray());
        var next = Source(Entry("disabled", 0) with { Enabled = false }, Entry("missing", 0) with { Track = Entry("missing", 0).Track with { Available = false } }, Entry("next", 1), Entry("next", 2));
        var player = new ControlledPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([longAlbum, next], longAlbum.Id, true);
        await coordinator.LoadAsync(longAlbum.Entries[^1].Id); player.End();
        Assert.Equal(next.Entries[2].Id, player.Snapshot.EntryId);
    }
    [Fact]
    public async Task NativePreparedTransitionAdoptsTheNewPlaylistBeforePreparingItsSecondTrack()
    {
        var first = Source(Entry("A", 1)); var next = Source(Entry("B", 1), Entry("B", 2));
        var player = new ControlledPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, next], first.Id, true); await coordinator.LoadAsync(first.Entries[0].Id);
        Assert.Equal(next.Entries[0].Id, player.Prepared!.EntryId);
        player.Transition(); Assert.Equal(next.Id, coordinator.SourcePlaylistId);
        Assert.Equal(next.Entries[0].Id, coordinator.ActiveEntry!.Id); Assert.Equal(next.Entries[1].Id, player.Prepared!.EntryId);
        player.End(); Assert.Equal(next.Entries[1].Id, player.Snapshot.EntryId);
        Assert.Equal(new[] { first.Entries[0].Id, next.Entries[0].Id, next.Entries[1].Id }, coordinator.CaptureOrder().History.Select(entry => entry.Id));
    }
    [Theory]
    [InlineData(RepeatMode.One)]
    [InlineData(RepeatMode.All)]
    public async Task RepeatPoliciesRetainTheirCurrentScope(RepeatMode repeat)
    {
        var first = Source(Entry("A", 1), Entry("A", 2)); var next = Source(Entry("B", 1)); var player = new ControlledPlayer();
        await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, next], first.Id, true); coordinator.Repeat = repeat;
        await coordinator.LoadAsync(first.Entries[^1].Id); player.End();
        Assert.Equal(first.Entries[repeat == RepeatMode.One ? 1 : 0].Id, player.Snapshot.EntryId);
        Assert.Equal(first.Id, coordinator.SourcePlaylistId);
    }
    [Fact]
    public async Task QueuePrecedesContinuationAndHistoryStillReturnsToThePreviousPlaylist()
    {
        var first = Source(Entry("A", 1)); var next = Source(Entry("B", 1)); var queued = Entry("queued", 1); var player = new ControlledPlayer();
        await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, next], first.Id, true); await coordinator.LoadAsync(first.Entries[0].Id);
        coordinator.Enqueue([queued], true); var queueId = coordinator.Queue[0].Entry.Id;
        player.End(); Assert.Equal(queueId, player.Snapshot.EntryId); Assert.Equal(first.Id, coordinator.SourcePlaylistId);
        player.End(); Assert.Equal(next.Entries[0].Id, player.Snapshot.EntryId);
        Assert.True(await coordinator.PreviousAsync()); Assert.Equal(queueId, player.Snapshot.EntryId);
    }
    [Fact]
    public async Task BrowsingAndPublishingNewOrderPreserveAnAutomaticallyAdoptedSource()
    {
        var first = Source(Entry("A", 1)); var next = Source(Entry("B", 1), Entry("B", 2)); var third = Source(Entry("C", 1));
        var player = new ControlledPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, next, third], first.Id, true); await coordinator.LoadAsync(first.Entries[0].Id); player.Transition();
        coordinator.SetPlaylistSources([first, next, third], null, false);
        Assert.Equal(next.Id, coordinator.SourcePlaylistId); player.End(); Assert.Equal(next.Entries[1].Id, player.Snapshot.EntryId);
        coordinator.SetPlaylistSources([first, third, next], null, false); Assert.Null(player.Prepared);
        player.End(); Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
    }
    [Fact]
    public async Task ExplicitStopManualNextAndDetachedSourcesNeverStartAFollowingPlaylist()
    {
        var first = Source(Entry("A", 1)); var next = Source(Entry("B", 1)); var player = new ControlledPlayer();
        await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, next], first.Id, true); await coordinator.LoadAsync(first.Entries[0].Id);
        await coordinator.StopAsync(); Assert.Single(player.Loaded);
        await coordinator.LoadAsync(first.Entries[0].Id); await coordinator.NextAsync(); Assert.Equal(2, player.Loaded.Count);
        await coordinator.LoadAsync(first.Entries[0].Id); coordinator.SetPlaylistSources([next], null, false); player.End();
        Assert.Equal(3, player.Loaded.Count); Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
    }
    [Fact]
    public async Task ShuffleFinishesEachPlaylistWithoutRepeatingBeforeContinuing()
    {
        var first = Source(Entry("A", 1), Entry("A", 2), Entry("A", 3)); var next = Source(Entry("B", 1), Entry("B", 2));
        var player = new ControlledPlayer(); await using var coordinator = new PlaybackCoordinator(player, new Random(1));
        coordinator.SetPlaylistSources([first, next], first.Id, true); coordinator.Shuffle = true;
        await coordinator.LoadAsync(first.Entries[0].Id);
        for (var i = 0; i < 5; i++) player.End();
        Assert.Equal(5, player.Loaded.Distinct().Count()); Assert.Equal(5, player.Loaded.Count);
        Assert.All(player.Loaded.Take(3), id => Assert.Contains(first.Entries, entry => entry.Id == id));
        Assert.All(player.Loaded.Skip(3), id => Assert.Contains(next.Entries, entry => entry.Id == id));
    }
    [Fact]
    public async Task BrokenDecoderCandidatesCanBeSkippedButOutputErrorsStopContinuation()
    {
        var first = Source(Entry("A", 1)); var broken = Source(Entry("broken", 1)); var next = Source(Entry("B", 1));
        var player = new ControlledPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, broken, next], first.Id, true); await coordinator.LoadAsync(first.Entries[0].Id);
        player.FailId = broken.Entries[0].Id; player.FailCategory = AudioErrorCategory.Decoder; player.End();
        Assert.Equal(next.Entries[0].Id, player.Snapshot.EntryId);
        coordinator.SetPlaylistSources([first, broken, next], first.Id, true); await coordinator.LoadAsync(first.Entries[0].Id);
        player.FailCategory = AudioErrorCategory.OutputUnavailable; var count = player.Loaded.Count; player.End();
        Assert.Equal(count, player.Loaded.Count); Assert.Equal(PlaybackState.Error, player.Snapshot.State);
    }
    [Fact]
    public async Task PublicStandaloneSourceNeverInheritsAnApplicationPlaylistPlan()
    {
        var first = Source(Entry("A", 1)); var next = Source(Entry("B", 1)); var standalone = Entry("standalone", 1);
        var player = new ControlledPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, next], first.Id, true);
        coordinator.SetEntries([standalone]); await coordinator.LoadAsync(standalone.Id); player.End();
        Assert.Null(coordinator.SourcePlaylistId); Assert.Single(player.Loaded); Assert.Null(player.Prepared);
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
    }
    [Fact]
    public async Task InFlightNativeTransitionUsesItsActualIdentityAfterTabsWereReordered()
    {
        var first = Source(Entry("A", 1)); var next = Source(Entry("B", 1), Entry("B", 2)); var third = Source(Entry("C", 1));
        var player = new ControlledPlayer(); await using var coordinator = new PlaybackCoordinator(player);
        coordinator.SetPlaylistSources([first, next, third], first.Id, true); await coordinator.LoadAsync(first.Entries[0].Id);
        var inFlight = player.Prepared!.EntryId;
        coordinator.SetPlaylistSources([first, third, next], null, false); Assert.Equal(third.Entries[0].Id, player.Prepared!.EntryId);
        player.Transition(inFlight);
        Assert.Equal(next.Id, coordinator.SourcePlaylistId); Assert.Equal(inFlight, coordinator.ActiveEntry!.Id);
        Assert.Equal(next.Entries[1].Id, player.Prepared!.EntryId);
    }
    private sealed class ControlledPlayer : IAudioPlayer, IAdvancedAudioPlayer
    {
        public PlaybackSnapshot Snapshot { get; private set; } = PlaybackSnapshot.Empty;
        public event Action<PlaybackSnapshot>? SnapshotChanged;
        public List<Guid> Loaded { get; } = [];
        public AudioRequest? Prepared { get; private set; }
        public Guid? FailId { get; set; }
        public AudioErrorCategory FailCategory { get; set; }
        private void Publish(PlaybackSnapshot snapshot) { Snapshot = snapshot with { Revision = Snapshot.Revision + 1 }; SnapshotChanged?.Invoke(Snapshot); }
        public void End() => Publish(Snapshot with { State = PlaybackState.Stopped, Ended = true, Transitioned = false });
        public void Transition(Guid? actual = null) => Publish(Snapshot with { Generation = Snapshot.Generation + 1, EntryId = actual ?? Prepared!.EntryId, Transitioned = true });
        public Task<bool> LoadAsync(AudioRequest request, bool autoPlay, CancellationToken cancellationToken = default)
        {
            var error = request.EntryId == FailId ? new AudioError(FailCategory, "test", "owned test failure") : null;
            Publish(Snapshot with { Generation = Snapshot.Generation + 1, EntryId = request.EntryId, State = error is not null ? PlaybackState.Error : autoPlay ? PlaybackState.Playing : PlaybackState.Stopped,
                Duration = TimeSpan.FromSeconds(3), Position = TimeSpan.Zero, Ended = false, Transitioned = false, Error = error });
            if (error is null) Loaded.Add(request.EntryId); return Task.FromResult(error is null);
        }
        public Task<bool> PrepareNextAsync(AudioRequest? request, bool repeatOne) { Prepared = request; return Task.FromResult(true); }
        public Task<bool> StopAsync() { Publish(Snapshot with { Generation = Snapshot.Generation + 1, State = PlaybackState.Stopped, Ended = false }); return Task.FromResult(true); }
        public Task<bool> PlayAsync() => Task.FromResult(true);
        public Task<bool> PauseAsync() => Task.FromResult(true);
        public Task<bool> SeekAsync(TimeSpan position) => Task.FromResult(true);
        public Task<bool> SetVolumeAsync(double volume, bool muted) => Task.FromResult(true);
        public Task<AudioDevice[]> GetDevicesAsync() => Task.FromResult(Array.Empty<AudioDevice>());
        public Task<bool> SetProcessingAsync(AudioProcessingSettings settings) => Task.FromResult(true);
        public Task<bool> SetOutputAsync(AudioOutputSettings settings) => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
