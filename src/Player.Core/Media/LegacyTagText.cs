using System.Text;
using Player.Core.Playback;

namespace Player.Core.Media;

/// <summary>Read-only recovery of Cyrillic tags mislabeled as Latin-1/Windows-1252.</summary>
public static class LegacyTagText
{
    private static readonly Encoding Cyrillic = CreateEncoding(1251);
    private static readonly Encoding Western = CreateEncoding(1252);
    private static readonly Encoding Latin = Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static Encoding CreateEncoding(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public static MediaTrack Recover(MediaTrack track) => track with
    {
        Title = Recover(track.Title, track.Path)!, Artist = Recover(track.Artist, track.Path),
        Album = Recover(track.Album, track.Path), AlbumArtist = Recover(track.AlbumArtist, track.Path),
        Genre = Recover(track.Genre, track.Path)
    };

    public static string? Recover(string? text, string context = "")
    {
        if (string.IsNullOrEmpty(text)) return text;
        var cyrillicContext = context.Any(IsCyrillic) || text.Any(IsCyrillic);
        var result = new StringBuilder(text.Length);
        for (var start = 0; start < text.Length;)
        {
            // ASCII separators remain verbatim, including paths, punctuation and whitespace.
            if (text[start] < 128 && !char.IsLetterOrDigit(text[start])) { result.Append(text[start++]); continue; }
            var end = start + 1;
            while (end < text.Length && (text[end] >= 128 || char.IsLetterOrDigit(text[end]))) end++;
            var word = text[start..end];
            result.Append(RecoverWord(word, cyrillicContext)); start = end;
        }
        return result.ToString();
    }

    private static string RecoverWord(string word, bool cyrillicContext)
    {
        if (word.Any(IsCyrillic) || word.All(c => c < 128)) return word;
        foreach (var encoding in new[] { Latin, Western })
        {
            try
            {
                var bytes = encoding.GetBytes(word);
                var candidate = Cyrillic.GetString(bytes);
                var letters = candidate.Count(char.IsLetter);
                // Mixed Latin/Cyrillic words and ordinary accented Latin names are ambiguous.
                if (letters < (cyrillicContext ? 2 : 3) || candidate.Any(c => char.IsLetter(c) && !IsCyrillic(c)) ||
                    candidate.Any(char.IsControl) || !encoding.GetString(bytes).Equals(word, StringComparison.Ordinal) ||
                    !Cyrillic.GetBytes(candidate).AsSpan().SequenceEqual(bytes)) continue;
                return candidate;
            }
            catch (ArgumentException) { /* Not a losslessly reversible legacy word. */ }
        }
        return word;
    }

    private static bool IsCyrillic(char value) => value is >= '\u0400' and <= '\u04ff' && char.IsLetter(value);
}
