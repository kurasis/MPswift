using System.Text.Json;

namespace Player.Core.Integration;

/// <summary>Local preview text, bounded and redacted before it reaches a clipboard or file.</summary>
public static class DiagnosticReport
{
    public const int MaximumCharacters = 32768;
    public static string Redact(string text, IEnumerable<string> privatePrefixes)
    {
        text = RedactPaths(text, PreparePrefixes(privatePrefixes));
        var length = Math.Min(text.Length, MaximumCharacters);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length];
    }

    internal static string[] PreparePrefixes(IEnumerable<string> prefixes) => prefixes
        .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.TrimEnd('\\', '/')).Where(p => p.Length > 0)
        .SelectMany(p => new[] { p, p.Replace('\\', '/'), p.Replace('/', '\\') })
        .SelectMany(p => new[] { p, JsonSerializer.Serialize(p)[1..^1] })
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length).ToArray();

    internal static string RedactPaths(string text, IEnumerable<string> preparedPrefixes)
    {
        foreach (var prefix in preparedPrefixes)
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
        return text;
    }
}
