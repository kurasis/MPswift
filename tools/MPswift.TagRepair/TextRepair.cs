using System.Text;
using System.Text.RegularExpressions;
using Player.Core.Media;

namespace MPswift.TagRepair;

internal static class TextRepair
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Encoding[] Legacy = CreateEncodings();
    private static Encoding[] CreateEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return new[] { 28591, 1252, 1251 }.Select(code => Encoding.GetEncoding(code, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)).ToArray();
    }

    internal static string? Recover(string? value, string context)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var current = value;
        // Strictly reversible UTF-8 mojibake, including UTF-8 decoded as Windows-1251.
        for (var pass = 0; pass < 2; pass++)
        {
            if (current.Count(c => c is 'Ð' or 'Ñ' or 'Р' or 'С') < 2) break;
            var found = false;
            foreach (var encoding in Legacy)
            {
                try
                {
                    var bytes = encoding.GetBytes(current); var candidate = Utf8.GetString(bytes);
                    if (candidate == current || !candidate.Any(IsCyrillic) || candidate.Contains('\ufffd') ||
                        candidate.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t') ||
                        !encoding.GetString(bytes).Equals(current, StringComparison.Ordinal) || !Utf8.GetBytes(candidate).AsSpan().SequenceEqual(bytes)) continue;
                    current = candidate; found = true; break;
                }
                catch (ArgumentException) { }
            }
            if (!found) break;
        }
        var recovered = LegacyTagText.Recover(current, context)!;
        // Recover short Belarusian prepositions only when longer words confirm this encoding.
        if (recovered != current && !current.Any(IsCyrillic))
            foreach (var encoding in Legacy.Take(2))
                try
                {
                    var candidate = Legacy[2].GetString(encoding.GetBytes(current));
                    if (candidate.Contains('\ufffd') || candidate.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t')) continue;
                    if (Regex.Matches(candidate, @"\p{L}+", RegexOptions.CultureInvariant).Any(match =>
                        match.Value.Any(IsCyrillic) && match.Value.Any(c => !IsCyrillic(c)))) continue;
                    if (encoding.GetString(Legacy[2].GetBytes(candidate)) == current) return candidate;
                }
                catch (ArgumentException) { }
        return recovered;
    }

    internal static byte[] Cue(byte[] bytes, string context, int fallbackCodePage)
    {
        // CUE can be the first inspected file, before Recover initializes legacy encodings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("CUE exceeds 4 MiB.");
        string text;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) text = new UTF32Encoding(false, true, true).GetString(bytes, 4, bytes.Length - 4);
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) text = new UTF32Encoding(true, true, true).GetString(bytes, 4, bytes.Length - 4);
        else text = CueSheet.Decode(bytes, Encoding.GetEncoding(fallbackCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
        if (text.Contains('\ufffd') || text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
            throw new InvalidDataException("CUE contains replacement/control characters; original retained.");
        const RegexOptions syntax = RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        if (!Regex.IsMatch(text, @"^[ \t]*FILE[ \t]+.+[ \t]+(?:WAVE|MP3|AIFF)[ \t]*\r?$", syntax) ||
            !Regex.IsMatch(text, @"^[ \t]*TRACK[ \t]+[0-9]+[ \t]+AUDIO[ \t]*\r?$", syntax) ||
            !Regex.IsMatch(text, @"^[ \t]*INDEX[ \t]+01[ \t]+[0-9]+:[0-5][0-9]:(?:[0-6][0-9]|7[0-4])[ \t]*\r?$", syntax))
            throw new InvalidDataException("No recognizable CUE FILE/TRACK/INDEX structure; original retained.");
        // FILE paths and all INDEX/timing/structural lines remain exactly as decoded.
        text = Regex.Replace(text, @"(?m)^(?<prefix>[ \t]*(?:TITLE|PERFORMER|SONGWRITER|REM[ \t]+(?:GENRE|COMMENT))[ \t]+)(?<value>[^\r\n]*)",
            match => match.Groups["prefix"].Value + Recover(match.Groups["value"].Value, context),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        return Utf8.GetBytes(text);
    }
    private static bool IsCyrillic(char c) => c is >= '\u0400' and <= '\u04ff' && char.IsLetter(c);
}
