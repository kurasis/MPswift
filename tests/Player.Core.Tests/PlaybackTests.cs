using System.Collections.Concurrent;
using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class PlaybackTests
{
    [Fact]
    public async Task SlowSupersededLoadNeverStartsAndLatestTrackWins()
    {
        var backend = new TestBackend { BlockPath = "slow" };
        await using var player = new SerializedAudioPlayer(() => backend);
        var slow = player.LoadAsync(Request("slow"), true);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var final = Request("latest");
        var latest = player.LoadAsync(final, false);
        backend.Release.Set();
        Assert.False(await slow);
        Assert.True(await latest);
        Assert.Equal(final.EntryId, player.Snapshot.EntryId);
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
        Assert.Empty(backend.PlayedPaths);
        Assert.Equal(new[] { "slow", "latest" }, backend.OpenedPaths);
    }

    [Fact]
    public async Task RapidLoadsAreCoalescedAndStopCancelsPendingAutoplay()
    {
        var backend = new TestBackend { BlockPath = "slow" };
        await using var player = new SerializedAudioPlayer(() => backend);
        var first = player.LoadAsync(Request("slow"), true);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var tasks = Enumerable.Range(0, 300).Select(i => player.LoadAsync(Request("track" + i), true)).ToArray();
        var stop = player.StopAsync();
        backend.Release.Set();
        await Task.WhenAll(tasks.Append(first).Append(stop));
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
        Assert.Equal(TimeSpan.Zero, player.Snapshot.Position);
        Assert.Empty(backend.PlayedPaths);
        Assert.Single(backend.OpenedPaths);
    }

    [Fact]
    public async Task CanceledCurrentLoadReleasesSourceAndLeavesNoLoadingState()
    {
        var backend = new TestBackend { BlockPath = "slow" };
        await using var player = new SerializedAudioPlayer(() => backend);
        using var cancel = new CancellationTokenSource();
        var load = player.LoadAsync(Request("slow"), true, cancel.Token);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel(); backend.Release.Set();
        Assert.False(await load);
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
        Assert.Null(backend.Path);
        Assert.Empty(backend.PlayedPaths);
    }

    [Fact]
    public async Task PauseResumeSeekAndStopPreserveEntryAndUseOneOwnerThread()
    {
        var backend = new TestBackend();
        await using var player = new SerializedAudioPlayer(() => backend);
        var request = Request("track");
        Assert.True(await player.LoadAsync(request, true));
        Assert.True(await player.SeekAsync(TimeSpan.FromSeconds(4)));
        Assert.True(await player.PauseAsync());
        Assert.Equal(PlaybackState.Paused, player.Snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(4), player.Snapshot.Position);
        Assert.True(await player.PlayAsync());
        Assert.True(await player.StopAsync());
        Assert.Equal(request.EntryId, player.Snapshot.EntryId);
        Assert.Equal(TimeSpan.Zero, player.Snapshot.Position);
        Assert.Single(backend.OpenedPaths);
        Assert.Single(backend.Threads.Distinct());
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(100, 10)]
    public async Task SeekIsClampedAndKeepsPausedState(int requested, int expected)
    {
        var backend = new TestBackend();
        await using var player = new SerializedAudioPlayer(() => backend);
        await player.LoadAsync(Request("track"), true);
        await player.PauseAsync();
        Assert.True(await player.SeekAsync(TimeSpan.FromSeconds(requested)));
        Assert.Equal(TimeSpan.FromSeconds(expected), player.Snapshot.Position);
        Assert.Equal(PlaybackState.Paused, player.Snapshot.State);
    }

    [Fact]
    public async Task UnknownDurationAndZeroDurationDisableSeek()
    {
        var backend = new TestBackend { Duration = null };
        await using var player = new SerializedAudioPlayer(() => backend);
        await player.LoadAsync(Request("unknown"), false);
        Assert.False(player.Snapshot.CanSeek);
        Assert.False(await player.SeekAsync(TimeSpan.FromSeconds(5)));
        backend.Duration = TimeSpan.Zero;
        await player.LoadAsync(Request("zero"), false);
        Assert.False(player.Snapshot.CanSeek);
        Assert.False(await player.SeekAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task VolumeAndMuteAreIndependentAndClamped()
    {
        var backend = new TestBackend();
        await using var player = new SerializedAudioPlayer(() => backend);
        await player.SetVolumeAsync(0.25, true);
        await player.LoadAsync(Request("track"), false);
        Assert.Equal((0.25, true), backend.Gain);
        await player.SetVolumeAsync(0.25, false);
        Assert.Equal((0.25, false), backend.Gain);
        await player.SetVolumeAsync(2, false);
        Assert.Equal(1, player.Snapshot.Volume);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await player.SetVolumeAsync(double.NaN, false));
    }

    [Fact]
    public async Task OutputFailureKeepsLoadedTrackForRetryAndDoesNotShowPlaying()
    {
        var backend = new TestBackend { FailOutput = true };
        await using var player = new SerializedAudioPlayer(() => backend);
        var request = Request("track");
        Assert.False(await player.LoadAsync(request, true));
        Assert.Equal(PlaybackState.DeviceUnavailable, player.Snapshot.State);
        Assert.Equal(AudioErrorCategory.OutputUnavailable, player.Snapshot.Error!.Category);
        Assert.Equal(request.EntryId, player.Snapshot.EntryId);
        backend.FailOutput = false;
        Assert.True(await player.PlayAsync());
        Assert.Single(backend.OpenedPaths);
    }

    [Fact]
    public async Task DisposeWaitsForInFlightOpenAndReleasesResourcesOnOwnerThread()
    {
        var backend = new TestBackend { BlockPath = "slow" };
        var player = new SerializedAudioPlayer(() => backend);
        var load = player.LoadAsync(Request("slow"), true);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dispose = player.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        backend.Release.Set();
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await load);
        Assert.True(backend.Disposed);
        Assert.Empty(backend.PlayedPaths);
        Assert.Single(backend.Threads.Distinct());
        Assert.False(await player.PlayAsync());
    }

    [Fact]
    public async Task ExplicitBadFileDoesNotSilentlyPlayAnotherEntry()
    {
        var backend = new TestBackend();
        var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var bad = Entry("bad"); var good = Entry("good");
        coordinator.SetEntries([bad, good]);
        Assert.False(await coordinator.LoadAsync(bad.Id));
        Assert.Equal(AudioErrorCategory.Decoder, player.Snapshot.Error!.Category);
        Assert.Empty(backend.PlayedPaths);
    }

    [Fact]
    public async Task NextSkipsBadDisabledMissingEntriesAndTerminates()
    {
        var backend = new TestBackend();
        var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var first = Entry("first"); var bad = Entry("bad"); var disabled = Entry("disabled") with { Enabled = false };
        var missing = Entry("missing") with { Track = Entry("missing").Track with { Available = false } };
        var last = Entry("last");
        coordinator.SetEntries([first, bad, disabled, missing, last]);
        await coordinator.LoadAsync(first.Id);
        Assert.True(await coordinator.NextAsync());
        Assert.Equal(new[] { "first", "last" }, backend.PlayedPaths);
        Assert.False(await coordinator.NextAsync());
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
        coordinator.SetEntries([disabled, missing]);
        Assert.False(await coordinator.NextAsync());
    }

    [Fact]
    public async Task PreviousUsesActuallyPlayedHistoryAndThreeSecondRestartRule()
    {
        var backend = new TestBackend();
        var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var entries = new[] { Entry("first"), Entry("never-played"), Entry("last") };
        coordinator.SetEntries(entries);
        await coordinator.LoadAsync(entries[0].Id);
        await coordinator.LoadAsync(entries[2].Id);
        await coordinator.SeekAsync(TimeSpan.FromSeconds(4));
        await coordinator.PreviousAsync();
        Assert.Equal(entries[2].Id, player.Snapshot.EntryId);
        await coordinator.PreviousAsync();
        Assert.Equal(entries[0].Id, player.Snapshot.EntryId);
        Assert.DoesNotContain("never-played", backend.PlayedPaths);
    }

    [Fact]
    public async Task RemovingPlayingEntryRetainsPlaybackAndNextUsesRemainingSource()
    {
        var backend = new TestBackend();
        var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var first = Entry("first"); var second = Entry("second"); var last = Entry("last");
        coordinator.SetEntries([first, second, last]);
        await coordinator.LoadAsync(second.Id);
        coordinator.SetEntries([first, last]);
        Assert.Equal(PlaybackState.Playing, player.Snapshot.State);
        await coordinator.NextAsync();
        Assert.Equal(last.Id, player.Snapshot.EntryId);
    }

    [Theory]
    [InlineData("пЕСНЯ")]
    [InlineData("Café")]
    [InlineData("e\u0301")]
    [InlineData("%_")]
    public void SearchIsUnicodeNormalizedCaseInsensitiveAndLiteral(string query) =>
        Assert.True(PlaylistSearch.Matches(new MediaTrack(Guid.NewGuid(), "C:/Музыка/file.wav", "Песня Café %_"), query));

    [Fact]
    public async Task PreparedThenPlayedEntryIsRecordedInActuallyStartedHistory()
    {
        var backend = new TestBackend();
        var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var first = Entry("prepared"); var last = Entry("last");
        coordinator.SetEntries([first, last]);
        await coordinator.LoadAsync(first.Id, false);
        Assert.Empty(backend.PlayedPaths);
        await coordinator.PlayAsync();
        await coordinator.LoadAsync(last.Id);
        Assert.True(await coordinator.PreviousAsync());
        Assert.Equal(first.Id, player.Snapshot.EntryId);
    }

    [Fact]
    public async Task NaturalEndAdvancesFullSourceAndExhaustionStopsWithoutLooping()
    {
        var backend = new TestBackend();
        var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var first = Entry("first"); var disabled = Entry("disabled") with { Enabled = false }; var last = Entry("last");
        coordinator.SetEntries([first, disabled, last]);
        var startedLast = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exhausted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long lastGeneration = -1;
        player.SnapshotChanged += snapshot =>
        {
            if (snapshot.EntryId == last.Id && snapshot.State == PlaybackState.Playing)
            { Volatile.Write(ref lastGeneration, snapshot.Generation); startedLast.TrySetResult(); }
            var playedGeneration = Volatile.Read(ref lastGeneration);
            if (playedGeneration >= 0 && snapshot.Generation > playedGeneration && snapshot.EntryId == last.Id && snapshot.State == PlaybackState.Stopped && !snapshot.Ended)
                exhausted.TrySetResult();
        };
        await coordinator.LoadAsync(first.Id);
        backend.Ended = true;
        await startedLast.Task.WaitAsync(TimeSpan.FromSeconds(5));
        backend.Ended = true;
        await exhausted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "first", "last" }, backend.PlayedPaths);
        Assert.Equal(PlaybackState.Stopped, player.Snapshot.State);
        Assert.Equal(TimeSpan.Zero, player.Snapshot.Position);
    }

    [Fact]
    public async Task PlayUsesNewSelectionWhenStoppedButResumesPausedSource()
    {
        var backend = new TestBackend();
        var player = new SerializedAudioPlayer(() => backend);
        await using var coordinator = new PlaybackCoordinator(player);
        var first = Entry("first"); var second = Entry("second");
        coordinator.SetEntries([first, second]);
        await coordinator.LoadAsync(first.Id);
        await coordinator.PauseAsync();
        await coordinator.PlayAsync(second.Id);
        Assert.Equal(first.Id, player.Snapshot.EntryId);
        await coordinator.StopAsync();
        await coordinator.PlayAsync(second.Id);
        Assert.Equal(second.Id, player.Snapshot.EntryId);
    }

    private static AudioRequest Request(string path) => new(Guid.NewGuid(), path);
    private static PlaylistEntry Entry(string path) => new(Guid.NewGuid(), new MediaTrack(Guid.NewGuid(), path, path));

    private sealed class TestBackend : IAudioBackend
    {
        public string? BlockPath { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public ConcurrentQueue<string> OpenedPaths { get; } = new();
        public ConcurrentQueue<string> PlayedPaths { get; } = new();
        public ConcurrentQueue<int> Threads { get; } = new();
        public TimeSpan? Duration { get; set; } = TimeSpan.FromSeconds(10);
        public bool FailOutput { get; set; }
        public bool Disposed { get; private set; }
        public string? Path { get; private set; }
        public (double Volume, bool Muted) Gain { get; private set; }
        private TimeSpan _position;
        private int _ended;
        public bool Ended { get => Volatile.Read(ref _ended) != 0; set => Volatile.Write(ref _ended, value ? 1 : 0); }
        private void Call() => Threads.Enqueue(Environment.CurrentManagedThreadId);
        public AudioSourceInfo Open(string path)
        {
            Call(); OpenedPaths.Enqueue(path);
            if (path == BlockPath) { Entered.TrySetResult(); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test release gate timed out."); }
            if (path == "bad") throw new AudioBackendException(AudioErrorCategory.Decoder, "Test decode failed.");
            Path = path;
            Ended = false;
            return new AudioSourceInfo(Duration, new AudioFormatInfo(48000, 2, "test"), true);
        }
        public void Play() { Call(); if (FailOutput) throw new AudioBackendException(AudioErrorCategory.OutputUnavailable, "Test device unavailable."); PlayedPaths.Enqueue(Path!); }
        public void Pause() => Call();
        public void Stop() { Call(); _position = TimeSpan.Zero; }
        public void Seek(TimeSpan position) { Call(); _position = position; }
        public void SetVolume(double volume, bool muted) { Call(); Gain = (volume, muted); }
        public BackendPosition ReadPosition() { Call(); return new BackendPosition(_position, Ended); }
        public void CloseSource() { Call(); Path = null; _position = TimeSpan.Zero; }
        public void Dispose() { Call(); Disposed = true; Path = null; }
    }
}
