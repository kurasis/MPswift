using System.IO;
using Player.App.Services.Audio;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.App.Services.Library;

internal sealed record FileInformationSnapshot(string Path, long Bytes, DateTime ModifiedUtc, TimeSpan Duration,
    int SampleRate, int Bitrate, int Channels, int Bits, string Description, string TagTypes,
    IReadOnlyDictionary<string, string> General, IReadOnlyDictionary<string, string> Id3v1, IReadOnlyDictionary<string, string> Id3v2, string Lyrics);

/// <summary>Bounded, read-only snapshots; no TagLib objects or file handles reach the view.</summary>
internal static class FileInformationReader
{
    internal static FileInformationSnapshot Read(MediaTrack track)
    {
        using var source = LocalReadLease.Open(BassSmokeSession.ValidateSourcePath(track.Path));
        MetadataReadGuard.Validate(source.Stream, source.Path);
        using var file = TagLib.File.Create(source.Path, TagLib.ReadStyle.Average);
        var tag = file.Tag; var properties = file.Properties;
        var general = Fields(tag, source.Path, recover: true);
        if (track.Segment is { } segment)
        {
            general["CueTrackLabel"] = track.Title;
            general["CueRangeLabel"] = segment.Start.ToString(@"hh\:mm\:ss\.fff") + " — " + (segment.End?.ToString(@"hh\:mm\:ss\.fff") ?? "…");
        }
        var v1 = file.TagTypesOnDisk.HasFlag(TagLib.TagTypes.Id3v1) ? Fields(file.GetTag(TagLib.TagTypes.Id3v1, false), source.Path, false) : [];
        var v2 = file.TagTypesOnDisk.HasFlag(TagLib.TagTypes.Id3v2) ? Fields(file.GetTag(TagLib.TagTypes.Id3v2, false), source.Path, false) : [];
        if (file.TagTypesOnDisk.HasFlag(TagLib.TagTypes.Id3v2) && file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
            v2["TagVersionLabel"] = "ID3v2." + id3.Version;
        return new(source.Path, source.Stream.Length, File.GetLastWriteTimeUtc(source.Path), properties.Duration,
            properties.AudioSampleRate, properties.AudioBitrate, properties.AudioChannels, properties.BitsPerSample,
            Limit(properties.Description), file.TagTypesOnDisk.ToString(), general, v1, v2, Limit(tag.Lyrics, 65536));
    }
    private static Dictionary<string, string> Fields(TagLib.Tag? tag, string path, bool recover)
    {
        if (tag is null) return [];
        string Text(string? value) => recover ? LegacyTagText.Recover(Limit(value), path)! : Limit(value);
        string Many(string[] values) => Text(string.Join(", ", values.Take(64).Select(value => Limit(value))));
        return new()
        {
            ["FileTitleLabel"] = Text(tag.Title), ["FileArtistLabel"] = Many(tag.Performers), ["FileAlbumLabel"] = Text(tag.Album),
            ["FileAlbumArtistLabel"] = Many(tag.AlbumArtists), ["FileGenreLabel"] = Many(tag.Genres),
            ["FileYearLabel"] = tag.Year == 0 ? "" : tag.Year.ToString(), ["TrackLabel"] = tag.Track == 0 ? "" : tag.Track.ToString(),
            ["DiscLabel"] = tag.Disc == 0 ? "" : tag.Disc.ToString(), ["FileCommentLabel"] = Text(tag.Comment),
            ["TrackGainLabel"] = Gain(tag.ReplayGainTrackGain), ["AlbumGainLabel"] = Gain(tag.ReplayGainAlbumGain)
        };
    }
    private static string Gain(double value) => double.IsFinite(value) ? value.ToString("0.00", Resources.Strings.Culture) + " dB" : "";
    private static string Limit(string? value, int maximum = 4096) => value is null ? "" : value[..Math.Min(value.Length, maximum)];
}
