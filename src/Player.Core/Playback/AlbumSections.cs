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
    public static bool IsSameAlbum(MediaTrack first, MediaTrack second)
    {
        var left = AlbumKey(first); var right = AlbumKey(second);
        return string.Equals(left.Folder, right.Folder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Album, right.Album, StringComparison.OrdinalIgnoreCase);
    }
    private static (string Folder, string Album) AlbumKey(MediaTrack track)
    {
        // Normalize Windows paths consistently in platform-independent tests too.
        var path = track.Path.Replace('\\', '/'); var slash = path.LastIndexOf('/');
        return (slash >= 0 ? path[..slash] : "", track.Album?.Trim() ?? "");
    }
    public static IReadOnlyList<AlbumSection> Create(IEnumerable<MediaTrack> tracks)
    {
        var sections = new List<AlbumSection>();
        string? previousFolder = null, previousAlbum = null;
        AlbumSection? current = null;
        var index = 0;
        foreach (var track in tracks)
        {
            var (folder, album) = AlbumKey(track);
            if (current is null || !string.Equals(folder, previousFolder, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(album, previousAlbum, StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) sections.Add(current with { Count = index - current.StartIndex });
                var folderName = folder[(folder.LastIndexOf('/') + 1)..];
                current = new(index, 0, album.Length > 0 ? album : folderName, folder);
                previousFolder = folder; previousAlbum = album;
            }
            index++;
        }
        if (current is not null) sections.Add(current with { Count = index - current.StartIndex });
        return sections;
    }
}
