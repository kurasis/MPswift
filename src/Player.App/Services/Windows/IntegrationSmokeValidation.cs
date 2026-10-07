using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Controls;
using Player.App.Resources;
using Player.App.Services.Audio;
using Player.App.Services.Storage;
using Player.App.ViewModels;
using Player.App.Views;

namespace Player.App.Services.Windows;

public static class IntegrationSmokeValidation
{
    public static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Check(window.Title == "MPswift" && window.Icon is not null, "Product title/window icon missing.");
        // Construct the production command under an owned directory containing a hostile executable name; never run it.
        var revealDirectory = Path.Combine(Path.GetTempPath(), "mpswift-reveal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(revealDirectory);
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            File.WriteAllText(Path.Combine(revealDirectory, "explorer.exe"), "owned non-executable shadow probe");
            Environment.CurrentDirectory = revealDirectory;
            var reveal = MainWindow.CreateShowFileStartInfo(fixture);
            Check(reveal.FileName == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") && Path.IsPathFullyQualified(reveal.FileName), "Explorer command could search the working directory/PATH.");
            Check(!reveal.UseShellExecute && reveal.ArgumentList.SequenceEqual(new[] { "/select,", LocalFileAccess.ValidateFile(fixture) }), "Source path was interpolated into a shell command.");
        }
        finally { Environment.CurrentDirectory = previousDirectory; Directory.Delete(revealDirectory, true); }
        var appHost = Path.Combine(AppContext.BaseDirectory, "MPswift.exe");
        Check(FileVersionInfo.GetVersionInfo(appHost).ProductName == "MPswift", "Apphost product metadata was not renamed.");
        using (var executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(appHost))
            Check(executableIcon is not null, "Apphost icon resource missing.");
        // These child processes must refuse before reaching the running player's IPC or clipboard.
        var rejectedDirectory = Path.Combine(Path.GetTempPath(), UiSmokeWorkspace.Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rejectedDirectory);
        try
        {
            var clipboard = Clipboard.GetDataObject();
            var clipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            foreach (var rejectedCase in new[] { "missing-marker", "wrong-token", "foreign-file" })
            {
                if (rejectedCase != "missing-marker")
                    File.WriteAllText(Path.Combine(rejectedDirectory, UiSmokeWorkspace.Marker), rejectedCase == "wrong-token" ? Guid.NewGuid().ToString("N") : Path.GetFileName(rejectedDirectory)[UiSmokeWorkspace.Prefix.Length..]);
                if (rejectedCase == "foreign-file") File.WriteAllText(Path.Combine(rejectedDirectory, "personal.txt"), "preserve me");
                var beforeFiles = Directory.GetFiles(rejectedDirectory, "*", SearchOption.AllDirectories);
                var beforeBytes = beforeFiles.ToDictionary(path => path, File.ReadAllBytes);
                var start = new ProcessStartInfo(appHost) { UseShellExecute = false, WorkingDirectory = rejectedDirectory };
                foreach (var argument in new[] { "--ui-smoke", fixture, fixture }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Rejected UI smoke did not start.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    Check(process.ExitCode == 2, "Unowned UI smoke reached validation or IPC instead of refusing.");
                }
                catch { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } throw; }
                Check(beforeFiles.SequenceEqual(Directory.GetFiles(rejectedDirectory, "*", SearchOption.AllDirectories)) &&
                    !Directory.GetDirectories(rejectedDirectory).Any(), "Rejected UI smoke created artifacts or user data.");
                Check(beforeBytes.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)), "Rejected UI smoke modified existing files.");
                Check((Clipboard.ContainsText() ? Clipboard.GetText() : null) == clipboardText &&
                    (Clipboard.GetDataObject()?.GetFormats() ?? []).SequenceEqual(clipboard?.GetFormats() ?? []), "Rejected UI smoke changed the clipboard.");
            }
        }
        finally { Directory.Delete(rejectedDirectory, true); }
        var app = (App)Application.Current;
        async Task LaunchAsync(params string[] args)
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "MPswift.exe")) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Second launch did not start.");
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Check(process.ExitCode == 0, "Second instance did not forward and exit cleanly."); }
            catch { if (!process.HasExited) process.Kill(); throw; }
        }
        async Task AwaitAsync(Func<bool> predicate)
        {
            var deadline = Stopwatch.StartNew();
            while (!predicate() && deadline.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            Check(predicate(), "IPC request did not reach the existing window/model.");
        }
        window.Hide(); await LaunchAsync(); await AwaitAsync(() => window.IsVisible);
        var before = model.Entries.Count;
        await Task.WhenAll(LaunchAsync(fixture), LaunchAsync(fixture));
        await AwaitAsync(() => model.Entries.Count == before + 2 && !model.IsImporting);
        Check(!model.IsPlaying, "Second launch autoplayed without --play.");
        using (var pipe = new NamedPipeClientStream(".", app.InstancePipeName!, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await pipe.ConnectAsync(timeout.Token);
            var oversized = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(oversized, Player.Core.Integration.OpenRequest.MaximumBytes + 1);
            await pipe.WriteAsync(oversized, timeout.Token); var answer = new byte[1]; await pipe.ReadExactlyAsync(answer, timeout.Token);
            Check(answer[0] == 0, "Oversized IPC request was accepted.");
        }
        await LaunchAsync(); // The server must remain usable after a malformed client.
        var seek = (WaveformControl)window.FindName("WaveformView");
        var peer = UIElementAutomationPeer.CreatePeerForElement(seek) ?? throw new InvalidOperationException("Seek automation peer missing.");
        var range = (IRangeValueProvider?)peer.GetPattern(PatternInterface.RangeValue) ?? throw new InvalidOperationException("Seek RangeValue pattern missing.");
        Check(range.Maximum == model.DurationSeconds && !range.IsReadOnly && peer.GetName() == Strings.Seek, "Seek accessible range/name incorrect.");
        range.SetValue(0.25); await AwaitAsync(() => Math.Abs(model.SeekPosition - 0.25) < 0.01);
        // Software-routed real WPF events test shortcut ownership, not a physical keyboard claim.
        var repeat = (ComboBox)window.FindName("RepeatBox"); model.Search = "no-match";
        repeat.IsDropDownOpen = true; await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var presentation = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("WPF input source missing.");
        var previewEscape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, presentation, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        repeat.RaiseEvent(previewEscape);
        Check(!previewEscape.Handled && model.Search == "no-match", "Window stole dropdown Escape or cleared search while a transient control owned it.");
        repeat.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, presentation, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        Check(!repeat.IsDropDownOpen, "Dropdown did not close on its own Escape route."); model.Search = "";
        var resources = new ResourceManager("Player.App.Resources.Strings", typeof(Strings).Assembly);
        var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var russian = resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!;
        var resourceCount = 0;
        foreach (System.Collections.DictionaryEntry entry in english)
        { Check(russian.GetString((string)entry.Key) is { Length: > 0 }, "Russian resource missing: " + entry.Key); resourceCount++; }
        Check(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == model.WindowSettings.Language, "Saved language was not applied at startup.");
        var playButton = (Button)window.FindName("PlayPauseButton");
        Check(UIElementAutomationPeer.CreatePeerForElement(playButton)!.GetName() == Strings.Play &&
            playButton.ToolTip?.ToString() == Strings.Play, "Transport accessible name/tooltip was not localized.");
        Check(model.State == Strings.Get("State" + model.Snapshot.State), "IPC changed the product language of playback state.");
        var source = model.Playlists.FirstOrDefault(p => p.Id == model.SourcePlaylistId);
        if (source is not null) Check(model.PlaybackSource == string.Format(Strings.Culture, Strings.Get("PlaybackSource"), source.Name), "IPC changed the product language of source status.");
        Check(Strings.Culture.TwoLetterISOLanguageName == model.WindowSettings.Language, "Application resource culture drifted.");
        var closeToTray = model.WindowSettings.CloseToTray;
        using (var tray = new TrayService(window, model))
        {
            model.WindowSettings = model.WindowSettings with { CloseToTray = true };
            var state = model.Snapshot.State; window.Close(); Check(!window.IsVisible && model.Snapshot.State == state, "Close-to-tray changed playback or disposed the engine.");
            window.ShowAndActivate(); model.WindowSettings = model.WindowSettings with { CloseToTray = closeToTray };
        }
        Check(!app.MediaSessionAvailable || app.MediaMetadataMatches(model), "SMTC metadata did not follow the active coordinator model.");
        var selected = model.SelectedPlaylist;
        // Create nearly 10k real WPF rows within the production global 10k-entry limit.
        model.CreatePlaylist("Viewport validation");
        model.AddLibraryTracks(Enumerable.Repeat(selected.Entries[0].Entry.Track, 10000));
        var list = (ListBox)window.FindName("PlaylistList"); window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var realized = Count<ListBoxItem>(list);
        var playlistRows = model.Entries.Count;
        Check(model.Entries.Count > 9900 && realized > 0 && realized < 150, "Playlist realized unbounded row containers.");
        var scrollPerformance = await PerformanceValidation.ScrollAsync(window, list);
        model.DeletePlaylist(); model.SelectedPlaylist = selected;
        window.Width = 640; window.Height = 520; window.UpdateLayout();
        Check(((Button)window.FindName("PlayPauseButton")).ActualWidth > 0 && list.ActualHeight > 0, "Minimum layout hid transport/playlist.");
        window.Width = 840; window.Height = 860;
        return new { Status = "windows-integration-passed", SecondProcessActivation = true, ConcurrentFileForwarding = true, NoImplicitAutoplay = true,
            OversizedIpcRejected = true, CurrentUserOnlyPipe = true, ExplorerQualified = true, OwnedUiSmokeRequired = true,
            RejectedUiSmokeNoFilesOrClipboard = true, SeekAutomationRange = true, SoftwareRoutedDropdownEscape = true, CloseToTrayPreservesState = true,
            Language = model.WindowSettings.Language, ResourceKeys = resourceCount, RealizedRowContainers = realized, PlaylistRows = playlistRows, GlobalPlaylistCapacity = 10000,
            ScrollPerformance = scrollPerformance,
            MediaSession = app.MediaSessionAvailable ? "registered; metadata synchronized" : "unavailable in this Windows session",
            GlobalMediaKeyPress = "not-run", ScreenReader = "not-run", PhysicalDpiAndMonitorMoves = "not-run" };
    }
    private static int Count<T>(DependencyObject parent) where T : DependencyObject
    { var count = parent is T ? 1 : 0; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) count += Count<T>(VisualTreeHelper.GetChild(parent, i)); return count; }
}
