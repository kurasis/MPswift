namespace Player.Core.Playback;

public sealed record MediaTrack(Guid Id, string Path, string Title, string? Artist = null, string? Album = null,
    TimeSpan? DurationHint = null, string? FormatHint = null, bool Available = true, Player.Core.Media.TrackSegment? Segment = null, string? CueDocument = null, int? CueNumber = null,
    ReplayGainTags? ReplayGain = null);
public sealed record PlaylistEntry(Guid Id, MediaTrack Track, bool Enabled = true);

public static class PlaylistSearch
{
    public static bool Matches(MediaTrack track, string query)
    {
        query = query.Normalize(System.Text.NormalizationForm.FormC);
        return new[] { track.Title, track.Artist, track.Album, Path.GetFileName(track.Path) }
            .Any(value => value?.Normalize(System.Text.NormalizationForm.FormC).Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }
}
