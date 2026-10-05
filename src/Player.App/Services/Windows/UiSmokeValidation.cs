using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Player.App.Resources;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Playback;

namespace Player.App.Services.Windows;

/// <summary>Explicit --ui-smoke development route. Exercises real imports, native preparation and WPF bindings; no fake output.</summary>
public sealed class UiSmokeValidation : TraceListener
{
    private readonly List<string> _bindingErrors = [];
    public UiSmokeValidation()
    {
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        PresentationTraceSources.DataBindingSource.Listeners.Add(this);
    }
    public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) _bindingErrors.Add(message); }
    public override void WriteLine(string? message) => Write(message);

    public async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string taggedFixture, string output)
    {
        await model.AddPathsAsync([fixture, fixture]);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(model.Entries.Count == 2, "Import did not create two distinct occurrences.");
        Require(model.Entries[0].Id != model.Entries[1].Id && model.Entries[0].Entry.Track.Id == model.Entries[1].Entry.Track.Id, "Duplicate identity contract failed.");
        Require(model.Title == Strings.Get("NothingPlaying") && !model.IsPlaying, "Import unexpectedly selected/started playback.");
        var first = model.Entries[0];
        model.SelectedEntry = first;
        await model.PrepareAsync(first.Id);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(model.CanSeek && model.DurationSeconds > 0 && model.Title == first.Title, "Native preparation did not reach the WPF model.");
        var title = (TextBlock)window.FindName("NowPlayingTitle");
        Require(title.Text == first.Title, "Now-playing title binding failed.");
        var slider = (Slider)window.FindName("SeekSlider");
        Require(slider.IsEnabled && slider.Maximum == model.DurationSeconds, "Seek range binding failed.");
        Require(window.Background is SolidColorBrush background && background.Color == Color.FromRgb(0x24, 0x24, 0x24), "Dark window theme was not applied to the derived window.");
        await model.CommitSeekAsync(1);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(Math.Abs(model.SeekPosition - 1) < 0.01 && !model.IsPlaying, "Seek failed or started stopped audio.");
        model.Search = "no-match-for-this-fixture";
        Require(model.VisibleEntries.Cast<object>().Count() == 0 && model.Title == first.Title, "Search mutated playback source.");
        model.Search = "";
        model.Entries[1].Enabled = false;
        await model.AddPathsAsync([taggedFixture]);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(model.Entries.Count == 3 && model.Entries[2].Title == "Fixture — Музыка", "Real Unicode FLAC metadata was not read.");
        using (File.Open(taggedFixture, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        await model.PrepareAsync(model.Entries[2].Id);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(title.Text == "Fixture — Музыка" && model.CanSeek && !model.IsPlaying, "Tagged native source did not reach title binding.");
        await model.CommitSeekAsync(1);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(Math.Abs(model.SeekPosition - 1) < 0.01, "Tagged FLAC production seek failed.");
        await model.WaveformCompletion.WaitAsync(TimeSpan.FromSeconds(30));
        Require(model.Waveform is { } wave && wave.Minimum.Length == 300 && wave.Minimum.Min() < -0.045 && wave.Maximum.Max() > 0.045, "Real waveform did not reach the view model.");
        var sourceTabId = model.SelectedPlaylist.Id;
        var activeEntryId = model.Entries[2].Id;
        var originalOrder = model.Entries.Select(e => e.Id).ToArray();
        model.DuplicatePlaylist(); model.RenamePlaylist("Музыка %_ ' saved");
        var duplicateTabId = model.SelectedPlaylist.Id;
        var duplicateFirst = model.Entries[0];
        model.MoveEntries([duplicateFirst], 1);
        Require(model.Entries[1].Id == duplicateFirst.Id && model.SourcePlaylistId == sourceTabId, "Editing another tab changed the active sequence.");
        Require(model.Playlists.First(p => p.Id == sourceTabId).Entries.Select(e => e.Id).SequenceEqual(originalOrder), "Reordering another tab mutated source order.");
        var expectedOrder = model.Entries.Select(e => e.Id).ToArray();
        model.Search = "no-match"; model.MoveEntries([duplicateFirst], -1);
        Require(model.Entries.Select(e => e.Id).SequenceEqual(expectedOrder), "Filtered manual reorder was not rejected."); model.Search = "";
        model.Enqueue([model.Entries[0], model.Entries[2]], false); model.Enqueue([model.Entries[1]], true);
        model.Repeat = RepeatMode.All; model.Shuffle = true;
        var queuedIds = model.Queue.Select(q => q.Id).ToArray();
        await model.ConfigureAudioAsync(new(ReplayGain: ReplayGainMode.Album, CrossfadeSeconds: 3), new());
        model.Volume = 23; model.Muted = true;
        await model.CommitSeekAsync(1);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await model.SaveNowAsync();
        await model.DisposeAsync();
        model = ((App)Application.Current).CreateModel(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", "stage-c-data"));
        window.DataContext = model;
        await model.InitializeAsync();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await model.WaveformCompletion.WaitAsync(TimeSpan.FromSeconds(30));
        Require(model.Playlists.Count == 2 && model.SelectedPlaylist.Id == duplicateTabId && model.SourcePlaylistId == sourceTabId, "Playlist selection/source identity was not restored.");
        Require(model.Entries.Select(e => e.Id).SequenceEqual(expectedOrder) && !model.Entries[0].Enabled, "Stable order or enabled state was lost after reopen.");
        Require(model.Playlists.First(p => p.Id == sourceTabId).Entries[2].Id == activeEntryId && model.Title == "Fixture — Музыка", "Active item was not restored by stable identity.");
        Require(Math.Abs(model.SeekPosition - 1) < 0.01 && !model.IsPlaying && model.Volume == 23 && model.Muted, "Session position/gain/mute failed or restoration autoplayed.");
        Require(model.Waveform is not null, "Cached waveform was not restored.");
        Require(model.Queue.Select(q => q.Id).SequenceEqual(queuedIds) && model.Repeat == RepeatMode.All && model.Shuffle, "Queue/repeat/shuffle state was not restored.");
        Require(model.WindowSettings.Processing is { ReplayGain: ReplayGainMode.Album, CrossfadeSeconds: 3 }, "Audio processing settings were not restored.");
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(_bindingErrors.Count == 0, "WPF binding warnings/errors: " + string.Join("; ", _bindingErrors.Take(8)));
        var client = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(client.ActualWidth), (int)Math.Ceiling(client.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(client);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output);
        using (var file = File.Create(Path.Combine(output, "stage-c-window.png"))) encoder.Save(file);
        return new
        {
            Status = "ui-smoke-passed", Environment = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ImportedEntries = model.Entries.Count, DistinctEntryIds = true, SharedTrackIdentity = true,
            ImportDidNotAutoplay = true, NativePreparation = true, DurationSeconds = model.DurationSeconds,
            SeekPositionSeconds = model.SeekPosition, SearchLeavesSourceUnchanged = true, BindingErrors = _bindingErrors.Count,
            UnicodeMetadata = true, MetadataHandleReleased = true, DarkTheme = true,
            PersistentTabs = 2, StableOrderAndIds = true, EditingOtherTabKeepsSource = true, SessionReopenedWithoutAutoplay = true, SavedVolume = model.Volume, SavedMuted = model.Muted, RealWaveform = true, WaveformBuckets = model.Waveform!.Minimum.Length,
            PersistentQueueItems = model.Queue.Count, QueueRepeatShuffleRestore = true, ProcessingSettingsRestore = true,
            Screenshot = "stage-c-window.png", WasapiOutput = "not-run", Listening = "not-run"
        };
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    protected override void Dispose(bool disposing)
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}
