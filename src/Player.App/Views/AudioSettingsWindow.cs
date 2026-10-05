using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Player.App.ViewModels;
using Player.Core.Playback;

namespace Player.App.Views;

public sealed class AudioSettingsWindow : Window
{
    public AudioSettingsWindow(Window owner, PlayerViewModel model, AudioDevice[] devices)
    {
        Owner = owner; Title = "Audio output and processing"; Width = 600; Height = 700;
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = new ScrollViewer { Content = panel };
        var settings = (model.WindowSettings.Processing ?? new()).Validate(); var output = model.WindowSettings.Output ?? new();
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        Label("Output device (changing output pauses playback)");
        var device = new ComboBox { ItemsSource = devices, DisplayMemberPath = "Name", SelectedItem = devices.FirstOrDefault(d => d.Id == output.DeviceId) ?? devices.FirstOrDefault() }; panel.Children.Add(device);
        var exclusive = new CheckBox { Content = "Exclusive mode — may prevent other apps using this endpoint", IsChecked = output.Exclusive, Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(exclusive);
        Label("Unavailable exclusive formats report an error. Shared mode is never selected silently.");
        Label("ReplayGain (tags only; missing tags mean 0 dB)");
        var gain = new ComboBox { ItemsSource = Enum.GetValues<ReplayGainMode>(), SelectedItem = settings.ReplayGain }; panel.Children.Add(gain);
        var eq = new CheckBox { Content = "Enable equalizer", IsChecked = settings.EqualizerEnabled, Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(eq);
        var bands = new Slider[10];
        for (var i = 0; i < bands.Length; i++)
        {
            var row = new DockPanel(); var name = new TextBlock { Text = AudioProcessingSettings.Centers[i] + " Hz", Width = 70 }; row.Children.Add(name);
            bands[i] = new Slider { Minimum = -12, Maximum = 12, Value = settings.Bands![i], TickFrequency = 1, SmallChange = 1, ToolTip = "Gain in dB", Margin = new Thickness(8, 5, 8, 5) }; row.Children.Add(bands[i]); panel.Children.Add(row);
        }
        Label("Bands above output Nyquist are bypassed. Positive EQ adds conservative headroom; final overrange samples are saturated and reported in diagnostics.");
        Label("Preamp (dB)"); var preamp = new Slider { Minimum = -24, Maximum = 12, Value = settings.PreampDb, SmallChange = 1 }; panel.Children.Add(preamp);
        Label("Automatic equal-power crossfade (seconds; 0 = off). CUE continuity and repeat-one bypass overlap.");
        var fade = new Slider { Minimum = 0, Maximum = 10, Value = settings.CrossfadeSeconds, SmallChange = 1, TickFrequency = 1, IsSnapToTickEnabled = true }; panel.Children.Add(fade);
        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(buttons);
        AudioProcessingSettings Capture() => new(eq.IsChecked == true, preamp.Value, bands.Select(b => b.Value).ToArray(), (ReplayGainMode)gain.SelectedItem, fade.Value);
        void Set(AudioProcessingSettings preset) { preset = preset.Validate(); eq.IsChecked = preset.EqualizerEnabled; preamp.Value = preset.PreampDb; for (var i = 0; i < 10; i++) bands[i].Value = preset.Bands![i]; gain.SelectedItem = preset.ReplayGain; fade.Value = preset.CrossfadeSeconds; }
        void Button(string title, RoutedEventHandler handler) { var b = new Button { Content = title, Margin = new Thickness(4) }; b.Click += handler; buttons.Children.Add(b); }
        Button("Flat", (_, _) => { foreach (var b in bands) b.Value = 0; preamp.Value = 0; });
        Button("Save preset", (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Local EQ preset (*.json)|*.json", FileName = "audio-preset.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { Services.Audio.LocalFileAccess.ValidateDirectory(Path.GetDirectoryName(dialog.FileName)!); Services.Storage.SettingsFile.Export(dialog.FileName, model.WindowSettings with { Processing = Capture() }); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Preset could not be saved"); }
        });
        Button("Load preset", (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Local EQ preset (*.json)|*.json" }; if (dialog.ShowDialog(this) != true) return;
            try { Services.Audio.BassSmokeSession.ValidateSourcePath(dialog.FileName); if (new FileInfo(dialog.FileName).Length > 65536) throw new InvalidDataException("Preset too large."); var preset = JsonSerializer.Deserialize<Player.Core.Library.PlayerSettings>(File.ReadAllText(dialog.FileName))!.Validate(); Set(preset.Processing ?? new()); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Preset could not be loaded"); }
        });
        Button("Apply", async (_, _) => { await model.ConfigureAudioAsync(Capture(), new((device.SelectedItem as AudioDevice)?.Id, exclusive.IsChecked == true)); Close(); });
        Button("Cancel", (_, _) => Close());
    }
}
