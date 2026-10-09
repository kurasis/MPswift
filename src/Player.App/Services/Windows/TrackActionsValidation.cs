using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Player.App.Controls;
using Player.App.Resources;
using Player.App.Services.Library;
using Player.App.Services.Storage;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Media;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Real WPF/SQLite and owned file controls; native recycle dialogs require manual confirmation.</summary>
internal static class TrackActionsValidation
{
    internal static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string taggedFixture, string output)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        var initial = model.Playlists.ToArray(); var selected = model.SelectedPlaylist; var settings = model.WindowSettings;
        var snapshot = model.Snapshot; var source = model.SourcePlaylistId; var queueIds = model.Queue.Select(row => row.Id).ToHashSet();
        var directory = Path.Combine(output, "owned-track-actions"); Directory.CreateDirectory(directory);
        var flac = Path.Combine(directory, "Информация.flac"); File.Copy(taggedFixture, flac);
        var mp3 = Path.Combine(directory, "ID3 информация.mp3"); File.Copy(Path.Combine(Path.GetDirectoryName(taggedFixture)!, "mp3-cbr.mp3"), mp3);
        using (var file = TagLib.File.Create(mp3))
        {
            file.GetTag(TagLib.TagTypes.Id3v1, true); file.GetTag(TagLib.TagTypes.Id3v2, true);
            file.Tag.Title = "Owned ID3 title"; file.Tag.Performers = ["Owned artist"];
            file.Tag.Lyrics = "Owned lyrics\nБеларускі тэкст"; file.Save();
        }
        var hashes = new Dictionary<string, string> { [flac] = Hash(flac), [mp3] = Hash(mp3) };
        var list = (ListBox)window.FindName("PlaylistList"); ContextMenu? menu = null; FileInformationWindow? information = null;
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); window.UpdateLayout(); }
        static IEnumerable<MenuItem> Items(ItemsControl parent) => parent.Items.OfType<MenuItem>().SelectMany(item => new[] { item }.Concat(Items(item)));
        async Task Invoke(string action)
        {
            Items(menu!).Single(item => Equals(item.Tag, action)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await window.TrackActionCompletion.WaitAsync(TimeSpan.FromSeconds(30)); await Idle();
        }
        try
        {
            model.CreatePlaylist("Owned track menu"); var tab = model.SelectedPlaylist;
            await model.AddPathsAsync([flac, mp3, flac]); await Idle();
            var rows = model.Entries.ToArray(); list.SelectedItems.Clear(); list.SelectedItems.Add(rows[0]); list.SelectedItems.Add(rows[1]);
            var unselected = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(rows[2]);
            unselected.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
            Check(list.SelectedItems.Count == 1 && list.SelectedItems.Contains(rows[2]), "Right-click did not select its new target.");
            list.SelectedItems.Clear(); list.SelectedItems.Add(rows[0]); list.SelectedItems.Add(rows[1]);
            var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(rows[1]);
            container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
            Check(list.SelectedItems.Count == 2, "Right-click lost an existing multi-selection.");
            var opening = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [container, true, -1d, -1d], null)!;
            opening.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent; container.RaiseEvent(opening);
            menu = list.ContextMenu; menu.PlacementTarget = container; menu.IsOpen = true; await Idle();
            var actions = Items(menu).ToArray();
            foreach (var name in new[] { "Play", "Add", "Favorites", "Queue", "Information", "Location", "PreviousPlaylist", "CopyFiles", "Rating",
                "AfterPlaying", "Send", "SendPlaylist", "Deletion", "Recycle", "Enabled" })
                Check(actions.Any(item => Equals(item.Tag, name)), "Missing track action: " + name);
            Check(actions.All(item => item.Tag is not "FindLibrary") && actions.Where(item => item.Icon is not null).All(item => item.Icon is AppIcon { Width: 18, Height: 18 }), "Track menu contains an excluded action or inconsistent icons.");
            Check(actions.Single(item => Equals(item.Tag, "Information")).InputGestureText == "F4" &&
                actions.Single(item => Equals(item.Tag, "Location")).InputGestureText == "Alt+O" &&
                actions.Single(item => Equals(item.Tag, "Recycle")).InputGestureText == "Ctrl+Del" && menu.ActualHeight > 200, "Track popup shortcuts/layout were not rendered.");
            CustomizationValidation.Render(menu, output, "track-menu-" + settings.Language + ".png"); menu.IsOpen = false;
            await Invoke("Rating4"); Check(rows.All(row => row.Rating == 4), "Menu rating did not update selected and duplicate tracks.");
            await Invoke("Enabled"); Check(!rows[0].Enabled && !rows[1].Enabled && rows[2].Enabled, "Disable changed unselected occurrences.");
            menu = window.CreateTrackContextMenu(rows[1]); await Invoke("Enabled"); Check(rows.All(row => row.Enabled), "Enable failed for selected tracks.");
            await Invoke("Favorites"); await Invoke("Favorites");
            var favorites = model.Playlists.Single(item => item.Id == model.WindowSettings.FavoritesPlaylistId);
            Check(favorites.Name == Strings.Get("FavoritesPlaylist") && favorites.Entries.Count == 2 && favorites.Entries.All(row => row.Rating == 4) &&
                model.SelectedPlaylist == tab && model.SourcePlaylistId == source, "Favorites duplicated tracks, lost ratings or changed playback source.");
            await model.SaveNowAsync(); var saved = await ((SqlitePlayerStore)model.LibraryIndex!).LoadAsync();
            Check(saved.Playlists.Single(item => item.Id == favorites.Id).Entries.Select(row => row.Track.Id).SequenceEqual(favorites.Entries.Select(row => row.Entry.Track.Id)) &&
                new SettingsFile(Path.Combine(output, "stage-c-data")).Load().FavoritesPlaylistId == favorites.Id, "Favorites were not saved in SQLite/settings.");
            var before = favorites.Entries.Select(row => row.Id).ToHashSet();
            menu = window.CreateTrackContextMenu(rows[1]); await Invoke("SendPlaylist:" + favorites.Id);
            Check(favorites.Entries.Count == 4 && favorites.Entries.Skip(2).All(row => !before.Contains(row.Id) && row.Rating == 4), "Send-to-playlist did not create independent entries with shared ratings.");
            await Invoke("AddQueue"); var added = model.Queue.Where(row => !queueIds.Contains(row.Id)).ToArray();
            Check(added.Length == 2, "Queue menu did not enqueue the selection.");
            await Invoke("RemoveQueue"); Check(model.Queue.Select(row => row.Id).ToHashSet().SetEquals(queueIds), "Queue removal affected existing queued items.");
            await Invoke("CopyFiles"); Check(Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().SequenceEqual(new[] { flac, mp3 }), "Clipboard contains no real selected-file paths.");
            await Invoke("Information"); information = window.InformationWindow!; await Idle(); await information.LoadCompletion.WaitAsync(TimeSpan.FromSeconds(30));
            Check(information.PathBox.IsReadOnly && information.PathBox.Text == mp3 && information.Tabs.Items.Count == 4 && information.Snapshot is { Id3v1.Count: > 0, Id3v2.Count: > 0 } info &&
                info.General["FileTitleLabel"] == "Owned ID3 title" && info.Lyrics.Contains("Беларускі", StringComparison.Ordinal), "Read-only file information failed to display actual ID3 tags/lyrics.");
            CustomizationValidation.Render((FrameworkElement)information.Content, output, "file-information-" + settings.Language + ".png");
            information.Navigate(-1); await information.LoadCompletion.WaitAsync(TimeSpan.FromSeconds(30));
            Check(information.Snapshot is { Path: var current, SampleRate: > 0, Bytes: > 0, Id3v1.Count: 0, Id3v2.Count: 0 } && current == flac, "File navigation did not display the actual FLAC snapshot.");
            information.Close(); information = null;
            var copyFolder = Path.Combine(directory, "copies"); Directory.CreateDirectory(copyFolder);
            var copied = await TrackFileOperations.CopyAsync([flac, mp3, flac], copyFolder, CancellationToken.None);
            Check(copied.Completed.Length == 2 && copied.Errors.Length == 0 && hashes.All(pair => Hash(Path.Combine(copyFolder, Path.GetFileName(pair.Key))) == pair.Value), "Actual selected-file copy failed content verification or deduplication.");
            var refused = await TrackFileOperations.CopyAsync([flac, mp3], copyFolder, CancellationToken.None);
            Check(refused.Completed.Length == 0 && refused.Errors.Length == 2 && hashes.All(pair => Hash(Path.Combine(copyFolder, Path.GetFileName(pair.Key))) == pair.Value), "Copy overwrote existing files.");
            var collisionFolder = Path.Combine(directory, "collision"); Directory.CreateDirectory(collisionFolder);
            var duplicate = Path.Combine(collisionFolder, Path.GetFileName(flac)); File.Copy(flac, duplicate);
            var emptyFolder = Path.Combine(directory, "empty-destination"); Directory.CreateDirectory(emptyFolder);
            try { await TrackFileOperations.CopyAsync([flac, duplicate], emptyFolder, CancellationToken.None); throw new InvalidOperationException("Duplicate basenames were copied."); }
            catch (IOException) { Check(Directory.GetFiles(emptyFolder).Length == 0, "Duplicate-name refusal left partial copies."); }
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                try { await TrackFileOperations.CopyAsync([flac], copyFolder, canceled.Token); throw new InvalidOperationException("Cancelled copy proceeded."); }
                catch (OperationCanceledException) { }
            }
            var protectedFile = TrackFileOperations.Recycle([flac], flac);
            Check(protectedFile.Completed.Length == 0 && protectedFile.Errors.Length == 1, "Recycle did not protect the loaded source.");
            var cueRow = new PlaylistRowViewModel(new PlaylistEntry(Guid.NewGuid(), rows[0].Entry.Track with { Segment = new TrackSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1)) }));
            var preview = new RecycleFilesWindow(window, [rows[0], cueRow]); preview.Show(); await Idle();
            Check(preview.Paths.Length == 1 && preview.CancelButton.IsDefault && !preview.ConfirmButton.IsDefault &&
                ((DockPanel)preview.Content).Children.OfType<TextBlock>().Single().Text.Contains(Strings.Get("RecycleCueWarning"), StringComparison.Ordinal), "Recycle preview lacks safe cancellation, physical deduplication or CUE warning.");
            preview.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!preview.IsVisible && hashes.All(pair => Hash(pair.Key) == pair.Value), "Cancelling recycle changed audio bytes.");
            model.SelectedPlaylist = selected; await Invoke("Rating5");
            Check(rows.All(row => row.Rating == 4), "A stale track popup changed a different playlist.");
            Check(model.SourcePlaylistId == source && model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State && model.Snapshot.Position == snapshot.Position,
                "Track editing/file information interrupted the active source.");
            var sourceTab = model.Playlists.Single(item => item.Id == source); var sourceOrder = sourceTab.Entries.ToArray();
            model.SelectedPlaylist = sourceTab;
            try
            {
                var active = sourceTab.Entries.Single(row => row.Id == snapshot.EntryId);
                model.PlaceAfterPlaying([sourceOrder[0], active]);
                Check(sourceTab.Entries.IndexOf(sourceOrder[0]) == sourceTab.Entries.IndexOf(active) + 1, "Place-after-playing failed or moved the active row itself.");
                var order = sourceTab.Entries.Select(row => row.Id).ToArray(); model.Search = "owned-no-match"; model.PlaceAfterPlaying([sourceOrder[1]]);
                Check(sourceTab.Entries.Select(row => row.Id).SequenceEqual(order), "Filtered place-after-playing reordered the source.");
            }
            finally
            {
                model.Search = "";
                for (var i = 0; i < sourceOrder.Length; i++) model.DropEntries([sourceOrder[i]], i);
                model.SelectedPlaylist = selected;
            }
            foreach (var path in hashes.Keys) using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            return new { Status = "track-actions-passed", RoutedRightClick = true, MultiSelectionPreserved = true, LocalizedPopup = true, MenuHierarchyAndShortcuts = true,
                FavoritesDeduplicatedAndPersisted = true, IndependentPlaylistCopies = true, QueueSelectionOnly = true, RatingAndEnabledSelection = true,
                ClipboardFileDropList = true, ActualReadOnlyTagsAndLyrics = true, FileNavigation = true, MetadataHandlesReleased = true, CopiedHashes = true,
                ExistingCopiesPreserved = true, DuplicateCopyNamesRefused = true, CancelledCopy = true, LoadedSourceRecycleRefused = true, CancelledRecyclePreservesSources = true,
                SharedCueWarning = true, StaleSelectionRefused = true, PlaceAfterPlayingStableAndFiltered = true, PlaybackPreserved = true, NativeRecycleDialogs = "manual-not-run" };
        }
        finally
        {
            information?.Close(); if (menu is not null) menu.IsOpen = false;
            foreach (var item in model.Queue.Where(row => !queueIds.Contains(row.Id)).ToArray()) model.RemoveQueued(item.Id);
            foreach (var tab in model.Playlists.Except(initial).ToArray()) { model.SelectedPlaylist = tab; model.DeletePlaylist(); }
            model.SelectedPlaylist = selected; model.WindowSettings = settings; list.SelectedItems.Clear(); Clipboard.Clear(); await model.SaveNowAsync();
            Directory.Delete(directory, true);
        }
    }
}
