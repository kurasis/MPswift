using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;

namespace Player.App.Services.Windows;

internal static class PlaylistTabsValidation
{
    public static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var selected = model.SelectedPlaylist; var initial = model.Playlists.ToArray();
        var snapshot = model.Snapshot; var source = model.SourcePlaylistId; var queue = model.Queue.Select(q => q.Id).ToArray();
        var tabs = (ListBox)window.FindName("PlaylistTabs");
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); window.UpdateLayout(); }
        ListBoxItem Container(PlaylistTabViewModel tab) => (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(tab);
        void OpenMenu(PlaylistTabViewModel tab)
        {
            var container = Container(tab);
            var args = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [container, true, -1d, -1d], null)!;
            args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent; container.RaiseEvent(args);
            Check(!args.Handled && ReferenceEquals(model.SelectedPlaylist, tab), "Tab context menu did not select the clicked target.");
            container.ContextMenu.PlacementTarget = container; container.ContextMenu.IsOpen = true;
        }
        void Action(PlaylistTabViewModel tab, string action)
        {
            var menu = Container(tab).ContextMenu;
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, action)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
        }
        DragEventArgs Route(UIElement target, Point point, PlaylistTabViewModel tab, RoutedEvent routedEvent, bool owned = true)
        {
            var data = new DataObject(MainWindow.PlaylistTabDragFormat, new MainWindow.PlaylistTabDragPayload(owned ? window : null!, tab));
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, point], null)!;
            args.RoutedEvent = routedEvent; target.RaiseEvent(args);
            Check(args.Handled, "Tab drag event escaped the production handler."); return args;
        }
        try
        {
            model.CreatePlaylist("Owned tab A"); var a = model.SelectedPlaylist;
            await model.AddPathsAsync([fixture]);
            model.CreatePlaylist("Owned tab B"); var b = model.SelectedPlaylist;
            model.CreatePlaylist("Owned tab C"); var c = model.SelectedPlaylist;
            model.SelectedPlaylist = selected; await Idle();
            OpenMenu(a); await Idle();
            Check(Container(a).ContextMenu.ActualHeight > 0 && Container(a).ContextMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, Strings.DeletePlaylist)),
                "Localized tab menu did not render management actions.");
            CustomizationValidation.Render(Container(a).ContextMenu, output, "playlist-tab-menu-" + model.WindowSettings.Language + ".png");
            // Change selection while the popup is open: its actions must retain the clicked target.
            model.SelectedPlaylist = selected; Action(a, "Duplicate"); var copy = model.SelectedPlaylist;
            Check(copy.Name.StartsWith(a.Name, StringComparison.Ordinal) && copy.Entries.Count == 1 && copy.Entries[0].Id != a.Entries[0].Id,
                "Tab menu duplicated the current selection instead of its own target.");
            OpenMenu(b); model.SelectedPlaylist = selected;
            var deleteResponse = UiAuditValidation.RespondToDeletion(window, false); Action(b, "Delete"); await deleteResponse;
            Check(model.Playlists.Contains(b), "Cancelled tab deletion removed its target.");
            OpenMenu(b); model.SelectedPlaylist = selected;
            deleteResponse = UiAuditValidation.RespondToDeletion(window, true); Action(b, "Delete"); await deleteResponse;
            Check(!model.Playlists.Contains(b) && model.Playlists.Contains(selected), "Tab menu deleted the wrong playlist.");
            await Idle(); OpenMenu(a); Action(a, "Right"); await Idle();
            Check(model.Playlists.IndexOf(a) == model.Playlists.IndexOf(c) + 1, "Tab-menu movement did not move the target right.");

            // Before and after halves use real routed DragOver/Drop positions on actual tab containers.
            model.SelectedPlaylist = c; var dragSelection = model.SelectedPlaylist;
            var target = Container(c);
            var over = Route(target, new Point(1, target.ActualHeight / 2), copy, UIElement.DragOverEvent);
            Check(over.Effects == DragDropEffects.Move, "Owned tab drag was refused.");
            Route(target, new Point(1, target.ActualHeight / 2), copy, UIElement.DropEvent); await Idle();
            Check(model.Playlists.IndexOf(copy) + 1 == model.Playlists.IndexOf(c) && ReferenceEquals(model.SelectedPlaylist, dragSelection), "Dropping before a tab used the wrong removal-adjusted position or changed selection.");
            target = Container(a);
            Route(target, new Point(target.ActualWidth - 1, target.ActualHeight / 2), copy, UIElement.DropEvent); await Idle();
            Check(model.Playlists.IndexOf(copy) == model.Playlists.IndexOf(a) + 1 && ReferenceEquals(model.SelectedPlaylist, dragSelection), "Dropping after a tab used the wrong insertion position or changed selection.");
            var order = model.Playlists.Select(tab => tab.Id).ToArray(); var current = model.SelectedPlaylist;
            target = Container(copy);
            Route(target, new Point(1, 1), copy, UIElement.DropEvent);
            Check(model.Playlists.Select(tab => tab.Id).SequenceEqual(order), "Self-drop changed tab order.");
            Check(Route(Container(c), new Point(1, 1), a, UIElement.DragOverEvent, false).Effects == DragDropEffects.None,
                "Foreign tab payload was accepted.");
            Route(Container(c), new Point(1, 1), a, UIElement.DropEvent, false);
            Route((UIElement)window.FindName("PlaylistList"), new Point(1, 1), a, UIElement.DropEvent);
            Check(model.Playlists.Select(tab => tab.Id).SequenceEqual(order) && ReferenceEquals(model.SelectedPlaylist, current), "Rejected drops changed order/selection.");
            await model.SaveNowAsync();
            var persisted = await ((Storage.SqlitePlayerStore)model.LibraryIndex!).LoadAsync();
            Check(persisted.Playlists.Select(tab => tab.Id).SequenceEqual(order), "Dragged tab order did not persist to SQLite.");
            Check(model.SourcePlaylistId == source && model.Snapshot.EntryId == snapshot.EntryId && model.Snapshot.State == snapshot.State &&
                model.Snapshot.Position == snapshot.Position && model.Queue.Select(q => q.Id).SequenceEqual(queue), "Playlist management interrupted playback or queue identity.");
            return new { Status = "playlist-tabs-passed", ClickedTabManagement = true, KeyboardContextRoute = true, LocalizedPopup = true,
                DuplicateAndDeleteTarget = true, MenuMovement = true, BeforeAndAfterDrop = true, SelfAndForeignAndOutsideDrops = true,
                SQLiteOrder = true, SelectionPreservedOnMove = true, PlaybackAndQueuePreserved = true, SoftwareRoutedWpfEvents = true };
        }
        finally
        {
            foreach (var tab in model.Playlists.Except(initial).ToArray()) { Container(tab)?.ContextMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); model.SelectedPlaylist = tab; model.DeletePlaylist(); }
            for (var i = 0; i < initial.Length; i++) model.MovePlaylist(initial[i], i);
            model.SelectedPlaylist = selected; await model.SaveNowAsync();
        }
    }
}
