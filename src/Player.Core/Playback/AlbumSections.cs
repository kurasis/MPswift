namespace Player.Core.Playback;

public sealed record AlbumSection(int StartIndex, int Count, string Title, string Folder);

/// <summary>Headings for contiguous visible runs only; never sorts or merges playlist occurrences.</summary>
public static class AlbumSections
{
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
