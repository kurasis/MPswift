namespace Player.Core.Playback;

public sealed record AlbumSection(int StartIndex, int Count, string Title, string Folder);

/// <summary>Album identity, contiguous headings and explicit block movement; heading generation never sorts occurrences.</summary>
public static class AlbumSections
{
    /// <summary>Moves one contiguous album run to an existing album boundary; occurrence order and IDs are retained.</summary>
    public static PlaylistEntry[] MoveRun(IReadOnlyList<PlaylistEntry> entries, Guid headingId, int insertionIndex)
    {
        var original = entries.ToArray();
        var sections = Create(entries.Select(entry => entry.Track));
        var source = sections.FirstOrDefault(section => entries[section.StartIndex].Id == headingId);
        if (source is null || insertionIndex < 0 || insertionIndex > entries.Count ||
            (insertionIndex != entries.Count && !sections.Any(section => section.StartIndex == insertionIndex)) ||
            (insertionIndex >= source.StartIndex && insertionIndex <= source.StartIndex + source.Count)) return original;
        var moved = original.Skip(source.StartIndex).Take(source.Count).ToArray();
        var remaining = original.Take(source.StartIndex).Concat(original.Skip(source.StartIndex + source.Count)).ToList();
        remaining.InsertRange(insertionIndex > source.StartIndex ? insertionIndex - source.Count : insertionIndex, moved);
        return remaining.ToArray();
    }
    public static bool IsSameAlbum(MediaTrack first, MediaTrack second) =>
        SameFolder(FolderOf(first.Path), FolderOf(second.Path)) && AlbumOf(first).Equals(AlbumOf(second), StringComparison.OrdinalIgnoreCase);
    // Headings are rebuilt after every playlist change; compare spans and allocate strings only for a new heading.
    private static ReadOnlySpan<char> FolderOf(string path) { var slash = path.AsSpan().LastIndexOfAny('\\', '/'); return slash >= 0 ? path.AsSpan(0, slash) : []; }
    private static ReadOnlySpan<char> AlbumOf(MediaTrack track) => track.Album.AsSpan().Trim();
    /// <summary>Case-insensitive folder equality treating both separators alike, as Windows paths do in platform-independent tests too.</summary>
    private static bool SameFolder(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (left.Length != right.Length) return false;
        while (true)
        {
            var leftSlash = left.IndexOfAny('\\', '/'); var rightSlash = right.IndexOfAny('\\', '/');
            if (leftSlash != rightSlash) return false;
            if (leftSlash < 0) return left.Equals(right, StringComparison.OrdinalIgnoreCase);
            if (!left[..leftSlash].Equals(right[..rightSlash], StringComparison.OrdinalIgnoreCase)) return false;
            left = left[(leftSlash + 1)..]; right = right[(rightSlash + 1)..];
        }
    }
    public static IReadOnlyList<AlbumSection> Create(IEnumerable<MediaTrack> tracks)
    {
        var sections = new List<AlbumSection>();
        MediaTrack? previous = null;
        AlbumSection? current = null;
        var index = 0;
        foreach (var track in tracks)
        {
            var folder = FolderOf(track.Path); var album = AlbumOf(track);
            if (previous is null || !SameFolder(folder, FolderOf(previous.Path)) || !album.Equals(AlbumOf(previous), StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) sections.Add(current with { Count = index - current.StartIndex });
                var normalized = folder.ToString().Replace('\\', '/');
                var folderName = normalized[(normalized.LastIndexOf('/') + 1)..];
                current = new(index, 0, album.Length > 0 ? album.ToString() : folderName, normalized);
                previous = track;
            }
            index++;
        }
        if (current is not null) sections.Add(current with { Count = index - current.StartIndex });
        return sections;
    }
}
