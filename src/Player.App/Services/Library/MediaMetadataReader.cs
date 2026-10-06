using System.IO;
using Player.App.Services.Audio;
using Player.Core.Playback;

namespace Player.App.Services.Library;

public static class MediaMetadataReader
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".wav", ".aif", ".aiff", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".alac", ".m4b", ".wma", ".ape", ".wv", ".mpc", ".tta", ".dsf", ".dff" };
    public static MediaTrack Read(string path, Guid id, Action<string> diagnostic)
    {
        var available = File.Exists(path);
        var track = new MediaTrack(id, path, Path.GetFileNameWithoutExtension(path), FormatHint: Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), Available: available);
        if (!available) return track;
        BassSmokeSession.ValidateSourcePath(path);
        try
        {
            Player.Core.Media.MetadataReadGuard.Validate(path);
            using var file = TagLib.File.Create(path, TagLib.ReadStyle.Average); var tag = file.Tag;
            return track with
            {
                Title = Bounded(tag.Title) ?? track.Title, Artist = Join(tag.Performers), Album = Bounded(tag.Album), AlbumArtist = Join(tag.AlbumArtists),
                Genre = Join(tag.Genres), TrackNumber = tag.Track, DiscNumber = tag.Disc, Year = tag.Year,
                DurationHint = file.Properties.Duration > TimeSpan.Zero ? file.Properties.Duration : null,
                SampleRateHint = file.Properties.AudioSampleRate, ChannelsHint = file.Properties.AudioChannels, BitrateHint = file.Properties.AudioBitrate,
                ReplayGain = new(double.IsFinite(tag.ReplayGainTrackGain) ? tag.ReplayGainTrackGain : null, double.IsFinite(tag.ReplayGainAlbumGain) ? tag.ReplayGainAlbumGain : null,
                    double.IsFinite(tag.ReplayGainTrackPeak) && tag.ReplayGainTrackPeak > 0 ? tag.ReplayGainTrackPeak : null, double.IsFinite(tag.ReplayGainAlbumPeak) && tag.ReplayGainAlbumPeak > 0 ? tag.ReplayGainAlbumPeak : null)
            };
        }
        catch (Exception e) when (e is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotImplementedException)
        { diagnostic("Metadata fallback: " + Path.GetFileName(path) + "; " + e.Message); return track; }
    }
    private static string? Bounded(string? text) => string.IsNullOrWhiteSpace(text) ? null : text[..Math.Min(text.Length, 4096)];
    private static string? Join(IEnumerable<string> values)
    {
        var result = new System.Text.StringBuilder(4096);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (result.Length > 0) result.Append(", "[..Math.Min(2, 4096 - result.Length)]);
            result.Append(value.AsSpan(0, Math.Min(value.Length, 4096 - result.Length)));
            if (result.Length == 4096) break;
        }
        return Bounded(result.ToString());
    }
}
