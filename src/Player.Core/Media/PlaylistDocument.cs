using System.Globalization;
using System.Text;
using Player.Core.Playback;

namespace Player.Core.Media;

public sealed record PlaylistDocument(string[] Paths, string[] Diagnostics)
{
    public static PlaylistDocument Parse(string text, string documentPath, bool pls = false)
    {
        if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("Playlist document exceeds 4 MiB.");
        documentPath = LocalMediaPath.Parse(documentPath).Value;
        var lines = text.Split('\n'); if (lines.Length > 100000) throw new InvalidDataException("Playlist line limit exceeded.");
        var references = new List<string>(); var diagnostics = new List<string>();
        if (pls)
        {
            var files = new SortedDictionary<int, string>();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r').Trim(); var equal = line.IndexOf('='); if (equal <= 4 || !line.StartsWith("File", StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(line.AsSpan(4, equal - 4), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n is < 1 or > 10000 || !files.TryAdd(n, line[(equal + 1)..])) throw new InvalidDataException("Invalid/duplicate PLS file index.");
            }
            references.AddRange(files.Values);
        }
        else foreach (var raw in lines) { var line = raw.TrimEnd('\r').Trim(); if (line.Length != 0 && !line.StartsWith('#')) references.Add(line); if (references.Count > 10000) throw new InvalidDataException("Playlist entry limit exceeded."); }
        var paths = new List<string>();
        foreach (var reference in references)
        {
            try
            {
                var path = CueSheet.Resolve(documentPath, reference);
                if (new[] { ".m3u", ".m3u8", ".pls", ".cue" }.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("Recursive document expansion is disabled.");
                paths.Add(path);
            }
            catch (ArgumentException e) { if (diagnostics.Count < 100) diagnostics.Add(reference[..Math.Min(reference.Length, 512)] + ": " + e.Message); }
        }
        return new(paths.ToArray(), diagnostics.ToArray());
    }
    public static string ExportM3u8(IEnumerable<PlaylistEntry> entries, string destination)
    {
        destination = LocalMediaPath.Parse(destination).Value;
        var list = entries.Take(10001).ToArray(); if (list.Length > 10000) throw new InvalidDataException("Export limit exceeded.");
        if (list.Any(e => e.Track.Segment is not null)) throw new InvalidOperationException("Ordinary M3U8 cannot preserve CUE segments. Export a full-source playlist or retain the original CUE document.");
        var directory = destination[..(destination.LastIndexOf('\\') + 1)]; var text = new StringBuilder("#EXTM3U\n");
        foreach (var entry in list)
        {
            var path = LocalMediaPath.Parse(entry.Track.Path).Value;
            text.Append(path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ? path[directory.Length..] : path).Append('\n');
        }
        return text.ToString();
    }
}
