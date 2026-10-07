namespace Player.Core.Integration;

/// <summary>Local preview text, bounded and redacted before it reaches a clipboard or file.</summary>
public static class DiagnosticReport
{
    public const int MaximumCharacters = 32768;
    public static string Redact(string text, IEnumerable<string> privatePrefixes)
    {
        foreach (var prefix in privatePrefixes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.TrimEnd('\\', '/')).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length))
        {
            var cursor = 0;
            while ((cursor = text.IndexOf(prefix, cursor, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = cursor + prefix.Length;
                if (end == text.Length || !char.IsLetterOrDigit(text[end]) && text[end] is not ('_' or '-' or '.'))
                { text = text[..cursor] + "<local>" + text[end..]; cursor += 7; }
                else cursor = end;
            }
        }
        var length = Math.Min(text.Length, MaximumCharacters);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length];
    }
}
