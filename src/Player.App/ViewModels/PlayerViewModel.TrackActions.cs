using Player.App.Resources;
using Player.Core.Playback;

namespace Player.App.ViewModels;

public partial class PlayerViewModel
{
    public string? ActiveSourcePath => _coordinator.ActiveEntry?.Track.Path;
    public bool CanPlayPreviousPlaylist => Playlists.Any(tab => tab.Id == WindowSettings.PreviousPlaylistId &&
        tab.Id != _sourcePlaylistId && tab.Entries.Any(row => row.Enabled && row.Entry.Track.Available));

    public bool CopyEntriesToPlaylist(IEnumerable<PlaylistRowViewModel> selected, Guid targetId)
    {
        var target = Playlists.FirstOrDefault(tab => tab.Id == targetId);
        var rows = selected.Distinct().Where(row => _knownRows.GetValueOrDefault(row.Id) == row).ToArray();
        if (target is null || rows.Length == 0 || IsImporting || _closing) return false;
        if (_knownRows.Count + rows.Length > 10000) { Message = Strings.Get("ImportLimit"); return false; }
        var previous = _applyingRatings; _applyingRatings = true;
        try
        {
            foreach (var row in rows)
            {
                AddRow(target, row.Entry with { Id = Guid.NewGuid(), AddedUtcTicks = DateTime.UtcNow.Ticks });
                target.Entries[^1].Rating = row.Rating;
            }
        }
        finally { _applyingRatings = previous; }
        UpdateEntries(); return true;
    }

    public void AddToFavorites(IEnumerable<PlaylistRowViewModel> selected)
    {
        if (IsImporting || _closing) return;
        var rows = selected.DistinctBy(row => row.Entry.Track.Id).Where(row => _knownRows.GetValueOrDefault(row.Id) == row).ToArray();
        if (rows.Length == 0) return;
        var target = Playlists.FirstOrDefault(tab => tab.Id == WindowSettings.FavoritesPlaylistId);
        target ??= Playlists.FirstOrDefault(tab => tab.Name == Strings.Get("FavoritesPlaylist") || tab.Name == "Избранное" || tab.Name == "Favorites");
        if (target is null)
        {
            if (Playlists.Count >= 100 || _knownRows.Count + rows.Length > 10000) { Message = Strings.Get("ImportLimit"); return; }
            target = new PlaylistTabViewModel(Guid.NewGuid(), Strings.Get("FavoritesPlaylist")); Playlists.Add(target);
        }
        WindowSettings = WindowSettings with { FavoritesPlaylistId = target.Id };
        var existing = target.Entries.Select(row => row.Entry.Track.Id).ToHashSet();
        CopyEntriesToPlaylist(rows.Where(row => !existing.Contains(row.Entry.Track.Id)), target.Id);
        ScheduleSave(true);
    }

    public async Task PlayPreviousPlaylistAsync()
    {
        if (!CanPlayPreviousPlaylist || _closing || IsImporting) return;
        var target = Playlists.Single(tab => tab.Id == WindowSettings.PreviousPlaylistId);
        SelectedPlaylist = target;
        await PlayEntryAsync(target.Entries.First(row => row.Enabled && row.Entry.Track.Available));
    }

    public void PlaceAfterPlaying(IEnumerable<PlaylistRowViewModel> selected)
    {
        if (!CanReorder || _sourcePlaylistId != SelectedPlaylist.Id || Snapshot.EntryId is not { } active) return;
        var current = Entries.FirstOrDefault(row => row.Id == active);
        if (current is null) return;
        DropEntries(selected.Where(row => row.Id != active), Entries.IndexOf(current) + 1);
    }

    public void SetEntriesEnabled(IEnumerable<PlaylistRowViewModel> selected, bool enabled)
    {
        if (_closing || IsImporting) return;
        _batchRowChanges = true;
        try { foreach (var row in selected.Where(row => _knownRows.GetValueOrDefault(row.Id) == row)) row.Enabled = enabled; }
        finally { _batchRowChanges = false; UpdateEntries(); }
    }

    public void MarkFilesUnavailable(IEnumerable<string> paths)
    {
        var removed = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _knownRows.Values.Where(row => removed.Contains(row.Path)))
            row.UpdateTrack(row.Entry.Track with { Available = false });
        UpdateEntries();
    }
}
