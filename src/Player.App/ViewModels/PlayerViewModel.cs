using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Player.App.Resources;
using Player.App.Services.Library;
using Player.App.Services.Windows;
using Player.Core;
using Player.Core.Playback;

namespace Player.App.ViewModels;

public partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAudioPlayer _player;
    private readonly PlaybackCoordinator _coordinator;
    private readonly IMediaImportService _importer;
    private readonly IFileDialogService _dialogs;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, PlaylistRowViewModel> _knownRows = [];
    private CancellationTokenSource? _importCancellation;
    private Task _importTask = Task.CompletedTask;
    private long _revision = -1;
    private bool _applyingSnapshot;
    private bool _closing;
    private long _seekRequest;
    private bool _pendingSeek;
    private PlaylistRowViewModel? _playingRow;
    public ObservableCollection<PlaylistRowViewModel> Entries { get; } = [];
    public System.ComponentModel.ICollectionView VisibleEntries { get; }
    public string ProductName => ProductInfo.Name;

    [ObservableProperty] private PlaylistRowViewModel? _selectedEntry;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _title = Strings.Get("NothingPlaying");
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private string _format = "";
    [ObservableProperty] private string _elapsed = "0:00";
    [ObservableProperty] private string _durationText = "—";
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private double _seekPosition;
    [ObservableProperty] private double _volume = 50;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _state = Strings.Get("StateEmpty");
    [ObservableProperty] private string _message = Strings.Get("EmptyPrompt");
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string _playlistStatus = "";
    public string PlayPauseLabel => IsPlaying ? Strings.Get("Pause") : Strings.Get("Play");
    public bool SeekPreview { get; set; }
    public bool CanTransport => HasEntries || _player.Snapshot.EntryId is not null;

    public PlayerViewModel(IAudioPlayer player, PlaybackCoordinator coordinator, IMediaImportService importer, IFileDialogService dialogs, Dispatcher dispatcher)
    {
        _player = player; _coordinator = coordinator; _importer = importer; _dialogs = dialogs; _dispatcher = dispatcher;
        VisibleEntries = CollectionViewSource.GetDefaultView(Entries);
        VisibleEntries.Filter = item => item is PlaylistRowViewModel row && PlaylistSearch.Matches(row.Entry.Track, Search);
        _player.SnapshotChanged += OnSnapshot;
        ApplySnapshot(player.Snapshot);
        UpdateEntries();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task AddFilesAsync() => AddPathsAsync(_dialogs.PickFiles());
    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task AddFolderAsync()
    {
        var folder = _dialogs.PickFolder();
        return folder is null ? Task.CompletedTask : AddPathsAsync([folder]);
    }
    private bool CanImport() => !IsImporting && !_closing;
    [RelayCommand] private void CancelImport() => _importCancellation?.Cancel();

    public Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (!CanImport()) return Task.CompletedTask;
        _importTask = ImportAsync(paths.Take(10001).ToArray());
        return _importTask;
    }
    private async Task ImportAsync(string[] paths)
    {
        if (paths.Length == 0) return;
        IsImporting = true;
        _importCancellation = new CancellationTokenSource();
        var progress = new Progress<ImportProgress>(report =>
        {
            if (_closing) return;
            foreach (var entry in report.Entries)
            {
                var row = new PlaylistRowViewModel(entry);
                row.EligibilityChanged += UpdateEntries;
                Entries.Add(row); _knownRows.Add(row.Id, row);
            }
            UpdateEntries();
        });
        try
        {
            var result = await _importer.ImportAsync(paths, progress, _importCancellation.Token, 10000 - Entries.Count);
            Message = string.Format(CultureInfo.CurrentCulture, Strings.Get("Imported"), result.Added, result.Errors);
            if (result.LimitReached) Message += " " + Strings.Get("ImportLimit");
            Details = string.Join(Environment.NewLine, result.Details);
        }
        catch (OperationCanceledException) { Message = Strings.Get("ImportCanceled"); }
        catch (Exception error) { Message = Strings.Get("ErrorUnexpected"); Details = error.Message; }
        finally { IsImporting = false; _importCancellation.Dispose(); _importCancellation = null; }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PlayEntryAsync(PlaylistRowViewModel? row) => row is null ? Task.CompletedTask : _coordinator.LoadAsync(row.Id);
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PlayPauseAsync() => IsPlaying ? _coordinator.PauseAsync() : _coordinator.PlayAsync(SelectedEntry?.Id);
    [RelayCommand(AllowConcurrentExecutions = true)] private Task StopAsync() => _coordinator.StopAsync();
    [RelayCommand(AllowConcurrentExecutions = true)] private Task NextAsync() => _coordinator.NextAsync();
    [RelayCommand(AllowConcurrentExecutions = true)] private Task PreviousAsync() => _coordinator.PreviousAsync();
    [RelayCommand] private void ToggleMute() => Muted = !Muted;
    [RelayCommand] private void ClearSearch() => Search = "";
    [RelayCommand] private void RemoveEntries(IEnumerable<PlaylistRowViewModel>? rows)
    {
        foreach (var row in rows?.ToArray() ?? []) { row.EligibilityChanged -= UpdateEntries; Entries.Remove(row); _knownRows.Remove(row.Id); }
        UpdateEntries();
    }

    public Task PrepareAsync(Guid entryId) => _coordinator.LoadAsync(entryId, false);
    public async Task CommitSeekAsync(double seconds)
    {
        if (_closing || !CanSeek || !double.IsFinite(seconds)) return;
        var request = ++_seekRequest;
        _pendingSeek = true;
        try { await _coordinator.SeekAsync(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, DurationSeconds))); }
        finally
        {
            if (request == _seekRequest) { _pendingSeek = false; ApplySnapshot(_player.Snapshot); }
        }
    }

    partial void OnSeekPositionChanged(double value)
    {
        if (!_applyingSnapshot && !SeekPreview) _ = ObserveAsync(CommitSeekAsync(value));
    }
    partial void OnVolumeChanged(double value) { if (!_applyingSnapshot && !_closing) _ = ObserveAsync(_coordinator.SetVolumeAsync(value / 100, Muted)); }
    partial void OnMutedChanged(bool value) { if (!_applyingSnapshot && !_closing) _ = ObserveAsync(_coordinator.SetVolumeAsync(Volume / 100, value)); }
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseLabel));
    partial void OnIsImportingChanged(bool value) { AddFilesCommand.NotifyCanExecuteChanged(); AddFolderCommand.NotifyCanExecuteChanged(); }
    partial void OnSearchChanged(string value) { VisibleEntries.Refresh(); UpdatePlaylistStatus(); }

    private void OnSnapshot(PlaybackSnapshot snapshot)
    {
        if (_closing || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() => ApplySnapshot(snapshot), DispatcherPriority.Background);
    }
    public void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        if (_closing || snapshot.Revision < _revision) return;
        _revision = snapshot.Revision;
        _applyingSnapshot = true;
        try
        {
            IsPlaying = snapshot.State == PlaybackState.Playing;
            State = Strings.Get("State" + snapshot.State);
            CanSeek = snapshot.CanSeek && snapshot.State is not (PlaybackState.Loading or PlaybackState.Error);
            DurationSeconds = Math.Max(0, snapshot.Duration?.TotalSeconds ?? 0);
            DurationText = FormatTime(snapshot.Duration);
            Elapsed = FormatTime(snapshot.Position);
            if (!SeekPreview && !_pendingSeek) SeekPosition = Math.Clamp(snapshot.Position.TotalSeconds, 0, DurationSeconds);
            Volume = snapshot.Volume * 100; Muted = snapshot.Muted;
            var track = snapshot.EntryId is { } id && _knownRows.TryGetValue(id, out var row) ? row.Entry.Track : _coordinator.ActiveEntry?.Track;
            if (track is not null)
            {
                Title = track.Title; Artist = track.Artist ?? Strings.Get("UnknownArtist"); Album = track.Album ?? "";
            }
            Format = snapshot.SourceFormat is { } source ? string.Format(CultureInfo.CurrentCulture, Strings.Get("SourceFormat"),
                source.Codec, source.SampleRate, source.Channels) : "";
            if (snapshot.SourceFormat?.BitDepth is { } bits) Format += " · " + string.Format(CultureInfo.CurrentCulture, Strings.Get("SourceBitDepth"), bits);
            if (snapshot.OutputFormat is { } output) Format += " · " + string.Format(CultureInfo.CurrentCulture, Strings.Get("OutputFormat"), output.SampleRate, output.Channels);
            if (snapshot.Error is { } error) { Message = Strings.Get(error.ResourceKey); Details = error.Detail; }
            else if (snapshot.EntryId is not null && !IsImporting) { Message = Strings.Get("SessionTemporary"); Details = ""; }
            var active = snapshot.EntryId is { } entryId ? _knownRows.GetValueOrDefault(entryId) : null;
            if (!ReferenceEquals(_playingRow, active))
            {
                if (_playingRow is not null) _playingRow.IsPlaying = false;
                _playingRow = active;
                if (active is not null) active.IsPlaying = true;
            }
        }
        finally { _applyingSnapshot = false; }
        OnPropertyChanged(nameof(CanTransport));
    }

    private void UpdateEntries() { _coordinator.SetEntries(Entries.Select(e => e.Entry)); HasEntries = Entries.Count > 0; OnPropertyChanged(nameof(CanTransport)); UpdatePlaylistStatus(); }
    private void UpdatePlaylistStatus() => PlaylistStatus = string.Format(CultureInfo.CurrentCulture, Strings.Get("PlaylistCount"), VisibleEntries.Cast<object>().Count(), Entries.Count);
    private async Task ObserveAsync(Task operation)
    {
        // UI fire-and-observe adapter; all native mutation remains in the engine queue.
        try { await operation; }
        catch (Exception error) { if (!_closing) { Message = Strings.Get("ErrorUnexpected"); Details = error.Message; } }
    }
    public static string FormatTime(TimeSpan? duration) => duration is not { } value ? "—" : value.TotalHours >= 1
        ? $"{(long)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(long)value.TotalMinutes}:{value.Seconds:00}";

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        _player.SnapshotChanged -= OnSnapshot;
        _importCancellation?.Cancel();
        await _importTask;
        foreach (var row in Entries) row.EligibilityChanged -= UpdateEntries;
        await _coordinator.DisposeAsync();
    }
}
