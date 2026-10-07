using Player.Core.Media;

namespace Player.Core.Playback;

/// <summary>Keep the first source occurrence; CUE songs differ by exact logical bounds.</summary>
public static class PlaylistDuplicates
{
    public static IReadOnlyList<Guid> FindRemovable(IEnumerable<PlaylistEntry> entries)
    {
        var seen = new HashSet<(string Path, long? Start, long? End)>();
        var remove = new List<Guid>();
        foreach (var entry in entries)
        {
            var path = LocalMediaPath.Parse(entry.Track.Path).Value.ToUpperInvariant();
            var key = (path, entry.Track.Segment?.Start.Ticks, entry.Track.Segment?.End?.Ticks);
            if (!seen.Add(key)) remove.Add(entry.Id);
        }
        return remove;
    }
}
