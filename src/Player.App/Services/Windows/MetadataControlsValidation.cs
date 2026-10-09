using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using Player.App.Controls;
using Player.App.Resources;
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

        static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        var list = (ListBox)window.FindName("PlaylistList");
        var row = model.Entries[0]; var originalRating = row.Rating;
        list.ScrollIntoView(row); window.UpdateLayout();
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row);
        var stars = Descendants<RatingStars>(container).Single();
        var count = model.Entries.Count; var playing = model.IsPlaying;
        try
        {
            row.Rating = 0;
            foreach (var expected in new[] { 4, 0 })
            {
                var point = stars.PointToScreen(new Point(3 * 22 + 11, stars.ActualHeight / 2));
                Check(SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y)), "Rating cursor positioning failed.");
                await Task.Delay(100); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                stars.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                Check(stars.Rating == expected && row.Rating == expected, "Rating click/clear did not update the real row binding.");
                Check(model.Entries.Where(item => item.Entry.Track.Id == row.Entry.Track.Id).All(item => item.Rating == expected), "Star rating did not synchronize duplicate tracks.");
            }
            var input = PresentationSource.FromVisual(window)!;
            foreach (var (key, expected) in new[] { (Key.Right, 1), (Key.End, 5), (Key.Left, 4), (Key.Delete, 0), (Key.Space, 1), (Key.Home, 0) })
            {
                var preview = new KeyEventArgs(Keyboard.PrimaryDevice, input, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                stars.RaiseEvent(preview); Check(!preview.Handled, "Window consumed rating keyboard input.");
                stars.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, input, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
                Check(row.Rating == expected && model.Entries.Count == count && model.IsPlaying == playing, "Rating keyboard input changed playback/removed a row or failed its binding.");
            }
            var peer = UIElementAutomationPeer.CreatePeerForElement(stars)!;
            var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
            Check(peer.GetName() == Strings.Rating && range.Minimum == 0 && range.Maximum == 5 && !range.IsReadOnly, "Stars lack a localized editable automation range.");
            range.SetValue(3); Check(stars.Rating == 3 && row.Rating == 3, "Automation star value broke the binding.");
            foreach (var value in new[] { -1d, 6d, .5, double.NaN })
            {
                try { range.SetValue(value); throw new InvalidOperationException("Invalid automation rating accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)stars.ActualWidth, (int)stars.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(stars); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var image = File.Create(Path.Combine(output, "rating-stars-" + model.WindowSettings.Language + ".png")); png.Save(image);
        }
        finally { row.Rating = originalRating; }
        foreach (var (name, key, kind) in new[] { ("AddFilesButton", "AddFiles", AppIconKind.FileAdd), ("AddFolderButton", "AddFolder", AppIconKind.FolderAdd) })
        {
            var button = (Button)window.FindName(name);
            Check(button.Content is AppIcon icon && icon.Kind == kind && button.Command is not null && button.ToolTip is not null &&
                UIElementAutomationPeer.CreatePeerForElement(button)!.GetName() == Strings.Get(key), "Import icon action lost its command or localized label.");
        }
        Check(Descendants<AppIcon>(window).All(icon => icon.Width == 18 && icon.Height == 18), "Action icons do not share the 18-DIP canvas.");
        return new { Status = "metadata-controls-passed", BelarusianTagLibRecovery = true, OwnedSourceUnchanged = true,
            VolumeClickBothDirections = true, VolumeBinding = true, VolumeRestored = model.Volume == original,
            RatingMouseAndClear = true, RatingKeyboardIsolation = true, RatingAccessibleRange = true,
            RatingDuplicateBinding = true, RatingRestored = row.Rating == originalRating, LocalizedImportIcons = true, ConsistentActionIconCanvas = true };
    }
}
