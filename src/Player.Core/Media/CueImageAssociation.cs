namespace Player.Core.Media;

/// <summary>Resolve a missing single-image FILE reference without changing the CUE document.</summary>
public static class CueImageAssociation
{
    public static CueSheet Resolve(CueSheet sheet, string document, IReadOnlyList<string> localFlacs, Func<string, bool> exists)
    {
        if (sheet.Diagnostics.Length != 0 || sheet.Songs.Length < 2) return sheet;
        var reference = sheet.Songs[0].Path;
        if (sheet.Songs.Any(song => !string.Equals(song.Path, reference, StringComparison.OrdinalIgnoreCase)) || exists(reference)) return sheet;
        var directory = Directory(document);
        if (!string.Equals(Directory(reference), directory, StringComparison.OrdinalIgnoreCase)) return sheet;
        var images = localFlacs.Where(path => string.Equals(Directory(path), directory, StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matching = images.Where(path => string.Equals(Stem(path), Stem(reference), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length == 0) matching = images.Where(path => string.Equals(Stem(path), Stem(document), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length == 0 && images.Length == 1) matching = images;
        if (matching.Length != 1) return sheet;
        return sheet with { Songs = sheet.Songs.Select(song => song with { Path = matching[0] }).ToArray() };
    }

    private static string Directory(string path) => path[..(path.LastIndexOf('\\') + 1)];
    private static string Stem(string path)
    {
        var file = path[(path.LastIndexOf('\\') + 1)..];
        var extension = file.LastIndexOf('.'); return extension < 0 ? file : file[..extension];
    }
}
