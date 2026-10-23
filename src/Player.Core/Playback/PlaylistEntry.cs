namespace Player.Core.Playback;

public sealed record MediaTrack(Guid Id, string Path, string Title, string? Artist = null, string? Album = null,
    TimeSpan? DurationHint = null, string? FormatHint = null, bool Available = true, Player.Core.Media.TrackSegment? Segment = null, string? CueDocument = null, int? CueNumber = null,
    ReplayGainTags? ReplayGain = null, string? AlbumArtist = null, string? Genre = null, uint TrackNumber = 0, uint DiscNumber = 0, uint Year = 0,
    int SampleRateHint = 0, int ChannelsHint = 0, int BitrateHint = 0);
public sealed record PlaylistEntry(Guid Id, MediaTrack Track, bool Enabled = true, long AddedUtcTicks = 0);

public static class PlaylistSearch
{
    // Runs for every row on each search keystroke: no per-row arrays, and the file name is only extracted when needed.
    public static bool Matches(MediaTrack track, string query)
    {
        if (query.Length == 0) return true;
        query = query.Normalize(System.Text.NormalizationForm.FormC);
        return Contains(track.Title, query) || Contains(track.Artist, query) || Contains(track.Album, query) || Contains(Path.GetFileName(track.Path), query);
    }
    private static bool Contains(string? value, string query) =>
        value?.Normalize(System.Text.NormalizationForm.FormC).Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
