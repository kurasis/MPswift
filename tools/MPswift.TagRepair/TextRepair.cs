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

    internal static string? Recover(string? value, string context, bool album = false)
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
        return RecoverContextualLegacyWords(current, recovered, context, album);
    }

    private static string RecoverContextualLegacyWords(string original, string text, string context, bool album)
    {
        // Keep filename and album-folder evidence scoped to the relevant tag.
        const RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        // Normalize separators for lexical evidence only; never use this to open a file.
        var path = Regex.Replace(context, @"[\\/]+", "/", options);
        var evidence = Path.GetFileNameWithoutExtension(path);
        if (album) evidence += " " + Path.GetFileName(Path.GetDirectoryName(path));
        var contextWords = Regex.Matches(evidence, @"[\p{L}\p{N}]+", options)
            .Select(match => match.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Windows-1251 Ч becomes the non-letter multiplication sign in Latin-1.
        const string words = @"(?:[^\x00-\x7F]|[A-Za-z0-9])+";
        foreach (var encoding in Legacy.Take(2))
        {
            // Combine distinct longer Cyrillic field and scoped path words. This
            // also supports Unicode tags previously repaired only partly.
            var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool LongerEvidence(string word) => word.Count(IsCyrillic) >= 4 &&
                (!word.Any(c => char.IsLetter(c) && !IsCyrillic(c)) ||
                 contextWords.Contains(word) && !word.Any(c => char.IsLetter(c) && !IsCyrillic(c) && c is not 'i' and not 'I'));
            foreach (var word in contextWords)
                if (LongerEvidence(word)) confirmed.Add(word);
            foreach (Match match in Regex.Matches(original, words, options))
            {
                var word = match.Value;
                var candidate = word.Any(IsCyrillic) ? word : DecodeLegacyWord(word, encoding);
                if (candidate is not null && LongerEvidence(candidate)) confirmed.Add(candidate);
            }
            text = Regex.Replace(text, words, match =>
            {
                var word = match.Value;
                if (word.Any(IsCyrillic)) return word;
                var candidate = DecodeLegacyWord(word, encoding);
                if (candidate is null) return word;
                var cyrillic = candidate.Count(IsCyrillic);
                var exactWord = contextWords.Contains(candidate);
                // Preserve ASCII fragments (M, Best, i) exactly. Admit a mixed
                // word only when the filename or immediate album folder agrees.
                if (exactWord && cyrillic >= 2 && word.Any(char.IsAsciiLetter) &&
                    !candidate.Any(c => char.IsLetter(c) && !IsCyrillic(c) && !char.IsAsciiLetter(c))) return candidate;
                if (word.Any(c => c is 'i' or 'I') && cyrillic >= 2 &&
                    !candidate.Any(c => char.IsLetter(c) && !IsCyrillic(c) && c is not 'i' and not 'I') &&
                    (exactWord || cyrillic >= 3 && confirmed.Count >= 2)) return candidate;
                // Recover one-letter prepositions beside a Latin artist only with
                // both filename evidence and a corroborating Cyrillic field word.
                if (confirmed.Count > 0 && exactWord && candidate is "з" or "ў" or "і" or "у" or "я") return candidate;
                return word;
            }, options);
        }
        return text;
    }

    private static string? DecodeLegacyWord(string word, Encoding encoding)
    {
        try
        {
            var bytes = encoding.GetBytes(word);
            var candidate = Legacy[2].GetString(bytes);
            if (!candidate.Any(char.IsControl) && encoding.GetString(bytes) == word &&
                Legacy[2].GetBytes(candidate).AsSpan().SequenceEqual(bytes)) return candidate;
        }
        catch (ArgumentException) { }
        return null;
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
