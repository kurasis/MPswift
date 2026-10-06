using Player.Core.Playback;

namespace Player.Core.Library;

public sealed record PlaylistState(Guid Id, string Name, PlaylistEntry[] Entries);
public sealed record SessionState(Guid? SelectedPlaylistId, Guid? SourcePlaylistId, PlaylistEntry? ActiveEntry, long PositionTicks, PlaybackOrderState? Order = null);
public sealed record LibraryState(PlaylistState[] Playlists, SessionState Session)
{
    public static LibraryState CreateDefault() => new([new(Guid.NewGuid(), "Default", [])], new(null, null, null, 0));
    public void Validate()
    {
        if (Playlists.Length is < 1 or > 100 || Playlists.Sum(p => p.Entries.Length) > 10000) throw new InvalidDataException("Playlist limits exceeded.");
        if (Playlists.Any(p => p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200) || Playlists.Select(p => p.Id).Distinct().Count() != Playlists.Length)
            throw new InvalidDataException("Invalid playlist identity/name.");
        var entries = Playlists.SelectMany(p => p.Entries).ToArray();
        if (entries.Any(e => e.Id == Guid.Empty || e.Track.Id == Guid.Empty || e.AddedUtcTicks < 0 || e.AddedUtcTicks > DateTime.MaxValue.Ticks) || entries.Select(e => e.Id).Distinct().Count() != entries.Length)
            throw new InvalidDataException("Invalid entry identity.");
        foreach (var track in entries.Select(e => e.Track).Append(Session.ActiveEntry?.Track).Concat(Session.Order?.Queue.Select(q => q.Entry.Track) ?? []).Concat(Session.Order?.History.Select(e => e.Track) ?? []).OfType<MediaTrack>())
        {
            if (track.Id == Guid.Empty || string.IsNullOrWhiteSpace(track.Path) || track.Path.Length > 32767 || track.Title is null || track.Title.Length > 4096 || track.Artist?.Length > 4096 || track.Album?.Length > 4096 || track.FormatHint?.Length > 4096 || track.AlbumArtist?.Length > 4096 || track.Genre?.Length > 4096)
                throw new InvalidDataException("Invalid saved track metadata.");
            Player.Core.Media.LocalMediaPath.Parse(track.Path);
            if (track.CueDocument is { } cue) Player.Core.Media.LocalMediaPath.Parse(cue);
            if (track.Segment is { } segment && (segment.Start > TimeSpan.FromDays(365) || segment.End > TimeSpan.FromDays(365))) throw new InvalidDataException("Invalid segment bounds.");
        }
        if (Session.Order is { } order) { var validator = new PlaybackOrder(); validator.Restore(order); }
        if (Session.ActiveEntry is { Id: var activeId } && activeId == Guid.Empty) throw new InvalidDataException("Invalid session identity.");
        if (Session.PositionTicks < 0 || Session.PositionTicks > TimeSpan.FromDays(365).Ticks) throw new InvalidDataException("Invalid session position.");
    }
}

public interface IPlayerStore : IAsyncDisposable
{
    Task<LibraryState> LoadAsync();
    Task SaveAsync(LibraryState state, bool playlistsChanged);
    Task BackupAsync(string destination);
}

public sealed record PlayerSettings(int SchemaVersion = 1, double Volume = 50, bool Muted = false,
    int WaveformCacheMiB = 512, double WindowWidth = 840, double WindowHeight = 860, AudioProcessingSettings? Processing = null, AudioOutputSettings? Output = null,
    string Language = "en", bool CloseToTray = false, double? WindowLeft = null, double? WindowTop = null, bool WindowMaximized = false)
{
    public PlayerSettings Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported settings schema. Original settings were preserved.");
        return this with
        {
            Language = Language is "en" or "ru" ? Language : "en",
            WindowLeft = WindowLeft is { } left && double.IsFinite(left) && Math.Abs(left) <= 32768 ? left : null,
            WindowTop = WindowTop is { } top && double.IsFinite(top) && Math.Abs(top) <= 32768 ? top : null,
            Processing = (Processing ?? new()).Validate(),
            Output = Output ?? new(),
            Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 100) : 50,
            WaveformCacheMiB = Math.Clamp(WaveformCacheMiB, 16, 2048),
            WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 640, 7680) : 840,
            WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 520, 4320) : 860
        };
    }
}
