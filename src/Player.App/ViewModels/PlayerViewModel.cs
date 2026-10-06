using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows.Media;
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
    private readonly ILibraryIndexStore? _index;
    private readonly LibraryScanner? _scanner;
    private readonly LibraryWatcher _watcher;
    private readonly ArtworkService _artwork = new();
    private CancellationTokenSource? _scanCancellation, _artCancellation;
    private Task _scanTask = Task.CompletedTask, _artTask = Task.CompletedTask, _statisticsTask = Task.CompletedTask;
    private readonly Dictionary<Guid, int> _pendingRatings = [];
    private readonly HashSet<Guid> _pendingRoots = [];
    private readonly ListeningMeter _listening = new();
    private readonly long _clockOrigin = Stopwatch.GetTimestamp();
    private bool _applyingRatings;
    private Guid? _artEntry;
    public ObservableCollection<LibraryRoot> LibraryRoots { get; } = [];
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatus = "";
    [ObservableProperty] private ImageSource? _coverArt;
    public Task ScanCompletion => _scanTask;
    public Task ArtworkCompletion => _artTask;
    public ILibraryIndexStore? LibraryIndex => _index;
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
    public ObservableCollection<QueueItem> Queue { get; } = [];
    public RepeatMode[] RepeatModes { get; } = Enum.GetValues<RepeatMode>();
    [ObservableProperty] private RepeatMode _repeat;
    [ObservableProperty] private bool _shuffle;
    public ObservableCollection<PlaylistTabViewModel> Playlists { get; } = [];
    public ObservableCollection<PlaylistRowViewModel> Entries => SelectedPlaylist.Entries;
    public System.ComponentModel.ICollectionView VisibleEntries { get; private set; } = null!;
    public string ProductName => ProductInfo.Name;
    public string ProductVersion => ProductInfo.Version;
    private System.Text.Encoding? CueFallbackEncoding
    {
        get
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return WindowSettings.CueCodePage == 0 ? null : System.Text.Encoding.GetEncoding(WindowSettings.CueCodePage,
                System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback);
        }
    }
    public void RefreshLanguage()
    {
        State = Strings.Get("State" + Snapshot.State);
        OnPropertyChanged(nameof(PlayPauseLabel));
        var source = Playlists.FirstOrDefault(p => p.Id == _sourcePlaylistId);
        PlaybackSource = Snapshot.EntryId is null ? "" : source is null ? Strings.Get("DetachedSource") : string.Format(Strings.Culture, Strings.Get("PlaybackSource"), source.Name);
        var track = Snapshot.EntryId is { } id ? _knownRows.GetValueOrDefault(id)?.Entry.Track ?? _coordinator.ActiveEntry?.Track : null;
        if (track is null) Title = Strings.Get("NothingPlaying");
        else if (track.Artist is null) Artist = Strings.Get("UnknownArtist");
        if (_savedLibraryVersion == _libraryVersion) SaveStatus = Strings.Get("Saved");
        RefreshFormatLabels(Snapshot);
        if (WaveformStatus.Length > 0) WaveformStatus = Strings.Get(_waveTask.IsCompleted ? "WaveformUnavailable" : "WaveformLoading");
        foreach (var row in _knownRows.Values) row.RefreshLanguage();
        UpdatePlaylistStatus();
    }
    private PlayerSettings _windowSettings = new();
    public PlayerSettings WindowSettings
    {
        get => _windowSettings;
        set
        {
            var refresh = _windowSettings.ShowAlbumSections != value.ShowAlbumSections;
            _windowSettings = value;
            if (refresh && VisibleEntries is not null) UpdatePlaylistStatus();
        }
    }
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
    [ObservableProperty] private string _playbackSource = "";
    public string PlayPauseLabel => IsPlaying ? Strings.Get("Pause") : Strings.Get("Play");
    public bool SeekPreview { get; set; }
    public bool CanTransport => HasEntries || _player.Snapshot.EntryId is not null;
    public bool HasVisibleEntries => VisibleEntries is not null && !VisibleEntries.IsEmpty;
    public bool CanReorder => string.IsNullOrEmpty(Search) && !IsImporting;
    public Task ImportCompletion => _importTask;
    public PlaybackSnapshot Snapshot => _player.Snapshot;
    public Task HandleMediaAsync(string button)
    {
        if (_closing) return Task.CompletedTask;
        return button switch
        {
            "Play" => _player.Snapshot.State == PlaybackState.Playing ? Task.CompletedTask : PlayPauseAsync(),
            "Pause" => _coordinator.PauseAsync(), "Stop" => _coordinator.StopAsync(),
            "Next" => _coordinator.NextAsync(), "Previous" => _coordinator.PreviousAsync(), _ => Task.CompletedTask
        };
    }
    public Task WaveformCompletion => _waveTask;
    public Guid? SourcePlaylistId => _sourcePlaylistId;

    public PlayerViewModel(IAudioPlayer player, PlaybackCoordinator coordinator, IMediaImportService importer, IFileDialogService dialogs,
        Dispatcher dispatcher, IPlayerStore store, SettingsFile settings, IWaveformService waveforms)
    {
        _player = player; _coordinator = coordinator; _importer = importer; _dialogs = dialogs; _dispatcher = dispatcher;
        _store = store; _settings = settings; _waveforms = waveforms;
        _index = store as ILibraryIndexStore; _scanner = _index is null ? null : new(_index);
        _watcher = new(ids => { if (!_closing && !_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => ScanRoots(ids)); });
        var tab = new PlaylistTabViewModel(Guid.NewGuid(), Strings.Get("DefaultPlaylist")); Playlists.Add(tab); SelectedPlaylist = tab;
        _player.SnapshotChanged += OnSnapshot;
        _coordinator.OrderChanged += OnOrderChanged;
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
        if (state.Session.Order is { } order) _coordinator.RestoreOrder(order);
        Repeat = _coordinator.Repeat; Shuffle = _coordinator.Shuffle; RefreshQueue();
        if (_player is IAdvancedAudioPlayer advanced) { await advanced.SetProcessingAsync(WindowSettings.Processing ?? new()); await advanced.SetOutputAsync(WindowSettings.Output ?? new()); }
        await _coordinator.SetVolumeAsync(WindowSettings.Volume / 100, WindowSettings.Muted);
        if (state.Session.ActiveEntry is { } active)
            await _coordinator.RestoreAsync(active, TimeSpan.FromTicks(state.Session.PositionTicks));
        ApplySnapshot(_player.Snapshot);
        _initialized = true;
        AddFilesCommand.NotifyCanExecuteChanged(); AddFolderCommand.NotifyCanExecuteChanged();
        SaveStatus = Strings.Get("Saved");
        if (_index is not null)
        {
            foreach (var root in await _index.GetRootsAsync()) LibraryRoots.Add(root);
            await RefreshRatingsAsync(); _watcher.Watch(LibraryRoots);
        }
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
        row.RatingChanged += RatingChanged; tab.Entries.Add(row); _knownRows.Add(row.Id, row);
    }
    private void RowChanged() => UpdateEntries();
    private void SyncSource()
    {
        var source = Playlists.FirstOrDefault(p => p.Id == _sourcePlaylistId);
        _coordinator.SetEntries(source?.Entries.Select(e => e.Entry) ?? []);
    }
    private void ChooseSource()
    { _sourcePlaylistId = SelectedPlaylist.Id; SyncSource(); }

    public async Task AddLibraryRootAsync(string path)
    {
        if (_index is null || LibraryRoots.Count >= 100) return;
        Services.Audio.LocalFileAccess.ValidateDirectory(path);
        if (LibraryRoots.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        var root = new LibraryRoot(Guid.NewGuid(), path); await _index.PutRootAsync(root); LibraryRoots.Add(root); _watcher.Watch(LibraryRoots); ScanRoots([root.Id]);
    }
    public async Task DisableLibraryRootAsync(LibraryRoot root)
    { if (_index is null) return; var disabled = root with { Enabled = false }; await _index.PutRootAsync(disabled); LibraryRoots[LibraryRoots.IndexOf(root)] = disabled; _watcher.Watch(LibraryRoots); }
    public void ScanRoots(IEnumerable<Guid>? roots = null)
    {
        if (_closing || _scanner is null) return;
        foreach (var id in roots ?? LibraryRoots.Where(r => r.Enabled).Select(r => r.Id)) _pendingRoots.Add(id);
        if (!IsScanning) _scanTask = ScanPendingAsync();
    }
    public void CancelScan() { _pendingRoots.Clear(); _scanCancellation?.Cancel(); }
    private async Task ScanPendingAsync()
    {
        IsScanning = true; _scanCancellation = new();
        try
        {
            while (_pendingRoots.Count > 0)
            {
                var id = _pendingRoots.First(); _pendingRoots.Remove(id); var root = LibraryRoots.FirstOrDefault(r => r.Id == id && r.Enabled); if (root is null) continue;
                var known = _knownRows.Values.Where(r => r.Entry.Track.Segment is null).Select(r => r.Entry.Track).DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(t => t.Path, t => t.Id, StringComparer.OrdinalIgnoreCase);
                var progress = new Progress<ScanProgress>(p => { if (_closing) return; ScanStatus = string.Format(Strings.Culture, Strings.Get("ScanProgress"), p.Seen, p.Changed, p.Errors); if (p.Details.Length > 0) Details = string.Join(Environment.NewLine, p.Details); });
                await Task.Run(() => _scanner!.ScanAsync(root, known, progress, _scanCancellation.Token));
            }
            await ReconcilePlaylistMetadataAsync();
            if (_coordinator.ActiveEntry?.Track.Path is { } artworkPath) _artTask = LoadArtworkAsync(artworkPath);
        }
        catch (OperationCanceledException) { ScanStatus = Strings.Get("ScanCanceled"); }
        catch (Exception e) { ScanStatus = Strings.Get("ScanFailed"); Details = e.Message; }
        finally { IsScanning = false; _scanCancellation.Dispose(); _scanCancellation = null; }
    }
    private async Task ReconcilePlaylistMetadataAsync()
    {
        if (_index is null) return;
        var paths = _knownRows.Values.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var batch in paths.Chunk(64))
        {
            foreach (var file in await _index.FindFilesAsync(batch))
                foreach (var row in _knownRows.Values.Where(r => string.Equals(r.Path, file.Path, StringComparison.OrdinalIgnoreCase)).ToArray())
                    row.UpdateTrack(row.Entry.Track.Segment is null ? file.Track with { Id = row.Entry.Track.Id, Available = file.Available } : row.Entry.Track with { Available = file.Available });
        }
        _importer.RememberTracks(_knownRows.Values.Select(r => r.Entry.Track)); UpdateEntries();
    }
    public void AddLibraryTracks(IEnumerable<MediaTrack> tracks)
    {
        if (IsImporting || _closing) { Message = Strings.Get("WaitImport"); return; }
        foreach (var track in tracks.Take(10001)) { if (_knownRows.Count >= 10000) break; AddRow(SelectedPlaylist, new(Guid.NewGuid(), track, AddedUtcTicks: DateTime.UtcNow.Ticks)); }
        UpdateEntries(); _statisticsTask = ObserveAsync(RefreshRatingsAsync());
    }
    private async Task RefreshRatingsAsync()
    {
        if (_index is null) return; var stats = await _index.GetStatisticsAsync(_knownRows.Values.Select(r => r.Entry.Track.Id).Distinct().ToArray());
        _applyingRatings = true; try { foreach (var statistic in stats) foreach (var row in _knownRows.Values.Where(r => r.Entry.Track.Id == statistic.TrackId)) row.Rating = statistic.Rating; } finally { _applyingRatings = false; }
    }
    private void RatingChanged(PlaylistRowViewModel row)
    {
        if (_applyingRatings || _index is null || _closing) return;
        _applyingRatings = true; try { foreach (var duplicate in _knownRows.Values.Where(r => r.Entry.Track.Id == row.Entry.Track.Id)) duplicate.Rating = Math.Clamp(row.Rating, 0, 5); } finally { _applyingRatings = false; }
        _pendingRatings[row.Entry.Track.Id] = Math.Clamp(row.Rating, 0, 5);
        _statisticsTask = SaveRatingAfterAsync(_statisticsTask, row.Entry.Track.Id, Math.Clamp(row.Rating, 0, 5));
    }
    private async Task SaveRatingAfterAsync(Task preceding, Guid track, int rating)
    { await preceding; await ObserveAsync(_index!.SetRatingAsync(track, rating)); }
    public void SortPlaylist(string field)
    {
        if (!string.IsNullOrEmpty(Search) || IsImporting) { Message = Strings.Get("ClearBeforeSort"); return; }
        Func<PlaylistRowViewModel, object?> key = field switch
        {
            "Artist" => r => r.Entry.Track.Artist, "Album" => r => r.Entry.Track.Album, "Track" => r => r.Entry.Track.TrackNumber == 0 ? null : r.Entry.Track.DiscNumber * 100000L + r.Entry.Track.TrackNumber,
            "Duration" => r => r.Entry.Track.DurationHint?.Ticks, "Date added" => r => r.Entry.AddedUtcTicks == 0 ? null : r.Entry.AddedUtcTicks, "Path" => r => r.Path, _ => r => r.Title
        };
        var sorted = Entries.OrderBy(r => key(r) is null).ThenBy(key).ToArray();
        for (var i = 0; i < sorted.Length; i++) Entries.Move(Entries.IndexOf(sorted[i]), i); UpdateEntries();
    }
    public async Task ExportPlaylistAsync(string path)
    {
        if (!Path.GetExtension(path).Equals(".m3u8", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose an M3U8 document filename; audio files are never overwritten by export.");
        var text = Player.Core.Media.PlaylistDocument.ExportM3u8(Entries.Select(r => r.Entry), path);
        await Task.Run(() => { Services.Audio.LocalFileAccess.ValidateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N"); try { File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false, true)); File.Move(temporary, path, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); } });
    }
    public async Task RelinkAsync(PlaylistRowViewModel row, string path)
    {
        path = Services.Audio.BassSmokeSession.ValidateSourcePath(path);
        // Use an independent real decoder to validate replacement and CUE bounds without disturbing playback.
        await Task.Run(() => { using var context = new Services.Audio.NativeDecodeContext(); var source = Services.Audio.BassMixerGraph.OpenSource(new(row.Id, path, row.Entry.Track.Segment)); ManagedBass.Bass.StreamFree(source.Handle); });
        var track = row.Entry.Track with { Path = path, Available = true };
        if (_index is not null) await _index.RelinkAsync(track.Id, path);
        foreach (var duplicate in _knownRows.Values.Where(r => r.Entry.Track.Id == track.Id)) duplicate.UpdateTrack(track);
        _coordinator.RelinkSnapshots(track.Id, path); _importer.RememberTracks([track]); UpdateEntries(); _waveEntry = null; _artEntry = null; ApplySnapshot(_player.Snapshot); await SaveNowAsync();
    }
    partial void OnRepeatChanged(RepeatMode value) { _coordinator.Repeat = value; ScheduleSave(false); }
    partial void OnShuffleChanged(bool value) { _coordinator.Shuffle = value; ScheduleSave(false); }
    public void Enqueue(IEnumerable<PlaylistRowViewModel> rows, bool next)
    { _coordinator.Enqueue(rows.Select(r => r.Entry), next); RefreshQueue(); ScheduleSave(false); }
    public void RemoveQueued(Guid id) => _coordinator.RemoveQueued(id);
    public void MoveQueued(Guid id, int delta) => _coordinator.MoveQueued(id, delta);
    [RelayCommand] private void ClearQueue() => _coordinator.ClearQueue();
    [RelayCommand] private void ClearHistory() { _coordinator.ClearHistory(); if (_index is not null) _statisticsTask = ClearListeningAfterAsync(_statisticsTask); }
    private async Task ClearListeningAfterAsync(Task preceding) { await preceding; await ObserveAsync(_index!.ClearListeningAsync()); }
    private void OnOrderChanged()
    { if (!_closing && !_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => { RefreshQueue(); ScheduleSave(false); }, DispatcherPriority.Background); }
    private void RefreshQueue() { Queue.Clear(); foreach (var item in _coordinator.Queue) Queue.Add(item); }
    public Task<AudioDevice[]> GetDevicesAsync() => _player is IAdvancedAudioPlayer advanced ? advanced.GetDevicesAsync() : Task.FromResult(Array.Empty<AudioDevice>());
    public async Task ConfigureAudioAsync(AudioProcessingSettings processing, AudioOutputSettings output)
    {
        processing = processing.Validate();
        if (_player is IAdvancedAudioPlayer advanced) { await advanced.SetProcessingAsync(processing); await advanced.SetOutputAsync(output); }
        WindowSettings = WindowSettings with { Processing = processing, Output = output }; ScheduleSave(false);
    }
    [RelayCommand(CanExecute = nameof(CanImport))] private Task AddFilesAsync() => AddPathsAsync(_dialogs.PickFiles());
    [RelayCommand(CanExecute = nameof(CanImport))] private Task AddFolderAsync()
    { var folder = _dialogs.PickFolder(); return folder is null ? Task.CompletedTask : AddPathsAsync([folder]); }
    private bool CanImport() => !IsImporting && !_closing && _initialized;
    public bool CanAcceptFileDrop => CanImport();
    [RelayCommand] private void CancelImport() => _importCancellation?.Cancel();
    public Task AddPathsAsync(IEnumerable<string> paths, System.Text.Encoding? fallbackEncoding = null)
    {
        if (!CanImport()) return Task.CompletedTask;
        _importTask = ImportAsync(paths.Take(10001).ToArray(), SelectedPlaylist, fallbackEncoding); return _importTask;
    }
    public Task AddDroppedPathsAsync(IEnumerable<string> paths, bool createFolderPlaylists)
    {
        if (!CanImport()) return Task.CompletedTask;
        if (!createFolderPlaylists) return AddPathsAsync(paths);
        _importTask = ImportDroppedPathsAsync(paths.Take(10001).ToArray());
        return _importTask;
    }
    private async Task ImportDroppedPathsAsync(string[] paths)
    {
        if (paths.Length == 0) return;
        var originalTarget = SelectedPlaylist;
        IsImporting = true; _importCancellation = new CancellationTokenSource();
        try
        {
            foreach (var input in paths)
            {
                if (_closing || _importCancellation.IsCancellationRequested) return;
                if (_knownRows.Count >= 10000) { Message = Strings.Get("ImportLimit"); return; }
                string path;
                try
                {
                    // Match the importer: reject network/device paths before probing them.
                    path = Path.GetFullPath(input);
                    Player.Core.Media.LocalMediaPath.Parse(path.EndsWith('\\') ? path + "local-folder" : path);
                    if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) throw new IOException("Network drives are unsupported.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                { Message = Strings.Get("ErrorFileUnavailable"); Details = error.Message; continue; }
                if (Directory.Exists(path))
                {
                    if (Playlists.Count >= 100) { Message = Strings.Get("TabLimit"); return; }
                    try
                    {
                        Services.Audio.LocalFileAccess.ValidateDirectory(path);
                        var directory = new DirectoryInfo(path);
                        var name = directory.Name.Trim();
                        if (name.Length == 0) name = directory.FullName;
                        // Folder names can exceed the persisted playlist-name limit.
                        if (name.Length > 200) name = name[..(char.IsHighSurrogate(name[199]) ? 199 : 200)];
                        CreatePlaylist(name);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                    { Message = Strings.Get("ErrorFileUnavailable"); Details = error.Message; continue; }
                    await ImportBatchAsync([path], SelectedPlaylist, null);
                }
                else if (Playlists.Contains(originalTarget)) await ImportBatchAsync([path], originalTarget, null);
                // Progress callbacks belong to the captured target and finish before the next batch.
                await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }
        }
        finally { IsImporting = false; _importCancellation.Dispose(); _importCancellation = null; }
    }
    private async Task ImportAsync(string[] paths, PlaylistTabViewModel target, System.Text.Encoding? fallbackEncoding)
    {
        if (paths.Length == 0) return;
        IsImporting = true; _importCancellation = new CancellationTokenSource();
        try { await ImportBatchAsync(paths, target, fallbackEncoding); }
        finally { IsImporting = false; _importCancellation.Dispose(); _importCancellation = null; }
    }
    private async Task ImportBatchAsync(string[] paths, PlaylistTabViewModel target, System.Text.Encoding? fallbackEncoding)
    {
        var progress = new Progress<ImportProgress>(report =>
        {
            if (_closing || !Playlists.Contains(target)) return;
            foreach (var entry in report.Entries) { if (_knownRows.Count >= 10000) { _importCancellation?.Cancel(); Message = Strings.Get("ImportLimit"); break; } AddRow(target, entry); }
            UpdateEntries();
        });
        try
        {
            var result = await _importer.ImportAsync(paths, progress, _importCancellation!.Token, 10000 - _knownRows.Count, fallbackEncoding ?? CueFallbackEncoding);
            Message = string.Format(Strings.Culture, Strings.Get("Imported"), result.Added, result.Errors);
            if (result.LimitReached) Message += " " + Strings.Get("ImportLimit");
            Details = string.Join(Environment.NewLine, result.Details);
        }
        catch (OperationCanceledException) { Message = Strings.Get("ImportCanceled"); }
        catch (Exception error) { Message = Strings.Get("ErrorUnexpected"); Details = error.Message; }
        finally { await RefreshRatingsAsync(); }
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
        { row.EligibilityChanged -= RowChanged; row.RatingChanged -= RatingChanged; Entries.Remove(row); _knownRows.Remove(row.Id); }
        UpdateEntries();
    }
    public Task ExpandCueImagesAsync(IEnumerable<PlaylistRowViewModel> selected)
    {
        if (!CanImport()) return Task.CompletedTask;
        _importTask = ExpandCueImagesCoreAsync(selected.ToArray());
        return _importTask;
    }
    private async Task ExpandCueImagesCoreAsync(PlaylistRowViewModel[] selected)
    {
        if (!CanImport()) return;
        var target = SelectedPlaylist;
        var rows = selected.Where(row => row.Entry.Track.Segment is null &&
            Path.GetExtension(row.Path).Equals(".flac", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        if (rows.Length == 0) { Message = Strings.SelectFlacImage; return; }
        var details = new List<string>();
        void Diagnostic(string text) { if (details.Count < 20) details.Add(text[..Math.Min(text.Length, 1024)]); }
        var expanded = 0;
        IsImporting = true; _importCancellation = new CancellationTokenSource();
        try
        {
            var token = _importCancellation.Token;
            var fallback = CueFallbackEncoding;
            var discovery = new CueAlbumDiscovery(Diagnostic, token, fallback);
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var cue = await Task.Run(() => discovery.Find(row.Path), token);
                    if (cue is null) { Diagnostic(Strings.NoMatchingCue + " " + row.Path); continue; }
                    var imported = new List<PlaylistEntry>();
                    var summary = await _importer.ImportAsync([cue], new CollectedImportProgress(imported), token,
                        10000 - _knownRows.Count + 1, fallback);
                    token.ThrowIfCancellationRequested();
                    if (summary.Errors != 0 || summary.LimitReached || imported.Count < 2 || imported.Any(entry =>
                        entry.Track.Segment is null || !string.Equals(entry.Track.Path, row.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var detail in summary.Details) Diagnostic(detail);
                        Diagnostic(summary.LimitReached ? Strings.Get("ImportLimit") : Strings.NoMatchingCue);
                        continue;
                    }
                    if (_knownRows.Count - 1 + imported.Count > 10000) { Diagnostic(Strings.Get("ImportLimit")); continue; }
                    var position = target.Entries.IndexOf(row);
                    if (position < 0 || !Playlists.Contains(target)) continue;
                    // Keep active/queued occurrences intact in the coordinator, just as normal
                    // removal does. The replacement does not start or interrupt playback.
                    row.EligibilityChanged -= RowChanged; row.RatingChanged -= RatingChanged;
                    target.Entries.RemoveAt(position); _knownRows.Remove(row.Id);
                    PlaylistRowViewModel? first = null;
                    foreach (var entry in imported)
                    {
                        AddRow(target, entry with { Enabled = row.Enabled });
                        var added = target.Entries[^1]; first ??= added;
                        target.Entries.Move(target.Entries.Count - 1, position++);
                    }
                    if (ReferenceEquals(SelectedPlaylist, target)) SelectedEntry = first;
                    expanded++; UpdateEntries();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
                { Diagnostic(error.Message); }
            }
            await RefreshRatingsAsync();
            if (expanded > 0) await SaveNowAsync();
            Message = expanded > 0 ? string.Format(Strings.Culture, Strings.CueImagesExpanded, expanded) : Strings.NoMatchingCue;
            Details = string.Join(Environment.NewLine, details);
        }
        catch (OperationCanceledException) { Message = Strings.Get("ImportCanceled"); }
        finally { IsImporting = false; _importCancellation.Dispose(); _importCancellation = null; }
    }
    private sealed class CollectedImportProgress(List<PlaylistEntry> entries) : IProgress<ImportProgress>
    { public void Report(ImportProgress value) => entries.AddRange(value.Entries); }

    public void CreatePlaylist(string name)
    {
        name = ValidateName(name);
        if (Playlists.Count >= 100) { Message = Strings.Get("TabLimit"); return; }
        var tab = new PlaylistTabViewModel(Guid.NewGuid(), name); Playlists.Add(tab); SelectedPlaylist = tab; UpdateEntries();
    }
    public void RenamePlaylist(string name) { SelectedPlaylist.Name = ValidateName(name); ApplySnapshot(_player.Snapshot); ScheduleSave(true); }
    public void DuplicatePlaylist()
    {
        if (IsImporting) return;
        if (_knownRows.Count + Entries.Count > 10000 || Playlists.Count >= 100) { Message = Strings.Get("ImportLimit"); return; }
        var source = SelectedPlaylist;
        var tab = new PlaylistTabViewModel(Guid.NewGuid(), ValidateName(source.Name.Length <= 190 ? source.Name + Strings.Get("CopySuffix") : source.Name[..190] + Strings.Get("CopySuffix")));
        foreach (var row in source.Entries) AddRow(tab, row.Entry with { Id = Guid.NewGuid() });
        Playlists.Add(tab); SelectedPlaylist = tab; UpdateEntries();
    }
    public void DeletePlaylist()
    {
        if (IsImporting) return;
        var tab = SelectedPlaylist;
        foreach (var row in tab.Entries) { row.EligibilityChanged -= RowChanged; row.RatingChanged -= RatingChanged; _knownRows.Remove(row.Id); }
        Playlists.Remove(tab);
        if (Playlists.Count == 0) Playlists.Add(new(Guid.NewGuid(), Strings.Get("DefaultPlaylist")));
        SelectedPlaylist = Playlists[0]; UpdateEntries();
    }
    public void MoveTab(int delta)
    {
        var from = Playlists.IndexOf(SelectedPlaylist);
        var to = Math.Clamp(from + delta, 0, Playlists.Count - 1);
        MovePlaylist(SelectedPlaylist, to > from ? to + 1 : to);
    }
    public bool CanMovePlaylists => !_closing && !IsImporting && Playlists.Count > 1;
    /// <summary>Insertion index is measured before removing the dragged tab.</summary>
    public bool MovePlaylist(PlaylistTabViewModel tab, int insertionIndex)
    {
        var from = Playlists.IndexOf(tab);
        if (!CanMovePlaylists || from < 0 || insertionIndex < 0 || insertionIndex > Playlists.Count) return false;
        var to = insertionIndex > from ? insertionIndex - 1 : insertionIndex;
        if (to == from) return false;
        Playlists.Move(from, to); ScheduleSave(true);
        return true;
    }
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
            var sourceTab = Playlists.FirstOrDefault(p => p.Id == _sourcePlaylistId);
            PlaybackSource = snapshot.EntryId is null ? "" : sourceTab is null ? Strings.Get("DetachedSource") : string.Format(Strings.Culture, Strings.Get("PlaybackSource"), sourceTab.Name);
            if (snapshot.EntryId != _waveEntry && (snapshot.State == PlaybackState.Loading || snapshot.SourceFormat is null))
            {
                _waveCancellation?.Cancel(); ++_waveGeneration; _waveEntry = null;
                Waveform = null; WaveformStatus = Strings.Get(snapshot.State == PlaybackState.Loading ? "StateLoading" : "WaveformUnavailable");
            }
            var track = snapshot.EntryId is { } id && _knownRows.TryGetValue(id, out var row) ? row.Entry.Track : _coordinator.ActiveEntry?.Track;
            if (track is not null && snapshot.Duration is { } decodedDuration && snapshot.State != PlaybackState.Loading && snapshot.EntryId is { } decodedEntry && _knownRows.TryGetValue(decodedEntry, out var decodedRow) && decodedRow.Entry.Track.DurationHint != decodedDuration)
            { decodedRow.UpdateTrack(track with { DurationHint = decodedDuration }); ScheduleSave(true); }
            if (track is not null && snapshot.EntryId != _artEntry) { _artEntry = snapshot.EntryId; _artTask = LoadArtworkAsync(track.Path); }
            if (_index is not null && _listening.Update(snapshot, track?.Id, Stopwatch.GetElapsedTime(_clockOrigin), DateTime.UtcNow) is { } occurrence) _statisticsTask = SaveListeningAfterAsync(_statisticsTask, occurrence);
            if (track is not null) { Title = track.Title; Artist = track.Artist ?? Strings.Get("UnknownArtist"); Album = track.Album ?? ""; }
            RefreshFormatLabels(snapshot);
            if (snapshot.Error is { } error) { Message = Strings.Get(error.ResourceKey); Details = error.Detail; }
            else if (snapshot.EntryId is not null && !IsImporting) { Message = ""; Details = ""; }
            var active = snapshot.EntryId is { } entryId ? _knownRows.GetValueOrDefault(entryId) : null;
            if (!ReferenceEquals(_playingRow, active))
            { if (_playingRow is not null) _playingRow.IsPlaying = false; _playingRow = active; if (active is not null) active.IsPlaying = true; }
            if (track is not null && snapshot.SourceFormat is not null && snapshot.EntryId != _waveEntry && snapshot.State != PlaybackState.Loading)
            { _waveEntry = snapshot.EntryId; _waveTask = LoadWaveformAsync(track.Path, false); }
        }
        finally { _applyingSnapshot = false; }
        OnPropertyChanged(nameof(CanTransport));
        if (snapshot.State != _previousState || DateTime.UtcNow - _lastSessionSave >= TimeSpan.FromSeconds(10))
        { _previousState = snapshot.State; _lastSessionSave = DateTime.UtcNow; ScheduleSave(false); }
    }
    private void RefreshFormatLabels(PlaybackSnapshot snapshot)
    {
        Format = snapshot.SourceFormat is { } source ? string.Format(Strings.Culture, Strings.Get("SourceFormat"), source.Codec, source.SampleRate, source.Channels) : "";
        if (snapshot.SourceFormat?.BitDepth is { } bits) Format += " · " + string.Format(Strings.Culture, Strings.Get("SourceBitDepth"), bits);
        if (snapshot.OutputFormat is { } output) Format += " · " + string.Format(Strings.Culture, Strings.Get("OutputFormat"), output.SampleRate, output.Channels);
    }
    private async Task SaveListeningAfterAsync(Task preceding, ListeningEvent occurrence) { await preceding; await ObserveAsync(_index!.RecordListeningAsync(occurrence)); }
    private async Task LoadArtworkAsync(string path)
    {
        _artCancellation?.Cancel(); _artCancellation?.Dispose(); _artCancellation = new(); var token = _artCancellation.Token; CoverArt = null;
        try { var image = await _artwork.LoadAsync(path, token); if (!token.IsCancellationRequested && !_closing) CoverArt = image; }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!_closing && !token.IsCancellationRequested) Details = e.Message; }
    }
    private async Task LoadWaveformAsync(string path, bool refresh)
    {
        _waveCancellation?.Cancel(); _waveCancellation?.Dispose(); _waveCancellation = new();
        var token = _waveCancellation.Token; var generation = ++_waveGeneration;
        Waveform = null; WaveformStatus = Strings.Get("WaveformLoading");
        var progress = new Progress<double>(value => { if (!_closing && generation == _waveGeneration) WaveformStatus = string.Format(Strings.Culture, Strings.Get("WaveformProgress"), value); });
        try
        {
            var data = await _waveforms.AnalyzeAsync(path, progress, token, refresh);
            if (!_closing && generation == _waveGeneration) { Waveform = _coordinator.ActiveEntry?.Track.Segment is { } segment ? data.Slice(segment) : data; WaveformStatus = ""; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closing && generation == _waveGeneration) { WaveformStatus = Strings.Get("WaveformUnavailable"); Details = error.Message; } }
    }
    [RelayCommand] private Task RefreshWaveformAsync()
    { var path = _coordinator.ActiveEntry?.Track.Path; return path is null ? Task.CompletedTask : (_waveTask = LoadWaveformAsync(path, true)); }
    public async Task BackupAsync(string path)
    {
        await SaveNowAsync();
        await _saveGate.WaitAsync();
        try
        {
            // Export the committed settings paired with the saved database, not edits made while backup I/O awaits.
            var settings = await Task.Run(_settings.Load);
            if (System.IO.Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) await BackupBundle.CreateAsync(_store, settings, path);
            else { await _store.BackupAsync(path); await Task.Run(() => SettingsFile.Export(path + ".settings.json", settings)); }
        }
        finally { _saveGate.Release(); }
    }
    private void UpdateEntries(bool changed = true)
    { SyncSource(); VisibleEntries?.Refresh(); if (_initialized) ApplySnapshot(_player.Snapshot); HasEntries = Entries.Count > 0; OnPropertyChanged(nameof(CanTransport)); UpdatePlaylistStatus(); if (changed) ScheduleSave(true); }
    private void UpdatePlaylistStatus()
    {
        OnPropertyChanged(nameof(HasVisibleEntries));
        if (VisibleEntries is null) return;
        var rows = VisibleEntries.Cast<PlaylistRowViewModel>().ToArray();
        var sections = WindowSettings.ShowAlbumSections ? AlbumSections.Create(rows.Select(row => row.Entry.Track)) : [];
        var sectionIndex = 0;
        for (var i = 0; i < rows.Length; i++)
            rows[i].Section = sectionIndex < sections.Count && sections[sectionIndex].StartIndex == i ? sections[sectionIndex++] : null;
        PlaylistStatus = string.Format(Strings.Culture, Strings.Get("PlaylistCount"), rows.Length, Entries.Count);
    }
    private LibraryState Capture() => new(Playlists.Select(p => p.Capture()).ToArray(),
        new(SelectedPlaylist.Id, _sourcePlaylistId, _coordinator.ActiveEntry, _player.Snapshot.Position.Ticks, _coordinator.CaptureOrder()));
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
            if (_index is not null)
            {
                foreach (var (track, rating) in _pendingRatings.ToArray()) { await _index.SetRatingAsync(track, rating); if (_pendingRatings.GetValueOrDefault(track) == rating) _pendingRatings.Remove(track); }
            }
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
    public ValueTask DisposeAsync() => CloseAsync(true);
    public ValueTask DisposeWithoutSavingAsync() => CloseAsync(false);
    private async ValueTask CloseAsync(bool save)
    {
        if (_closing) return;
        _closing = true; _player.SnapshotChanged -= OnSnapshot; _coordinator.OrderChanged -= OnOrderChanged;
        _scanCancellation?.Cancel(); _artCancellation?.Cancel(); _importCancellation?.Cancel(); _waveCancellation?.Cancel(); _saveCancellation?.Cancel();
        await _importTask; await _waveTask; await _saveTask; await _scanTask; await _artTask; await _statisticsTask;
        try { if (save) await SaveNowAsync(); }
        catch { _closing = false; _player.SnapshotChanged += OnSnapshot; _coordinator.OrderChanged += OnOrderChanged; throw; }
        foreach (var row in _knownRows.Values) { row.EligibilityChanged -= RowChanged; row.RatingChanged -= RatingChanged; }
        _watcher.Dispose(); await _waveforms.DisposeAsync(); await _coordinator.DisposeAsync(); await _store.DisposeAsync();
        _waveCancellation?.Dispose(); _saveCancellation?.Dispose();
    }
}
