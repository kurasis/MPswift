using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.Services.Storage;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Library;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Owned production settings/cache/duplicates/diagnostics/reopen workflows, never hardware substitutes.</summary>
internal static class CompletionValidation
{
    public static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); }
        var settings = model.WindowSettings; var selected = model.SelectedPlaylist; var originalSnapshot = model.Snapshot;
        var source = model.SourcePlaylistId; var queue = model.Queue.Select(q => q.Id).ToArray();
        var data = ((App)Application.Current).DataDirectory;
        var database = Path.Combine(data, "library.db");
        var beforeData = File.ReadAllBytes(database);
        var preferences = new PreferencesWindow(window, model);
        preferences.Show(); await preferences.CacheRefreshCompletion.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            preferences.CacheBudgetBox.Text = "15";
            preferences.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await preferences.ApplyCompletion;
            Check(preferences.IsVisible && model.WindowSettings.WaveformCacheMiB == settings.WaveformCacheMiB, "Invalid cache budget was silently clamped/applied.");
            preferences.ClearCacheButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await preferences.CacheClearCompletion.WaitAsync(TimeSpan.FromSeconds(15));
            Check((await model.GetCacheUsageAsync()) is { Files: 0, Bytes: 0 }, "Cache clear left completed peak files.");
            Check(File.ReadAllBytes(database).AsSpan().SequenceEqual(beforeData), "Cache clear changed the playlist database.");
            Check(model.Snapshot.EntryId == originalSnapshot.EntryId && model.Snapshot.State == originalSnapshot.State, "Cache management changed playback.");
        }
        finally { preferences.Close(); }
        await model.RefreshWaveformCommand.ExecuteAsync(null); await model.WaveformCompletion.WaitAsync(TimeSpan.FromSeconds(20));
        Check(model.Waveform is not null, "Cleared cache could not regenerate the current native waveform.");

        model.CreatePlaylist("Owned duplicates"); var tab = model.SelectedPlaylist;
        await model.AddPathsAsync([fixture, fixture, fixture]);
        var entries = tab.Entries.Select(row => row.Entry).ToArray();
        var ids = PlaylistDuplicates.FindRemovable(entries);
        Check(ids.Count == 2 && ids.SequenceEqual(entries.Skip(1).Select(e => e.Id)), "Duplicate preview did not retain the first occurrence.");
        model.Enqueue([tab.Entries[1]], false); var queuedDuplicate = model.Queue.Last().Id;
        var preview = new DuplicateEntriesWindow(window, entries.Skip(1).ToArray());
        preview.Show(); await Idle(); CustomizationValidation.Render((FrameworkElement)preview.Content, output, "duplicates-preview-" + settings.Language + ".png"); preview.Close();
        Check(tab.Entries.Count == 3, "Closing duplicate preview removed entries.");
        Check(!model.RemovePlaylistDuplicates(tab.Id, entries.Reverse().Select(e => e.Id).ToArray(), ids), "Stale preview modified playlist order.");
        Check(model.RemovePlaylistDuplicates(tab.Id, entries.Select(e => e.Id).ToArray(), ids) && tab.Entries.Single().Id == entries[0].Id, "Confirmed duplicate removal lost the first entry.");
        Check(model.Queue.Any(q => q.Id == queuedDuplicate) && model.SourcePlaylistId == source && model.Snapshot.EntryId == originalSnapshot.EntryId, "Duplicate removal changed source/current/queued snapshots.");
        await model.SaveNowAsync();
        model.DeletePlaylist(); model.RemoveQueued(queuedDuplicate); model.SelectedPlaylist = selected;
        Check(model.Queue.Select(q => q.Id).SequenceEqual(queue), "Duplicate cleanup lost existing queued snapshots.");

        model.Details = Path.Combine(data, "owned diagnostic.flac");
        var report = model.CreateDiagnosticReport(await new Audio.NativeDiagnostics().VerifyAsync());
        Check(!report.Contains("owned diagnostic.flac") || report.Contains("<local>"), "Diagnostic paths were not redacted.");
        var escapedData = System.Text.Json.JsonSerializer.Serialize(data)[1..^1];
        Check(!report.Contains(escapedData, StringComparison.OrdinalIgnoreCase), "Diagnostic report retained the private data directory.");
        var diagnostics = new DiagnosticsWindow(window, report);
        try
        {
            diagnostics.Show(); await Idle();
            Check(diagnostics.Preview.IsReadOnly && diagnostics.Preview.Text == report, "Diagnostic preview changed or exposed editable source data.");
            Clipboard.SetText("MPswift owned clipboard fixture");
            diagnostics.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Clipboard.GetText() == report, "Copy diagnostics did not copy the redacted preview.");
            Clipboard.SetText("MPswift owned clipboard fixture");
            CustomizationValidation.Render((FrameworkElement)diagnostics.Content, output, "diagnostics-preview-" + settings.Language + ".png");
        }
        finally { diagnostics.Close(); model.Details = ""; }
        foreach (var notification in new[] { 4, 18 })
        {
            SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x0218, notification, 0);
            await window.PowerPauseCompletion.WaitAsync(TimeSpan.FromSeconds(15));
        }
        await Idle();
        Check(model.Snapshot.State != PlaybackState.Playing && model.Snapshot.EntryId == originalSnapshot.EntryId &&
            Math.Abs((model.Snapshot.Position - originalSnapshot.Position).TotalSeconds) < .01, "Power policy did not pause without losing source/position.");

        var owned = Path.Combine(output, "completion-reopen-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(owned);
        try
        {
            foreach (var policy in new[] { (Session: true, Position: true), (Session: true, Position: false), (Session: false, Position: true) })
            {
                var directory = Path.Combine(owned, policy.Session + "-" + policy.Position); Directory.CreateDirectory(directory);
                var entry = new PlaylistEntry(Guid.NewGuid(), new(Guid.NewGuid(), fixture, "Owned restored track")); var playlist = Guid.NewGuid();
                var order = new PlaybackOrder { Repeat = RepeatMode.All, Shuffle = true }; order.SetSource([entry]); order.Started(entry, false); order.Enqueue([entry], false);
                var saved = new LibraryState([new(playlist, "Owned restore", [entry])], new(playlist, playlist, entry, TimeSpan.FromSeconds(.75).Ticks, order.Capture()));
                await using (var store = new SqlitePlayerStore(Path.Combine(directory, "library.db"))) { await store.LoadAsync(); await store.SaveAsync(saved, true); await store.SetRatingAsync(entry.Track.Id, 4); }
                new SettingsFile(directory).Save(new PlayerSettings { RestoreSession = policy.Session, RestorePosition = policy.Position, Accent = "green", DefaultRepeat = RepeatMode.One });
                var reopened = ((App)Application.Current).CreateModel(directory);
                try
                {
                    await reopened.InitializeAsync(); await Idle();
                    Check(reopened.Entries.Single().Id == entry.Id && reopened.Entries.Single().Rating == 4, "Restore preference lost playlists or ratings.");
                    Check(reopened.Snapshot.State != PlaybackState.Playing, "Restore preference started audio.");
                    Check(policy.Session ? reopened.Snapshot.EntryId == entry.Id && reopened.Queue.Count == 1 && reopened.Repeat == RepeatMode.All :
                        reopened.Snapshot.EntryId is null && reopened.Queue.Count == 0 && reopened.Repeat == RepeatMode.One, "Restore preference did not control active item/queue/order defaults.");
                    var expected = policy.Session && policy.Position ? .75 : 0;
                    Check(Math.Abs(reopened.Snapshot.Position.TotalSeconds - expected) < .01, "Restore position preference was ignored.");
                }
                finally { await reopened.DisposeAsync(); }
            }
        }
        finally { AppearanceService.Apply(settings.Accent); Directory.Delete(owned, true); }
        model.SelectedPlaylist = selected;
        Check(model.Snapshot.EntryId == originalSnapshot.EntryId && model.SourcePlaylistId == source, "Owned reopened models changed live playback source.");
        return new { Status = "completion-passed", CacheClearPreservesDatabase = true, CacheRegeneration = true, InvalidBudgetRejected = true,
            DuplicatePreviewAndCancel = true, StalePreviewRefused = true, FirstOccurrenceRetained = true, QueueAndPlaybackPreserved = true,
            RedactedPreviewAndActualClipboard = true, RestoreOnWithoutPositionAndOff = true, RatingsPreserved = true,
            PowerPolicy = "owned paused-source software workflow passed; physical sleep/resume remains not-run" };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint parameter, nint data);
}
