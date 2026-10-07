namespace Player.Core.Playback;

/// <summary>Source order, snapshot queue, repeat/shuffle and actual-playback history; search is never a source.</summary>
public sealed class PlaybackCoordinator : IAsyncDisposable
{
    private readonly IAudioPlayer _player;
    private readonly object _gate = new();
    private PlaylistEntry[] _entries = [];
    private readonly PlaybackOrder _order;
    private PlaylistEntry? _cursor;
    private long _intent;
    private long _handledEnd = -1;
    private bool _disposed;
    private Task _advance = Task.CompletedTask;
    private PlaylistEntry? _preparedEntry;
    private bool _loading;

    public PlaybackCoordinator(IAudioPlayer player, Random? random = null) { _player = player; _order = new(random); _player.SnapshotChanged += OnSnapshot; }
    public event Action? OrderChanged;
    public RepeatMode Repeat { get { lock (_gate) return _order.Repeat; } set { lock (_gate) _order.Repeat = value; Changed(); } }
    public bool Shuffle { get { lock (_gate) return _order.Shuffle; } set { lock (_gate) _order.Shuffle = value; Changed(); } }
    public QueueItem[] Queue { get { lock (_gate) return _order.Queue; } }
    public void Enqueue(IEnumerable<PlaylistEntry> entries, bool next) { lock (_gate) _order.Enqueue(entries, next); Changed(); }
    public void RemoveQueued(Guid id) { lock (_gate) _order.Remove(id); Changed(); }
    public void MoveQueued(Guid id, int delta) { lock (_gate) _order.Move(id, delta); Changed(); }
    public void ClearQueue() { lock (_gate) _order.Clear(); Changed(); }
    public void RelinkSnapshots(Guid trackId, string path)
    {
        lock (_gate) { _order.Relink(trackId, path); if (_cursor?.Track.Id == trackId) _cursor = _cursor with { Track = _cursor.Track with { Path = path, Available = true } }; _preparedEntry = null; }
        Changed();
    }
    public void ClearHistory() { lock (_gate) _order.ClearHistory(); Changed(); }
    public PlaybackOrderState CaptureOrder() { lock (_gate) return _order.Capture(); }
    public void RestoreOrder(PlaybackOrderState state) { lock (_gate) _order.Restore(state); Changed(); }
    private void Changed() { OrderChanged?.Invoke(); RefreshPrepared(); }
    private void RefreshPrepared()
    {
        if (_player is not IAdvancedAudioPlayer advanced) return;
        lock (_gate)
        {
            if (_disposed || _loading || _player.Snapshot.State is not (PlaybackState.Playing or PlaybackState.Paused)) return;
            var next = _order.Candidates(_cursor, true).FirstOrDefault();
            if (next == _preparedEntry) return;
            _preparedEntry = next;
            _ = advanced.PrepareNextAsync(next is null ? null : new(next.Id, next.Track.Path, next.Track.Segment, next.Track.ReplayGain), _order.Repeat == RepeatMode.One && next?.Id == _cursor?.Id);
        }
    }
    public PlaylistEntry? ActiveEntry { get { lock (_gate) return _cursor; } }
    public void SetEntries(IEnumerable<PlaylistEntry> entries)
    {
        var array = entries.ToArray();
        if (array.Select(e => e.Id).Distinct().Count() != array.Length) throw new ArgumentException("Entry IDs must be unique.");
        lock (_gate)
        {
            _entries = array;
            if (_cursor is { } active && array.FirstOrDefault(e => e.Id == active.Id) is { } updated) _cursor = updated;
            _order.SetSource(array);
            _preparedEntry = null;
        }
        RefreshPrepared();
    }

    public Task<bool> LoadAsync(Guid entryId, bool autoPlay = true)
    {
        PlaylistEntry? entry;
        long intent;
        lock (_gate) { if (_disposed) return Task.FromResult(false); intent = ++_intent; entry = _entries.FirstOrDefault(e => e.Id == entryId); if (entry is not null) _order.Started(entry, false, false); }
        return entry is null ? Task.FromResult(false) : LoadEntryAsync(entry, intent, autoPlay, true);
    }

    public async Task<bool> PlayAsync(Guid? selected = null)
    {
        Task<bool> play;
        PlaylistEntry? active;
        long intent;
        lock (_gate)
        {
            if (_disposed) return false;
            var snapshot = _player.Snapshot;
            if (snapshot.EntryId is not null &&
                (snapshot.State is PlaybackState.Paused or PlaybackState.DeviceUnavailable ||
                 snapshot.State == PlaybackState.Stopped && (selected is null || selected == snapshot.EntryId)))
            {
                intent = ++_intent;
                active = _cursor;
                play = _player.PlayAsync();
            }
            else
            {
                var entry = _entries.FirstOrDefault(e => e.Id == selected) ?? _entries.FirstOrDefault(e => e.Enabled && e.Track.Available);
                play = entry is null ? Task.FromResult(false) : LoadAsync(entry.Id);
                active = null;
                intent = _intent;
            }
        }
        var success = await play.ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            if (success && active is not null && !(_order.Capture().History.LastOrDefault()?.Id == active.Id)) _order.Started(active, false);
        }
        return success;
    }

    public async Task<bool> RestoreAsync(PlaylistEntry entry, TimeSpan position)
    {
        long intent;
        lock (_gate) { if (_disposed) return false; intent = ++_intent; }
        if (!await LoadEntryAsync(entry, intent, false, false).ConfigureAwait(false)) return false;
        Task<bool>? seek;
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            seek = _player.Snapshot.CanSeek ? _player.SeekAsync(position) : null;
        }
        if (seek is not null && !await seek.ConfigureAwait(false)) return false;
        lock (_gate) return !_disposed && intent == _intent;
    }

    public Task<bool> PauseAsync() => _player.PauseAsync();
    public async Task<bool> SeekAsync(TimeSpan position) { var result = await _player.SeekAsync(position).ConfigureAwait(false); lock (_gate) _preparedEntry = null; RefreshPrepared(); return result; }
    public Task<bool> SetVolumeAsync(double volume, bool muted) => _player.SetVolumeAsync(volume, muted);
    public Task<bool> StopAsync() { lock (_gate) { ++_intent; _preparedEntry = null; _loading = false; return _player.StopAsync(); } }
    public Task<bool> NextAsync() { long intent; lock (_gate) intent = ++_intent; return AdvanceAsync(intent); }

    public async Task<bool> PreviousAsync()
    {
        long intent;
        PlaylistEntry? entry;
        lock (_gate)
        {
            intent = ++_intent;
            entry = _order.Previous(_cursor, _player.Snapshot.Position);
        }
        return entry is not null && await LoadEntryAsync(entry, intent, true, false).ConfigureAwait(false);
    }

    private async Task<bool> LoadEntryAsync(PlaylistEntry entry, long intent, bool autoPlay, bool remember)
    {
        Task<bool> load;
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            _cursor = entry; _loading = true;
            load = _player.LoadAsync(new AudioRequest(entry.Id, entry.Track.Path, entry.Track.Segment, entry.Track.ReplayGain), autoPlay);
        }
        var success = await load.ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            _loading = false;
            if (success && autoPlay && remember) _order.Started(entry, _order.Queue.Any(q => q.Entry.Id == entry.Id));
        }
        if (success) Changed();
        return success;
    }

    private async Task<bool> AdvanceAsync(long intent, bool natural = false)
    {
        PlaylistEntry[] candidates;
        bool fromHistory;
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            var forward = natural || _order.Queue.Length > 0 ? null : _order.Forward();
            fromHistory = forward is not null;
            candidates = forward is null ? _order.Candidates(_cursor, natural) : [forward];
        }
        foreach (var candidate in candidates.DistinctBy(e => e.Id))
        {
            if (await LoadEntryAsync(candidate, intent, true, !fromHistory).ConfigureAwait(false)) { Changed(); return true; }
            lock (_gate) _order.Failed(candidate);
            lock (_gate) if (_disposed || intent != _intent) return false;
            if (_player.Snapshot.Error?.Category is not (AudioErrorCategory.FileUnavailable or AudioErrorCategory.Decoder)) return false;
        }
        lock (_gate) if (_disposed || intent != _intent) return false;
        if (_player.Snapshot.Error is null) await _player.StopAsync().ConfigureAwait(false);
        return false;
    }

    private void OnSnapshot(PlaybackSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (snapshot.State == PlaybackState.Loading) _preparedEntry = null;
            if (snapshot.Transitioned && _preparedEntry is { } transitioned)
            {
                _cursor = transitioned;
                _order.Started(transitioned, _order.Queue.Any(q => q.Entry.Id == transitioned.Id));
                _preparedEntry = null; Changed();
            }
            if (snapshot.State == PlaybackState.Playing) RefreshPrepared();
            if (!snapshot.Ended || snapshot.Generation == _handledEnd || snapshot.Generation != _player.Snapshot.Generation) return;
            _handledEnd = snapshot.Generation;
            _advance = AdvanceAsync(_intent, true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task advance;
        lock (_gate) { _disposed = true; ++_intent; advance = _advance; }
        _player.SnapshotChanged -= OnSnapshot;
        await _player.StopAsync().ConfigureAwait(false);
        await advance.ConfigureAwait(false);
        await _player.DisposeAsync().ConfigureAwait(false);
    }
}
