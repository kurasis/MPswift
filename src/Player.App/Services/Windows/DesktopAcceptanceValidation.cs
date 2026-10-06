using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

namespace Player.App.Services.Windows;

/// <summary>Actual WPF automation/focus/layout and declared DPI policy; physical desktop gates stay separate.</summary>
internal static class DesktopAcceptanceValidation
{
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    private static void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    public static async Task<object> RunOwnedAsync(MainWindow window, PlayerViewModel model, string phase)
    {
        var root = AcceptanceWorkspace.Validate(); var media = Path.Combine(root, "media"); var source = Path.Combine(media, "owned tone Музыка 🎵.wav");
        byte[] originalHash;
        if (phase == "first")
        {
            Check(model.Entries.Count == 0, "First-run acceptance requires a fresh portable database.");
            Directory.CreateDirectory(media);
            AcceptanceSignal.WriteWave(source);
            originalHash = SHA256.HashData(File.ReadAllBytes(source));
            var cue = Path.Combine(media, "owned.cue");
            File.WriteAllText(cue, "FILE \"owned tone Музыка 🎵.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"First\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Second\"\nINDEX 01 00:01:00\n", new System.Text.UTF8Encoding(false, true));
            await model.AddPathsAsync([source, cue]);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Check(model.Entries.Count == 3 && !model.IsPlaying, "Owned WAV/CUE import failed or autoplayed.");
            await model.PrepareAsync(model.Entries[2].Id);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await model.CommitSeekAsync(.25);
            Check(Math.Abs(model.DurationSeconds - 2) < .01 && !model.IsPlaying, "Owned native CUE segment was not prepared independently.");
            await model.PrepareAsync(model.Entries[0].Id);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await model.CommitSeekAsync(.5);
            await model.AddLibraryRootAsync(media); await model.ScanCompletion.WaitAsync(TimeSpan.FromSeconds(30));
        }
        else
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            using var previous = JsonDocument.Parse(File.ReadAllText(AcceptanceWorkspace.ReadFile(Path.Combine(root, "desktop-first.json"))));
            originalHash = Convert.FromHexString(previous.RootElement.GetProperty("SourceSha256").GetString()!);
            Check(model.Entries.Count == 3 && model.Entries.Select(e => e.Id).SequenceEqual(previous.RootElement.GetProperty("EntryIds").EnumerateArray().Select(e => e.GetGuid())) &&
                model.Snapshot.EntryId == previous.RootElement.GetProperty("SourceEntryId").GetGuid() &&
                Math.Abs(model.SeekPosition - .5) < .01 && !model.IsPlaying, "Actual apphost restart lost entry/session or autoplayed.");
        }
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Check(model.CanSeek && Math.Abs(model.DurationSeconds - 3) < .01, "Owned source was not natively prepared.");
        await model.WaveformCompletion.WaitAsync(TimeSpan.FromSeconds(30));
        Check(model.Waveform is not null, "Owned native waveform did not reach the window.");
        var indexed = await model.LibraryIndex!.SearchAsync("owned tone");
        Check(indexed.Total >= 1 && indexed.Files.Any(f => f.Track.Path == source), "Owned library workflow or restored index failed.");
        Check(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Native import/library/preparation/waveform or restart changed its audio source.");
        var desktop = await RunAsync(window, model, root);
        await model.SaveNowAsync();
        Check(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Desktop workflow changed its audio source.");
        return new { Status = "g10-owned-desktop-passed", Phase = phase, SourceEntryId = model.Snapshot.EntryId,
            EntryIds = model.Entries.Select(e => e.Id), NativeCueAndLibrary = true,
            SourceFile = Path.GetFileName(source), SourceSha256 = Convert.ToHexString(originalHash).ToLowerInvariant(),
            Provenance = "Owned deterministic PCM16, 48 kHz stereo, 3 seconds, 997 Hz; CC0", NativeDurationSeconds = model.DurationSeconds,
            NoAutoplay = true, NativeWaveform = true, ActualProcessRestart = phase == "restart", Desktop = desktop };
    }
    public static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string output)
    {
        var context = GetThreadDpiAwarenessContext();
        Check(AreDpiAwarenessContextsEqual(context, new nint(-4)), "Window thread is not PerMonitorV2-aware.");
        var dpi = GetDpiForWindow(new WindowInteropHelper(window).Handle);
        Check(dpi > 0, "Actual window DPI is unavailable.");
        var snapshot = model.Snapshot; var volume = model.Volume; var muted = model.Muted;
        var search = (TextBox)window.FindName("SearchBox");
        var gain = (Slider)window.FindName("VolumeSlider");
        var list = (ListBox)window.FindName("PlaylistList");
        var tabs = (ListBox)window.FindName("PlaylistTabs");
        var waveform = (FrameworkElement)window.FindName("WaveformView");
        var repeat = (ComboBox)window.FindName("RepeatBox");
        var play = (Button)window.FindName("PlayPauseButton");
        var controls = Descendants(window).OfType<UIElement>().Where(e => e.IsVisible &&
            e is Button or CheckBox or ComboBox or Slider or TextBox or ListBox).ToArray();
        var accessible = new List<object>();
        foreach (var element in controls)
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("Automation peer missing: " + element.GetType().Name);
            Check(!string.IsNullOrWhiteSpace(peer.GetName()), "Accessible control name is empty: " + element.GetType().Name);
            accessible.Add(new { Type = peer.GetAutomationControlType().ToString(), Name = peer.GetName(), Enabled = peer.IsEnabled(), KeyboardFocusable = peer.IsKeyboardFocusable() });
        }
        window.UpdateLayout();
        var selected = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(model.SelectedEntry ?? model.Entries[0]);
        var rowPeer = UIElementAutomationPeer.CreatePeerForElement(selected)!;
        Check(rowPeer.GetName() == ((PlaylistRowViewModel)selected.DataContext).Title, "Track automation name does not expose its title.");
        var tab = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(model.SelectedPlaylist);
        Check(UIElementAutomationPeer.CreatePeerForElement(tab)!.GetName() == model.SelectedPlaylist.Name &&
            UIElementAutomationPeer.CreatePeerForElement(list)!.GetName() == model.SelectedPlaylist.Name, "Playlist automation names lost the selected tab.");
        var gainProvider = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(gain)!.GetPattern(PatternInterface.RangeValue)!;
        Check(gainProvider.Minimum == 0 && gainProvider.Maximum == 100 && !gainProvider.IsReadOnly, "Volume automation range invalid.");
        gainProvider.SetValue(19);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Check(model.Volume == 19 && gainProvider.Value == 19, "Volume automation did not update the production model.");
        gainProvider.SetValue(volume);
        var muteProvider = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement((CheckBox)window.FindName("MuteBox"))!.GetPattern(PatternInterface.Toggle)!;
        muteProvider.Toggle(); Check(model.Muted != muted, "Mute automation did not toggle.");
        muteProvider.Toggle(); Check(model.Muted == muted, "Mute automation did not restore state.");
        var seekProvider = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(waveform)!.GetPattern(PatternInterface.RangeValue)!;
        Check(!seekProvider.IsReadOnly && seekProvider.Maximum == model.DurationSeconds, "Waveform automation range invalid.");
        seekProvider.SetValue(.25);
        var deadline = Stopwatch.StartNew();
        while (Math.Abs(model.SeekPosition - .25) > .01 && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Check(Math.Abs(model.SeekPosition - .25) < .01 && !model.IsPlaying, "Automation seek failed or autoplayed.");
        await model.CommitSeekAsync(snapshot.Position.TotalSeconds);
        var essential = new Dictionary<UIElement, string> { [search] = "search", [gain] = "volume", [list] = "tracks", [tabs] = "playlists", [waveform] = "waveform", [repeat] = "repeat", [play] = "play" };
        var reached = new HashSet<string>(); var focusSteps = new List<string>();
        search.Focus();
        for (var i = 0; i < 160; i++)
        {
            foreach (var (element, name) in essential) if (element.IsKeyboardFocusWithin) reached.Add(name);
            if (Keyboard.FocusedElement is DependencyObject focused)
            {
                var focusedPeer = focused is UIElement ui ? UIElementAutomationPeer.CreatePeerForElement(ui) : null;
                focusSteps.Add(focusedPeer?.GetName() ?? focused.GetType().Name);
                if (focused is UIElement input) Check(input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "Keyboard focus could not advance.");
            }
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (reached.Count == essential.Count) break;
        }
        Check(reached.Count == essential.Count, "Essential controls unreachable by Tab navigation: " + string.Join(", ", essential.Values.Except(reached)));
        foreach (var name in new[] { "PlaybackStateText", "StatusMessageText" })
            Check(AutomationProperties.GetLiveSetting((DependencyObject)window.FindName(name)) == AutomationLiveSetting.Polite, "State/error text is not a polite live region.");
        var width = window.Width; var height = window.Height;
        var images = new List<object>();
        try
        {
            window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var client = (FrameworkElement)window.Content;
            foreach (var element in new FrameworkElement[] { search, gain, play, repeat, waveform })
            {
                var bounds = element.TransformToAncestor(client).TransformBounds(new Rect(element.RenderSize));
                Check(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                    bounds.Right <= client.ActualWidth + 1 && bounds.Bottom <= client.ActualHeight + 1, "Essential control clipped at minimum size: " + element.Name);
            }
            images.Add(Render(client, output, "g10-minimum-" + model.WindowSettings.Language + ".png", 96));
            window.Width = width; window.Height = height; window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            foreach (var renderDpi in new[] { 96, 144, 192 }) images.Add(Render(client, output, "g10-render-" + model.WindowSettings.Language + "-" + renderDpi + ".png", renderDpi));
        }
        finally { window.Width = width; window.Height = height; window.UpdateLayout(); search.Focus(); }
        Check(model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State && !model.IsPlaying && model.Volume == volume && model.Muted == muted,
            "Desktop automation mutated playback identity/state or left settings changed.");
        using var identity = WindowsIdentity.GetCurrent();
        return new { Status = "g10-desktop-automation-passed", ProcessId = Environment.ProcessId, Language = model.WindowSettings.Language,
            Os = RuntimeInformation.OSDescription, Build = Environment.OSVersion.Version.Build,
            ElevatedAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            PerMonitorV2 = true, ActualWindowDpi = dpi, HighContrastObserved = SystemParameters.HighContrast,
            Monitors = System.Windows.Forms.Screen.AllScreens.Select(s => new { s.Primary, Bounds = s.Bounds.ToString(), WorkArea = s.WorkingArea.ToString() }),
            AccessibleControls = accessible, TrackAndPlaylistNames = true, VolumeMuteAndSeekProviders = true,
            SoftwareTabNavigation = new { Required = essential.Values, Reached = reached, Steps = focusSteps },
            StateAndErrorLiveRegions = true, MinimumSizeControlsWithinClient = true, Renderings = images,
            PhysicalDpiChangesAndMonitorRemoval = "not-run", NarratorAndPhysicalKeyboard = "not-run", CleanWindows11NoInstalledRuntime = "not-run",
            OfflineNetworkTraffic = "separate ETW process trace", RenderMethod = "Actual WPF visual rendered to 96/144/192 DPI bitmaps; not physical DPI transitions" };
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static object Render(FrameworkElement visual, string directory, string name, int dpi)
    {
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi / 96), (int)Math.Ceiling(visual.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
        return new { File = name, Dpi = dpi, bitmap.PixelWidth, bitmap.PixelHeight };
    }
}
