using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.Services.Audio;
using Player.App.Services.Library;
using Player.App.Services.Storage;
using Player.App.Services.Waveforms;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Real WPF close requests and native/SQLite release, with a controlled pending save.</summary>
internal static class WindowShutdownValidation
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static Task Idle(Window window) => window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private static void CheckDisabledSurface(MainWindow window)
    {
        window.IsEnabled = false; window.UpdateLayout();
        foreach (var name in new[] { "PlaylistList", "PlaylistTabs" })
        {
            var list = (ListBox)window.FindName(name);
            var surface = (Border)list.Template.FindName("ListSurface", list);
            Check(surface.Background is SolidColorBrush brush && brush.Color == ((SolidColorBrush)list.FindResource("BackgroundBrush")).Color,
                "Disabled playlist uses the light system background.");
        }
        window.IsEnabled = true;
    }

    internal static async Task<object> RunAsync(MainWindow original, string fixture, string output)
    {
        var directory = Path.Combine(output, "window-shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        try
        {
            foreach (var failSave in new[] { false, true })
            {
                var data = Path.Combine(directory, failSave ? "failed-save" : "pending-save"); Directory.CreateDirectory(data);
                var music = Path.Combine(data, "owned.wav"); File.Copy(fixture, music);
                var database = Path.Combine(data, "library.db");
                var store = new PendingSaveStore(new SqlitePlayerStore(database));
                var player = new SerializedAudioPlayer(() => new BassAudioBackend());
                var waveforms = new BassWaveformService(new WaveformCache(Path.Combine(data, "waveforms")));
                var model = new PlayerViewModel(player, new PlaybackCoordinator(player), new MediaImportService(), new FileDialogService(),
                    original.Dispatcher, store, new SettingsFile(data), waveforms);
                var window = new MainWindow { DataContext = model }; var closed = false;
                Task? response = null;
                try
                {
                    await model.InitializeAsync(); window.Show(); await model.AddPathsAsync([music]);
                    await model.PrepareAsync(model.Entries.Single().Id); await Idle(window);
                    await model.WaveformCompletion; await model.ArtworkCompletion; await model.SaveNowAsync();
                    Check(model.CanSeek, "Close control did not use actual native audio preparation.");
                    CheckDisabledSurface(window);
                    // Close-to-tray remains a reversible hide with live resources.
                    model.WindowSettings = model.WindowSettings with { CloseToTray = true };
                    window.Close();
                    Check(!window.IsVisible && window.IsEnabled && window.ShutdownCompletion.IsCompleted,
                        "Close-to-tray began resource shutdown.");
                    window.ShowAndActivate();
                    model.WindowSettings = model.WindowSettings with { CloseToTray = false };
                    var entry = model.Entries.Single().Id; var tab = model.SelectedPlaylist.Id;
                    var sidecars = new[] { database + "-wal", database + "-shm" }.ToDictionary(path => path, path =>
                    { using var lease = DataFileLease.OpenExisting(path, true)!; return lease.Identity; });
                    var nativeHandle = new WindowInteropHelper(window).Handle;
                    // Quiesce earlier debounce work before arming a one-shot final-save fault.
                    // Otherwise an old autosave can consume the fault and the final save legitimately succeeds.
                    await model.SaveCompletion;
                    if (failSave) response = DeclineUnsavedExitAsync(window);
                    model.RenamePlaylist("Latest unsaved close control"); model.Volume = 72;
                    model.Enqueue(model.Entries, false);
                    var queue = model.Queue.Select(item => item.Id).ToArray();
                    store.Arm(failSave);
                    var timer = Stopwatch.StartNew(); window.Close(); var hideMilliseconds = timer.Elapsed.TotalMilliseconds;
                    Check(!window.IsVisible && !window.IsEnabled && !window.ShutdownCompletion.IsCompleted,
                        "Actual close did not hide the window synchronously before the pending save.");
                    await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Idle(window);
                    window.Close(); window.ShowAndActivate(); await Idle(window);
                    Check(!window.IsVisible && store.GatedCalls == 1 && window.DesktopPanel?.IsVisible != true,
                        "Repeated close/restore reopened the shutting-down window or desktop panel.");
                    Check(IsWindow(nativeHandle), "Window was destroyed before its pending save finished.");
                    store.Release.TrySetResult();
                    await window.ShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(30));
                    if (response is not null)
                    {
                        await response;
                        Check(window.IsVisible && window.IsEnabled && model.Entries.Single().Id == entry && model.SelectedPlaylist.Id == tab,
                            "Declined unsaved exit did not restore the window with its playlist.");
                        await window.CloseForValidationAsync();
                    }
                    var closeMilliseconds = timer.Elapsed.TotalMilliseconds;
                    Check(closeMilliseconds < 2000, "Owned graceful close retained the Windows SQLite deletion retry delay.");
                    Check(store.ArmedSaveCalls == (failSave ? 2 : 1), "A cancelled autosave competed with the final close save.");
                    closed = true;
                    Check(!IsWindow(nativeHandle), "Graceful close retained the native window.");
                    using (File.Open(music, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    using (File.Open(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    foreach (var (path, identity) in sidecars)
                    { using var lease = DataFileLease.OpenExisting(path)!; Check(lease.Identity == identity, "Close deleted or replaced a pinned WAL/SHM file."); }
                    await using (var reopened = new SqlitePlayerStore(database))
                    {
                        var saved = await reopened.LoadAsync();
                        Check(saved.Playlists.Single().Name == "Latest unsaved close control" && saved.Playlists.Single().Entries.Single().Id == entry &&
                            saved.Session.SelectedPlaylistId == tab && saved.Session.ActiveEntry?.Id == entry &&
                            saved.Session.Order!.Queue.Select(item => item.Id).SequenceEqual(queue), "Close lost the latest playlist/session/queue.");
                    }
                    Check(new SettingsFile(data).Load().Volume == 72, "Close lost the latest settings.");
                    results.Add(new { SaveFailure = failSave, SameTurnHidden = true, HideMilliseconds = hideMilliseconds,
                        GracefulCloseMilliseconds = closeMilliseconds, ReopenVerificationMilliseconds = timer.Elapsed.TotalMilliseconds - closeMilliseconds,
                        WalSidecarIdentitiesRetained = true, PendingSavePreserved = true, RepeatedCloseAndRestoreGuard = true,
                        SaveFailureDialogRestoresWindow = failSave, NativeFilesReleased = true, LatestDataReopened = true });
                }
                finally
                {
                    store.Release.TrySetResult();
                    if (!closed) await window.CloseForValidationAsync();
                    await waveforms.DisposeAsync(); await player.DisposeAsync(); await store.DisposeAsync();
                }
            }
            var released = ((App)Application.Current).CreateModel(Path.Combine(directory, "already-released"));
            var releasedWindow = new MainWindow { DataContext = released };
            try
            {
                await released.InitializeAsync(); releasedWindow.Show(); await released.DisposeAsync();
                await releasedWindow.CloseForValidationAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(!releasedWindow.IsVisible, "Already released model caused reentrant close.");
            }
            finally { if (releasedWindow.IsVisible) await releasedWindow.CloseForValidationAsync(); }
            return new { Status = "window-shutdown-passed", DisabledListsKeepTheme = true, CloseToTrayPreserved = true, AlreadyReleasedModelClose = true, Cases = results,
                Scope = "Actual WPF/native decode/SQLite; controlled pending save, no physical driver latency guarantee" };
        }
        finally { Directory.Delete(directory, true); original.ShowAndActivate(); }
    }

    private sealed class PendingSaveStore(SqlitePlayerStore inner) : IPlayerStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int GatedCalls { get; private set; }
        public int ArmedSaveCalls { get; private set; }
        private bool _armed, _fail;
        private bool _countSaves;
        public void Arm(bool fail) { _armed = true; _fail = fail; _countSaves = true; }
        public Task<LibraryState> LoadAsync() => inner.LoadAsync();
        public async Task SaveAsync(LibraryState state, bool playlistsChanged)
        {
            if (_countSaves) ArmedSaveCalls++;
            if (_armed)
            {
                _armed = false; GatedCalls++; Entered.TrySetResult(); await Release.Task;
                if (_fail) throw new IOException("Owned shutdown save failure.");
            }
            await inner.SaveAsync(state, playlistsChanged);
        }
        public Task BackupAsync(string destination) => inner.BackupAsync(destination);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static Task DeclineUnsavedExitAsync(MainWindow window)
    {
        var owner = new WindowInteropHelper(window).Handle;
        return Task.Run(async () =>
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                var dialog = GetLastActivePopup(owner); var name = new StringBuilder(64);
                GetClassName(dialog, name, name.Capacity); GetWindowThreadProcessId(dialog, out var process);
                if (dialog != owner && GetWindow(dialog, 4) == owner && process == Environment.ProcessId && name.ToString() == "#32770")
                {
                    try { await window.Dispatcher.InvokeAsync(() => Check(window.IsVisible && window.IsEnabled,
                        "Save failure opened a dialog against the hidden/disabled main window.")); }
                    finally { SendMessage(dialog, 0x0111, 7, 0); } // WM_COMMAND / IDNO, only this owned warning.
                    return;
                }
                await Task.Delay(20);
            }
            throw new TimeoutException("Owned save-failure warning did not appear.");
        });
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern nint GetLastActivePopup(nint window);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int maximum);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint parameter, nint data);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
}
