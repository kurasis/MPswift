using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Services.Storage;
using System.IO;

namespace Player.App.Views;

public partial class MainWindow : Window
{
    private const string EntryDragFormat = "LocalAudioPlayer.PlaylistEntries";
    private Point? _dragOrigin;
    private PlaylistRowViewModel[] _dragRows = [];
    private bool _shutdownComplete;
    private bool _shutdownStarted;
    private bool _exitRequested;
    public Task RestoreCompletion { get; private set; } = Task.CompletedTask;
    private PlayerViewModel Model => (PlayerViewModel)DataContext;
    public MainWindow()
    {
        InitializeComponent();
        WaveformView.PreviewSeek += seconds => { Model.SeekPreview = true; Model.SeekPosition = seconds; };
        WaveformView.CommitSeek += async seconds => { Model.SeekPreview = false; await Model.CommitSeekAsync(seconds); };
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width);
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        ApplyContrastTheme();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is PlayerViewModel previous) previous.PropertyChanged -= OnAccessibleStateChanged;
            if (args.NewValue is PlayerViewModel current) current.PropertyChanged += OnAccessibleStateChanged;
        };
        SystemParameters.StaticPropertyChanged += OnSystemSettings;
        Closed += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemSettings;
            if (DataContext is PlayerViewModel current) current.PropertyChanged -= OnAccessibleStateChanged;
        };
    }
    private void OnAccessibleStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(PlayerViewModel.State) or nameof(PlayerViewModel.Message)) ||
            !System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_shutdownComplete || !ReferenceEquals(sender, DataContext)) return;
            var text = args.PropertyName == nameof(PlayerViewModel.State) ? PlaybackStateText : StatusMessageText;
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(
                System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
    private async void OnTrackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element && ItemsControl.ContainerFromElement(PlaylistList, element) is ListBoxItem { DataContext: PlaylistRowViewModel row }
            && !OwnsInput(element)) await Model.PlayEntryCommand.ExecuteAsync(row);
    }
    private void OnRemove(object sender, RoutedEventArgs e) => Model.RemoveEntriesCommand.Execute(PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>().ToArray());
    private void OnHelp(object sender, RoutedEventArgs e) => new HelpWindow(this, ((App)Application.Current).DataDirectory).Show();
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && Model.CanAcceptFileDrop ? DragDropEffects.Copy :
            e.Data.GetDataPresent(EntryDragFormat) && Model.CanReorder && PlaylistList.IsMouseOver ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetDataPresent(EntryDragFormat) && e.Data.GetData(EntryDragFormat) is PlaylistRowViewModel[] rows && PlaylistList.IsMouseOver)
        {
            var container = ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) as ListBoxItem;
            var insertion = container?.DataContext is PlaylistRowViewModel row ? Model.Entries.IndexOf(row) + (e.GetPosition(container).Y > container.ActualHeight / 2 ? 1 : 0) : Model.Entries.Count;
            var ownRows = rows.Where(Model.Entries.Contains).ToArray();
            Model.DropEntries(ownRows, insertion);
            PlaylistList.SelectedItems.Clear(); foreach (var ownRow in ownRows) PlaylistList.SelectedItems.Add(ownRow);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await ImportFileDropAsync(paths, e.OriginalSource as DependencyObject);
    }
    internal Task ImportFileDropAsync(string[] paths, DependencyObject? source)
    {
        // Use the routed target, including empty strip space and tab children, rather
        // than mouse position: asynchronous imports may continue after the pointer moves.
        while (source is not null)
        {
            if (ReferenceEquals(source, PlaylistTabStrip)) return Model.AddDroppedPathsAsync(paths, true);
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : (source as FrameworkContentElement)?.Parent ?? LogicalTreeHelper.GetParent(source);
        }
        return Model.AddDroppedPathsAsync(paths, false);
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
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { OnCreatePlaylist(sender, e); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2 && PlaylistTabs.IsKeyboardFocusWithin) { OnRenamePlaylist(sender, e); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (!SearchBox.IsKeyboardFocusWithin && OwnsShortcut(e.OriginalSource as DependencyObject, e.Key, Keyboard.Modifiers)) return;
            Model.ClearSearchCommand.Execute(null); e.Handled = true; return;
        }
        if (e.Key == Key.F1) { OnHelp(sender, e); e.Handled = true; return; }
        if (OwnsShortcut(e.OriginalSource as DependencyObject, e.Key, Keyboard.Modifiers)) return;
        if (PlaylistList.IsKeyboardFocusWithin && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.Up or Key.Down)
        { Model.MoveEntries(PlaylistList.SelectedItems.Cast<PlaylistRowViewModel>(), e.Key == Key.Up ? -1 : 1); e.Handled = true; return; }
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
            if (element is TextBoxBase or PasswordBox or ButtonBase or Slider or ComboBox or MenuItem) return true;
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : (element as FrameworkContentElement)?.Parent ?? LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private static bool OwnsShortcut(DependencyObject? element, Key key, ModifierKeys modifiers)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or PasswordBox or ComboBox or MenuItem or Slider) return true;
            // Buttons/checkboxes own Space and Enter, but do not consume playlist Ctrl+A,
            // reorder, seek or volume shortcuts just because a row checkbox has focus.
            if (element is ButtonBase && modifiers == ModifierKeys.None && key is Key.Space or Key.Enter) return true;
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
        if (!RestoreCompletion.IsCompleted) return;
        if (Model.WindowSettings.CloseToTray && !_exitRequested) { Hide(); return; }
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        IsEnabled = false;
        Model.WindowSettings = Model.WindowSettings with { WindowWidth = RestoreBounds.Width, WindowHeight = RestoreBounds.Height,
            WindowLeft = RestoreBounds.Left, WindowTop = RestoreBounds.Top, WindowMaximized = WindowState == WindowState.Maximized };
        try { await Model.DisposeAsync(); await ((App)Application.Current).FlushDiagnosticsAsync(); _shutdownComplete = true; Close(); }
        catch (Exception error)
        {
            Model.Message = Strings.Get("ErrorUnexpected"); Model.Details = error.Message;
            _shutdownStarted = false; IsEnabled = true;
            if (MessageBox.Show(this, Strings.Get("CloseWithoutSaving"), Strings.Get("SaveFailed"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try { await Model.DisposeWithoutSavingAsync(); _shutdownComplete = true; Close(); }
                catch (Exception cleanup) { Model.Details = cleanup.Message; }
            }
        }
    }
    public void ShowAndActivate() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    public void ExitApplication() { _exitRequested = true; Close(); }
    private void OnPreferences(object sender, RoutedEventArgs e) => new PreferencesWindow(this, Model).ShowDialog();
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OnSystemMenu(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Right) SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this))); }
    private void OnSystemSettings(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.BeginInvoke(ApplyContrastTheme); }
    private void ApplyContrastTheme()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        foreach (var dictionary in dictionaries.Where(d => d.Source?.OriginalString.EndsWith("HighContrast.xaml", StringComparison.Ordinal) == true).ToArray()) dictionaries.Remove(dictionary);
        if (SystemParameters.HighContrast) dictionaries.Add(new ResourceDictionary { Source = new Uri("/MPswift;component/Themes/HighContrast.xaml", UriKind.Relative) });
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
    private void OnLibrary(object sender, RoutedEventArgs e) => new LibraryWindow(this, Model).Show();
    private void OnSort(object sender, RoutedEventArgs e) { if (sender is MenuItem { Tag: string field }) Model.SortPlaylist(field); }
    private async void OnExportPlaylist(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = Strings.Get("PlaylistFilter"), FileName = "playlist.m3u8" }; if (dialog.ShowDialog(this) != true) return;
        try { await Model.ExportPlaylistAsync(dialog.FileName); Model.Message = Strings.Get("Exported"); }
        catch (Exception error) { Model.Message = Strings.Get("ExportFailed"); Model.Details = error.Message; }
    }
    private async void OnRelink(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedEntry is not { } row) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Strings.Get("ReplacementSource") }; if (dialog.ShowDialog(this) != true) return;
        try { await Model.RelinkAsync(row, dialog.FileName); }
        catch (Exception error) { Model.Message = Strings.Get("RelinkFailed"); Model.Details = error.Message; }
    }
    private void OnCopyPath(object sender, RoutedEventArgs e) { if (Model.SelectedEntry is { } row) Clipboard.SetText(row.Path); }
    private void OnShowFile(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedEntry is not { } row) return;
        try { var path = Services.Audio.BassSmokeSession.ValidateSourcePath(row.Path); var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false }; start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); System.Diagnostics.Process.Start(start); }
        catch (Exception error) { Model.Details = error.Message; }
    }
    private async void OnProperties(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedEntry is not { } row) return; var track = row.Entry.Track;
        Player.Core.Library.TrackStatistics? statistics = null;
        try { if (Model.LibraryIndex is { } index) statistics = (await index.GetStatisticsAsync([track.Id])).FirstOrDefault(); } catch (Exception error) { Model.Details = error.Message; }
        var played = statistics?.LastPlayedUtcTicks is { } last ? new DateTime(last, DateTimeKind.Utc).ToLocalTime().ToString("g", Strings.Culture) : "—";
        MessageBox.Show(this, $"{track.Title}\n{track.Artist}\n{track.Album}\n{track.Path}\n\n{Strings.Get("MetadataHints")}:\n{track.SampleRateHint} {Strings.Get("HzUnit")} · {track.ChannelsHint} {Strings.Get("ChannelsUnit")} · {track.BitrateHint} {Strings.Get("KbpsUnit")}\n{Strings.Get("DiscLabel")} {track.DiscNumber}, {Strings.Get("TrackLabel")} {track.TrackNumber}, {track.Year}\n{track.Genre}\nCUE: {track.CueDocument ?? "—"}\n{Strings.Rating}: {row.Rating}\n{Strings.Get("CountedPlays")}: {statistics?.PlayCount ?? 0}\n{Strings.Get("LastPlayed")}: {played}", Strings.Get("TrackProperties"));
    }
    private async void OnLegacyImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("DocumentFilter") };
        if (dialog.ShowDialog(this) != true) return;
        var selection = new Window { Owner = this, Title = Strings.Get("LegacyEncoding"), Width = 330, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(12) }; selection.Content = panel;
        panel.Children.Add(new TextBlock { Text = Strings.Get("SelectEncoding"), TextWrapping = TextWrapping.Wrap });
        var encodings = new ComboBox { ItemsSource = new[] { Strings.Get("Encoding1251"), Strings.Get("Encoding1252"), Strings.Get("Encoding866") }, SelectedIndex = 0, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(encodings);
        var button = new Button { Content = Strings.Get("Import") }; button.Click += (_, _) => selection.DialogResult = true; panel.Children.Add(button);
        if (selection.ShowDialog() != true) return;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        await Model.AddPathsAsync([dialog.FileName], System.Text.Encoding.GetEncoding(new[] { 1251, 1252, 866 }[encodings.SelectedIndex], System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback));
    }
    private void OnPlayNext(object sender, RoutedEventArgs e) => Model.Enqueue(Model.Entries.Where(r => PlaylistList.SelectedItems.Contains(r)), true);
    private void OnAddQueue(object sender, RoutedEventArgs e) => Model.Enqueue(Model.Entries.Where(r => PlaylistList.SelectedItems.Contains(r)), false);
    private void OnQueue(object sender, RoutedEventArgs e) => new QueueWindow(this, Model).Show();
    private async void OnAudioSettings(object sender, RoutedEventArgs e)
    {
        try { new AudioSettingsWindow(this, Model, await Model.GetDevicesAsync()).ShowDialog(); }
        catch (Exception error) { Model.Message = Strings.Get("DevicesUnavailable"); Model.Details = error.Message; }
    }
    private async void OnBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = Strings.Get("BackupFilter"), FileName = "player-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        if (dialog.ShowDialog(this) != true) return;
        try { Services.Audio.LocalFileAccess.ValidateDirectory(System.IO.Path.GetDirectoryName(dialog.FileName)!); await Model.BackupAsync(dialog.FileName); Model.Message = Strings.Get("BackupSaved"); }
        catch (Exception error) { Model.Message = Strings.Get("SaveFailed"); Model.Details = error.Message; }
    }
    private async void OnRestoreBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("BackupFilter") };
        if (dialog.ShowDialog(this) != true || MessageBox.Show(this, Strings.Get("RestoreBackupPrompt"), Strings.RestoreBackup, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await RestoreBackupAsync(Services.Audio.LocalFileAccess.ValidateFile(dialog.FileName)); Model.Message = Strings.Get("BackupRestored"); }
        catch (Exception error) { Model.Message = Strings.Get("SaveFailed"); Model.Details = error.Message; }
    }
    public async Task RestoreBackupAsync(string archive)
    {
        if (!RestoreCompletion.IsCompleted) throw new InvalidOperationException("Restore already in progress.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); RestoreCompletion = completion.Task;
        var app = (App)Application.Current; var directory = app.DataDirectory;
        var closedModel = false;
        IsEnabled = false;
        try
        {
            await Model.SaveNowAsync();
            foreach (Window owned in OwnedWindows.Cast<Window>().ToArray()) owned.Close();
            app.SuspendMedia();
            await Model.DisposeWithoutSavingAsync();
            closedModel = true;
            try
            {
                if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase)) await BackupBundle.RestoreAsync(directory, archive);
                else await Task.Run(() => DatabaseRecovery.Restore(Path.Combine(directory, "library.db"), archive));
            }
            finally
            {
                // Failed validation/installation reopens the preserved current data. Never manufacture an empty replacement.
                if (!File.Exists(Path.Combine(directory, "library.db")) || !File.Exists(Path.Combine(directory, "settings.json")))
                    throw new IOException("Saved files are unavailable after restore; retained originals require recovery.");
                var replacement = app.CreateModel(directory);
                try { await replacement.InitializeAsync(); }
                catch { await replacement.DisposeWithoutSavingAsync(); throw; }
                DataContext = replacement; closedModel = false; app.RebindMedia(this, replacement);
            }
        }
        catch (Exception error) when (closedModel)
        {
            MessageBox.Show(this, Strings.Get("StartupFailure") + "\n\n" + error.Message, Strings.Get("SavedDataUnavailable"), MessageBoxButton.OK, MessageBoxImage.Error);
            _shutdownComplete = true; app.Shutdown(1); throw;
        }
        finally { IsEnabled = true; completion.SetResult(); }
    }
    public async Task CloseForValidationAsync()
    {
        await Model.DisposeAsync();
        await ((App)Application.Current).FlushDiagnosticsAsync();
        _shutdownComplete = true;
        Close();
    }
}
