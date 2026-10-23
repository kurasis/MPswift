using CommunityToolkit.Mvvm.ComponentModel;
using Player.App.Resources;
using Player.Core.Playback;

namespace Player.App.ViewModels;

public partial class PlaylistRowViewModel : ObservableObject
{
    private PlaylistEntry entry;
    public PlaylistRowViewModel(PlaylistEntry value) { entry = value; _enabled = value.Enabled; }
    public void UpdateTrack(MediaTrack track)
    {
        if (track == entry.Track) return;
        entry = entry with { Track = track }; OnPropertyChanged(nameof(Entry)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Path)); OnPropertyChanged(nameof(Metadata)); OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(Availability));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSection), nameof(SectionTitle), nameof(SectionInfo), nameof(SectionFolder), nameof(SelectAlbumLabel))]
    private AlbumSection? _section;
    public bool HasSection => Section is not null;
    public string SectionTitle => Section?.Title ?? "";
    public string SectionFolder => Section?.Folder ?? "";
    public string SelectAlbumLabel => string.Format(Strings.Culture, Strings.Get("SelectAlbumTracks"), SectionTitle);
    public string SectionInfo => Section is { } section
        ? string.Format(Strings.Culture, Strings.AlbumSectionCount, section.Count) + " · " + string.Join(" / ", section.Folder.Split('/').TakeLast(2)) : "";

    public static int[] RatingOptions { get; } = [0, 1, 2, 3, 4, 5];
    // Kept in sync with Enabled so reading it (every source sync, capture and filter pass) allocates nothing.
    public PlaylistEntry Entry => entry;
    public Guid Id => entry.Id;
    public string Title => entry.Track.Title;
    public string Path => entry.Track.Path;
    public string Metadata => string.Join(" · ", new[] { entry.Track.Artist, entry.Track.Album, entry.Track.FormatHint }.Where(v => !string.IsNullOrEmpty(v)));
    public string Duration => PlayerViewModel.FormatTime(entry.Track.DurationHint);
    public string Availability => entry.Track.Available ? "" : Strings.Get("Unavailable");
    public void RefreshLanguage() { OnPropertyChanged(nameof(SectionInfo)); OnPropertyChanged(nameof(SelectAlbumLabel)); OnPropertyChanged(nameof(Availability)); }
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _rating;
    public event Action<PlaylistRowViewModel>? RatingChanged;
    partial void OnRatingChanged(int value) => RatingChanged?.Invoke(this);
    [ObservableProperty] private bool _isPlaying;
    public event Action? EligibilityChanged;
    partial void OnEnabledChanged(bool value) { entry = entry with { Enabled = value }; EligibilityChanged?.Invoke(); }
}
