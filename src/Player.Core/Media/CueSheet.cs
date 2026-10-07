using System.Globalization;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Player.Core.Playback;

namespace Player.Core.Media;

public sealed record CueSong(int Number, string Path, string Title, string? Performer, string? Album,
    long StartFrame, long? EndFrame, IReadOnlyDictionary<string, string> Remarks);
public sealed record CueSheet(CueSong[] Songs, string[] Diagnostics)
{
    public static CueSheet Parse(string text, string documentPath)
    {
        if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("CUE document exceeds 4 MiB.");
        documentPath = LocalMediaPath.Parse(documentPath).Value;
        var tracks = new List<Builder>(); var errors = new List<string>();
        string? file = null, album = null, performer = null; Builder? current = null;
        var remarks = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var line in text.Split('\n'))
        {
            if (++lineNumber > 100000) throw new InvalidDataException("CUE line limit exceeded.");
            try
            {
                var words = Tokenize(line); if (words.Count == 0) continue;
                var command = words[0].ToUpperInvariant();
                switch (command)
                {
                    case "FILE":
                        if (words.Count != 3) throw new InvalidDataException("FILE requires a quoted path and format.");
                        file = Resolve(documentPath, words[1]); current = null; break;
                    case "TRACK":
                        if (words.Count != 3 || !int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > 999 || words[2].ToUpperInvariant() != "AUDIO" || file is null)
                            throw new InvalidDataException("Expected TRACK number AUDIO after FILE.");
                        if (tracks.Count >= 10000 || tracks.Any(t => t.Number == number)) throw new InvalidDataException("Duplicate track number or track limit exceeded.");
                        current = new(number, file, performer, remarks); tracks.Add(current); break;
                    case "TITLE":
                    case "PERFORMER":
                        if (words.Count != 2 || words[1].Length > 4096) throw new InvalidDataException("Invalid metadata field.");
                        if (command == "TITLE") { if (current is null) album = words[1]; else current.Title = words[1]; }
                        else { if (current is null) performer = words[1]; else current.Performer = words[1]; } break;
                    case "INDEX":
                        if (words.Count != 3 || current is null) throw new InvalidDataException("INDEX requires a track.");
                        if (words[1] is not ("00" or "01")) break;
                        var frame = ParseFrames(words[2]);
                        if (words[1] == "00") { if (current.Start is not null || current.Pregap is not null) throw new InvalidDataException("INDEX 00 order invalid."); current.Pregap = frame; }
                        else { if (current.Start is not null || current.Pregap > frame) throw new InvalidDataException("INDEX 01 order invalid."); current.Start = frame; } break;
                    case "REM":
                        if (words.Count >= 3 && words[1].Length <= 100)
                        { var value = string.Join(' ', words.Skip(2)); if (value.Length > 4096) throw new InvalidDataException("REM too long."); if (current is null) remarks = remarks.SetItem(words[1], value); else current.Remarks = current.Remarks.SetItem(words[1], value); } break;
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or OverflowException)
            {
                if (errors.Count < 100) errors.Add($"Line {lineNumber}: {e.Message}");
                if (current is not null) current.Invalid = true;
                // A bad FILE must not reuse a previous source.
                if (line.TrimStart().StartsWith("FILE", StringComparison.OrdinalIgnoreCase)) { file = null; current = null; }
            }
        }
        var songs = new List<CueSong>();
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var next = i + 1 < tracks.Count && string.Equals(tracks[i + 1].Path, t.Path, StringComparison.OrdinalIgnoreCase) ? tracks[i + 1] : null;
            if (t.Invalid || t.Start is null || next is { Start: null } || next?.Start <= t.Start || next?.Pregap < t.Start)
            { if (errors.Count < 100) errors.Add($"Track {t.Number}: missing/decreasing/impossible index; segment rejected."); continue; }
            songs.Add(new(t.Number, t.Path, t.Title ?? $"Track {t.Number:00}", t.Performer ?? performer, album, t.Start.Value, next?.Start, t.Remarks));
        }
        return new(songs.ToArray(), errors.ToArray());
    }
    public static string Decode(byte[] bytes, Encoding? explicitFallback = null)
    {
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Document exceeds 4 MiB.");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException) when (explicitFallback is not null) { return explicitFallback.GetString(bytes); }
    }
    public static string Resolve(string document, string reference)
    {
        if (reference.Length == 0 || reference.StartsWith('/') || reference.StartsWith('\\') || reference.Contains("://")) throw new ArgumentException("Offline local path required.");
        return LocalMediaPath.Parse(reference.Length >= 3 && reference[1] == ':' ? reference : document[..(document.LastIndexOf('\\') + 1)] + reference).Value;
    }
    public static Guid TrackId(string documentPath, CueSong song)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(LocalMediaPath.Parse(documentPath).Value.ToUpperInvariant() + "|" + song.Number + "|" + song.StartFrame));
        return new Guid(bytes.AsSpan(0, 16));
    }
    private static long ParseFrames(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)) || !long.TryParse(parts[0], out var minute) || !int.TryParse(parts[1], out var second) || !int.TryParse(parts[2], out var frame) || second >= 60 || frame >= 75)
            throw new InvalidDataException("Invalid mm:ss:ff time.");
        return checked((minute * 60 + second) * 75 + frame);
    }
    private static List<string> Tokenize(string line)
    {
        var words = new List<string>(); var i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++; if (i == line.Length) break;
            var quoted = line[i] == '"'; if (quoted) i++; var start = i;
            while (i < line.Length && (quoted ? line[i] != '"' : !char.IsWhiteSpace(line[i]))) i++;
            if (quoted && i == line.Length) throw new InvalidDataException("Unterminated quote.");
            words.Add(line[start..i]); if (quoted) { i++; if (i < line.Length && !char.IsWhiteSpace(line[i])) throw new InvalidDataException("Expected whitespace after quote."); }
        }
        return words;
    }
    private sealed class Builder(int number, string path, string? performer, ImmutableDictionary<string, string> remarks)
    { public int Number = number; public string Path = path; public string? Performer = performer, Title; public long? Start, Pregap; public bool Invalid; public ImmutableDictionary<string, string> Remarks = remarks; }
}
