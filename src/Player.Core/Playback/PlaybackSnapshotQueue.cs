namespace Player.Core.Playback;

/// <summary>One scheduled UI drain; only adjacent ordinary playing-position updates can replace one another.</summary>
public sealed class PlaybackSnapshotQueue
{
    private readonly object _gate = new();
    private readonly List<PlaybackSnapshot> _pending = [];
    private bool _scheduled;
    public bool Enqueue(PlaybackSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_pending.Count > 0 && IsPositionUpdate(_pending[^1], snapshot)) _pending[^1] = snapshot;
            else _pending.Add(snapshot);
            if (_scheduled) return false;
            _scheduled = true; return true;
        }
    }
    public PlaybackSnapshot[] Drain()
    {
        lock (_gate) { var batch = _pending.ToArray(); _pending.Clear(); _scheduled = false; return batch; }
    }
    public void Clear() { lock (_gate) { _pending.Clear(); _scheduled = false; } }
    private static bool IsPositionUpdate(PlaybackSnapshot first, PlaybackSnapshot next) =>
        first.State == PlaybackState.Playing && next.State == first.State && next.Revision > first.Revision &&
        next.Generation == first.Generation && next.EntryId == first.EntryId && next.Duration == first.Duration &&
        next.SourceFormat == first.SourceFormat && next.OutputFormat == first.OutputFormat && next.CanSeek == first.CanSeek &&
        next.Volume == first.Volume && next.Muted == first.Muted && first.Error is null && next.Error is null &&
        !first.Ended && !next.Ended && !first.Transitioned && !next.Transitioned &&
        next.Position >= first.Position && next.Position - first.Position <= TimeSpan.FromSeconds(2);
}
