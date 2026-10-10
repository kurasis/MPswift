using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.Services.Storage;
using Player.App.ViewModels;
using Player.App.Views;
using Forms = System.Windows.Forms;

namespace Player.App.Services.Windows;

/// <summary>Actual owned WPF windows and native stacking; never changes Explorer or external window positions.</summary>
internal static class DesktopPanelValidation
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint parameter, nint data);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    private static void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static Task Idle(MainWindow window) => window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private static bool Below(nint panel, nint other)
    {
        var seen = new HashSet<nint>();
        for (var next = DesktopPanelLayer.GetWindow(other, 2); next != 0 && seen.Count < 10000 && seen.Add(next); next = DesktopPanelLayer.GetWindow(next, 2))
            if (next == panel) return true;
        return false;
    }
    private static IEnumerable<Button> Buttons(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }
    internal static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        var settings = model.WindowSettings; var volume = model.Volume; var shuffle = model.Shuffle; var position = model.SeekPosition;
        var source = model.Snapshot.EntryId; var sourcePlaylist = model.SourcePlaylistId; var queue = model.Queue.Select(item => item.Id).ToArray();
        var state = window.WindowState; var playing = model.IsPlaying;
        var blocker = new Window { Width = 220, Height = 100, Left = 20, Top = 20, Title = "Owned desktop panel stacking control", ShowInTaskbar = false };
        try
        {
            model.WindowSettings = settings with { DesktopPanelEnabled = true, DesktopPanelLeft = null, DesktopPanelTop = null };
            blocker.Show(); blocker.Activate(); await Idle(window);
            var other = new WindowInteropHelper(blocker).Handle; var foreground = GetForegroundWindow();
            Check(foreground == other, "Owned stacking control did not become foreground.");
            window.WindowState = WindowState.Minimized; await Idle(window);
            var panel = window.DesktopPanel;
            Check(panel is { IsVisible: true }, "Minimizing did not show desktop controls.");
            var controls = panel!; var handle = controls.Handle;
            Check(controls.Owner is null && !controls.Topmost && !controls.ShowInTaskbar && !controls.ShowActivated, "Desktop panel has activating/owned/taskbar behavior.");
            var style = DesktopPanelLayer.Style(handle);
            Check((style & 0x08000080) == 0x08000080 && (style & 0x00040008) == 0, "Native panel lacks NOACTIVATE/TOOLWINDOW or is topmost/APPWINDOW.");
            Check(GetForegroundWindow() == foreground && Below(handle, other), "Panel stole focus or stacked above an ordinary window.");
            Check(DesktopPanelLayer.SetWindowPos(handle, 0, 0, 0, 0, 0, DesktopPanelLayer.NoMove | DesktopPanelLayer.NoSize | DesktopPanelLayer.NoActivate), "Owned raise control failed.");
            Check(Below(handle, other), "Panel allowed a native raise above an ordinary window.");
            Check(SendMessage(handle, DesktopPanelLayer.MouseActivate, other, 0) == 3 && GetForegroundWindow() == foreground,
                "Native panel clicks activate instead of delivering a nonactivating click.");
            Check(controls.TrackTitle.Text == model.Title && ReferenceEquals(controls.DataContext, model), "Panel displays a separate/stale playback model.");
            foreach (var command in new ICommand[] { model.PreviousCommand, model.StopCommand, model.PlayPauseCommand, model.NextCommand })
                Check(Buttons(controls).Any(button => ReferenceEquals(button.Command, command)), "Panel transport lost an existing command binding.");
            var volumePeer = UIElementAutomationPeer.CreatePeerForElement(controls.VolumeSlider)!;
            var volumeRange = (IRangeValueProvider)volumePeer.GetPattern(PatternInterface.RangeValue);
            volumeRange.SetValue(45); await Idle(window);
            Check(volumePeer.GetName() == Strings.Get("Volume") && Math.Abs(model.Volume - 45) < .01, "Panel volume lost its accessible two-way binding.");
            controls.ShuffleButton.IsChecked = !shuffle; await Idle(window);
            Check(model.Shuffle == !shuffle, "Panel shuffle binding did not update playback order.");
            model.Shuffle = shuffle; model.Volume = volume;

            controls.Place(100000, -100000); await Idle(window);
            var bounds = DesktopPanelLayer.ReadBounds(handle); var area = Forms.Screen.FromHandle(handle).WorkingArea;
            Check(bounds.Left >= area.Left && bounds.Top >= area.Top && bounds.Right <= area.Right && bounds.Bottom <= area.Bottom,
                "Offscreen saved position was not fitted into the current monitor.");
            controls.Place(area.Left + 30, area.Bottom - (bounds.Bottom - bounds.Top) - 20); await Idle(window);
            bounds = DesktopPanelLayer.ReadBounds(handle);
            Check(model.WindowSettings.DesktopPanelLeft == bounds.Left && model.WindowSettings.DesktopPanelTop == bounds.Top, "Moved panel did not remember physical screen coordinates.");
            await model.SaveNowAsync();
            var saved = new SettingsFile(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", "stage-c-data")).Load();
            Check(saved.DesktopPanelEnabled && saved.DesktopPanelLeft == bounds.Left && saved.DesktopPanelTop == bounds.Top, "Panel option/position did not persist with the session.");

            var slider = controls.PositionSlider; slider.ApplyTemplate(); controls.UpdateLayout();
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            Check(slider.IsMoveToPointEnabled && slider.IsEnabled && track.ActualWidth > 20, "Panel has no enabled native seek track.");
            var point = track.PointToScreen(new Point(track.Thumb.ActualWidth / 2 + .65 * (track.ActualWidth - track.Thumb.ActualWidth), track.ActualHeight / 2));
            Check(SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y)), "Owned panel cursor positioning failed.");
            await Task.Delay(100); await Idle(window);
            slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            await Idle(window); await controls.SeekCompletion;
            Check(!model.SeekPreview && Math.Abs(model.SeekPosition - model.DurationSeconds * .65) < .1 && model.Snapshot.EntryId == source && !model.IsPlaying,
                "Actual panel seek routing failed, changed sources or autoplayed.");
            await model.CommitSeekAsync(position); await Idle(window);
            CustomizationValidation.Render(controls, output, "desktop-panel-" + model.WindowSettings.Language + ".png");

            controls.RestoreButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Idle(window);
            Check(window.IsVisible && window.WindowState != WindowState.Minimized && !controls.IsVisible, "Restore arrow did not return to the main player.");
            window.WindowState = WindowState.Minimized; await Idle(window);
            Check(ReferenceEquals(window.DesktopPanel, controls) && controls.IsVisible && DesktopPanelLayer.ReadBounds(handle).Left == bounds.Left,
                "Repeated minimization recreated the panel or lost its placement.");
            window.ShowAndActivate();
            for (var preset = 2; preset <= 5; preset++)
            {
                var placement = new PreferencesWindow(window, model); placement.Show(); await Idle(window); await placement.CacheRefreshCompletion;
                placement.DesktopPanelPositionBox.SelectedIndex = preset;
                placement.ApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await placement.ApplyCompletion;
                window.WindowState = WindowState.Minimized; await Idle(window);
                var placed = DesktopPanelLayer.ReadBounds(handle);
                var target = Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
                Check(placed.Left == (preset is 2 or 4 ? target.Left : target.Right - (placed.Right - placed.Left)) &&
                    placed.Top == (preset is 2 or 3 ? target.Top : target.Bottom - (placed.Bottom - placed.Top)),
                    "Accessible position preset did not move the panel to the requested corner.");
                Check(model.WindowSettings.DesktopPanelLeft == placed.Left && model.WindowSettings.DesktopPanelTop == placed.Top,
                    "Position preset lost fitted physical coordinates.");
                await model.SaveNowAsync(); var positioned = new SettingsFile(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", "stage-c-data")).Load();
                Check(positioned.DesktopPanelLeft == placed.Left && positioned.DesktopPanelTop == placed.Top, "Position preset did not persist.");
                window.ShowAndActivate();
            }
            controls.Place(bounds.Left, bounds.Top);
            var canceledPlacement = new PreferencesWindow(window, model); canceledPlacement.Show(); await Idle(window); await canceledPlacement.CacheRefreshCompletion;
            canceledPlacement.DesktopPanelPositionBox.SelectedIndex = 1; canceledPlacement.Close();
            Check(model.WindowSettings.DesktopPanelLeft == bounds.Left && model.WindowSettings.DesktopPanelTop == bounds.Top, "Cancelled preset changed panel coordinates.");
            var preferences = new PreferencesWindow(window, model); preferences.Show(); await Idle(window);
            await preferences.CacheRefreshCompletion;
            Check(preferences.DesktopPanelBox.IsChecked == true, "Preferences lost the enabled desktop-panel option.");
            preferences.DesktopPanelBox.IsChecked = false; preferences.ApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await preferences.ApplyCompletion; await Idle(window);
            Check(!preferences.IsVisible && !model.WindowSettings.DesktopPanelEnabled, "Preferences did not apply the desktop-panel option.");
            window.WindowState = WindowState.Minimized; await Idle(window);
            Check(!controls.IsVisible, "Disabled desktop option left a visible panel.");
            await model.SaveNowAsync();
            Check(!new SettingsFile(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", "stage-c-data")).Load().DesktopPanelEnabled,
                "Disabled desktop option was not saved.");
            model.WindowSettings = model.WindowSettings with { DesktopPanelEnabled = true }; await Idle(window);
            Check(controls.IsVisible, "Re-enabling the option did not restore minimized controls.");
            window.IsEnabled = false; await Idle(window); Check(!controls.IsVisible, "Disabled main window retained actionable desktop controls.");
            window.IsEnabled = true; await Idle(window); Check(controls.IsVisible, "Re-enabled main window lost its desktop controls.");
            window.DataContext = null; await Idle(window); Check(!controls.IsVisible && controls.DataContext is null, "Model replacement retained old desktop bindings.");
            window.DataContext = model; await Idle(window); Check(controls.IsVisible && ReferenceEquals(controls.DataContext, model), "Panel did not bind to the replacement model.");
            window.ShowAndActivate(); window.Hide(); await Idle(window);
            Check(controls.IsVisible, "Close-to-tray hiding did not show desktop controls.");
            controls.RestoreButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Idle(window);
            Check(window.IsVisible && !controls.IsVisible && model.Snapshot.EntryId == source && model.SourcePlaylistId == sourcePlaylist && model.IsPlaying == playing &&
                Math.Abs(model.SeekPosition - position) < .01 && model.Queue.Select(item => item.Id).SequenceEqual(queue), "Panel lifecycle changed playback/session or failed tray restoration.");
            await ValidateShutdownAsync(window, fixture, output);
            return new { Status = "desktop-panel-passed", MinimizedPanelVisible = true, NativeBottomStacking = true, NativeRaiseRefused = true,
                NonactivatingClickDelivery = true, NoTaskbarOrOwner = true, ActualTitleAndTransportBindings = true,
                AccessibleVolumeBinding = true, ShuffleBinding = true, ActualNativeSeekRouting = true, PlaybackAndQueuePreserved = true,
                MonitorBoundsClamped = true, PhysicalPositionPersisted = true, RestoreArrow = true, RepeatedMinimization = true,
                PreferencesOptionApplied = true, DisabledOptionPersisted = true, DisabledMainWindowHidden = true, ModelRebinding = true, TrayHideRestore = true,
                AccessiblePositionPresets = true, PositionPresetPersisted = true, PositionDraftCancel = true,
                DelayedInitialization = true, PanelClosedWithMain = true, OwnedSourceAndDatabaseReleased = true,
                PhysicalMonitorCount = Forms.Screen.AllScreens.Length,
                PhysicalPointerDrag = "manual-not-run", MixedDpiMonitorTransitions = "manual-not-run", WindowsShowDesktop = "manual-not-run" };
        }
        finally
        {
            window.DataContext = model; window.IsEnabled = true; window.ShowAndActivate(); window.WindowState = state;
            blocker.Close(); model.Volume = volume; model.Shuffle = shuffle; model.WindowSettings = settings;
            await model.CommitSeekAsync(position); await model.SaveNowAsync();
        }
    }
    private static async Task ValidateShutdownAsync(MainWindow original, string fixture, string output)
    {
        var directory = Path.Combine(output, "owned-desktop-panel-shutdown"); Directory.CreateDirectory(directory);
        var music = Path.Combine(directory, "owned.wav"); File.Copy(fixture, music);
        var model = ((App)Application.Current).CreateModel(directory);
        var window = new MainWindow { DataContext = model }; var closed = false;
        try
        {
            window.Show(); window.WindowState = WindowState.Minimized; await Idle(original);
            Check(window.DesktopPanel is null, "An uninitialized player displayed a desktop panel.");
            await model.InitializeAsync(); await Idle(original);
            var panel = window.DesktopPanel;
            Check(panel is { IsVisible: true }, "Initialization while minimized did not create desktop controls.");
            await model.AddPathsAsync([music]); await model.PrepareAsync(model.Entries.Single().Id);
            await Idle(original); await model.WaveformCompletion.WaitAsync(TimeSpan.FromSeconds(30));
            var handle = panel!.Handle;
            await window.CloseForValidationAsync(); closed = true; await Idle(original);
            Check(window.DesktopPanel is null && panel.DataContext is null && !panel.IsVisible && !IsWindow(handle), "Closing the player retained the native panel/timer/model.");
            using (File.Open(music, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            using (File.Open(Path.Combine(directory, "library.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            if (!closed) await window.CloseForValidationAsync();
            Directory.Delete(directory, true); original.ShowAndActivate();
        }
    }
}
