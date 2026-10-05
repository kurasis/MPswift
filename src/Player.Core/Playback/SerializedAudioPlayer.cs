using System.Collections.Concurrent;

namespace Player.Core.Playback;

/// <summary>Bounded command queue, dedicated native-context thread, latest-load/seek wins.</summary>
public sealed class SerializedAudioPlayer : IAudioPlayer
{
    private readonly BlockingCollection<Action> _commands = new(128);
    private readonly object _gate = new();
    private readonly Func<IAudioBackend> _factory;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Empty;
    private IAudioBackend? _backend;
    private AudioRequest? _request;
    private Guid? _loadedEntry;
    private long _loadedGeneration;
    private AudioSourceInfo? _info;
    private long _generation;
    private long _revision;
    private bool _closing;
    private bool _seekQueued;
    private PendingSeek? _pendingSeek;
    private PendingLoad? _pendingLoad;
    private bool _loadQueued;
    private int _overflow;

    public SerializedAudioPlayer(Func<IAudioBackend> factory)
    {
        _factory = factory;
        new Thread(Run) { IsBackground = true, Name = "Player audio engine" }.Start();
    }

    public PlaybackSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public event Action<PlaybackSnapshot>? SnapshotChanged;

    public Task<bool> LoadAsync(AudioRequest request, bool autoPlay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EntryId == Guid.Empty) throw new ArgumentException("Entry identity is required.", nameof(request));
        long generation;
        lock (_gate)
        {
            if (_closing) return Task.FromResult(false);
            generation = ++_generation;
            _request = request;
        }
        Publish(Snapshot with { Generation = generation, State = PlaybackState.Loading, EntryId = request.EntryId,
            Position = TimeSpan.Zero, Duration = null, SourceFormat = null, OutputFormat = null, CanSeek = false, Error = null, Ended = false });
        var completion = NewCompletion();
        lock (_gate)
        {
            if (_closing || generation != _generation) return Task.FromResult(false);
            _pendingLoad?.Completion.TrySetResult(false);
            _pendingLoad = new PendingLoad(generation, request, autoPlay, cancellationToken, completion);
            if (!_loadQueued)
            {
                _loadQueued = true;
                if (!_commands.TryAdd(ProcessLoad))
                {
                    _loadQueued = false;
                    _pendingLoad = null;
                    Interlocked.Exchange(ref _overflow, 1);
                    completion.TrySetResult(false);
                }
            }
        }
        return completion.Task;
    }

    private void ProcessLoad()
    {
        PendingLoad? load;
        lock (_gate) { load = _pendingLoad; _pendingLoad = null; _loadQueued = false; }
        if (load is null) return;
        var success = false;
        try
        {
            if (IsCurrent(load.Generation))
            {
                load.Token.ThrowIfCancellationRequested();
                success = Prepare(load.Request, load.Generation, load.Token);
                if (success && load.AutoPlay) Start(load.Generation);
                success &= IsCurrent(load.Generation);
            }
        }
        catch (OperationCanceledException)
        {
            success = false;
            if (IsCurrent(load.Generation))
            {
                try
                {
                    // A request canceled before Open must also silence the previously playing source.
                    _backend?.CloseSource();
                    _loadedEntry = null;
                    _info = null;
                    Publish(Snapshot with { Generation = load.Generation, State = PlaybackState.Stopped, CanSeek = false });
                }
                catch (Exception error) { Fail(error, load.Generation); }
            }
        }
        catch (Exception error) { success = false; Fail(error, load.Generation); }
        load.Completion.TrySetResult(success);
    }

    public Task<bool> PlayAsync()
    {
        var generation = CurrentGeneration();
        return Execute(generation, () =>
        {
            AudioRequest? request;
            lock (_gate) request = _request;
            if (request is null) return false;
            if (_loadedEntry != request.EntryId && !Prepare(request, generation, default)) return false;
            Start(generation);
            return IsCurrent(generation);
        });
    }

    public Task<bool> PauseAsync()
    {
        var generation = CurrentGeneration();
        return Execute(generation, () =>
        {
            if (_backend is null || _loadedEntry is null) return false;
            _backend.Pause();
            var position = _backend.ReadPosition();
            Publish(Snapshot with { Generation = generation, State = PlaybackState.Paused, Position = position.Position, Ended = false });
            return true;
        });
    }

    public Task<bool> StopAsync()
    {
        long generation;
        lock (_gate) { if (_closing) return Task.FromResult(false); generation = ++_generation; }
        Publish(Snapshot with { Generation = generation, State = Snapshot.EntryId is null ? PlaybackState.Empty : PlaybackState.Stopped,
            Position = TimeSpan.Zero, Error = null, Ended = false });
        return Execute(generation, () =>
        {
            _backend?.Stop();
            _loadedGeneration = generation;
            Publish(Snapshot with { Generation = generation, Position = TimeSpan.Zero });
            return true;
        });
    }

    public Task<bool> SetVolumeAsync(double volume, bool muted)
    {
        if (!double.IsFinite(volume)) throw new ArgumentOutOfRangeException(nameof(volume));
        volume = Math.Clamp(volume, 0, 1);
        // Volume belongs to the app, independently of a load generation.
        return Queue(() =>
        {
            _backend?.SetVolume(volume, muted);
            Publish(Snapshot with { Volume = volume, Muted = muted });
            return true;
        });
    }

    public Task<bool> SeekAsync(TimeSpan position)
    {
        var completion = NewCompletion();
        lock (_gate)
        {
            if (_closing) return Task.FromResult(false);
            _pendingSeek?.Completion.TrySetResult(false);
            _pendingSeek = new PendingSeek(_generation, position, completion);
            if (_seekQueued) return completion.Task;
            _seekQueued = true;
            if (!_commands.TryAdd(ProcessSeek))
            {
                _seekQueued = false;
                _pendingSeek = null;
                completion.TrySetResult(false);
            }
        }
        return completion.Task;
    }

    private void ProcessSeek()
    {
        PendingSeek? seek;
        lock (_gate) { seek = _pendingSeek; _pendingSeek = null; _seekQueued = false; }
        if (seek is null) return;
        var result = false;
        try
        {
            if (IsCurrent(seek.Generation) && Snapshot.CanSeek && _loadedEntry == Snapshot.EntryId && _backend is not null && _info is { Duration: { } duration, CanSeek: true })
            {
                var state = Snapshot.State;
                var target = seek.Position < TimeSpan.Zero ? TimeSpan.Zero : seek.Position > duration ? duration : seek.Position;
                _backend.Seek(target);
                Publish(Snapshot with { Generation = seek.Generation, Position = target, State = state, Ended = false });
                result = IsCurrent(seek.Generation);
            }
        }
        catch (Exception error) { Fail(error, seek.Generation); }
        seek.Completion.TrySetResult(result);
    }

    private bool Prepare(AudioRequest request, long generation, CancellationToken cancellationToken)
    {
        _backend ??= _factory();
        _backend.CloseSource();
        _loadedEntry = null;
        _info = null;
        var info = _backend.Open(request.Path);
        if (!IsCurrent(generation) || cancellationToken.IsCancellationRequested)
        {
            _backend.CloseSource();
            if (IsCurrent(generation)) Publish(Snapshot with { Generation = generation, State = PlaybackState.Stopped, CanSeek = false });
            return false;
        }
        _info = info;
        _loadedEntry = request.EntryId;
        _loadedGeneration = generation;
        _backend.SetVolume(Snapshot.Volume, Snapshot.Muted);
        Publish(Snapshot with { Generation = generation, State = PlaybackState.Stopped, EntryId = request.EntryId,
            Duration = info.Duration, SourceFormat = info.Format, CanSeek = info.CanSeek && info.Duration > TimeSpan.Zero,
            Position = TimeSpan.Zero, Error = null, Ended = false });
        return true;
    }

    private void Start(long generation)
    {
        if (!IsCurrent(generation)) return;
        _backend!.Play();
        if (!IsCurrent(generation)) { _backend.Stop(); return; }
        var position = _backend.ReadPosition();
        _loadedGeneration = generation;
        Publish(Snapshot with { Generation = generation, State = PlaybackState.Playing, Position = position.Position,
            OutputFormat = position.OutputFormat, Error = null, Ended = false });
    }

    private Task<bool> Execute(long generation, Func<bool> action) => Queue(() => IsCurrent(generation) && action(), generation);
    private Task<bool> Queue(Func<bool> action, long? generation = null)
    {
        var completion = NewCompletion();
        lock (_gate)
        {
            if (_closing) return Task.FromResult(false);
            if (!_commands.TryAdd(() =>
            {
                try { completion.TrySetResult(action()); }
                catch (OperationCanceledException)
                {
                    var current = generation ?? CurrentGeneration();
                    if (IsCurrent(current)) Publish(Snapshot with { Generation = current, State = PlaybackState.Stopped });
                    completion.TrySetResult(false);
                }
                catch (Exception error) { Fail(error, generation ?? CurrentGeneration()); completion.TrySetResult(false); }
            })) { Interlocked.Exchange(ref _overflow, 1); completion.TrySetResult(false); }
        }
        return completion.Task;
    }

    private void Run()
    {
        Exception? failure = null;
        try
        {
            while (!_commands.IsCompleted)
            {
                if (Interlocked.Exchange(ref _overflow, 0) != 0)
                    Fail(new AudioBackendException(AudioErrorCategory.Busy, "The engine command queue is full. Retry the operation."), CurrentGeneration());
                if (_commands.TryTake(out var work, Snapshot.State == PlaybackState.Playing ? 100 : Timeout.Infinite)) work();
                if (Snapshot.State != PlaybackState.Playing || _backend is null || !IsCurrent(_loadedGeneration)) continue;
                var generation = _loadedGeneration;
                try
                {
                    var position = _backend.ReadPosition();
                    Publish(Snapshot with { Generation = generation, Position = position.Position, OutputFormat = position.OutputFormat,
                        State = position.Ended ? PlaybackState.Stopped : PlaybackState.Playing, Ended = position.Ended });
                }
                catch (Exception error) { Fail(error, generation); }
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { _backend?.Dispose(); }
            catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
            _commands.Dispose();
            if (failure is null) _exited.TrySetResult();
            else _exited.TrySetException(failure);
        }
    }

    private void Fail(Exception error, long generation)
    {
        if (!IsCurrent(generation)) return;
        var native = error as AudioBackendException;
        var category = native?.Category ?? AudioErrorCategory.Unexpected;
        var detail = error.Message;
        try { _backend?.Pause(); }
        catch (Exception pauseError) { detail += " Output quiescence failed: " + pauseError.Message; }
        Publish(Snapshot with { Generation = generation,
            State = category == AudioErrorCategory.OutputUnavailable ? PlaybackState.DeviceUnavailable : PlaybackState.Error,
            Error = new AudioError(category, "Error" + category, detail, native?.NativeCode), Ended = false });
    }

    private bool IsCurrent(long generation) { lock (_gate) return !_closing && generation == _generation; }
    private long CurrentGeneration() { lock (_gate) return _generation; }
    private void Publish(PlaybackSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_closing || snapshot.Generation != _generation) return;
            snapshot = snapshot with { Revision = ++_revision };
            Volatile.Write(ref _snapshot, snapshot);
        }
        SnapshotChanged?.Invoke(snapshot);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_closing)
            {
                _closing = true;
                ++_generation;
                _pendingSeek?.Completion.TrySetResult(false);
                _pendingSeek = null;
                _pendingLoad?.Completion.TrySetResult(false);
                _pendingLoad = null;
                _commands.CompleteAdding();
            }
        }
        return new ValueTask(_exited.Task);
    }

    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed record PendingSeek(long Generation, TimeSpan Position, TaskCompletionSource<bool> Completion);
    private sealed record PendingLoad(long Generation, AudioRequest Request, bool AutoPlay, CancellationToken Token, TaskCompletionSource<bool> Completion);
}
