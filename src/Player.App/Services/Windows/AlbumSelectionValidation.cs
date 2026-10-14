using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.Services.Storage;
using Player.App.ViewModels;
using Player.App.Views;

namespace Player.App.Services.Windows;

/// <summary>Owned real WPF album selection and playlist-only menu/keyboard removal.</summary>
internal static class AlbumSelectionValidation
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static IEnumerable<MenuItem> Items(ItemsControl parent) => parent.Items.OfType<MenuItem>().SelectMany(item => new[] { item }.Concat(Items(item)));

    internal static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        var initial = model.Playlists.ToArray(); var selected = model.SelectedPlaylist; var settings = model.WindowSettings;
        var snapshot = model.Snapshot; var source = model.SourcePlaylistId; var queue = model.Queue.Select(item => item.Id).ToArray();
        var directory = Path.Combine(output, "album-selection-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var list = (ListBox)window.FindName("PlaylistList");
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); window.UpdateLayout(); }
        string Song(string folder, string name, string title)
        {
            var parent = Directory.CreateDirectory(Path.Combine(directory, folder)).FullName;
            var path = Path.Combine(parent, name); File.Copy(fixture, path);
            using var file = TagLib.File.Create(path); file.Tag.Album = "Owned album"; file.Tag.Title = title; file.Save(); return path;
        }
        async Task<Button> Header(PlaylistRowViewModel row)
        {
            list.ScrollIntoView(row); await Idle();
            var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row);
            return (Button)container.Template.FindName("AlbumHeading", container);
        }
        void Selection(params PlaylistRowViewModel[] rows) => Check(list.SelectedItems.Cast<PlaylistRowViewModel>().Select(row => row.Id).ToHashSet().SetEquals(rows.Select(row => row.Id)),
            "Album selection included unrelated/hidden rows or lost repeated occurrences.");
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        async Task PressHeaderKey(Button button, Key key)
        {
            button.Focus(); var input = PresentationSource.FromVisual(button)!;
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, input, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, input, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, input, 0, key) { RoutedEvent = Keyboard.KeyUpEvent }); await Idle();
        }
        async Task Delete(bool control)
        {
            window.ShowAndActivate(); list.Focus(); await Idle();
            try
            {
                if (control)
                {
                    keybd_event(0x11, 0, 0, 0);
                    var timer = Stopwatch.StartNew();
                    while ((Keyboard.Modifiers & ModifierKeys.Control) == 0 && timer.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(10);
                    Check(Keyboard.Modifiers == ModifierKeys.Control, "Owned Ctrl+Delete control could not press Control.");
                }
                else Check(Keyboard.Modifiers == ModifierKeys.None, "Owned Delete control has unexpected modifiers.");
                list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list)!, 0, Key.Delete) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Idle();
            }
            finally { if (control) { keybd_event(0x11, 0, 2, 0); await Idle(); } }
        }
        try
        {
            var first = Song("album-a", "one.flac", "Owned first song"); var second = Song("album-a", "two.flac", "Owned second song");
            var other = Song("album-b", "one.flac", "Unrelated same album name");
            var hashes = new Dictionary<string, string> { [first] = Hash(first), [second] = Hash(second), [other] = Hash(other) };
            model.WindowSettings = settings with { ShowAlbumSections = true }; model.Search = "";
            model.CreatePlaylist("Owned album selection"); var tab = model.SelectedPlaylist;
            await model.AddPathsAsync([first, other, second, first]); await Idle(); var rows = model.Entries.ToArray();
            Check(rows.Length == 4, "Owned album control did not import actual files and duplicate entries.");
            model.CreatePlaylist("Retained album references"); var retained = model.SelectedPlaylist; model.SelectedPlaylist = tab;
            model.CopyEntriesToPlaylist(rows, retained.Id); var retainedIds = retained.Entries.Select(row => row.Id).ToArray(); await Idle();
            var order = model.Entries.Select(row => row.Id).ToArray();
            var header = await Header(rows[0]);
            Check(header.IsVisible && header.Focusable && header.IsTabStop && AutomationProperties.GetName(header) == rows[0].SelectAlbumLabel &&
                AutomationProperties.GetHelpText(header) == Strings.Get("SelectAlbumHelp"), "Album button lacks its visible, localized accessible action.");
            list.SelectedItems.Clear(); list.SelectedItems.Add(rows[1]); Click(header); Selection(rows[0], rows[2], rows[3]);
            list.SelectedItems.Clear(); await PressHeaderKey(header, Key.Enter); Selection(rows[0], rows[2], rows[3]);
            list.SelectedItems.Clear(); await PressHeaderKey(header, Key.Space); Selection(rows[0], rows[2], rows[3]);
            var peer = UIElementAutomationPeer.CreatePeerForElement(header)!;
            list.SelectedItems.Clear(); ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke(); await Idle(); Selection(rows[0], rows[2], rows[3]);
            header.Focus(); await Idle();
            var surface = (Border)header.Template.FindName("AlbumHeaderSurface", header);
            Check(surface.BorderBrush is SolidColorBrush brush && brush.Color == ((SolidColorBrush)header.FindResource("AccentBrush")).Color, "Album keyboard focus has no visible border.");
            Check(model.Entries.Select(row => row.Id).SequenceEqual(order), "Selecting an album reordered its playlist.");
            CustomizationValidation.Render((FrameworkElement)window.Content, output, "album-selection-" + settings.Language + ".png");
            model.Search = "Owned first song"; await Idle(); header = await Header(rows[0]); Click(header); Selection(rows[0], rows[3]);
            model.Search = ""; await Idle(); header = await Header(rows[0]); Click(header); Selection(rows[0], rows[2], rows[3]);
            var menu = window.CreateTrackContextMenu(rows[0]); var remove = Items(menu).Single(item => Equals(item.Tag, "RemoveSelected"));
            Check(Equals(remove.Header, Strings.Get("RemoveSelectedFromPlaylist")) && remove.InputGestureText == "Del" &&
                Items(menu).All(item => item.Tag is not "Recycle"), "Track menu still exposes physical deletion.");
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await window.TrackActionCompletion; await Idle();
            Check(model.Entries.Count == 1 && model.Entries[0].Id == rows[1].Id && retained.Entries.Select(row => row.Id).SequenceEqual(retainedIds) &&
                hashes.All(pair => Hash(pair.Key) == pair.Value), "Menu removal changed files, unrelated rows or another playlist.");
            await model.SaveNowAsync(); var saved = await ((SqlitePlayerStore)model.LibraryIndex!).LoadAsync();
            Check(saved.Playlists.Single(item => item.Id == tab.Id).Entries.Select(row => row.Id).SequenceEqual(new[] { rows[1].Id }), "Playlist-only removal was not persisted.");
            model.SelectedPlaylist = retained; var retainedOrder = retained.Entries.Select(row => row.Id).ToArray();
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await window.TrackActionCompletion;
            Check(retained.Entries.Select(row => row.Id).SequenceEqual(retainedOrder), "A stale removal menu changed another playlist.");
            model.SelectedPlaylist = tab;
            list.SelectedItems.Clear(); list.SelectedItems.Add(model.Entries.Single()); await Delete(false);
            Check(model.Entries.Count == 0 && hashes.All(pair => Hash(pair.Key) == pair.Value), "Delete changed physical files.");
            await model.AddPathsAsync([first]); await Idle(); list.SelectedItems.Clear(); list.SelectedItems.Add(model.Entries.Single()); await Delete(true);
            Check(model.Entries.Count == 0 && hashes.All(pair => Hash(pair.Key) == pair.Value), "Ctrl+Delete changed physical files.");
            var cueFolder = Directory.CreateDirectory(Path.Combine(directory, "cue")).FullName;
            var image = Path.Combine(cueFolder, "image.flac"); File.Copy(fixture, image);
            var cue = Path.Combine(cueFolder, "album.cue");
            File.WriteAllText(cue, "TITLE \"Shared owned album\"\nFILE \"image.flac\" WAVE\n TRACK 01 AUDIO\n TITLE \"First song\"\n INDEX 01 00:00:00\n TRACK 02 AUDIO\n TITLE \"Second song\"\n INDEX 01 00:01:00\n", new UTF8Encoding(false));
            var imageHash = Hash(image); var cueHash = Hash(cue); await model.AddPathsAsync([cue]); await Idle();
            Check(model.Entries.Count == 2 && model.Entries.All(row => row.Entry.Track.Segment is not null), "Owned CUE control did not expand real audio.");
            header = await Header(model.Entries[0]); Click(header); Selection(model.Entries.ToArray());
            menu = window.CreateTrackContextMenu(model.Entries[0]); Items(menu).Single(item => Equals(item.Tag, "RemoveSelected")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await window.TrackActionCompletion; await Idle();
            Check(model.Entries.Count == 0 && Hash(image) == imageHash && Hash(cue) == cueHash, "CUE removal changed the shared image or document.");
            Check(model.SourcePlaylistId == source && model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State &&
                model.Snapshot.Position == snapshot.Position && model.Queue.Select(item => item.Id).SequenceEqual(queue), "Album editing interrupted playback or queue.");
            return new { Status = "album-selection-passed", RepeatedOccurrences = true, SameNamesInOtherFoldersExcluded = true, VisibleFilterOnly = true,
                RoutedHeaderClick = true, EnterAndSpace = true, AccessibleInvokeAndFocus = true, NoReorder = true, MenuPlaylistOnlyDeletion = true,
                DeletePlaylistOnly = true, ControlDeletePlaylistOnly = true, OtherPlaylistsPreserved = true, SavedRemoval = true,
                CueSharedImagePreserved = true, SourceHashesUnchanged = true, StaleMenuRefused = true, PlaybackAndQueuePreserved = true };
        }
        finally
        {
            model.Search = "";
            foreach (var tab in model.Playlists.Except(initial).ToArray()) { model.SelectedPlaylist = tab; model.DeletePlaylist(); }
            model.SelectedPlaylist = selected; model.WindowSettings = settings; list.SelectedItems.Clear(); await model.SaveNowAsync();
            Directory.Delete(directory, true);
        }
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
