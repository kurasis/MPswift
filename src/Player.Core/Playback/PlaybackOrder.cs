namespace Player.Core.Playback;

public enum RepeatMode { Off, All, One }
public sealed record QueueItem(Guid Id, PlaylistEntry Entry, Guid? OriginEntryId);
public sealed record PlaybackOrderState(RepeatMode Repeat, bool Shuffle, QueueItem[] Queue, Guid[] Remaining,
    PlaylistEntry[] History, int HistoryIndex, Guid? PlaylistCursorId, int RemovedCursorIndex = 0);

/// <summary>Bounded source order, snapshot queue and actually-started navigation history.</summary>
public sealed class PlaybackOrder
{
    private readonly Random _random;
    private PlaylistEntry[] _source = [];
    private readonly List<QueueItem> _queue = [];
    private readonly List<Guid> _remaining = [];
    private readonly List<PlaylistEntry> _history = [];
    private int _historyIndex = -1;
    private Guid? _cursor;
    private int _removed;
    private bool _shuffle;
    public RepeatMode Repeat { get; set; }
    public bool Shuffle { get => _shuffle; set { if (_shuffle == value) return; _shuffle = value; Refill(_cursor); if (_cursor is { } current) _remaining.Remove(current); } }
    public QueueItem[] Queue => _queue.ToArray();
    public PlaybackOrder(Random? random = null) => _random = random ?? Random.Shared;
    public void SetSource(PlaylistEntry[] entries)
    {
        var index = Array.FindIndex(_source, e => e.Id == _cursor);
        if (index >= 0 && entries.All(e => e.Id != _cursor)) _removed = Math.Min(index, entries.Length);
        var previous = _source.Select(e => e.Id).ToHashSet();
        _source = entries;
        _remaining.RemoveAll(id => !Eligible().Any(e => e.Id == id));
        if (_shuffle)
            foreach (var entry in Eligible().Where(e => !previous.Contains(e.Id) && e.Id != _cursor))
                _remaining.Insert(_random.Next(_remaining.Count + 1), entry.Id);
    }
    public void Enqueue(IEnumerable<PlaylistEntry> entries, bool next)
    {
        var array = entries.Take(10001).Select(e => { var id = Guid.NewGuid(); return new QueueItem(id, e with { Id = id }, e.Id); }).ToArray();
        if (_queue.Count + array.Length > 10000) throw new InvalidOperationException("Queue limit is 10,000 items.");
        if (next) _queue.InsertRange(0, array); else _queue.AddRange(array);
    }
    public void Remove(Guid id) => _queue.RemoveAll(q => q.Id == id);
    public void Clear() => _queue.Clear();
    public void Move(Guid id, int delta)
    { var i = _queue.FindIndex(q => q.Id == id); if (i < 0) return; var item = _queue[i]; _queue.RemoveAt(i); _queue.Insert(Math.Clamp(i + delta, 0, _queue.Count), item); }
    public bool IsDetached(QueueItem item) => item.OriginEntryId is { } id && _source.All(e => e.Id != id);
    public PlaylistEntry[] Candidates(PlaylistEntry? active, bool natural)
    {
        var queued = _queue.Select(q => q.Entry).ToArray();
        if (natural && Repeat == RepeatMode.One && active is not null && (_source.Any(e => e.Id == active.Id) || _source.Length > 0 && _cursor != active.Id)) return queued.Concat([active]).ToArray();
        PlaylistEntry[] following;
        if (_shuffle)
        {
            if (_remaining.Count == 0 && Repeat == RepeatMode.All) Refill(active?.Id);
            following = _remaining.Select(id => _source.FirstOrDefault(e => e.Id == id)).OfType<PlaylistEntry>().ToArray();
        }
        else
        {
            var i = Array.FindIndex(_source, e => e.Id == _cursor);
            var start = _cursor is null ? 0 : i >= 0 ? i + 1 : _removed;
            following = _source.Skip(start).Where(IsEligible).ToArray();
            if (Repeat == RepeatMode.All) following = following.Concat(_source.Take(start).Where(IsEligible)).ToArray();
        }
        // A single traversal attempt tries each source at most once, even in Repeat All.
        return queued.Concat(following).ToArray();
    }
    public void Started(PlaylistEntry entry, bool queued, bool remember = true)
    {
        if (queued)
        {
            var i = _queue.FindIndex(q => q.Entry.Id == entry.Id);
            if (i >= 0) _queue.RemoveAt(i);
        }
        else
        {
            _cursor = entry.Id;
            _remaining.Remove(entry.Id);
        }
        if (!remember) return;
        if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(entry); if (_history.Count > 100) _history.RemoveAt(0); _historyIndex = _history.Count - 1;
    }
    public void Failed(PlaylistEntry entry)
    { var i = _queue.FindIndex(q => q.Entry.Id == entry.Id); if (i >= 0) _queue.RemoveAt(i); _remaining.Remove(entry.Id); }
    public PlaylistEntry? Previous(PlaylistEntry? current, TimeSpan position)
    { return position > TimeSpan.FromSeconds(3) || _historyIndex <= 0 ? current : _history[--_historyIndex]; }
    public PlaylistEntry? Forward() => _historyIndex + 1 < _history.Count ? _history[++_historyIndex] : null;
    public void ClearHistory() { _history.Clear(); _historyIndex = -1; }
    public PlaybackOrderState Capture() => new(Repeat, Shuffle, Queue, _remaining.ToArray(), _history.ToArray(), _historyIndex, _cursor, _removed);
    public void Restore(PlaybackOrderState state)
    {
        if (!Enum.IsDefined(state.Repeat) || state.Queue.Length > 10000 || state.History.Length > 100 || state.Remaining.Length > 10000 ||
            state.HistoryIndex < -1 || state.HistoryIndex >= state.History.Length || state.Queue.Select(q => q.Id).Distinct().Count() != state.Queue.Length || state.Remaining.Distinct().Count() != state.Remaining.Length)
            throw new InvalidDataException("Invalid playback order state.");
        Repeat = state.Repeat; _shuffle = state.Shuffle; _queue.Clear(); _queue.AddRange(state.Queue);
        _remaining.Clear(); _remaining.AddRange(state.Remaining.Where(id => _source.Any(e => e.Id == id && IsEligible(e))));
        _history.Clear(); _history.AddRange(state.History); _historyIndex = state.HistoryIndex; _cursor = state.PlaylistCursorId; _removed = Math.Clamp(state.RemovedCursorIndex, 0, _source.Length);
    }
    private IEnumerable<PlaylistEntry> Eligible() => _source.Where(IsEligible);
    private static bool IsEligible(PlaylistEntry e) => e.Enabled && e.Track.Available;
    private void Refill(Guid? exclude)
    {
        _remaining.Clear(); _remaining.AddRange(Eligible().Select(e => e.Id));
        for (var i = _remaining.Count - 1; i > 0; i--) { var j = _random.Next(i + 1); (_remaining[i], _remaining[j]) = (_remaining[j], _remaining[i]); }
        // No immediate repeat when there is another eligible item.
        if (_remaining.Count > 1 && _remaining[0] == exclude) (_remaining[0], _remaining[1]) = (_remaining[1], _remaining[0]);
        if (Repeat != RepeatMode.All && exclude is { } id) _remaining.Remove(id);
    }
}
