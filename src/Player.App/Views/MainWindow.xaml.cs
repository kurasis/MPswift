using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Player.App.Resources;
using Player.App.ViewModels;

namespace Player.App.Views;

public partial class MainWindow : Window
{
    private const string EntryDragFormat = "LocalAudioPlayer.PlaylistEntries";
    private Point? _dragOrigin;
    private PlaylistRowViewModel[] _dragRows = [];
    private bool _shutdownComplete;
    private bool _shutdownStarted;
    private PlayerViewModel Model => (PlayerViewModel)DataContext;
    public MainWindow()
    {
        InitializeComponent();
        WaveformView.PreviewSeek += seconds => { Model.SeekPreview = true; Model.SeekPosition = seconds; };
        WaveformView.CommitSeek += async seconds => { Model.SeekPreview = false; await Model.CommitSeekAsync(seconds); };
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
    }
    private async void OnTrackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element && ItemsControl.ContainerFromElement(PlaylistList, element) is ListBoxItem { DataContext: PlaylistRowViewModel row }
            && !OwnsInput(element)) await Model.PlayEntryCommand.ExecuteAsync(row);
    }
    private void OnRemove(object sender, RoutedEventArgs e) => Model.RemoveEntriesCommand.Execute(PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>().ToArray());
    private void OnHelp(object sender, RoutedEventArgs e) => MessageBox.Show(this, Strings.Get("HelpText"), Strings.Get("Help"), MessageBoxButton.OK, MessageBoxImage.Information);
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy :
            e.Data.GetDataPresent(EntryDragFormat) && Model.CanReorder && PlaylistList.IsMouseOver ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(EntryDragFormat) && e.Data.GetData(EntryDragFormat) is PlaylistRowViewModel[] rows && PlaylistList.IsMouseOver)
        {
            var container = ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) as ListBoxItem;
            var insertion = container?.DataContext is PlaylistRowViewModel row ? Model.Entries.IndexOf(row) + (e.GetPosition(container).Y > container.ActualHeight / 2 ? 1 : 0) : Model.Entries.Count;
            var ownRows = rows.Where(Model.Entries.Contains).ToArray();
            Model.DropEntries(ownRows, insertion);
            PlaylistList.SelectedItems.Clear(); foreach (var ownRow in ownRows) PlaylistList.SelectedItems.Add(ownRow);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await Model.AddPathsAsync(paths);
        e.Handled = true;
    }
    private void OnSeekStart(object sender, MouseButtonEventArgs e) => Model.SeekPreview = true;
    private async void OnSeekEnd(object sender, MouseButtonEventArgs e)
    {
        if (!Model.SeekPreview) return;
        Model.SeekPreview = false;
        await Model.CommitSeekAsync(SeekSlider.Value);
    }
    private async void OnSeekCaptureLost(object sender, MouseEventArgs e)
    {
        if (!Model.SeekPreview) return;
        Model.SeekPreview = false;
        await Model.CommitSeekAsync(SeekSlider.Value);
    }
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { Model.ClearSearchCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.F1) { OnHelp(sender, e); e.Handled = true; return; }
        if (PlaylistList.IsKeyboardFocusWithin && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.Up or Key.Down)
        { Model.MoveEntries(PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>(), e.Key == Key.Up ? -1 : 1); e.Handled = true; return; }
        if (OwnsInput(e.OriginalSource as DependencyObject)) return;
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Space) { await Model.PlayPauseCommand.ExecuteAsync(null); e.Handled = true; }
        else if (PlaylistList.IsKeyboardFocusWithin && e.Key == Key.Enter) { await Model.PlayEntryCommand.ExecuteAsync(Model.SelectedEntry); e.Handled = true; }
        else if (PlaylistList.IsKeyboardFocusWithin && e.Key == Key.Delete) { OnRemove(sender, e); e.Handled = true; }
        else if (PlaylistList.IsKeyboardFocusWithin && e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { PlaylistList.SelectAll(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Left or Key.Right) { await Model.CommitSeekAsync(Model.SeekPosition + (e.Key == Key.Right ? 5 : -5)); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Up or Key.Down) { Model.Volume = Math.Clamp(Model.Volume + (e.Key == Key.Up ? 5 : -5), 0, 100); e.Handled = true; }
    }
    private static bool OwnsInput(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or PasswordBox or ButtonBase or Slider) return true;
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : (element as FrameworkContentElement)?.Parent ?? LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete) return;
        e.Cancel = true;
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        IsEnabled = false;
        Model.WindowSettings = Model.WindowSettings with { WindowWidth = RestoreBounds.Width, WindowHeight = RestoreBounds.Height };
        try { await Model.DisposeAsync(); _shutdownComplete = true; Close(); }
        catch (Exception error)
        {
            Model.Message = Strings.Get("ErrorUnexpected"); Model.Details = error.Message;
            _shutdownStarted = false; IsEnabled = true;
        }
    }
    private void OnRowDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = null;
        if (e.ClickCount != 1 || OwnsInput(e.OriginalSource as DependencyObject) || !Model.CanReorder) return;
        if (ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: PlaylistRowViewModel row })
        { _dragOrigin = e.GetPosition(PlaylistList); _dragRows = PlaylistList.SelectedItems.Contains(row) ? PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>().ToArray() : [row]; }
    }
    private void OnRowDragMove(object sender, MouseEventArgs e)
    {
        if (_dragOrigin is not { } origin || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(PlaylistList);
        if (Math.Abs(point.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragOrigin = null;
        DragDrop.DoDragDrop(PlaylistList, new DataObject(EntryDragFormat, _dragRows), DragDropEffects.Move);
    }
    private void OnCreatePlaylist(object sender, RoutedEventArgs e)
    { var dialog = new PlaylistNameDialog(this, Strings.Get("NewPlaylist")); if (dialog.ShowDialog() == true) Model.CreatePlaylist(dialog.PlaylistName); }
    private void OnRenamePlaylist(object sender, RoutedEventArgs e)
    { var dialog = new PlaylistNameDialog(this, Model.SelectedPlaylist.Name); if (dialog.ShowDialog() == true) Model.RenamePlaylist(dialog.PlaylistName); }
    private void OnDuplicatePlaylist(object sender, RoutedEventArgs e) => Model.DuplicatePlaylist();
    private void OnDeletePlaylist(object sender, RoutedEventArgs e) => Model.DeletePlaylist();
    private void OnTabLeft(object sender, RoutedEventArgs e) => Model.MoveTab(-1);
    private void OnTabRight(object sender, RoutedEventArgs e) => Model.MoveTab(1);
    private void OnMoveUp(object sender, RoutedEventArgs e) => Model.MoveEntries(PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>(), -1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => Model.MoveEntries(PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>(), 1);
    private void OnPlaylistActions(object sender, RoutedEventArgs e)
    { PlaylistActions.ContextMenu.DataContext = Model; PlaylistActions.ContextMenu.PlacementTarget = PlaylistActions; PlaylistActions.ContextMenu.IsOpen = true; }
    private async void OnBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "SQLite backup (*.db)|*.db", FileName = "player-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db" };
        if (dialog.ShowDialog(this) != true) return;
        try { Services.Audio.LocalFileAccess.ValidateDirectory(System.IO.Path.GetDirectoryName(dialog.FileName)!); await Model.BackupAsync(dialog.FileName); Model.Message = Strings.Get("BackupSaved"); }
        catch (Exception error) { Model.Message = Strings.Get("SaveFailed"); Model.Details = error.Message; }
    }
    public async Task CloseForValidationAsync()
    {
        await Model.DisposeAsync();
        _shutdownComplete = true;
        Close();
    }
}
