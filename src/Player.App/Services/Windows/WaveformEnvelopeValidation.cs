using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Player.App.Controls;
using Player.App.Services.Storage;
using Player.App.Services.Waveforms;
using Player.App.ViewModels;
using Player.App.Views;
using Player.Core.Waveforms;

namespace Player.App.Services.Windows;

internal static class WaveformEnvelopeValidation
{
    public static async Task<object> RunAsync(MainWindow owner, PlayerViewModel model, string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var directory = Path.Combine(output, "waveform-envelope-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Transient and energy.wav");
        const int rate = 48000, frames = rate * 12;
        var expected = new double[frames / 480];
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8); writer.Write(36 + frames * 4); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(frames * 4);
            for (var frame = 0; frame < frames; frame++)
            {
                var time = (double)frame / rate;
                var envelope = .1 + .8 * (.5 + .5 * Math.Sin(2 * Math.PI * .4 * time));
                var value = frame % 480 == 0 ? 1 : envelope * Math.Sin(2 * Math.PI * 440 * time);
                var sample = (short)Math.Round(value * 32767); writer.Write(sample); writer.Write((short)-sample);
                expected[frame / 480] += (double)sample / 32768 * ((double)sample / 32768) / 480;
            }
        }
        var hash = SHA256.HashData(File.ReadAllBytes(path)); var snapshot = model.Snapshot;
        Window? host = null;
        try
        {
            WaveformData data;
            await using (var analyzer = new BassWaveformService(new WaveformCache(Path.Combine(directory, "Cache"))))
                data = await analyzer.AnalyzeAsync(path, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            Check(data.Rms is { Length: 1200 } && data.Minimum.All(v => v < -.999f) && data.Maximum.All(v => v > .999f), "Native transients or RMS buckets were lost.");
            var rms = data.Rms!;
            var error = rms.Select((value, i) => Math.Abs(value - Math.Sqrt(expected[i]))).Max();
            Check(error < .00001, "Native envelope differs from independently computed known PCM energy.");
            var columns = WaveformProjection.Create(data, 360);
            Check(columns.Max(column => column.Rms) - columns.Min(column => column.Rms) > .4 && columns.All(column => column.Rms < .8),
                "Full-scale transients still fill the solid envelope or hide energy variation.");
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Native PCM: full-scale transients, changing energy, opposite-phase stereo", Margin = new Thickness(0, 0, 0, 12) });
            var waveform = new WaveformControl { Data = data, Duration = 12, Position = 5, CanSeek = true, Height = 44, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(waveform);
            var control = new WaveformControl { Data = data, Duration = 12, Position = 5, CanSeek = true, Height = 44, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(control);
            var surface = new Border { Child = panel }; surface.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            host = new Window { Owner = owner, Width = 780, Height = 230, Content = surface, Title = "Owned native waveform validation" };
            host.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window)); host.Show();
            await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            CustomizationValidation.Render(surface, output, "waveform-envelope-" + model.WindowSettings.Language + ".png");
            var committed = -1d; waveform.CommitSeek += value => committed = value;
            waveform.SeekFromAutomation(3.5); Check(committed == 3.5, "Envelope rendering changed the seek contract.");
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            Check(hash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))) && model.Snapshot.EntryId == snapshot.EntryId &&
                model.Snapshot.State == snapshot.State && model.Snapshot.Position == snapshot.Position, "Waveform analysis changed source or playback.");
            return new { Status = "waveform-envelope-passed", ActualNativePcm = true, OppositePhasePreserved = true, PeakExtremaPreserved = true,
                RmsMaximumError = error, RmsMinimum = rms.Min(), RmsMaximum = rms.Max(), WeightedDisplayVariation = true,
                CachedGeometryAtTwoWidths = true, SeekUnchanged = true, SourceUnchanged = true, PlaybackPreserved = true, OldCacheAutomaticallyRegenerated = true };
        }
        finally { host?.Close(); Directory.Delete(directory, true); }
    }
}
