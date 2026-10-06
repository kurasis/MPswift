namespace Player.Core.Playback;

public sealed record AlbumSection(int StartIndex, int Count, string Title, string Folder);

/// <summary>Headings for contiguous visible runs only; never sorts or merges playlist occurrences.</summary>
public static class AlbumSections
{
    public static IReadOnlyList<AlbumSection> Create(IEnumerable<MediaTrack> tracks)
    {
        var sections = new List<AlbumSection>();
        string? previousFolder = null, previousAlbum = null;
        AlbumSection? current = null;
        var index = 0;
        foreach (var track in tracks)
        {
            // Windows paths must also be interpreted consistently by platform-independent tests.
            var path = track.Path.Replace('\\', '/');
            var slash = path.LastIndexOf('/');
            var folder = slash >= 0 ? path[..slash] : "";
            var album = track.Album?.Trim() ?? "";
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
