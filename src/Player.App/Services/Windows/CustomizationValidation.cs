using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;

namespace Player.App.Services.Windows;

/// <summary>Real menu popups/settings and persisted preferences in an owned UI smoke session.</summary>
internal static class CustomizationValidation
{
    public static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); }
        var language = model.WindowSettings.Language;
        var menuButton = (Button)window.FindName("PlaylistActions");
        var menu = menuButton.ContextMenu;
        menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(menu.IsOpen && menu.ActualHeight > 0 && menu.ActualHeight <= 440, "Compact menu failed to open or exceeded its scroll viewport.");
        Render(menu, output, "actions-menu-" + language + ".png");
        var submenu = menu.Items.OfType<MenuItem>().First();
        submenu.Focus();
        var input = PresentationSource.FromVisual(menu)!;
        submenu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, input, 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        await Idle();
        Check(submenu.IsSubmenuOpen, "Menu keyboard navigation did not open its submenu.");
        var popup = (System.Windows.Controls.Primitives.Popup)submenu.Template.FindName("PART_Popup", submenu);
        Check(popup.IsOpen && popup.Child is FrameworkElement { ActualHeight: > 0 }, "Menu submenu template did not render.");
        Render((FrameworkElement)popup.Child, output, "actions-submenu-" + language + ".png");
        submenu.IsSubmenuOpen = false;
        // Exercise a real nested action and restore the tab without mutating the source playlist.
        var initialTabs = model.Playlists.Count; var selected = model.SelectedPlaylist;
        var duplicate = submenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.DuplicatePlaylist));
        duplicate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(model.Playlists.Count == initialTabs + 1 && model.SelectedPlaylist.Id != selected.Id, "Nested menu action was disconnected.");
        model.DeletePlaylist(); model.SelectedPlaylist = selected; menu.IsOpen = false;

        var settingsButton = (Button)window.FindName("SettingsButton");
        Check(UIElementAutomationPeer.CreatePeerForElement(settingsButton)!.GetName() == Strings.Settings, "Settings shortcut has no localized accessible name.");
        var before = model.WindowSettings;
        var snapshot = model.Snapshot;
        var dialog = new PreferencesWindow(window, model);
        try
        {
            dialog.Show(); await Idle();
            var settingsSurface = (FrameworkElement)dialog.Content;
            var applyBounds = dialog.ApplyButton.TransformToAncestor(settingsSurface).TransformBounds(new Rect(dialog.ApplyButton.RenderSize));
            Check(applyBounds.Width > 0 && applyBounds.Height > 0 && applyBounds.Left >= 0 && applyBounds.Top >= 0 &&
                applyBounds.Right <= settingsSurface.ActualWidth && applyBounds.Bottom <= settingsSurface.ActualHeight,
                "Settings Apply button was clipped outside the client surface.");
            Render(settingsSurface, output, "settings-" + language + ".png");
            dialog.LanguageBox.SelectedIndex = language == "ru" ? 0 : 1;
            dialog.AlbumSectionsBox.IsChecked = !before.ShowAlbumSections;
            dialog.CloseToTrayBox.IsChecked = !before.CloseToTray;
            dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await dialog.ApplyCompletion.WaitAsync(TimeSpan.FromSeconds(15));
            var saved = new Storage.SettingsFile(((App)Application.Current).DataDirectory).Load();
            Check(saved.Language != before.Language && saved.ShowAlbumSections != before.ShowAlbumSections && saved.CloseToTray != before.CloseToTray,
                "Settings Apply did not persist language/grouping/close behavior.");
            Check(!dialog.IsVisible && model.Message == Strings.RestartLanguage && Strings.Culture.TwoLetterISOLanguageName == language,
                "Language change did not retain the current interface with explicit restart guidance.");
            Check(model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State, "Settings changed the active playback identity/state.");
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            model.WindowSettings = before; await model.SaveNowAsync();
        }
        var canceled = new PreferencesWindow(window, model);
        canceled.Show(); canceled.AlbumSectionsBox.IsChecked = !before.ShowAlbumSections; canceled.Close();
        Check(model.WindowSettings.ShowAlbumSections == before.ShowAlbumSections, "Cancel applied a draft preference.");
        return new { Status = "customization-passed", CompactMenu = true, KeyboardSubmenu = true, NestedAction = true,
            LocalizedSettingsShortcut = true, SettingsPersisted = true, DraftCancel = true, PlaybackPreserved = true,
            LanguageChange = "persisted; applies at the next app launch" };
    }

    internal static void Render(FrameworkElement visual, string output, string name)
    {
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output); using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
    }
}
