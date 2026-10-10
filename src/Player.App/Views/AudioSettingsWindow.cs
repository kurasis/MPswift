using Player.App.Resources;
using Player.App.Controls;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Player.App.ViewModels;
using Player.Core.Playback;

namespace Player.App.Views;

public sealed class AudioSettingsWindow : Window
{
    internal Button ApplyButton { get; private set; } = null!;
    internal TextBlock StatusText { get; } = AccessibleStatus.Create();
    internal Task ApplyCompletion { get; private set; } = Task.CompletedTask;
    internal ComboBox DeviceBox { get; private set; } = null!;
    internal ComboBox ReplayGainBox { get; private set; } = null!;
    public AudioSettingsWindow(Window owner, PlayerViewModel model, AudioDevice[] devices)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Title = Strings.Get("AudioTitle"); Width = 600; Height = 700; MinWidth = 420; MinHeight = 400;
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = new ScrollViewer { Content = panel };
        var settings = (model.WindowSettings.Processing ?? new()).Validate(); var output = model.WindowSettings.Output ?? new();
        void Hint(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        void Label(string key, Control target)
        {
            var caption = new Label { Content = new TextBlock { Text = Strings.Get(key), TextWrapping = TextWrapping.Wrap }, Target = target, Padding = new Thickness(0, 8, 0, 4) };
            caption.MouseLeftButtonDown += (_, _) => target.Focus();
            AutomationProperties.SetLabeledBy(target, caption); AutomationProperties.SetName(target, Strings.Get(key)); panel.Children.Add(caption);
        }
        if (output.DeviceId is { } saved && devices.All(d => d.Id != saved)) devices = devices.Append(new(saved, Strings.Get("SavedOutputUnavailable"), 0, 0)).ToArray();
        devices = devices.Select(d => d.Id is null ? d with { Name = Strings.Get("DefaultOutput") } : d).ToArray();
        var device = new ComboBox { ItemsSource = devices, DisplayMemberPath = "Name", SelectedItem = devices.FirstOrDefault(d => d.Id == output.DeviceId) ?? devices.FirstOrDefault() }; DeviceBox = device; Label("OutputDevice", device); panel.Children.Add(device);
        var exclusive = new CheckBox { Content = Strings.Get("ExclusiveMode"), IsChecked = output.Exclusive, Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(exclusive);
        Hint(Strings.Get("ExclusivePolicy"));
        var gain = new ComboBox { ItemsSource = Enum.GetValues<ReplayGainMode>().Select(mode => new KeyValuePair<ReplayGainMode, string>(mode, Strings.Get("Gain" + mode))).ToArray(), DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = settings.ReplayGain }; ReplayGainBox = gain; Label("ReplayGainHelp", gain); panel.Children.Add(gain);
        var eq = new CheckBox { Content = Strings.Get("EnableEq"), IsChecked = settings.EqualizerEnabled, Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(eq);
        var bands = new Slider[10];
        for (var i = 0; i < bands.Length; i++)
        {
            var row = new DockPanel(); var frequency = AudioProcessingSettings.Centers[i].ToString(Strings.Culture) + " " + Strings.Get("HzUnit");
            bands[i] = new Slider { Minimum = -12, Maximum = 12, Value = settings.Bands![i], TickFrequency = 1, SmallChange = 1, ToolTip = Strings.Get("GainDb"), Margin = new Thickness(8, 5, 8, 5), IsMoveToPointEnabled = true };
            var name = new Label { Content = frequency, Width = 70, Target = bands[i], Padding = new Thickness(0, 5, 0, 5) };
            name.MouseLeftButtonDown += (_, _) => name.Target.Focus();
            AutomationProperties.SetLabeledBy(bands[i], name); AutomationProperties.SetName(bands[i], frequency + ", " + Strings.Get("GainDb"));
            row.Children.Add(name); row.Children.Add(bands[i]); panel.Children.Add(row);
        }
        Hint(Strings.Get("EqPolicy"));
        var preamp = new Slider { Minimum = -24, Maximum = 12, Value = settings.PreampDb, SmallChange = 1, IsMoveToPointEnabled = true }; Label("Preamp", preamp); panel.Children.Add(preamp);
        var fade = new Slider { Minimum = 0, Maximum = 10, Value = settings.CrossfadeSeconds, SmallChange = 1, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true }; Label("CrossfadeHelp", fade); panel.Children.Add(fade);
        StatusText.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(StatusText);
        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(buttons);
        AudioProcessingSettings Capture() => new(eq.IsChecked == true, preamp.Value, bands.Select(b => b.Value).ToArray(), (ReplayGainMode)gain.SelectedValue, fade.Value);
        void Set(AudioProcessingSettings preset) { preset = preset.Validate(); eq.IsChecked = preset.EqualizerEnabled; preamp.Value = preset.PreampDb; for (var i = 0; i < 10; i++) bands[i].Value = preset.Bands![i]; gain.SelectedValue = preset.ReplayGain; fade.Value = preset.CrossfadeSeconds; }
        Button Button(string title, RoutedEventHandler handler) { var b = new Button { Content = title, Margin = new Thickness(4) }; b.Click += handler; buttons.Children.Add(b); return b; }
        Button(Strings.Get("Flat"), (_, _) => { foreach (var b in bands) b.Value = 0; preamp.Value = 0; });
        Button(Strings.Get("SavePreset"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = Strings.Get("PresetFilter"), FileName = "audio-preset.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { Services.Audio.LocalFileAccess.ValidateDirectory(Path.GetDirectoryName(dialog.FileName)!); Services.Storage.SettingsFile.Export(dialog.FileName, model.WindowSettings with { Processing = Capture() }); }
            catch (Exception e) { MessageBox.Show(this, e.Message, Strings.Get("PresetSaveFailed")); }
        });
        Button(Strings.Get("LoadPreset"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Get("PresetFilter") }; if (dialog.ShowDialog(this) != true) return;
            try { Services.Audio.BassSmokeSession.ValidateSourcePath(dialog.FileName); if (new FileInfo(dialog.FileName).Length > 65536) throw new InvalidDataException("Preset too large."); var preset = JsonSerializer.Deserialize<Player.Core.Library.PlayerSettings>(File.ReadAllText(dialog.FileName))!.Validate(); Set(preset.Processing ?? new()); }
            catch (Exception e) { MessageBox.Show(this, e.Message, Strings.Get("PresetLoadFailed")); }
        });
        var busy = false; var closed = false; Closed += (_, _) => closed = true;
        var cancel = Button(Strings.Get("Cancel"), (_, _) => Close()); cancel.IsCancel = true;
        ApplyButton = Button(Strings.Get("Apply"), (_, _) => { if (!busy && !closed) ApplyCompletion = ApplyAsync(); });
        ApplyButton.SetResourceReference(StyleProperty, "AccentButton");
        Closing += (_, args) => { if (busy) args.Cancel = true; };
        async Task ApplyAsync()
        {
            busy = true; ApplyButton.IsEnabled = false; cancel.IsEnabled = false; panel.IsEnabled = false;
            ApplyButton.Content = Strings.Get("ApplyingAudio"); AccessibleStatus.Update(StatusText, Strings.Get("ApplyingAudio"));
            var succeeded = false;
            try { await model.ConfigureAudioAsync(Capture(), new((device.SelectedItem as AudioDevice)?.Id, exclusive.IsChecked == true)); succeeded = true; }
            catch (Exception error)
            {
                if (!closed) AccessibleStatus.Update(StatusText, Strings.Get("AudioApplyFailed"));
                model.Message = Strings.ErrorUnexpected; model.Details = error.Message;
            }
            finally
            {
                busy = false;
                if (!closed) { panel.IsEnabled = true; ApplyButton.IsEnabled = true; cancel.IsEnabled = true; ApplyButton.Content = Strings.Get("Apply"); }
            }
            if (succeeded && !closed) Close();
        }
    }
}
