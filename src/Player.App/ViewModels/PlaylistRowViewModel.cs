using CommunityToolkit.Mvvm.ComponentModel;
using Player.App.Resources;
using Player.Core.Playback;

namespace Player.App.ViewModels;

public partial class PlaylistRowViewModel(PlaylistEntry entry) : ObservableObject
{
    public PlaylistEntry Entry => entry with { Enabled = Enabled };
    public Guid Id => entry.Id;
    public string Title => entry.Track.Title;
    public string Path => entry.Track.Path;
    public string Metadata => string.Join(" · ", new[] { entry.Track.Artist, entry.Track.Album, entry.Track.FormatHint }.Where(v => !string.IsNullOrEmpty(v)));
    public string Duration => PlayerViewModel.FormatTime(entry.Track.DurationHint);
    public string Availability => entry.Track.Available ? "" : Strings.Get("Unavailable");
    [ObservableProperty] private bool _enabled = entry.Enabled;
    [ObservableProperty] private bool _isPlaying;
    public event Action? EligibilityChanged;
    partial void OnEnabledChanged(bool value) => EligibilityChanged?.Invoke();
}
