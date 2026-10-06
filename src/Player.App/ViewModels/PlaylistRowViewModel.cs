using CommunityToolkit.Mvvm.ComponentModel;
using Player.App.Resources;
using Player.Core.Playback;

namespace Player.App.ViewModels;

public partial class PlaylistRowViewModel : ObservableObject
{
    private PlaylistEntry entry;
    public PlaylistRowViewModel(PlaylistEntry value) { entry = value; _enabled = value.Enabled; }
    public void UpdateTrack(MediaTrack track) { entry = entry with { Track = track }; OnPropertyChanged(nameof(Entry)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Path)); OnPropertyChanged(nameof(Metadata)); OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(Availability)); }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSection), nameof(SectionTitle), nameof(SectionInfo), nameof(SectionFolder))]
    private AlbumSection? _section;
    public bool HasSection => Section is not null;
    public string SectionTitle => Section?.Title ?? "";
    public string SectionFolder => Section?.Folder ?? "";
    public string SectionInfo => Section is { } section
        ? string.Format(Strings.Culture, Strings.AlbumSectionCount, section.Count) + " · " + string.Join(" / ", section.Folder.Split('/').TakeLast(2)) : "";

    public static int[] RatingOptions { get; } = [0, 1, 2, 3, 4, 5];
    public PlaylistEntry Entry => entry with { Enabled = Enabled };
    public Guid Id => entry.Id;
    public string Title => entry.Track.Title;
    public string Path => entry.Track.Path;
    public string Metadata => string.Join(" · ", new[] { entry.Track.Artist, entry.Track.Album, entry.Track.FormatHint }.Where(v => !string.IsNullOrEmpty(v)));
    public string Duration => PlayerViewModel.FormatTime(entry.Track.DurationHint);
    public string Availability => entry.Track.Available ? "" : Strings.Get("Unavailable");
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _rating;
    public event Action<PlaylistRowViewModel>? RatingChanged;
    partial void OnRatingChanged(int value) => RatingChanged?.Invoke(this);
    [ObservableProperty] private bool _isPlaying;
    public event Action? EligibilityChanged;
    partial void OnEnabledChanged(bool value) => EligibilityChanged?.Invoke();
}
