namespace Player.Core.Playback;

internal sealed record PlaybackPlaylistSource(Guid Id, PlaylistEntry[] Entries);

public sealed partial class PlaybackCoordinator
{
    private PlaybackPlaylistSource[] _playlistSources = [];
    private Guid? _playlistSourceId, _preparedPlaylistId;
    internal Guid? SourcePlaylistId { get { lock (_gate) return _playlistSourceId; } }
    internal (Guid? SourceId, PlaylistEntry? Active, PlaybackOrderState Order) CaptureSourceState()
    { lock (_gate) return (_playlistSourceId, _cursor, _order.Capture()); }
    // UI publishes immutable order snapshots; the engine never enumerates UI collections or waits on the dispatcher.
    internal void SetPlaylistSources(PlaybackPlaylistSource[] sources, Guid? chosen, bool chooseSource)
    {
        lock (_gate)
        {
            _playlistSources = sources;
            if (chooseSource) _playlistSourceId = chosen;
            SetSourceEntries(sources.FirstOrDefault(source => source.Id == _playlistSourceId)?.Entries ?? []);
        }
    }
    private PlaybackPlaylistSource? FollowingSource(HashSet<Guid>? visited = null)
    {
        var index = Array.FindIndex(_playlistSources, source => source.Id == _playlistSourceId);
        if (index < 0) return null; // A detached source never jumps into an unrelated playlist.
        for (var i = index + 1; i < _playlistSources.Length; i++)
            if (visited?.Contains(_playlistSources[i].Id) != true && _playlistSources[i].Entries.Any(entry => entry.Enabled && entry.Track.Available))
                return _playlistSources[i];
        return null;
    }
    // A native transition already in flight can arrive after a UI reorder changes the prepared candidate.
    private PlaylistEntry? TransitionEntry(Guid? id) => id is null ? null : _preparedEntry?.Id == id ? _preparedEntry :
        _entries.FirstOrDefault(entry => entry.Id == id) ??
        _playlistSources.SelectMany(source => source.Entries).FirstOrDefault(entry => entry.Id == id) ??
        _order.Queue.FirstOrDefault(item => item.Entry.Id == id)?.Entry;
    private void BeginFollowingSource(PlaybackPlaylistSource source)
    {
        _playlistSourceId = source.Id; _entries = source.Entries;
        _order.ContinueWith(_entries);
        _preparedEntry = null; _preparedPlaylistId = null; _preparationDirty = true;
    }
}
