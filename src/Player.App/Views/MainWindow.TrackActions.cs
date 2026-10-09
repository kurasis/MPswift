using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Player.App.Controls;
using Player.App.Resources;
using Player.App.Services.Library;
using Player.App.ViewModels;

namespace Player.App.Views;

public partial class MainWindow
{
    private readonly CancellationTokenSource _trackActionsCancellation = new();
    internal Task TrackActionCompletion { get; private set; } = Task.CompletedTask;
    internal FileInformationWindow? InformationWindow { get; private set; }
    private PlaylistRowViewModel? _trackContextRow;

    private void OnTrackRightButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is not DependencyObject element || ItemsControl.ContainerFromElement(PlaylistList, element) is not ListBoxItem { DataContext: PlaylistRowViewModel row }) return;
        SelectTrackContext(row);
    }
    internal void SelectTrackContext(PlaylistRowViewModel row)
    {
        if (!Model.Entries.Contains(row)) return;
        if (!PlaylistList.SelectedItems.Contains(row))
        { PlaylistList.SelectedItems.Clear(); PlaylistList.SelectedItems.Add(row); }
        _trackContextRow = row;
    }
    private void OnTrackContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        var focus = _trackContextRow;
        if (focus is null || !Model.Entries.Contains(focus) || !PlaylistList.SelectedItems.Contains(focus)) focus = Model.SelectedEntry;
        if (focus is null) { args.Handled = true; return; }
        PlaylistList.ContextMenu = CreateTrackContextMenu(focus);
        _trackContextRow = null;
    }
    internal ContextMenu CreateTrackContextMenu(PlaylistRowViewModel focus)
    {
        var rows = Model.Entries.Where(row => PlaylistList.SelectedItems.Contains(row)).ToArray();
        if (!rows.Contains(focus)) rows = [focus];
        var sourceId = Model.SelectedPlaylist.Id;
        bool Current() => Model.SelectedPlaylist.Id == sourceId && rows.All(row => Model.Entries.Contains(row));
        var menu = new ContextMenu { Tag = "TrackMenu", MaxHeight = Math.Max(260, Math.Min(660, SystemParameters.WorkArea.Height - 40)) };
        MenuItem Item(string key, string action, Func<Task>? callback = null, AppIconKind? icon = null, string? shortcut = null)
        {
            var item = new MenuItem { Tag = action, InputGestureText = shortcut ?? "" };
            LocalizedStrings.Bind(item, HeaderedItemsControl.HeaderProperty, key);
            if (icon is { } kind) item.Icon = new AppIcon { Kind = kind };
            if (callback is not null) item.Click += (_, _) => BeginTrackAction(async () =>
            { if (!Current()) throw new InvalidOperationException(Strings.Get("TrackSelectionChanged")); await callback(); });
            return item;
        }
        static Task Done(Action action) { action(); return Task.CompletedTask; }
        void Add(MenuItem item) => menu.Items.Add(item);
        void Separator() => menu.Items.Add(new Separator());
        Add(Item("Play", "Play", () => Model.PlayEntryCommand.ExecuteAsync(focus), AppIconKind.Play, "Enter"));
        Separator();
        var add = Item("AddMenu", "Add", icon: AppIconKind.Add);
        add.Items.Add(Item("AddFiles", "AddFiles", () => Model.AddFilesCommand.ExecuteAsync(null), AppIconKind.FileAdd));
        add.Items.Add(Item("AddFolder", "AddFolder", () => Model.AddFolderCommand.ExecuteAsync(null), AppIconKind.FolderAdd));
        add.Items.Add(Item("NewPlaylist", "NewPlaylist", () => Done(() => OnCreatePlaylist(this, new RoutedEventArgs())), AppIconKind.Add)); Add(add);
        Add(Item("AddToFavorites", "Favorites", () => Done(() => Model.AddToFavorites(rows)), AppIconKind.Favorite));
        Separator();
        var queue = Item("QueueMenu", "Queue", icon: AppIconKind.Queue);
        queue.Items.Add(Item("PlayNext", "PlayNext", () => Done(() => Model.Enqueue(rows, true)), AppIconKind.Next));
        queue.Items.Add(Item("AddQueue", "AddQueue", () => Done(() => Model.Enqueue(rows, false)), AppIconKind.Queue));
        queue.Items.Add(Item("QueueRemove", "RemoveQueue", () => Done(() =>
        { var ids = rows.Select(row => row.Id).ToHashSet(); foreach (var item in Model.Queue.Where(item => ids.Contains(item.Entry.Id)).ToArray()) Model.RemoveQueued(item.Id); }), AppIconKind.Close));
        queue.Items.Add(Item("ShowQueue", "ShowQueue", () => Done(() => OnQueue(this, new RoutedEventArgs())), AppIconKind.Queue));
        queue.Items.Add(Item("ClearQueue", "ClearQueue", () => Done(() => Model.ClearQueueCommand.Execute(null)), AppIconKind.Delete)); Add(queue);
        Separator();
        Add(Item("FileInformation", "Information", () => Done(() => OpenFileInformation(focus)), AppIconKind.Help, "F4"));
        Add(Item("ShowFile", "Location", () => Done(() => System.Diagnostics.Process.Start(CreateShowFileStartInfo(focus.Path))), AppIconKind.Folder, "Alt+O"));
        var previous = Item("PlayPreviousPlaylist", "PreviousPlaylist", Model.PlayPreviousPlaylistAsync, AppIconKind.Previous);
        previous.IsEnabled = Model.CanPlayPreviousPlaylist; Add(previous);
        Add(Item("CopySelectedFiles", "CopyFiles", () => Done(() => CopyFilesToClipboard(rows)), AppIconKind.Copy));
        Separator();
        var rating = Item("Rating", "Rating", icon: AppIconKind.Favorite);
        foreach (var value in Enumerable.Range(0, 6))
        {
            var stars = value;
            var item = Item("Unrated", "Rating" + stars, () => Done(() => { foreach (var row in rows) row.Rating = stars; }));
            if (stars > 0) item.Header = new string('★', stars);
            item.IsCheckable = true; item.IsChecked = rows.All(row => row.Rating == stars); rating.Items.Add(item);
        }
        Add(rating); Separator();
        var after = Item("PlaceAfterPlaying", "AfterPlaying", () => Done(() => Model.PlaceAfterPlaying(rows)), AppIconKind.Next);
        after.IsEnabled = Model.CanReorder && Model.SourcePlaylistId == sourceId && Model.Snapshot.EntryId is { } active && Model.Entries.Any(row => row.Id == active); Add(after);
        var send = Item("SendToMenu", "Send", icon: AppIconKind.Export);
        send.Items.Add(Item("CopyToFolder", "CopyFolder", () => CopyFilesToFolderAsync(rows), AppIconKind.Folder));
        send.Items.Add(Item("ExportSelectedPlaylist", "ExportSelected", () => ExportSelectedAsync(rows), AppIconKind.Export)); Add(send);
        var playlists = Item("SendToPlaylist", "SendPlaylist", icon: AppIconKind.Queue);
        playlists.Items.Add(Item("NewPlaylist", "SendNewPlaylist", () => Done(() =>
        {
            var dialog = new PlaylistNameDialog(this, Strings.Get("NewPlaylistName"));
            if (dialog.ShowDialog() != true) return;
            var before = Model.SelectedPlaylist; Model.CreatePlaylist(dialog.PlaylistName);
            if (!ReferenceEquals(before, Model.SelectedPlaylist)) Model.CopyEntriesToPlaylist(rows, Model.SelectedPlaylist.Id);
        }), AppIconKind.Add));
        foreach (var tab in Model.Playlists.Where(tab => tab.Id != sourceId))
        {
            var target = tab.Id; var item = Item("SendToPlaylist", "SendPlaylist:" + target, () => Done(() => Model.CopyEntriesToPlaylist(rows, target)), AppIconKind.Queue);
            item.Header = tab.Name; playlists.Items.Add(item);
        }
        Add(playlists); Separator();
        var deletion = Item("DeletionMenu", "Deletion", icon: AppIconKind.Delete);
        deletion.Items.Add(Item("Remove", "RemoveEntries", () => Done(() => Model.RemoveEntriesCommand.Execute(rows)), AppIconKind.Close, "Del"));
        deletion.Items.Add(Item("RemoveDuplicates", "RemoveDuplicates", () => Done(() => OnRemoveDuplicates(this, new RoutedEventArgs())), AppIconKind.Delete));
        deletion.Items.Add(Item("ClearPlaylist", "ClearPlaylist", () => Done(() =>
        { if (MessageBox.Show(this, Strings.Get("ClearPlaylistConfirm"), Strings.Get("ClearPlaylist"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) Model.RemoveEntriesCommand.Execute(Model.Entries.ToArray()); }), AppIconKind.Delete)); Add(deletion);
        Add(Item("RecycleFiles", "Recycle", () => RecycleSelectedAsync(rows), AppIconKind.Delete, "Ctrl+Del"));
        Separator();
        var enabled = !rows.Any(row => row.Enabled);
        Add(Item(enabled ? "EnableSelected" : "DisableSelected", "Enabled", () => Done(() => Model.SetEntriesEnabled(rows, enabled)), AppIconKind.Stop));
        return menu;
    }
    private void BeginTrackAction(Func<Task> action)
    {
        if (!TrackActionCompletion.IsCompleted) { Model.Message = Strings.Get("FileOperationInProgress"); return; }
        TrackActionCompletion = ExecuteTrackActionAsync(action);
    }
    private async Task ExecuteTrackActionAsync(Func<Task> action)
    {
        try { if (!_shutdownStarted) await action(); }
        catch (OperationCanceledException) { if (!_shutdownStarted) Model.Message = Strings.Get("OperationCanceled"); }
        catch (Exception error) { if (!_shutdownStarted) { Model.Message = Strings.ErrorUnexpected; Model.Details = error.Message; } }
    }
    internal void OpenFileInformation(PlaylistRowViewModel row)
    {
        var rows = Model.Entries.Where(entry => PlaylistList.SelectedItems.Contains(entry)).ToArray();
        if (rows.Length <= 1) rows = Model.VisibleEntries.Cast<PlaylistRowViewModel>().ToArray();
        if (!rows.Contains(row)) rows = [row];
        InformationWindow = new FileInformationWindow(this, rows, row.Id); InformationWindow.Show();
    }
    private static void CopyFilesToClipboard(IEnumerable<PlaylistRowViewModel> rows)
    {
        var paths = new StringCollection();
        foreach (var path in rows.Select(row => row.Path).Distinct(StringComparer.OrdinalIgnoreCase)) paths.Add(Services.Audio.BassSmokeSession.ValidateSourcePath(path));
        Clipboard.SetFileDropList(paths);
    }
    private async Task CopyFilesToFolderAsync(PlaylistRowViewModel[] rows)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.Get("CopyToFolder") };
        if (dialog.ShowDialog(this) != true) return;
        var result = await TrackFileOperations.CopyAsync(rows.Select(row => row.Path), dialog.FolderName, _trackActionsCancellation.Token);
        Model.Message = string.Format(Strings.Culture, Strings.Get("FilesCopied"), result.Completed.Length, result.Errors.Length); Model.Details = string.Join(Environment.NewLine, result.Errors);
    }
    private async Task ExportSelectedAsync(PlaylistRowViewModel[] rows)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = Strings.Get("PlaylistFilter"), FileName = "selected.m3u8" };
        if (dialog.ShowDialog(this) != true) return;
        if (!Path.GetExtension(dialog.FileName).Equals(".m3u8", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(Strings.Get("PlaylistFilter"));
        var text = Player.Core.Media.PlaylistDocument.ExportM3u8(rows.Select(row => row.Entry), dialog.FileName);
        using var parent = Services.Storage.DataDirectoryLease.Open(Path.GetDirectoryName(dialog.FileName)!);
        // CreateNew preserves an existing document, even when another process races the dialog.
        var path = Path.Combine(parent.DirectoryPath, Path.GetFileName(dialog.FileName));
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var identity = Services.Storage.DataFileLease.Identify(output.SafeFileHandle); var completed = false;
        try
        {
            Services.Storage.DataFileLease.Check(output.SafeFileHandle);
            await output.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text), _trackActionsCancellation.Token); output.Flush(true); completed = true;
        }
        finally
        {
            if (!completed)
            {
                await output.DisposeAsync();
                if (!Services.Storage.DataFileLease.DeleteIfSame(path, identity)) throw new IOException("Incomplete playlist changed identity; unknown replacement retained.");
            }
        }
        Model.Message = Strings.Get("Exported");
    }
    private async Task RecycleSelectedAsync(PlaylistRowViewModel[] rows)
    {
        if (rows.Length == 0) return;
        var preview = new RecycleFilesWindow(this, rows);
        if (preview.ShowDialog() != true) return;
        var active = Model.ActiveSourcePath;
        var result = await Task.Run(() => TrackFileOperations.Recycle(preview.Paths, active));
        Model.MarkFilesUnavailable(result.Completed);
        Model.Message = string.Format(Strings.Culture, Strings.Get("FilesRecycled"), result.Completed.Length, result.Errors.Length); Model.Details = string.Join(Environment.NewLine, result.Errors);
    }
}
