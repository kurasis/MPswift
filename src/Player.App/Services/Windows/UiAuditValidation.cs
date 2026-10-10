using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Actual WPF forms and owned SQLite/native workflows for the ten UI audit findings.</summary>
internal static class UiAuditValidation
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    private static void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static Task Idle(Window window) => window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Named(Control control, string expected) => Check(UIElementAutomationPeer.CreatePeerForElement(control)!.GetName() == expected,
        "Auxiliary control has no correct localized UIA name: " + expected);
    private static void Polite(TextBlock text) => Check(AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite,
        "Auxiliary feedback has no polite live-region setting.");
    private static void LabelFocus(Control control)
    {
        var label = AutomationProperties.GetLabeledBy(control);
        Check(label is Label, "Input has no associated visible label.");
        Check(((Label)label!).Foreground is SolidColorBrush foreground && foreground.Color == ((SolidColorBrush)control.FindResource("TextPrimaryBrush")).Color,
            "Visible label has the system light-theme foreground in the dark dialog.");
        label!.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Check(control.IsKeyboardFocused, "Clicking the visible label did not focus its field.");
    }

    internal static Task RespondToDeletion(Window owner, bool confirm, Action<DeletePlaylistDialog>? inspect = null)
        => owner.Dispatcher.InvokeAsync(() =>
        {
            var dialog = Application.Current.Windows.OfType<DeletePlaylistDialog>().Single();
            try
            {
                Check(dialog.CancelButton.IsDefault && dialog.CancelButton.IsCancel && !dialog.DeleteButton.IsDefault,
                    "Playlist deletion is not cancel-default.");
                inspect?.Invoke(dialog); Click(confirm ? dialog.DeleteButton : dialog.CancelButton);
            }
            catch { if (dialog.IsVisible) Click(dialog.CancelButton); throw; }
        }, DispatcherPriority.Background).Task;

    internal static async Task<object> RunAsync(MainWindow owner, PlayerViewModel model, string fixture, string output)
    {
        var settings = model.WindowSettings; var selected = model.SelectedPlaylist;
        var snapshot = model.Snapshot; var source = model.SourcePlaylistId;
        var queue = model.Queue.Select(item => item.Id).ToArray();
        var originalTabs = model.Playlists.ToArray();
        try
        {
            model.CreatePlaylist("Owned UI deletion — Музыка"); var tab = model.SelectedPlaylist;
            await model.AddPathsAsync([fixture, fixture]); await Idle(owner);
            var entries = tab.Entries.Select(row => row.Id).ToArray();
            var submenu = ((Button)owner.FindName("PlaylistActions")).ContextMenu.Items.OfType<MenuItem>().First();
            var delete = submenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.DeletePlaylist));
            var response = RespondToDeletion(owner, false, dialog => CustomizationValidation.Render((FrameworkElement)dialog.Content, output, "delete-playlist-" + settings.Language + ".png"));
            delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await response;
            Check(model.Playlists.Contains(tab) && tab.Entries.Select(row => row.Id).SequenceEqual(entries), "Cancelled deletion lost playlist/order.");
            response = RespondToDeletion(owner, true, _ => model.SelectedPlaylist = selected);
            delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await response;
            Check(model.Playlists.Contains(tab) && model.Playlists.Contains(selected), "Stale confirmation deleted a different playlist.");
            model.SelectedPlaylist = tab;
            response = RespondToDeletion(owner, true); delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await response;
            Check(!model.Playlists.Contains(tab) && originalTabs.All(model.Playlists.Contains) && File.Exists(fixture), "Confirmed deletion affected other playlists/source files.");
            model.SelectedPlaylist = selected;

            var name = new PlaylistNameDialog(owner, "");
            var nameCheck = owner.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Named(name.NameBox, Strings.PlaylistName); Polite(name.ValidationText); LabelFocus(name.NameBox);
                    name.NameBox.Text = "  "; Click(name.SaveButton);
                    Check(name.IsVisible && name.ValidationText.Text == Strings.Get("PlaylistNameRequired") &&
                        AutomationProperties.GetHelpText(name.NameBox) == name.ValidationText.Text && name.NameBox.IsKeyboardFocused,
                        "Empty playlist name did not produce an associated, focused validation error.");
                    name.NameBox.Text = "  Valid name  "; Check(name.ValidationText.Text == "", "Correcting the name left a stale error.");
                    Click(name.SaveButton);
                }
                finally { if (name.IsVisible) name.Close(); }
            }, DispatcherPriority.Background);
            Check(name.ShowDialog() == true, "Valid name did not save."); await nameCheck.Task;
            Check(name.PlaylistName == "Valid name", "Saved name was not trimmed.");

            var audio = new AudioSettingsWindow(owner, model, await model.GetDevicesAsync());
            try
            {
                audio.Show(); await Idle(owner);
                Named(audio.DeviceBox, Strings.Get("OutputDevice")); Named(audio.ReplayGainBox, Strings.Get("ReplayGainHelp")); Polite(audio.StatusText);
                LabelFocus(audio.DeviceBox); LabelFocus(audio.ReplayGainBox);
                var sliders = CustomizationValidation.Descendants(audio).OfType<Slider>().ToArray();
                Check(sliders.Length == 12, "Audio form lost an EQ, preamp or crossfade slider.");
                var names = sliders.Select(slider => UIElementAutomationPeer.CreatePeerForElement(slider)!.GetName()).ToArray();
                Check(names.Distinct().Count() == 12 && names.All(value => !string.IsNullOrWhiteSpace(value)), "Audio sliders have missing/duplicate UIA names.");
                foreach (var center in AudioProcessingSettings.Centers)
                    Check(names.Contains(center.ToString(Strings.Culture) + " " + Strings.Get("HzUnit") + ", " + Strings.Get("GainDb")), "An EQ frequency is absent from its accessible name.");
                foreach (var slider in sliders) LabelFocus(slider);
                CustomizationValidation.Render((FrameworkElement)audio.Content, output, "audio-settings-" + settings.Language + ".png");
                Click(audio.ApplyButton); var first = audio.ApplyCompletion;
                if (!first.IsCompleted) Check(!audio.ApplyButton.IsEnabled && audio.StatusText.Text == Strings.Get("ApplyingAudio"), "Pending audio Apply has no disabled/busy feedback.");
                Click(audio.ApplyButton); Check(ReferenceEquals(first, audio.ApplyCompletion), "Repeated audio Apply started a second request.");
                await first.WaitAsync(TimeSpan.FromSeconds(15)); Check(!audio.IsVisible, "Successful native audio configuration did not close the dialog.");
            }
            finally { if (audio.IsVisible) audio.Close(); }

            await CheckLibraryAndFailureAsync(owner, fixture, output);
            var preferences = new PreferencesWindow(owner, model);
            try
            {
                preferences.Show(); await Idle(owner); await preferences.CacheRefreshCompletion;
                Polite(preferences.ValidationText); Polite(preferences.CacheValidationText);
                preferences.CacheBudgetBox.Text = "15"; Click(preferences.ApplyButton); await preferences.ApplyCompletion;
                Check(preferences.IsVisible && preferences.CacheValidationText.Text == Strings.Get("CacheBudgetInvalid") &&
                    AutomationProperties.GetHelpText(preferences.CacheBudgetBox) == preferences.CacheValidationText.Text && preferences.CacheBudgetBox.IsKeyboardFocused,
                    "Cache budget validation is not inline, associated and focused.");
                preferences.CacheBudgetBox.Text = settings.WaveformCacheMiB.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Check(preferences.CacheValidationText.Text == "", "Corrected cache budget retained an error.");
                Named(preferences.DesktopPanelPositionBox, Strings.Get("DesktopPanelPosition"));
                preferences.DesktopPanelPositionBox.SelectedIndex = 1; Click(preferences.ApplyButton); await preferences.ApplyCompletion;
                Check(model.WindowSettings.DesktopPanelLeft is null && model.WindowSettings.DesktopPanelTop is null, "Panel position reset did not apply.");
            }
            finally { if (preferences.IsVisible) preferences.Close(); }

            var infoRow = selected.Entries.First();
            var info = new FileInformationWindow(owner, [infoRow], infoRow.Id);
            try { info.Show(); await Idle(owner); await info.LoadCompletion; Polite(info.StatusText); Check(!string.IsNullOrWhiteSpace(info.StatusText.Text), "File information has no completion feedback."); }
            finally { info.Close(); }

            var button = (Button)owner.FindName("PlayPauseButton"); owner.ShowAndActivate(); await Idle(owner);
            var point = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            Check(SetCursorPos((int)point.X, (int)point.Y), "Cannot hover the owned accent button.");
            await Task.Delay(100); await Idle(owner);
            Check(button.IsMouseOver && button.Background is SolidColorBrush hover && hover.Color == ((SolidColorBrush)owner.FindResource("TextPrimaryBrush")).Color,
                "Accent hover did not visibly change the button background.");
            point = owner.PointToScreen(new Point(5, 5)); Check(SetCursorPos((int)point.X, (int)point.Y), "Cannot leave owned accent hover.");
            await Task.Delay(100); await Idle(owner);
            Check(!button.IsMouseOver && button.Background is SolidColorBrush resting && resting.Color == ((SolidColorBrush)owner.FindResource("AccentBrush")).Color,
                "Accent button did not restore its resting appearance.");

            Check(model.Snapshot.EntryId == snapshot.EntryId && model.SourcePlaylistId == source && model.Snapshot.State == snapshot.State &&
                model.Queue.Select(item => item.Id).SequenceEqual(queue), "UI audit regressions changed source/playback/queue.");
            return new { Status = "ui-audit-passed", CancelDefaultDeletion = true, CancelPreservesOrder = true, StaleDeletionRefused = true,
                ConfirmedDeletionKeepsMusic = true, LocalizedAuxiliaryNames = true, ClickableAssociatedLabels = true, EmptyNameValidation = true,
                NativeAudioRepeatedApplyGuard = true, AudioFailureInlineAndRetryable = true, PoliteAuxiliaryStatuses = true,
                LibraryPageBoundaries = true, LibraryOffsetReconciled = true, StaleLibraryResponseIgnored = true,
                InlineCacheValidation = true, AccessiblePanelPositionReset = true, ActualAccentHover = true, PlaybackAndQueuePreserved = true,
                NarratorSpeech = "manual-not-run" };
        }
        finally
        {
            foreach (var tab in model.Playlists.Except(originalTabs).ToArray()) { model.SelectedPlaylist = tab; model.DeletePlaylist(); }
            model.SelectedPlaylist = selected; await model.SavePreferencesAsync(settings);
            Strings.SetLanguage(settings.Language); model.RefreshLanguage(); model.Details = ""; await Idle(owner);
        }
    }

    private static async Task CheckLibraryAndFailureAsync(MainWindow owner, string fixture, string output)
    {
        var data = Path.Combine(output, "ui-audit-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        var model = ((App)Application.Current).CreateModel(data);
        LibraryWindow? library = null;
        try
        {
            await model.InitializeAsync(); var index = model.LibraryIndex!; var root = Guid.NewGuid();
            await index.PutRootAsync(new(root, data));
            var files = Enumerable.Range(0, 101).Select(i =>
            {
                var path = Path.Combine(data, "owned-index-" + i + ".wav");
                var title = "UI audit all " + i + (i < 100 ? " First100" : "") + (i == 0 ? " OnlyOne" : "");
                return new IndexedFile(Guid.NewGuid(), root, path, 0, 0, true, "ui-audit", new(Guid.NewGuid(), path, title));
            }).ToArray();
            foreach (var batch in files.Chunk(64)) await index.UpsertFilesAsync(batch);
            library = new LibraryWindow(owner, model); library.Show(); await Idle(owner); await library.SearchCompletion;
            Named(library.SearchBox, Strings.Get("LibrarySearch")); Named(library.RootsBox, Strings.Get("LibraryRoots")); Polite(library.StatusText);
            LabelFocus(library.SearchBox); LabelFocus(library.RootsBox);
            foreach (var test in new[] { (Query: "Absent audit title", Total: 0L), (Query: "OnlyOne", Total: 1L), (Query: "First100", Total: 100L), (Query: "UI audit all", Total: 101L) })
            {
                var page = await library.LoadPageAsync(test.Query, 0);
                Check(page!.Total == test.Total && !library.PreviousButton.IsEnabled && library.NextButton.IsEnabled == (test.Total > 100), "First-page boundary failed.");
                page = await library.LoadPageAsync(test.Query, 1000);
                var offset = test.Total > 100 ? 100 : 0;
                Check(page!.Offset == offset && library.PreviousButton.IsEnabled == (offset > 0) && !library.NextButton.IsEnabled, "Out-of-range offset did not reconcile.");
                Check(library.StatusText.Text == string.Format(Strings.Culture, Strings.Get("LibraryPage"), page.Total,
                    page.Files.Length == 0 ? 0 : offset + 1, page.Files.Length == 0 ? 0 : offset + page.Files.Length, model.ScanStatus), "Library range is invalid.");
            }
            var previousRequest = library.SearchCompletion; library.NextButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(ReferenceEquals(previousRequest, library.SearchCompletion), "Disabled Next still started a request.");
            var old = library.LoadPageAsync("UI audit all", 100); var pending = !old.IsCompleted;
            var current = library.LoadPageAsync("OnlyOne", 0); var oldPage = await old;
            Check((!pending || oldPage is null) && (await current)!.Total == 1 && library.ResultList.Items.Count == 1, "Stale library response replaced current results.");
            CustomizationValidation.Render((FrameworkElement)library.Content, output, "library-audit-" + Strings.Culture.TwoLetterISOLanguageName + ".png");
            library.Close(); library = null;
        }
        finally { library?.Close(); await model.DisposeAsync(); }

        // A genuinely shut-down engine refuses configuration; no simulated decoder/output is used.
        var failed = new AudioSettingsWindow(owner, model, []);
        try
        {
            failed.Show(); await Idle(owner); Click(failed.ApplyButton); await failed.ApplyCompletion;
            Check(failed.IsVisible && failed.ApplyButton.IsEnabled && failed.StatusText.Text == Strings.Get("AudioApplyFailed"),
                "Refused engine configuration closed the dialog or hid its error.");
            failed.UpdateLayout();
            var surface = (FrameworkElement)failed.Content;
            foreach (var element in new FrameworkElement[] { failed.StatusText, failed.ApplyButton })
            {
                var bounds = element.TransformToAncestor(surface).TransformBounds(new Rect(element.RenderSize));
                Check(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0 &&
                    bounds.Right <= surface.ActualWidth && bounds.Bottom <= surface.ActualHeight, "Audio failure/retry feedback is clipped below the viewport.");
            }
            Polite(failed.StatusText); CustomizationValidation.Render((FrameworkElement)failed.Content, output, "audio-settings-error-" + Strings.Culture.TwoLetterISOLanguageName + ".png");
        }
        finally { failed.Close(); }
    }
}
