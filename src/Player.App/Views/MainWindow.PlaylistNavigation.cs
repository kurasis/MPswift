using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Controls;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.Core.Playback;

namespace Player.App.Views;

public partial class MainWindow
{
    internal const string AlbumDragFormat = "MPswift.AlbumRun";
    internal sealed record AlbumDragPayload(MainWindow Owner, Guid PlaylistId, PlaylistRowViewModel Heading, Guid[] Order);
    private AlbumDragPayload? _albumDrag;
    private AlbumInsertionAdorner? _albumInsertion;
    private long _lastAlbumScroll;

    private void OnTabWheel(object sender, MouseWheelEventArgs e)
    {
        if (VisualChild<ScrollViewer>(PlaylistTabs) is not { } scroll || scroll.ScrollableWidth <= 0) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
    private void OnScrollTabs(object sender, RoutedEventArgs e)
    {
        if (VisualChild<ScrollViewer>(PlaylistTabs) is not { } scroll) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + (ReferenceEquals(sender, TabsPrevious) ? -1 : 1) * Math.Max(120, scroll.ViewportWidth * .75));
    }
    private void OnTabScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateTabNavigation();
    private void UpdateTabNavigation()
    {
        if (TabsPrevious is null || TabsNext is null || VisualChild<ScrollViewer>(PlaylistTabs) is not { } scroll) return;
        var overflow = scroll.ScrollableWidth > .5;
        TabsPrevious.Visibility = TabsNext.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        TabsPrevious.IsEnabled = overflow && scroll.HorizontalOffset > .5;
        TabsNext.IsEnabled = overflow && scroll.HorizontalOffset < scroll.ScrollableWidth - .5;
    }
    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, PlaylistTabs)) return;
        RevealSelectedTab();
    }
    private void OnTabStripSizeChanged(object sender, SizeChangedEventArgs e) => RevealSelectedTab();
    private void OnTabsLoaded(object sender, RoutedEventArgs e) => RevealSelectedTab();
    private void RevealSelectedTab() => Dispatcher.BeginInvoke(() =>
    {
        if (_shutdownStarted || PlaylistTabs.SelectedItem is null) return;
        PlaylistTabs.ScrollIntoView(PlaylistTabs.SelectedItem);
        UpdateTabNavigation();
    }, DispatcherPriority.Loaded);

    private void OnAllPlaylists(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted || sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, MaxHeight = Math.Min(440, SystemParameters.WorkArea.Height * .75) };
        foreach (var tab in Model.Playlists)
        {
            // TextBlock keeps underscores in user-provided names literal rather than access-key markers.
            var item = new MenuItem { Header = new TextBlock { Text = tab.Name, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis },
                ToolTip = tab.Name, IsCheckable = true, IsChecked = ReferenceEquals(tab, Model.SelectedPlaylist) };
            AutomationProperties.SetName(item, tab.Name);
            item.Click += (_, _) => { if (!_shutdownStarted && Model.Playlists.Contains(tab)) Model.SelectedPlaylist = tab; };
            menu.Items.Add(item);
        }
        button.ContextMenu = menu; menu.IsOpen = true;
    }

    private Button? AlbumHeader(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, PlaylistList))
        {
            if (source is Button { Name: "AlbumHeading" } header) return header;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
    private bool TryAlbumPayload(IDataObject data, out AlbumDragPayload? payload)
    {
        payload = data.GetData(AlbumDragFormat) as AlbumDragPayload;
        return payload is not null && ReferenceEquals(payload.Owner, this) && payload.PlaylistId == Model.SelectedPlaylist.Id &&
            Model.CanReorder && Model.WindowSettings.ShowAlbumSections && Model.Entries.Contains(payload.Heading) &&
            payload.Heading.HasSection && payload.Order.SequenceEqual(Model.Entries.Select(row => row.Id));
    }
    private AlbumSection[] CurrentAlbumRuns() => AlbumSections.Create(Model.Entries.Select(row => row.Entry.Track)).ToArray();
    private (int Index, double Edge) AlbumInsertion(DragEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: PlaylistRowViewModel row } container)
        {
            var index = Model.Entries.IndexOf(row);
            var section = CurrentAlbumRuns().First(run => index >= run.StartIndex && index < run.StartIndex + run.Count);
            var header = AlbumHeader(e.OriginalSource as DependencyObject);
            var after = header is not null ? e.GetPosition(header).Y >= header.ActualHeight / 2 : e.GetPosition(container).Y >= container.ActualHeight / 2;
            var boundary = section.StartIndex + (after ? section.Count : 0);
            var edgeRow = Model.Entries[after ? boundary - 1 : boundary];
            var edgeContainer = PlaylistList.ItemContainerGenerator.ContainerFromItem(edgeRow) as ListBoxItem;
            var edge = edgeContainer is not null ? edgeContainer.TransformToAncestor(PlaylistList).Transform(new Point(0, after ? edgeContainer.ActualHeight : 0)).Y :
                after ? PlaylistList.ActualHeight : 0;
            return (boundary, edge);
        }
        // Whitespace below the final realized row means the end, including empty playlists.
        return (Model.Entries.Count, PlaylistList.ActualHeight);
    }
    private void OnAlbumDragOver(DragEventArgs e)
    {
        var valid = TryAlbumPayload(e.Data, out _) && HasAncestor(e.OriginalSource as DependencyObject, PlaylistList);
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        ClearAlbumInsertion();
        if (valid)
        {
            var now = Environment.TickCount64;
            var y = e.GetPosition(PlaylistList).Y;
            if (now - _lastAlbumScroll >= 100 && VisualChild<ScrollViewer>(PlaylistList) is { } scroll)
            {
                if (y < 28) { scroll.LineUp(); _lastAlbumScroll = now; }
                else if (y > PlaylistList.ActualHeight - 28) { scroll.LineDown(); _lastAlbumScroll = now; }
            }
            if (AdornerLayer.GetAdornerLayer(PlaylistList) is { } layer)
            {
                _albumInsertion = new(PlaylistList, AlbumInsertion(e).Edge, (Brush)FindResource("AccentBrush")) { IsHitTestVisible = false };
                layer.Add(_albumInsertion);
            }
        }
        e.Handled = true;
    }
    private void OnAlbumDrop(DragEventArgs e)
    {
        ClearAlbumInsertion(); e.Effects = DragDropEffects.None;
        if (TryAlbumPayload(e.Data, out var payload) && HasAncestor(e.OriginalSource as DependencyObject, PlaylistList))
        {
            var heading = payload!.Heading;
            var source = heading.Section!;
            var rows = Model.Entries.Skip(source.StartIndex).Take(source.Count).ToArray();
            if (Model.MoveAlbum(heading, AlbumInsertion(e).Index))
            {
                PlaylistList.SelectedItems.Clear(); foreach (var row in rows) PlaylistList.SelectedItems.Add(row);
                PlaylistList.ScrollIntoView(heading); e.Effects = DragDropEffects.Move;
            }
        }
        e.Handled = true;
    }
    private void ClearAlbumInsertion()
    {
        if (_albumInsertion is null) return;
        AdornerLayer.GetAdornerLayer(PlaylistList)?.Remove(_albumInsertion); _albumInsertion = null;
    }
    private void OnAlbumDragLeave(object sender, DragEventArgs e) => ClearAlbumInsertion();
    private void OnAlbumContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button { DataContext: PlaylistRowViewModel heading } button || !Model.Entries.Contains(heading) || !heading.HasSection)
        { e.Handled = true; return; }
        var menu = new ContextMenu();
        var runs = CurrentAlbumRuns(); var position = Array.FindIndex(runs, run => Model.Entries[run.StartIndex] == heading);
        foreach (var delta in new[] { -1, 1 })
        {
            var item = new MenuItem { Header = Strings.Get(delta < 0 ? "MoveAlbumUp" : "MoveAlbumDown"), Tag = delta,
                IsEnabled = Model.CanReorder && position >= 0 && position + delta >= 0 && position + delta < runs.Length,
                Icon = new AppIcon { Kind = delta < 0 ? AppIconKind.Up : AppIconKind.Down } };
            var order = Model.Entries.Select(row => row.Id).ToArray(); var playlist = Model.SelectedPlaylist.Id;
            item.Click += (_, _) =>
            {
                if (playlist != Model.SelectedPlaylist.Id || !order.SequenceEqual(Model.Entries.Select(row => row.Id))) return;
                if (!item.IsEnabled || position < 0 || position + delta < 0 || position + delta >= runs.Length) return;
                var adjacent = runs[position + delta];
                if (Model.MoveAlbum(heading, adjacent.StartIndex + (delta > 0 ? adjacent.Count : 0))) PlaylistList.ScrollIntoView(heading);
            };
            menu.Items.Add(item);
        }
        button.ContextMenu = menu; menu.PlacementTarget = button;
        e.Handled = true; menu.IsOpen = true;
    }
}
