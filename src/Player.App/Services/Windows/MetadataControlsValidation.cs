using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Player.App.Services.Library;
using Player.App.ViewModels;
using Player.App.Views;

namespace Player.App.Services.Windows;

/// <summary>Owned legacy-tag fixture and actual WPF volume click routing.</summary>
internal static class MetadataControlsValidation
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);

    internal static async Task<object> RunAsync(MainWindow window, PlayerViewModel model, string fixture, string output)
    {
        static void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
        var path = Path.Combine(output, "owned-Беларускае-legacy.flac");
        File.Copy(fixture, path);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        static string Broken(string text) => Encoding.Latin1.GetString(Encoding.GetEncoding(1251).GetBytes(text));
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = Broken("Людзі"); file.Tag.Performers = [Broken("Кастусь Герашчанка")];
            file.Tag.Album = Broken("Цэпэліны"); file.Save();
        }
        var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
        var diagnostics = new List<string>();
        var track = MediaMetadataReader.Read(path, Guid.NewGuid(), diagnostics.Add);
        Check(track.Title == "Людзі" && track.Artist == "Кастусь Герашчанка" && track.Album == "Цэпэліны" && diagnostics.Count == 0,
            "Production TagLib metadata did not recover legacy Belarusian title/artist/album.");
        Check(hash.AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Metadata recovery changed owned audio bytes.");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        File.Delete(path);

        var slider = (Slider)window.FindName("VolumeSlider"); slider.ApplyTemplate(); window.UpdateLayout();
        var sliderTrack = (Track)slider.Template.FindName("PART_Track", slider);
        var original = model.Volume;
        try
        {
            Check(slider.IsMoveToPointEnabled && sliderTrack.ActualWidth > 20, "Volume slider has no move-to-point track.");
            foreach (var fraction in new[] { .8, .2 })
            {
                var x = sliderTrack.Thumb.ActualWidth / 2 + fraction * (sliderTrack.ActualWidth - sliderTrack.Thumb.ActualWidth);
                var point = sliderTrack.PointToScreen(new Point(x, sliderTrack.ActualHeight / 2));
                Check(SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y)), "Owned UI cursor positioning failed.");
                await Task.Delay(100); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                Check(Math.Abs(slider.Value - fraction * 100) <= 2 && Math.Abs(model.Volume - slider.Value) < .01,
                    "Volume click did not jump directly to its position or update the two-way binding.");
            }
        }
        finally { model.Volume = original; }
        return new { Status = "metadata-controls-passed", BelarusianTagLibRecovery = true, OwnedSourceUnchanged = true,
            VolumeClickBothDirections = true, VolumeBinding = true, VolumeRestored = model.Volume == original };
    }
}
