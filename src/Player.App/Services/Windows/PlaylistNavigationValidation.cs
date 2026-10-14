using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

internal static class PlaylistNavigationValidation
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static T? Child<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            if (Child<T>(child) is { } nested) return nested;
        }
        return null;
    }
    internal static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        var initial = model.Playlists.ToArray(); var selected = model.SelectedPlaylist; var settings = model.WindowSettings;
        var search = model.Search; var width = window.Width;
        var snapshot = model.Snapshot; var source = model.SourcePlaylistId; var queue = model.Queue.Select(item => item.Id).ToArray();
        var tabs = (ListBox)window.FindName("PlaylistTabs"); var list = (ListBox)window.FindName("PlaylistList");
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); window.UpdateLayout(); }
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        async Task<Button> Header(PlaylistRowViewModel row)
        {
            list.ScrollIntoView(row); await Idle(); var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row);
            return (Button)container.Template.FindName("AlbumHeading", container);
        }
        MainWindow.AlbumDragPayload Payload(PlaylistRowViewModel heading) => new(window, model.SelectedPlaylist.Id, heading, model.Entries.Select(row => row.Id).ToArray());
        DragEventArgs Route(UIElement target, Point point, MainWindow.AlbumDragPayload payload, RoutedEvent routedEvent)
        {
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [new DataObject(MainWindow.AlbumDragFormat, payload), DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, point], null)!;
            args.RoutedEvent = routedEvent; target.RaiseEvent(args);
            if (routedEvent != UIElement.DragLeaveEvent) Check(args.Handled, "Album drag escaped the production handler."); return args;
        }
        void Order(params PlaylistRowViewModel[] rows) => Check(model.Entries.SequenceEqual(rows), "Album move split, lost, duplicated or reordered song occurrences.");
        async Task MenuMove(PlaylistRowViewModel row, int delta)
        {
            var header = await Header(row);
            var args = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [header, true, -1d, -1d], null)!;
            args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent; header.RaiseEvent(args); await Idle();
            Check(args.Handled && header.ContextMenu.IsOpen, "Album keyboard context route did not open its menu.");
            var item = header.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, delta));
            Check(item.IsEnabled && Equals(item.Header, Strings.Get(delta < 0 ? "MoveAlbumUp" : "MoveAlbumDown")), "Album move menu lacks a localized enabled alternative.");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); header.ContextMenu.IsOpen = false; await Idle();
        }
        try
        {
            window.Width = window.MinWidth; model.Search = ""; model.WindowSettings = settings with { ShowAlbumSections = true };
            for (var i = 0; i < 24; i++) model.CreatePlaylist("Owned long playlist _" + i + " — Беларуская музыка");
            var last = model.SelectedPlaylist; await Idle();
            var scroll = Child<ScrollViewer>(tabs)!;
            var previous = (Button)window.FindName("TabsPrevious"); var next = (Button)window.FindName("TabsNext"); var all = (Button)window.FindName("AllPlaylists");
            Check(scroll.ScrollableWidth > 0 && scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Hidden && previous.IsVisible && next.IsVisible,
                "Overflow tabs retain the horizontal scrollbar or lack navigation.");
            var container = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(last);
            var bounds = container.TransformToAncestor(scroll).TransformBounds(new Rect(container.RenderSize));
            Check(bounds.Left >= -.5 && bounds.Right <= scroll.ViewportWidth + .5, "Newly selected last playlist is clipped or offscreen.");
            scroll.ScrollToLeftEnd(); await Idle();
            Check(!previous.IsEnabled && next.IsEnabled, "Tab boundary arrows have the wrong enabled state.");
            Click(next); await Idle(); Check(scroll.HorizontalOffset > 0, "Next arrow did not scroll tabs.");
            var offset = scroll.HorizontalOffset;
            tabs.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent }); await Idle();
            Check(scroll.HorizontalOffset > offset, "Wheel over tabs did not scroll horizontally.");
            Click(previous); await Idle(); Check(scroll.HorizontalOffset < offset + 120, "Previous arrow did not scroll backwards.");
            Click(all); await Idle();
            Check(all.ContextMenu.Items.Count == model.Playlists.Count && Child<ScrollViewer>(all.ContextMenu) is { ScrollableHeight: > 0 },
                "All-playlists menu omitted entries or cannot scroll its bounded list.");
            var firstMenu = all.ContextMenu.Items.OfType<MenuItem>().First();
            Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(firstMenu)), "Playlist picker lacks accessible names.");
            firstMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); all.ContextMenu.IsOpen = false; await Idle();
            Check(ReferenceEquals(model.SelectedPlaylist, initial[0]), "All-playlists picker selected the wrong playlist.");
            model.SelectedPlaylist = last; await Idle();
            window.Width = width; await Idle();
            container = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(last);
            bounds = container.TransformToAncestor(scroll).TransformBounds(new Rect(container.RenderSize));
            Check(bounds.Left >= -.5 && bounds.Right <= scroll.ViewportWidth + .5, "Resizing hid the selected tab.");
            window.Width = window.MinWidth; await Idle();
            CustomizationValidation.Render(window, output, "playlist-overflow-" + model.WindowSettings.Language + ".png");

            var directory = Directory.CreateDirectory(Path.Combine(output, "album-drag-" + Guid.NewGuid().ToString("N"))).FullName;
            string Song(string folder, string name)
            {
                var path = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, folder)).FullName, name + ".flac");
                File.Copy(fixture, path); using var file = TagLib.File.Create(path); file.Tag.Album = "Owned album " + folder; file.Tag.Title = name; file.Save(); return path;
            }
            var paths = new[] { Song("A", "A1"), Song("A", "A2"), Song("B", "B1"), Song("B", "B2"), Song("C", "C1") };
            var hashes = paths.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
            await model.AddPathsAsync(paths); var rows = model.Entries.ToArray(); await Idle();
            var payload = Payload(rows[0]); var target = await Header(rows[4]);
            Check(Route(target, new Point(2, target.ActualHeight - 2), payload, UIElement.DragOverEvent).Effects == DragDropEffects.Move,
                "Owned album drag was refused.");
            CustomizationValidation.Render(window, output, "album-insertion-" + model.WindowSettings.Language + ".png");
            Check(Route(target, new Point(2, target.ActualHeight - 2), payload, UIElement.DropEvent).Effects == DragDropEffects.Move, "Album drop after target was refused.");
            await Idle(); Order(rows[2], rows[3], rows[4], rows[0], rows[1]);
            Check(list.SelectedItems.Count == 2 && list.SelectedItems.Contains(rows[0]) && list.SelectedItems.Contains(rows[1]), "Moved album songs did not retain selection.");
            payload = Payload(rows[0]); target = await Header(rows[2]);
            Route(target, new Point(2, 2), payload, UIElement.DropEvent); await Idle(); Order(rows);
            target = await Header(rows[0]);
            Route(target, new Point(2, 2), Payload(rows[0]), UIElement.DropEvent); Order(rows);
            var foreign = Payload(rows[0]) with { Owner = null! }; target = await Header(rows[4]);
            Check(Route(target, new Point(2, 2), foreign, UIElement.DragOverEvent).Effects == DragDropEffects.None, "Foreign album payload accepted.");
            Route(target, new Point(2, 2), foreign, UIElement.DropEvent); Order(rows);
            var stale = Payload(rows[0]) with { Order = rows.Reverse().Select(row => row.Id).ToArray() };
            Route(target, new Point(2, 2), stale, UIElement.DropEvent); Order(rows);
            Route(tabs, new Point(2, 2), Payload(rows[0]), UIElement.DropEvent); Order(rows);
            var filtered = Payload(rows[0]); model.Search = "A1"; await Idle(); target = await Header(rows[0]);
            Check(Route(target, new Point(2, 2), filtered, UIElement.DragOverEvent).Effects == DragDropEffects.None, "Filtered album drag accepted.");
            Route(target, new Point(2, 2), filtered, UIElement.DropEvent); Order(rows);
            model.Search = ""; await Idle();
            await MenuMove(rows[0], 1); Order(rows[2], rows[3], rows[0], rows[1], rows[4]);
            await MenuMove(rows[0], -1); Order(rows);
            target = await Header(rows[0]); Route(target, new Point(2, 2), Payload(rows[4]), UIElement.DropEvent); await Idle();
            Order(rows[4], rows[0], rows[1], rows[2], rows[3]);
            await model.SaveNowAsync(); var reopened = await ((Storage.SqlitePlayerStore)model.LibraryIndex!).LoadAsync();
            Check(reopened.Playlists.Single(tab => tab.Id == last.Id).Entries.Select(entry => entry.Id).SequenceEqual(model.Entries.Select(row => row.Id)), "Album order did not persist to SQLite.");
            Check(paths.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).SequenceEqual(hashes), "Album reordering changed source audio files.");
            Check(model.SourcePlaylistId == source && model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State &&
                model.Snapshot.Position == snapshot.Position && model.Queue.Select(item => item.Id).SequenceEqual(queue), "Playlist navigation or album movement interrupted playback or queue.");
            CustomizationValidation.Render(window, output, "album-drag-" + model.WindowSettings.Language + ".png");
            // Real logical scrolling with virtualized song containers, while the dragged heading itself is offscreen.
            await model.AddPathsAsync(Enumerable.Repeat(paths[2], 32).ToArray()); await Idle();
            var songScroll = Child<ScrollViewer>(list)!; songScroll.ScrollToTop(); await Idle();
            Check(songScroll.ScrollableHeight > 0, "Owned long playlist did not create vertical overflow.");
            var autoPayload = Payload(rows[4]);
            Route(list, new Point(5, list.ActualHeight - 2), autoPayload, UIElement.DragOverEvent); await Idle();
            Check(songScroll.VerticalOffset > 0, "Dragging at the lower playlist edge did not scroll.");
            await Task.Delay(120);
            Route(list, new Point(5, 2), autoPayload, UIElement.DragOverEvent); await Idle();
            Check(songScroll.VerticalOffset == 0, "Dragging at the upper playlist edge did not scroll back.");
            Route(list, new Point(5, 2), autoPayload, UIElement.DragLeaveEvent);

            var cue = Path.Combine(Path.GetDirectoryName(paths[0])!, "owned.cue");
            await File.WriteAllTextAsync(cue, "TITLE \"Owned cue album\"\nFILE \"A1.flac\" WAVE\n TRACK 01 AUDIO\n TITLE \"Cue one\"\n INDEX 01 00:00:00\n TRACK 02 AUDIO\n TITLE \"Cue two\"\n INDEX 01 00:01:00\n");
            var cueHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cue)));
            model.CreatePlaylist("Owned cue album movement"); await model.AddPathsAsync([cue, paths[2]]); await Idle();
            var cueRows = model.Entries.ToArray();
            Check(cueRows.Length == 3 && cueRows[0].Entry.Track.Segment is not null && cueRows[1].Entry.Track.Segment is not null, "Cue move fixture did not import genuine segments.");
            target = await Header(cueRows[2]);
            Route(target, new Point(2, target.ActualHeight - 2), Payload(cueRows[0]), UIElement.DropEvent); await Idle();
            Order(cueRows[2], cueRows[0], cueRows[1]);
            Check(cueRows[0].Entry.Track.Path == cueRows[1].Entry.Track.Path &&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cue))) == cueHash &&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths[0]))) == hashes[0], "Moving CUE songs changed their shared image or cue document.");
            return new { Status = "playlist-navigation-passed", OverflowArrows = true, HorizontalWheel = true, SelectedTabVisible = true,
                SelectedTabVisibleAfterResize = true, BoundedAllPlaylists = true, LocalizedAccessiblePicker = true, BeforeAndAfterAlbumDrop = true, WholeBlockOrder = true, AlbumEdgeScrolling = true, CueSharedImageOrder = true,
                SelfForeignStaleOutsideFilteredRefused = true, KeyboardMenuAlternative = true, SelectionRetained = true, SQLiteOrder = true,
                SourceHashesUnchanged = true, PlaybackAndQueuePreserved = true, SoftwareRoutedWpfEvents = true };
        }
        finally
        {
            ((Button)window.FindName("AllPlaylists")).ContextMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
            foreach (var tab in model.Playlists.Except(initial).ToArray()) { model.SelectedPlaylist = tab; model.DeletePlaylist(); }
            model.WindowSettings = settings; model.SelectedPlaylist = selected; model.Search = search; window.Width = width;
            await model.SaveNowAsync();
        }
    }
}
