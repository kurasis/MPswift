using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Player.App.Resources;
using Player.App.Services.Library;
using Player.App.Services.Storage;
using Player.App.Services.Windows;
using Player.Core;
using Player.Core.Library;
using Player.Core.Playback;
using Player.Core.Waveforms;

namespace Player.App.ViewModels;

public partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAudioPlayer _player;
    private readonly PlaybackCoordinator _coordinator;
    private readonly IMediaImportService _importer;
    private readonly IFileDialogService _dialogs;
    private readonly Dispatcher _dispatcher;
    private readonly IPlayerStore _store;
    private readonly SettingsFile _settings;
    private readonly IWaveformService _waveforms;
    private readonly Dictionary<Guid, PlaylistRowViewModel> _knownRows = [];
    private readonly SemaphoreSlim _saveGate = new(1);
    private CancellationTokenSource? _importCancellation;
    private CancellationTokenSource? _saveCancellation;
    private CancellationTokenSource? _waveCancellation;
    private Task _importTask = Task.CompletedTask;
    private Task _saveTask = Task.CompletedTask;
    private Task _waveTask = Task.CompletedTask;
    private long _revision = -1;
    private bool _applyingSnapshot;
    private bool _closing;
    private bool _initialized;
    private long _seekRequest;
    private bool _pendingSeek;
    private PlaylistRowViewModel? _playingRow;
    private Guid? _sourcePlaylistId;
    private Guid? _waveEntry;
    private long _waveGeneration;
    private long _libraryVersion;
    private long _savedLibraryVersion;
    private DateTime _lastSessionSave = DateTime.UtcNow;
    private PlaybackState _previousState;
    public ObservableCollection<PlaylistTabViewModel> Playlists { get; } = [];
    public ObservableCollection<PlaylistRowViewModel> Entries => SelectedPlaylist.Entries;
    public System.ComponentModel.ICollectionView VisibleEntries { get; private set; } = null!;
    public string ProductName => ProductInfo.Name;
    public PlayerSettings WindowSettings { get; set; } = new();
    public bool Initialized => _initialized;

    [ObservableProperty] private PlaylistTabViewModel _selectedPlaylist = null!;
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
    [ObservableProperty] private WaveformData? _waveform;
    [ObservableProperty] private string _waveformStatus = "";
    [ObservableProperty] private string _saveStatus = "";
    public string PlayPauseLabel => IsPlaying ? Strings.Get("Pause") : Strings.Get("Play");
    public bool SeekPreview { get; set; }
    public bool CanTransport => HasEntries || _player.Snapshot.EntryId is not null;
    public bool CanReorder => string.IsNullOrEmpty(Search) && !IsImporting;
    public Task WaveformCompletion => _waveTask;
    public Guid? SourcePlaylistId => _sourcePlaylistId;

    public PlayerViewModel(IAudioPlayer player, PlaybackCoordinator coordinator, IMediaImportService importer, IFileDialogService dialogs,
        Dispatcher dispatcher, IPlayerStore store, SettingsFile settings, IWaveformService waveforms)
    {
        _player = player; _coordinator = coordinator; _importer = importer; _dialogs = dialogs; _dispatcher = dispatcher;
        _store = store; _settings = settings; _waveforms = waveforms;
        var tab = new PlaylistTabViewModel(Guid.NewGuid(), "Default"); Playlists.Add(tab); SelectedPlaylist = tab;
        _player.SnapshotChanged += OnSnapshot;
        ApplySnapshot(player.Snapshot);
    }
    public async Task InitializeAsync()
    {
        WindowSettings = await Task.Run(_settings.Load);
        var state = await _store.LoadAsync();
        Playlists.Clear(); _knownRows.Clear();
        foreach (var playlist in state.Playlists)
        {
            var tab = new PlaylistTabViewModel(playlist.Id, playlist.Name);
            foreach (var entry in playlist.Entries) AddRow(tab, entry);
            Playlists.Add(tab);
        }
        _importer.RememberTracks(state.Playlists.SelectMany(p => p.Entries).Select(e => e.Track));
        SelectedPlaylist = Playlists.FirstOrDefault(p => p.Id == state.Session.SelectedPlaylistId) ?? Playlists[0];
        _sourcePlaylistId = state.Session.SourcePlaylistId;
        SyncSource();
        await _coordinator.SetVolumeAsync(WindowSettings.Volume / 100, WindowSettings.Muted);
        if (state.Session.ActiveEntry is { } active)
            await _coordinator.RestoreAsync(active, TimeSpan.FromTicks(state.Session.PositionTicks));
        ApplySnapshot(_player.Snapshot);
        _initialized = true;
        AddFilesCommand.NotifyCanExecuteChanged(); AddFolderCommand.NotifyCanExecuteChanged();
        SaveStatus = Strings.Get("Saved");
    }
    partial void OnSelectedPlaylistChanged(PlaylistTabViewModel value)
    {
        if (value is null) return;
        VisibleEntries = CollectionViewSource.GetDefaultView(value.Entries);
        VisibleEntries.Filter = item => item is PlaylistRowViewModel row && PlaylistSearch.Matches(row.Entry.Track, Search);
        SelectedEntry = null;
        OnPropertyChanged(nameof(Entries)); OnPropertyChanged(nameof(VisibleEntries));
        UpdateEntries(false); ScheduleSave(false);
    }
    private void AddRow(PlaylistTabViewModel tab, PlaylistEntry entry)
    {
        var row = new PlaylistRowViewModel(entry); row.EligibilityChanged += RowChanged;
        tab.Entries.Add(row); _knownRows.Add(row.Id, row);
    }
    private void RowChanged() => UpdateEntries();
    private void SyncSource()
    {
        var source = Playlists.FirstOrDefault(p => p.Id == _sourcePlaylistId);
        _coordinator.SetEntries(source?.Entries.Select(e => e.Entry) ?? []);
    }
    private void ChooseSource()
    { _sourcePlaylistId = SelectedPlaylist.Id; SyncSource(); }

    [RelayCommand(CanExecute = nameof(CanImport))] private Task AddFilesAsync() => AddPathsAsync(_dialogs.PickFiles());
    [RelayCommand(CanExecute = nameof(CanImport))] private Task AddFolderAsync()
    { var folder = _dialogs.PickFolder(); return folder is null ? Task.CompletedTask : AddPathsAsync([folder]); }
    private bool CanImport() => !IsImporting && !_closing && _initialized;
    [RelayCommand] private void CancelImport() => _importCancellation?.Cancel();
    public Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (!CanImport()) return Task.CompletedTask;
        _importTask = ImportAsync(paths.Take(10001).ToArray(), SelectedPlaylist); return _importTask;
    }
    private async Task ImportAsync(string[] paths, PlaylistTabViewModel target)
    {
        if (paths.Length == 0) return;
        IsImporting = true; _importCancellation = new CancellationTokenSource();
        var progress = new Progress<ImportProgress>(report =>
        {
            if (_closing || !Playlists.Contains(target)) return;
            foreach (var entry in report.Entries) AddRow(target, entry);
            UpdateEntries();
        });
        try
        {
            var result = await _importer.ImportAsync(paths, progress, _importCancellation.Token, 10000 - _knownRows.Count);
            Message = string.Format(CultureInfo.CurrentCulture, Strings.Get("Imported"), result.Added, result.Errors);
            if (result.LimitReached) Message += " " + Strings.Get("ImportLimit");
            Details = string.Join(Environment.NewLine, result.Details);
        }
        catch (OperationCanceledException) { Message = Strings.Get("ImportCanceled"); }
        catch (Exception error) { Message = Strings.Get("ErrorUnexpected"); Details = error.Message; }
        finally { IsImporting = false; _importCancellation.Dispose(); _importCancellation = null; }
    }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task PlayEntryAsync(PlaylistRowViewModel? row)
    { if (row is null) return Task.CompletedTask; ChooseSource(); return _coordinator.LoadAsync(row.Id); }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task PlayPauseAsync()
    {
        if (IsPlaying) return _coordinator.PauseAsync();
        var snapshot = _player.Snapshot;
        if (snapshot.State is not (PlaybackState.Paused or PlaybackState.DeviceUnavailable) && (SelectedEntry is not null || snapshot.EntryId is null)) ChooseSource();
        return _coordinator.PlayAsync(SelectedEntry?.Id);
    }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task StopAsync() => _coordinator.StopAsync();
    [RelayCommand(AllowConcurrentExecutions = true)] private Task NextAsync() => _coordinator.NextAsync();
    [RelayCommand(AllowConcurrentExecutions = true)] private Task PreviousAsync() => _coordinator.PreviousAsync();
    [RelayCommand] private void ToggleMute() => Muted = !Muted;
    [RelayCommand] private void ClearSearch() => Search = "";
    [RelayCommand] private void RemoveEntries(IEnumerable<PlaylistRowViewModel>? rows)
    {
        foreach (var row in rows?.ToArray() ?? [])
        { row.EligibilityChanged -= RowChanged; Entries.Remove(row); _knownRows.Remove(row.Id); }
        UpdateEntries();
    }
    public void CreatePlaylist(string name)
    {
        name = ValidateName(name);
        if (Playlists.Count >= 100) { Message = Strings.Get("TabLimit"); return; }
        var tab = new PlaylistTabViewModel(Guid.NewGuid(), name); Playlists.Add(tab); SelectedPlaylist = tab; UpdateEntries();
    }
    public void RenamePlaylist(string name) { SelectedPlaylist.Name = ValidateName(name); ScheduleSave(true); }
    public void DuplicatePlaylist()
    {
        if (_knownRows.Count + Entries.Count > 10000 || Playlists.Count >= 100) { Message = Strings.Get("ImportLimit"); return; }
        var source = SelectedPlaylist;
        var tab = new PlaylistTabViewModel(Guid.NewGuid(), ValidateName(source.Name.Length <= 190 ? source.Name + " (copy)" : source.Name[..190] + " (copy)"));
        foreach (var row in source.Entries) AddRow(tab, row.Entry with { Id = Guid.NewGuid() });
        Playlists.Add(tab); SelectedPlaylist = tab; UpdateEntries();
    }
    public void DeletePlaylist()
    {
        if (IsImporting) return;
        var tab = SelectedPlaylist;
        foreach (var row in tab.Entries) { row.EligibilityChanged -= RowChanged; _knownRows.Remove(row.Id); }
        Playlists.Remove(tab);
        if (Playlists.Count == 0) Playlists.Add(new(Guid.NewGuid(), "Default"));
        SelectedPlaylist = Playlists[0]; UpdateEntries();
    }
    public void MoveTab(int delta)
    { var from = Playlists.IndexOf(SelectedPlaylist); var to = Math.Clamp(from + delta, 0, Playlists.Count - 1); Playlists.Move(from, to); ScheduleSave(true); }
    public void MoveEntries(IEnumerable<PlaylistRowViewModel> rows, int delta)
    {
        if (!CanReorder) { Message = Strings.Get("ReorderFiltered"); return; }
        var selected = rows.ToHashSet();
        var indices = Enumerable.Range(0, Entries.Count).Where(i => selected.Contains(Entries[i])).ToArray();
        if (delta > 0) Array.Reverse(indices);
        foreach (var index in indices)
        {
            var target = index + Math.Sign(delta);
            if (target >= 0 && target < Entries.Count && !selected.Contains(Entries[target])) Entries.Move(index, target);
        }
        UpdateEntries();
    }
    public void DropEntries(IEnumerable<PlaylistRowViewModel> rows, int insertion)
    {
        if (!CanReorder) { Message = Strings.Get("ReorderFiltered"); return; }
        var selected = rows.ToHashSet(); var ordered = Entries.Where(selected.Contains).ToArray();
        insertion = Math.Clamp(insertion, 0, Entries.Count);
        var adjusted = insertion - Entries.Take(insertion).Count(selected.Contains);
        foreach (var row in ordered) Entries.Remove(row);
        foreach (var row in ordered) Entries.Insert(adjusted++, row);
        UpdateEntries();
    }
    private static string ValidateName(string name)
    { name = name.Trim(); if (name.Length is < 1 or > 200) throw new ArgumentException("Playlist name must contain 1–200 characters."); return name; }
    public Task PrepareAsync(Guid entryId) { ChooseSource(); return _coordinator.LoadAsync(entryId, false); }
    public async Task CommitSeekAsync(double seconds)
    {
        if (_closing || !CanSeek || !double.IsFinite(seconds)) return;
        var request = ++_seekRequest; _pendingSeek = true;
        try { await _coordinator.SeekAsync(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, DurationSeconds))); }
        finally { if (request == _seekRequest) { _pendingSeek = false; ApplySnapshot(_player.Snapshot); ScheduleSave(false); } }
    }
    partial void OnSeekPositionChanged(double value) { if (!_applyingSnapshot && !SeekPreview) _ = ObserveAsync(CommitSeekAsync(value)); }
    partial void OnVolumeChanged(double value)
    { if (!_applyingSnapshot && !_closing) { _ = ObserveAsync(_coordinator.SetVolumeAsync(value / 100, Muted)); ScheduleSave(false); } }
    partial void OnMutedChanged(bool value)
    { if (!_applyingSnapshot && !_closing) { _ = ObserveAsync(_coordinator.SetVolumeAsync(Volume / 100, value)); ScheduleSave(false); } }
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseLabel));
    partial void OnIsImportingChanged(bool value)
    { AddFilesCommand.NotifyCanExecuteChanged(); AddFolderCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanReorder)); }
    partial void OnSearchChanged(string value) { VisibleEntries?.Refresh(); UpdatePlaylistStatus(); OnPropertyChanged(nameof(CanReorder)); }
    private void OnSnapshot(PlaybackSnapshot snapshot)
    {
        if (_closing || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() => ApplySnapshot(snapshot), DispatcherPriority.Background);
    }
    public void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        if (_closing || snapshot.Revision < _revision) return;
        _revision = snapshot.Revision; _applyingSnapshot = true;
        try
        {
            IsPlaying = snapshot.State == PlaybackState.Playing; State = Strings.Get("State" + snapshot.State);
            CanSeek = snapshot.CanSeek && snapshot.State is not (PlaybackState.Loading or PlaybackState.Error);
            DurationSeconds = Math.Max(0, snapshot.Duration?.TotalSeconds ?? 0); DurationText = FormatTime(snapshot.Duration); Elapsed = FormatTime(snapshot.Position);
            if (!SeekPreview && !_pendingSeek) SeekPosition = Math.Clamp(snapshot.Position.TotalSeconds, 0, DurationSeconds);
            Volume = snapshot.Volume * 100; Muted = snapshot.Muted;
            var track = snapshot.EntryId is { } id && _knownRows.TryGetValue(id, out var row) ? row.Entry.Track : _coordinator.ActiveEntry?.Track;
            if (track is not null) { Title = track.Title; Artist = track.Artist ?? Strings.Get("UnknownArtist"); Album = track.Album ?? ""; }
            Format = snapshot.SourceFormat is { } source ? string.Format(CultureInfo.CurrentCulture, Strings.Get("SourceFormat"), source.Codec, source.SampleRate, source.Channels) : "";
            if (snapshot.SourceFormat?.BitDepth is { } bits) Format += " · " + string.Format(CultureInfo.CurrentCulture, Strings.Get("SourceBitDepth"), bits);
            if (snapshot.OutputFormat is { } output) Format += " · " + string.Format(CultureInfo.CurrentCulture, Strings.Get("OutputFormat"), output.SampleRate, output.Channels);
            if (snapshot.Error is { } error) { Message = Strings.Get(error.ResourceKey); Details = error.Detail; }
            else if (snapshot.EntryId is not null && !IsImporting) { Message = ""; Details = ""; }
            var active = snapshot.EntryId is { } entryId ? _knownRows.GetValueOrDefault(entryId) : null;
            if (!ReferenceEquals(_playingRow, active))
            { if (_playingRow is not null) _playingRow.IsPlaying = false; _playingRow = active; if (active is not null) active.IsPlaying = true; }
            if (track is not null && snapshot.EntryId != _waveEntry && snapshot.State != PlaybackState.Loading)
            { _waveEntry = snapshot.EntryId; _waveTask = LoadWaveformAsync(track.Path, false); }
        }
        finally { _applyingSnapshot = false; }
        OnPropertyChanged(nameof(CanTransport));
        if (snapshot.State != _previousState || DateTime.UtcNow - _lastSessionSave >= TimeSpan.FromSeconds(10))
        { _previousState = snapshot.State; _lastSessionSave = DateTime.UtcNow; ScheduleSave(false); }
    }
    private async Task LoadWaveformAsync(string path, bool refresh)
    {
        _waveCancellation?.Cancel(); _waveCancellation?.Dispose(); _waveCancellation = new();
        var token = _waveCancellation.Token; var generation = ++_waveGeneration;
        Waveform = null; WaveformStatus = Strings.Get("WaveformLoading");
        var progress = new Progress<double>(value => { if (!_closing && generation == _waveGeneration) WaveformStatus = string.Format(CultureInfo.CurrentCulture, Strings.Get("WaveformProgress"), value); });
        try
        {
            var data = await _waveforms.AnalyzeAsync(path, progress, token, refresh);
            if (!_closing && generation == _waveGeneration) { Waveform = data; WaveformStatus = ""; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closing && generation == _waveGeneration) { WaveformStatus = Strings.Get("WaveformUnavailable"); Details = error.Message; } }
    }
    [RelayCommand] private Task RefreshWaveformAsync()
    { var path = _coordinator.ActiveEntry?.Track.Path; return path is null ? Task.CompletedTask : (_waveTask = LoadWaveformAsync(path, true)); }
    public async Task BackupAsync(string path)
    {
        await SaveNowAsync(); await _store.BackupAsync(path);
        var settings = WindowSettings with { Volume = Volume, Muted = Muted };
        await Task.Run(() => SettingsFile.Export(path + ".settings.json", settings));
    }
    private void UpdateEntries(bool changed = true)
    { SyncSource(); HasEntries = Entries.Count > 0; OnPropertyChanged(nameof(CanTransport)); UpdatePlaylistStatus(); if (changed) ScheduleSave(true); }
    private void UpdatePlaylistStatus()
    { if (VisibleEntries is not null) PlaylistStatus = string.Format(CultureInfo.CurrentCulture, Strings.Get("PlaylistCount"), VisibleEntries.Cast<object>().Count(), Entries.Count); }
    private LibraryState Capture() => new(Playlists.Select(p => p.Capture()).ToArray(),
        new(SelectedPlaylist.Id, _sourcePlaylistId, _coordinator.ActiveEntry, _player.Snapshot.Position.Ticks));
    private void ScheduleSave(bool libraryChanged)
    {
        if (!_initialized || _closing) return;
        if (libraryChanged) _libraryVersion++;
        _saveCancellation?.Cancel(); _saveCancellation?.Dispose(); _saveCancellation = new();
        SaveStatus = Strings.Get("Saving");
        _saveTask = SaveAfterDelayAsync(_saveCancellation.Token);
    }
    private async Task SaveAfterDelayAsync(CancellationToken token)
    {
        try { await Task.Delay(750, token); await SaveNowAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { SaveStatus = Strings.Get("SaveFailed"); Message = SaveStatus; Details = error.Message; }
    }
    public async Task SaveNowAsync()
    {
        if (!_initialized) return;
        await _saveGate.WaitAsync();
        try
        {
            var state = Capture(); var version = _libraryVersion;
            var settings = WindowSettings with { Volume = Volume, Muted = Muted };
            await _store.SaveAsync(state, version != _savedLibraryVersion);
            await Task.Run(() => _settings.Save(settings));
            _savedLibraryVersion = version;
            if (_libraryVersion == version) SaveStatus = Strings.Get("Saved");
        }
        finally { _saveGate.Release(); }
    }
    private async Task ObserveAsync(Task operation)
    { try { await operation; } catch (Exception error) { if (!_closing) { Message = Strings.Get("ErrorUnexpected"); Details = error.Message; } } }
    public static string FormatTime(TimeSpan? duration) => duration is not { } value ? "—" : value.TotalHours >= 1
        ? $"{(long)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(long)value.TotalMinutes}:{value.Seconds:00}";
    public async ValueTask DisposeAsync()
    {
        if (_closing) return;
        _closing = true; _player.SnapshotChanged -= OnSnapshot;
        _importCancellation?.Cancel(); _waveCancellation?.Cancel(); _saveCancellation?.Cancel();
        await _importTask; await _waveTask; await _saveTask;
        try { await SaveNowAsync(); }
        catch { _closing = false; _player.SnapshotChanged += OnSnapshot; throw; }
        foreach (var row in _knownRows.Values) row.EligibilityChanged -= RowChanged;
        await _waveforms.DisposeAsync(); await _coordinator.DisposeAsync(); await _store.DisposeAsync();
        _waveCancellation?.Dispose(); _saveCancellation?.Dispose();
    }
}
