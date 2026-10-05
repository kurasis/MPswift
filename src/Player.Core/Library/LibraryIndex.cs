using System.Text;
using Player.Core.Playback;

namespace Player.Core.Library;

public sealed record LibraryRoot(Guid Id, string Path, bool Enabled = true);
public sealed record IndexedFile(Guid Id, Guid RootId, string Path, long Size, long ModifiedUtcTicks, bool Available,
    string Generation, MediaTrack Track);
public sealed record TrackStatistics(Guid TrackId, int Rating, long PlayCount = 0, long? LastPlayedUtcTicks = null);
public sealed record ListeningEvent(Guid Id, Guid TrackId, long StartedUtcTicks, long ListenedTicks, bool Counted);
public sealed record LibraryPage(IndexedFile[] Files, long Total, int Offset);
public interface ILibraryIndexStore
{
    Task<LibraryRoot[]> GetRootsAsync();
    Task PutRootAsync(LibraryRoot root);
    Task<IndexedFile[]> FindFilesAsync(string[] paths);
    Task UpsertFilesAsync(IndexedFile[] files);
    Task CompleteScanAsync(Guid root, string generation);
    Task<LibraryPage> SearchAsync(string query, int offset = 0, int limit = 100);
    Task<TrackStatistics[]> GetStatisticsAsync(Guid[] tracks);
    Task SetRatingAsync(Guid track, int rating);
    Task RecordListeningAsync(ListeningEvent occurrence);
    Task ClearListeningAsync();
    Task RelinkAsync(Guid trackId, string path);
}
public static class LibrarySearch
{
    public static string Normalize(string text) => text.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    public static string Fields(MediaTrack track) => Normalize(string.Join('\n', track.Title, track.Artist, track.Album, track.Path));
}

/// <summary>Accumulates elapsed playing time only; seek distance never counts as listening.</summary>
public sealed class ListeningMeter
{
    private Guid? _entry;
    private Guid? _track;
    private Guid _occurrence;
    private DateTime _started;
    private TimeSpan _listened;
    private TimeSpan? _duration;
    private TimeSpan _lastTime;
    private bool _playing, _counted;
    private PlaybackState _previousState;
    public ListeningEvent? Update(PlaybackSnapshot snapshot, Guid? track, TimeSpan monotonic, DateTime utcNow)
    {
        ListeningEvent? result = null;
        if (_playing && monotonic >= _lastTime) _listened += TimeSpan.FromTicks(Math.Min((monotonic - _lastTime).Ticks, TimeSpan.FromSeconds(2).Ticks));
        var fresh = snapshot.State == PlaybackState.Loading && _previousState != PlaybackState.Loading || snapshot.EntryId != _entry || snapshot.Transitioned || snapshot.Ended || snapshot.State == PlaybackState.Stopped && _previousState is PlaybackState.Playing or PlaybackState.Paused;
        if (_entry is not null && _track is not null && !_counted && _duration is { } duration)
        {
            var threshold = TimeSpan.FromSeconds(Math.Max(5, Math.Min(duration.TotalSeconds / 2, 240)));
            if (duration.TotalSeconds < 5 ? snapshot.Ended : _listened >= threshold)
            { _counted = true; result = new(_occurrence, _track.Value, _started.Ticks, _listened.Ticks, true); }
        }
        if (fresh || _entry is null && snapshot.EntryId is not null)
        { _entry = snapshot.EntryId; _track = track; _occurrence = Guid.NewGuid(); _started = utcNow; _listened = TimeSpan.Zero; _counted = false; _duration = snapshot.Duration; }
        if (_duration is null) _duration = snapshot.Duration;
        _previousState = snapshot.State; _playing = snapshot.State == PlaybackState.Playing; _lastTime = monotonic;
        return result;
    }
}
