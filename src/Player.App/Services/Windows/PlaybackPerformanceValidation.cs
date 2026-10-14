using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Player.App.Controls;
using Player.App.Resources;
using Player.App.Services.Storage;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Controlled hot-path measurements; ordinary native playback/device CPU is a separate acceptance.</summary>
internal static class PlaybackPerformanceValidation
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static async Task<object> RunAsync(MainWindow window, PlayerViewModel model)
    {
        var entries = Enumerable.Range(0, 10000).Select(i => new PlaylistEntry(Guid.NewGuid(),
            new(Guid.NewGuid(), @"C:\OwnedMetadata\" + i + ".flac", "Owned " + i))).ToArray();
        var order = new PlaybackOrder(new Random(7)); order.SetSource(entries); order.Started(entries[0], false);
        for (var i = 0; i < 20; i++) { order.Candidates(entries[0], true); order.FirstCandidate(entries[0], true); }
        object Measure(Action action, out long bytes)
        {
            using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            for (var i = 0; i < 200; i++) action();
            timer.Stop(); bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            return new { Calls = 200, WallMilliseconds = timer.Elapsed.TotalMilliseconds, AllocatedBytes = bytes,
                ProcessCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds };
        }
        var old = Measure(() => { Check(order.Candidates(entries[0], true)[0] == entries[1], "Reference candidate changed."); }, out var oldBytes);
        var optimized = Measure(() => { Check(order.FirstCandidate(entries[0], true) == entries[1], "Optimized candidate changed."); }, out var newBytes);
        Check(newBytes < oldBytes / 20, "Next-candidate path still allocates full arrays.");

        await model.WaveformCompletion.WaitAsync(TimeSpan.FromSeconds(30));
        var basis = model.Snapshot;
        Check(basis.State != PlaybackState.Playing, "Controlled UI measurement requires the genuinely paused/stopped native source.");
        model.ApplySnapshot(basis); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var notifications = new Dictionary<string, int>();
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { var name = args.PropertyName ?? ""; notifications[name] = notifications.GetValueOrDefault(name) + 1; }
        model.PropertyChanged += Changed;
        var uiBefore = GC.GetAllocatedBytesForCurrentThread(); var uiClock = Stopwatch.StartNew();
        try
        {
            for (var i = 1; i <= 200; i++) model.ApplySnapshot(basis with { Position = TimeSpan.FromMilliseconds(i * 10) });
        }
        finally { model.PropertyChanged -= Changed; model.ApplySnapshot(basis); }
        uiClock.Stop(); var uiBytes = GC.GetAllocatedBytesForCurrentThread() - uiBefore;
        foreach (var label in new[] { nameof(model.Format), nameof(model.State), nameof(model.Title), nameof(model.Artist), nameof(model.Album),
            nameof(model.PlaybackSource), nameof(model.DurationText), nameof(model.CanTransport) })
            Check(notifications.GetValueOrDefault(label) == 0, "Position ticks changed stable metadata/format labels: " + label);
        Check(notifications.GetValueOrDefault(nameof(model.SeekPosition)) > 0 && notifications.GetValueOrDefault(nameof(model.Elapsed)) <= 3,
            "Position-only optimization lost transport updates or repeatedly formats seconds.");

        await model.SaveNowAsync(); await model.SaveCompletion;
        var store = (SqlitePlayerStore)model.LibraryIndex!; var captured = model.LibraryCaptures; var sessionWrites = store.SessionOnlyWrites;
        for (var i = 0; i < 3; i++) await model.SaveNowAsync();
        Check(model.LibraryCaptures == captured && store.SessionOnlyWrites >= sessionWrites + 3, "Unchanged-library save captured all playlist rows.");
        var persisted = await store.LoadAsync();
        Check(persisted.Session.PositionTicks == model.Snapshot.Position.Ticks && persisted.Playlists.Select(tab => tab.Id).SequenceEqual(model.Playlists.Select(tab => tab.Id)),
            "Session-only persistence lost position or library identity.");

        var mainWaveform = (WaveformControl)window.FindName("WaveformView");
        var control = new WaveformControl { Width = 720, Height = 64, Data = mainWaveform.Data, Duration = 180, CanSeek = true };
        Check(control.Data is not null, "Pixel-render measurement lacks the actual native-decoded waveform.");
        var probe = new Window { Owner = window, Content = new Grid { Children = { control } }, Width = 760, Height = 120,
            ShowInTaskbar = false, ShowActivated = false, Title = "Owned waveform rendering measurement" };
        long renders, renderRequests;
        try
        {
            probe.Show(); probe.UpdateLayout();
            await probe.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var initialRenders = control.RenderPasses; var initialRequests = control.PositionRenderRequests;
            for (var i = 1; i <= 100; i++)
            {
                control.Position = i * .1;
                await probe.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }
            renders = control.RenderPasses - initialRenders; renderRequests = control.PositionRenderRequests - initialRequests;
            var physicalPixels = (int)Math.Ceiling(control.ActualWidth * VisualTreeHelper.GetDpi(control).DpiScaleX * 10 / 180);
            Check(renderRequests > 0 && renderRequests <= physicalPixels + 1 && renderRequests < 100,
                "Waveform still invalidates for every subpixel playback tick.");
            Check(control.Position == 10, "Pixel throttling changed the precise seek/automation value.");
            control.Position = 0; await probe.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Check(control.PositionRenderRequests > initialRequests + renderRequests, "Seeking to the start failed to invalidate.");
        }
        finally { probe.Close(); }
        return new { Status = "playback-performance-passed", LargeSourceRows = entries.Length, FullCandidateReference = old, FirstCandidate = optimized,
            CandidateAllocationRatio = (double)newBytes / oldBytes, FirstCandidateEquivalent = true,
            PositionOnlyUi = new { Calls = 200, WallMilliseconds = uiClock.Elapsed.TotalMilliseconds, AllocatedBytes = uiBytes,
                StableLabelNotifications = 0, SeekNotifications = notifications.GetValueOrDefault(nameof(model.SeekPosition)), ElapsedNotifications = notifications.GetValueOrDefault(nameof(model.Elapsed)) },
            SessionOnlyWritesWithoutLibraryCapture = true, SessionReopened = true,
            Waveform = new { PositionUpdates = 100, renders, renderRequests, PrecisePosition = true, StartInvalidation = true },
            Scope = "Controlled same-process hot paths and real paused-source WPF/SQLite/native-decoded waveform; no physical playing-device CPU reduction claim" };
    }
}
