using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Software-routed WPF file-drop events over real local folders and SQLite.</summary>
internal static class FolderDropValidation
{
    public static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        static void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
        var directory = Path.Combine(output, "folder-drop-" + Guid.NewGuid().ToString("N"));
        var original = model.SelectedPlaylist;
        var initialIds = model.Playlists.Select(p => p.Id).ToHashSet();
        var originalOrder = original.Entries.Select(e => e.Id).ToArray();
        var snapshot = model.Snapshot;
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "Альбом 🎵");
        var nested = Path.Combine(first, "Disc 2"); Directory.CreateDirectory(nested);
        var second = Path.Combine(directory, "Other", "Альбом 🎵"); Directory.CreateDirectory(second);
        var empty = Path.Combine(directory, "Empty"); Directory.CreateDirectory(empty);
        File.Copy(fixture, Path.Combine(first, "one.wav")); File.Copy(fixture, Path.Combine(nested, "two.wav"));
        File.Copy(fixture, Path.Combine(second, "three.wav"));
        async Task DropAsync(UIElement target, params string[] paths)
        {
            var data = new DataObject(DataFormats.FileDrop, paths);
            // WPF exposes no public DragEventArgs constructor. This development-only
            // route raises its real routed event; the production handler does the import.
            var drag = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(1, 1)], null)!;
            drag.RoutedEvent = UIElement.DropEvent;
            target.RaiseEvent(drag);
            Check(drag.Handled, "Window did not handle the file-drop event.");
            await model.ImportCompletion.WaitAsync(TimeSpan.FromSeconds(20));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        try
        {
            var strip = (UIElement)window.FindName("PlaylistTabStrip");
            await DropAsync(strip, @"\\owned-invalid-source.invalid\music", @"\\?\C:\music");
            Check(model.Playlists.Count == initialIds.Count && original.Entries.Count == originalOrder.Length && !model.IsImporting &&
                model.Message == Strings.Get("ErrorFileUnavailable"), "Non-local/device drops were not rejected before import.");
            await DropAsync(strip, first, second, fixture);
            var created = model.Playlists.Where(p => !initialIds.Contains(p.Id)).ToArray();
            Check(created.Length == 2 && created.All(p => p.Name == "Альбом 🎵") &&
                created[0].Entries.Count == 2 && created[1].Entries.Count == 1,
                "Folder strip drop did not create separate named recursive playlists.");
            Check(original.Entries.Take(originalOrder.Length).Select(e => e.Id).SequenceEqual(originalOrder) && original.Entries.Count == originalOrder.Length + 1,
                "Mixed file/folder drop changed the original playlist or sent loose files to a new folder playlist.");
            Check(ReferenceEquals(model.SelectedPlaylist, created[1]), "The last folder playlist was not selected.");
            var tabs = (ListBox)window.FindName("PlaylistTabs"); window.UpdateLayout();
            var tab = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(created[0]);
            var tabChild = FindText(tab) ?? throw new InvalidOperationException("Realized playlist tab text is missing.");
            await DropAsync(tabChild, empty);
            Check(model.SelectedPlaylist.Name == "Empty" && model.Entries.Count == 0 && model.Playlists.Count == initialIds.Count + 3,
                "Drop on a tab child did not create an empty folder playlist.");
            model.SelectedPlaylist = created[0];
            await DropAsync((UIElement)window.FindName("PlaylistList"), second);
            Check(model.Entries.Count == 3 && model.Playlists.Count == initialIds.Count + 3,
                "Folder drop on the track list changed the add-to-selected behavior.");
            await model.SaveNowAsync();
            var saved = await ((Services.Storage.SqlitePlayerStore)model.LibraryIndex!).LoadAsync();
            foreach (var playlist in model.Playlists.Where(p => !initialIds.Contains(p.Id)))
            {
                var persisted = saved.Playlists.Single(p => p.Id == playlist.Id);
                Check(persisted.Name == playlist.Name && persisted.Entries.Select(e => e.Id).SequenceEqual(playlist.Entries.Select(e => e.Id)),
                    "Dropped folder playlist names/order/identities were not persisted.");
            }
            Check(model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State && model.Snapshot.Position == snapshot.Position &&
                !model.IsPlaying && !model.IsImporting, "File drops changed playback or left imports busy.");
            while (model.Playlists.Count < 100) model.CreatePlaylist("Owned tab-limit check");
            var limitedTarget = model.SelectedPlaylist;
            await DropAsync(strip, first);
            Check(model.Playlists.Count == 100 && ReferenceEquals(model.SelectedPlaylist, limitedTarget) && model.Entries.Count == 0 &&
                model.Message == Strings.Get("TabLimit") && !model.IsImporting, "Tab-limit drop imported into the wrong playlist or left the model busy.");
            return new { Status = "folder-drop-passed", SoftwareRoutedWpfEvents = true, RecursiveFolders = true,
                SeparateSameNameFolders = true, MixedFilesKeepOriginalTarget = true, TabChildAndEmptyStrip = true,
                EmptyFolder = true, TrackListKeepsSelectedTarget = true, SQLitePersistence = true, TabLimit = true,
                NonLocalDevicePathsRejected = true, NoAutoplay = true };
        }
        finally
        {
            foreach (var playlist in model.Playlists.Where(p => !initialIds.Contains(p.Id)).ToArray())
            { model.SelectedPlaylist = playlist; model.DeletePlaylist(); }
            model.SelectedPlaylist = original;
            model.RemoveEntriesCommand.Execute(original.Entries.Where(e => !originalOrder.Contains(e.Id)).ToArray());
            await model.SaveNowAsync();
            Directory.Delete(directory, true);
        }
    }
    private static TextBlock? FindText(DependencyObject parent)
    {
        if (parent is TextBlock text) return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindText(VisualTreeHelper.GetChild(parent, i)) is { } result) return result;
        return null;
    }
}
