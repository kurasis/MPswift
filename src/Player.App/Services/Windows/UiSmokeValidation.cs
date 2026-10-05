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

    public async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
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
        await model.CommitSeekAsync(1);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(Math.Abs(model.SeekPosition - 1) < 0.01 && !model.IsPlaying, "Seek failed or started stopped audio.");
        model.Search = "no-match-for-this-fixture";
        Require(model.VisibleEntries.Cast<object>().Count() == 0 && model.Title == first.Title, "Search mutated playback source.");
        model.Search = "";
        model.Entries[1].Enabled = false;
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Require(_bindingErrors.Count == 0, "WPF binding warnings/errors: " + string.Join("; ", _bindingErrors.Take(8)));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output);
        using (var file = File.Create(Path.Combine(output, "stage-b-window.png"))) encoder.Save(file);
        return new
        {
            Status = "ui-smoke-passed", Environment = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ImportedEntries = model.Entries.Count, DistinctEntryIds = true, SharedTrackIdentity = true,
            ImportDidNotAutoplay = true, NativePreparation = true, DurationSeconds = model.DurationSeconds,
            SeekPositionSeconds = model.SeekPosition, SearchLeavesSourceUnchanged = true, BindingErrors = _bindingErrors.Count,
            Screenshot = "stage-b-window.png", WasapiOutput = "not-run", Listening = "not-run"
        };
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    protected override void Dispose(bool disposing)
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}
