using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Player.Core.Library;

namespace Player.App.ViewModels;

public partial class PlaylistTabViewModel(Guid id, string name) : ObservableObject
{
    public Guid Id { get; } = id;
    [ObservableProperty] private string _name = name;
    public ObservableCollection<PlaylistRowViewModel> Entries { get; } = [];
    public PlaylistState Capture() => new(Id, Name, Entries.Select(e => e.Entry).ToArray());
}
