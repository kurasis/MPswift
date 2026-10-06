namespace Player.Core.Media;

/// <summary>Prefer a selected companion CUE once, while preserving deliberately repeated image inputs.</summary>
public static class CueImportSelection
{
    public static IEnumerable<string> Resolve(IReadOnlyList<string> sources, Func<string, string?> findImageCue)
    {
        var selectedCues = sources.Where(p => p.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var cue = source.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ? findImageCue(source) : null;
            if (cue is null) yield return source;
            else if (!selectedCues.Contains(cue)) yield return cue;
        }
    }
}
