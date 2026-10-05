namespace Player.Core.Playback;

/// <summary>Stage B sequential source and actual-playback history. Search visibility is never a source.</summary>
public sealed class PlaybackCoordinator : IAsyncDisposable
{
    private readonly IAudioPlayer _player;
    private readonly object _gate = new();
    private PlaylistEntry[] _entries = [];
    private readonly List<PlaylistEntry> _history = [];
    private int _historyIndex = -1;
    private PlaylistEntry? _cursor;
    private int _removedCursorIndex;
    private long _intent;
    private long _handledEnd = -1;
    private bool _disposed;
    private Task _advance = Task.CompletedTask;

    public PlaybackCoordinator(IAudioPlayer player) { _player = player; _player.SnapshotChanged += OnSnapshot; }
    public PlaylistEntry? ActiveEntry { get { lock (_gate) return _cursor; } }
    public void SetEntries(IEnumerable<PlaylistEntry> entries)
    {
        var array = entries.ToArray();
        if (array.Select(e => e.Id).Distinct().Count() != array.Length) throw new ArgumentException("Entry IDs must be unique.");
        lock (_gate)
        {
            var oldIndex = Array.FindIndex(_entries, e => e.Id == _cursor?.Id);
            if (oldIndex >= 0 && array.All(e => e.Id != _cursor?.Id)) _removedCursorIndex = Math.Min(oldIndex, array.Length);
            _entries = array;
        }
    }

    public Task<bool> LoadAsync(Guid entryId, bool autoPlay = true)
    {
        PlaylistEntry? entry;
        long intent;
        lock (_gate) { if (_disposed) return Task.FromResult(false); intent = ++_intent; entry = _entries.FirstOrDefault(e => e.Id == entryId); }
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
            if (success && active is not null && (_historyIndex < 0 || _history[_historyIndex].Id != active.Id)) Remember(active);
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
    public Task<bool> SeekAsync(TimeSpan position) => _player.SeekAsync(position);
    public Task<bool> SetVolumeAsync(double volume, bool muted) => _player.SetVolumeAsync(volume, muted);
    public Task<bool> StopAsync() { lock (_gate) { ++_intent; return _player.StopAsync(); } }
    public Task<bool> NextAsync() { long intent; lock (_gate) intent = ++_intent; return AdvanceAsync(intent); }

    public async Task<bool> PreviousAsync()
    {
        long intent;
        PlaylistEntry? entry;
        lock (_gate)
        {
            intent = ++_intent;
            if (_player.Snapshot.Position > TimeSpan.FromSeconds(3) || _historyIndex <= 0) entry = _cursor;
            else entry = _history[--_historyIndex];
        }
        return entry is not null && await LoadEntryAsync(entry, intent, true, false).ConfigureAwait(false);
    }

    private async Task<bool> LoadEntryAsync(PlaylistEntry entry, long intent, bool autoPlay, bool remember)
    {
        Task<bool> load;
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            _cursor = entry;
            load = _player.LoadAsync(new AudioRequest(entry.Id, entry.Track.Path), autoPlay);
        }
        var success = await load.ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            if (success && autoPlay && remember)
            {
                Remember(entry);
            }
        }
        return success;
    }

    private void Remember(PlaylistEntry entry)
    {
        if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(entry);
        if (_history.Count > 100) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;
    }

    private async Task<bool> AdvanceAsync(long intent)
    {
        PlaylistEntry[] candidates;
        lock (_gate)
        {
            if (_disposed || intent != _intent) return false;
            var index = Array.FindIndex(_entries, e => e.Id == _cursor?.Id);
            var start = _cursor is null ? 0 : index >= 0 ? index + 1 : _removedCursorIndex;
            candidates = _entries.Skip(start).Where(e => e.Enabled && e.Track.Available).ToArray();
        }
        foreach (var candidate in candidates)
        {
            if (await LoadEntryAsync(candidate, intent, true, true).ConfigureAwait(false)) return true;
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
            if (_disposed || !snapshot.Ended || snapshot.Generation == _handledEnd || snapshot.Generation != _player.Snapshot.Generation) return;
            _handledEnd = snapshot.Generation;
            _advance = AdvanceAsync(_intent);
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
