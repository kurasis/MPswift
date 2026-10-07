using Player.Core.Playback;

namespace Player.Core.Tests;

public sealed class CoordinatorShutdownTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task StopFailureStillDisposesPlayerAndRetainsEveryError(bool synchronous, bool disposeFails)
    {
        var player = new FailingPlayer(synchronous, disposeFails);
        var coordinator = new PlaybackCoordinator(player);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => coordinator.DisposeAsync().AsTask());
        var errors = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [error];
        Assert.Equal(disposeFails ? 2 : 1, errors.Length);
        Assert.Same(player.StopFailure, errors[0]);
        if (disposeFails) Assert.Same(player.DisposeFailure, errors[1]);
        Assert.Equal(1, player.DisposeCalls);
        Assert.Equal(0, player.Subscriptions);
    }

    private sealed class FailingPlayer(bool synchronous, bool disposeFails) : IAudioPlayer
    {
        public Exception StopFailure { get; } = new IOException("Owned stop failure.");
        public Exception DisposeFailure { get; } = new IOException("Owned disposal failure.");
        public int DisposeCalls { get; private set; }
        public int Subscriptions { get; private set; }
        public PlaybackSnapshot Snapshot => PlaybackSnapshot.Empty;
        public event Action<PlaybackSnapshot>? SnapshotChanged { add { Subscriptions++; } remove { Subscriptions--; } }
        public Task<bool> StopAsync() => synchronous ? throw StopFailure : Task.FromException<bool>(StopFailure);
        public ValueTask DisposeAsync() { DisposeCalls++; return disposeFails ? ValueTask.FromException(DisposeFailure) : ValueTask.CompletedTask; }
        public Task<bool> LoadAsync(AudioRequest request, bool autoPlay, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> PlayAsync() => Task.FromResult(false);
        public Task<bool> PauseAsync() => Task.FromResult(false);
        public Task<bool> SeekAsync(TimeSpan position) => Task.FromResult(false);
        public Task<bool> SetVolumeAsync(double volume, bool muted) => Task.FromResult(false);
    }
}
