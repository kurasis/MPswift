using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Player.App.Resources;
using Player.App.Controls;
using Player.App.Services.Windows;
using Player.App.ViewModels;

namespace Player.App.Views;

public sealed class PreferencesWindow : Window
{
    internal ComboBox LanguageBox { get; }
    internal ComboBox AccentBox { get; }
    internal ComboBox WaveformStyleBox { get; }
    internal ComboBox RepeatDefaultBox { get; }
    internal CheckBox ShuffleDefaultBox { get; }
    internal CheckBox AlbumSectionsBox { get; }
    internal CheckBox CloseToTrayBox { get; }
    internal CheckBox DesktopPanelBox { get; }
    internal ComboBox DesktopPanelPositionBox { get; }
    internal TextBlock ValidationText { get; } = AccessibleStatus.Create();
    internal TextBlock CacheValidationText { get; } = AccessibleStatus.Create();
    internal CheckBox RestoreSessionBox { get; }
    internal CheckBox RestorePositionBox { get; }
    internal ComboBox CueEncodingBox { get; }
    internal TextBox CacheBudgetBox { get; }
    internal Button ClearCacheButton { get; }
    internal Button ApplyButton { get; }
    internal Task ApplyCompletion { get; private set; } = Task.CompletedTask;
    internal Task CacheClearCompletion { get; private set; } = Task.CompletedTask;
    internal Task CacheRefreshCompletion { get; private set; } = Task.CompletedTask;

    public PreferencesWindow(Window owner, PlayerViewModel model)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Title = Strings.Settings; Width = 580; Height = 720; MinWidth = 460; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var surface = new Border { Child = root };
        surface.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); Content = surface;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var cancel = new Button { Content = Strings.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        ApplyButton = new Button { Content = Strings.Apply, IsDefault = true, Style = (Style)FindResource("AccentButton") }; footer.Children.Add(ApplyButton);
        var validation = ValidationText; validation.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(validation, Dock.Bottom); root.Children.Add(validation);
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        panel.Children.Add(new TextBlock { Text = Strings.Settings, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        void Section(string key) => panel.Children.Add(new TextBlock { Text = Strings.Get(key), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
        void Hint(string text)
        {
            var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8), FontSize = 12 };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); panel.Children.Add(hint);
        }
        void Label(string key) => panel.Children.Add(new TextBlock { Text = Strings.Get(key), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 7) });
        CheckBox Check(string key, bool value)
        { var box = new CheckBox { Content = Strings.Get(key), IsChecked = value, Margin = new Thickness(0, 3, 0, 3) }; panel.Children.Add(box); return box; }
        Section("InterfaceSection"); Label("Language");
        LanguageBox = new ComboBox { ItemsSource = new[] { "English", "Русский" }, SelectedIndex = model.WindowSettings.Language == "ru" ? 1 : 0, MinHeight = 32 };
        AutomationProperties.SetName(LanguageBox, Strings.Language); panel.Children.Add(LanguageBox);
        Label("AccentColor");
        var accents = new[] { "amber", "blue", "green", "violet" };
        AccentBox = new ComboBox { ItemsSource = accents.Select(a => Strings.Get("Accent_" + a)).ToArray(), SelectedIndex = Array.IndexOf(accents, model.WindowSettings.Accent), MinHeight = 32 };
        AutomationProperties.SetName(AccentBox, Strings.Get("AccentColor")); panel.Children.Add(AccentBox);
        Hint(Strings.Get("AccentHelp"));
        Label("WaveformStyle");
        WaveformStyleBox = new ComboBox { ItemsSource = new[] { Strings.Get("WaveformEnergy"), Strings.Get("WaveformPeaks") }, SelectedIndex = model.WindowSettings.WaveformStyle == "peaks" ? 1 : 0, MinHeight = 32 };
        AutomationProperties.SetName(WaveformStyleBox, Strings.Get("WaveformStyle")); panel.Children.Add(WaveformStyleBox);
        Section("PlaylistSection");
        AlbumSectionsBox = Check("ShowAlbumSections", model.WindowSettings.ShowAlbumSections);
        Hint(Strings.AlbumSectionsHelp); Label("CueEncoding");
        var cueCodePages = new[] { 1251, 1252, 866, 0 };
        CueEncodingBox = new ComboBox { ItemsSource = new[] { Strings.Get("Encoding1251"), Strings.Get("Encoding1252"), Strings.Get("Encoding866"), "UTF-8 / Unicode" },
            SelectedIndex = Array.IndexOf(cueCodePages, model.WindowSettings.CueCodePage), MinHeight = 32 };
        AutomationProperties.SetName(CueEncodingBox, Strings.Get("CueEncoding")); panel.Children.Add(CueEncodingBox);
        Hint(Strings.Get("CueEncodingHelp"));
        Section("PlaybackPreferences");
        Label("DefaultRepeat");
        RepeatDefaultBox = new ComboBox { ItemsSource = Enum.GetValues<Player.Core.Playback.RepeatMode>().Select(mode => new KeyValuePair<Player.Core.Playback.RepeatMode, string>(mode, Strings.Get("Repeat" + mode))).ToArray(), DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = model.WindowSettings.DefaultRepeat, MinHeight = 32 };
        AutomationProperties.SetName(RepeatDefaultBox, Strings.Get("DefaultRepeat")); panel.Children.Add(RepeatDefaultBox);
        ShuffleDefaultBox = Check("DefaultShuffle", model.WindowSettings.DefaultShuffle);
        Hint(Strings.Get("PlaybackDefaultsHelp"));
        RestoreSessionBox = Check("RestoreSession", model.WindowSettings.RestoreSession);
        RestorePositionBox = Check("RestorePosition", model.WindowSettings.RestorePosition);
        RestorePositionBox.IsEnabled = RestoreSessionBox.IsChecked == true;
        RestoreSessionBox.Click += (_, _) => RestorePositionBox.IsEnabled = RestoreSessionBox.IsChecked == true;
        Hint(Strings.Get("RestoreSessionHelp"));
        Section("BehaviorSection");
        CloseToTrayBox = Check("CloseToTray", model.WindowSettings.CloseToTray); Hint(Strings.CloseTrayHelp);
        DesktopPanelBox = Check("DesktopPanelEnabled", model.WindowSettings.DesktopPanelEnabled); Hint(Strings.Get("DesktopPanelHelp"));
        Label("DesktopPanelPosition");
        var positions = new[] { "PanelPositionKeep", "PanelPositionReset", "PanelPositionTopLeft", "PanelPositionTopRight", "PanelPositionBottomLeft", "PanelPositionBottomRight" };
        DesktopPanelPositionBox = new ComboBox { ItemsSource = positions.Select(Strings.Get).ToArray(), SelectedIndex = 0, MinHeight = 32 };
        AutomationProperties.SetName(DesktopPanelPositionBox, Strings.Get("DesktopPanelPosition")); panel.Children.Add(DesktopPanelPositionBox);
        Hint(Strings.Get("DesktopPanelPositionHelp"));
        Section("AudioSection"); Hint(Strings.AudioSettingsHelp);
        var audio = new Button { Content = Strings.Get("AudioSettings"), HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(audio);
        audio.Click += async (_, _) =>
        {
            audio.IsEnabled = false;
            try { new AudioSettingsWindow(this, model, await model.GetDevicesAsync()).ShowDialog(); }
            catch (Exception error) { AccessibleStatus.Update(validation, Strings.Get("DevicesUnavailable")); model.Message = Strings.Get("DevicesUnavailable"); model.Details = error.Message; }
            finally { audio.IsEnabled = true; }
        };
        Section("StorageSection"); Label("CacheBudget");
        CacheBudgetBox = new TextBox { Text = model.WindowSettings.WaveformCacheMiB.ToString(CultureInfo.InvariantCulture), MaxLength = 4, MinHeight = 32 };
        AutomationProperties.SetName(CacheBudgetBox, Strings.Get("CacheBudget")); panel.Children.Add(CacheBudgetBox);
        CacheValidationText.Margin = new Thickness(0, 4, 0, 0); panel.Children.Add(CacheValidationText);
        CacheBudgetBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(CacheBudgetBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 16 and <= 2048)
            { AccessibleStatus.Update(CacheValidationText, ""); AutomationProperties.SetHelpText(CacheBudgetBox, ""); }
        };
        Hint(Strings.Get("CacheBudgetHelp"));
        var cacheStatus = AccessibleStatus.Create(); cacheStatus.Text = Strings.Get("CacheChecking"); cacheStatus.Margin = new Thickness(0, 4, 0, 8); panel.Children.Add(cacheStatus);
        ClearCacheButton = new Button { Content = Strings.Get("ClearWaveformCache"), HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(ClearCacheButton);
        Hint(Strings.Get("ClearCacheHelp"));
        async Task RefreshCacheAsync()
        {
            try
            {
                var usage = await model.GetCacheUsageAsync();
                AccessibleStatus.Update(cacheStatus, usage is null ? Strings.Get("CacheUnavailable") : string.Format(Strings.Culture, Strings.Get("CacheUsage"), usage.Bytes / 1048576d, usage.Files));
                ClearCacheButton.IsEnabled = usage is not null;
            }
            catch (Exception error) { AccessibleStatus.Update(cacheStatus, Strings.Get("CacheFailed")); model.Details = error.Message; }
        }
        Loaded += (_, _) => CacheRefreshCompletion = RefreshCacheAsync();
        ClearCacheButton.Click += (_, _) => CacheClearCompletion = ClearAsync();
        async Task ClearAsync()
        {
            ClearCacheButton.IsEnabled = false;
            try { await model.ClearWaveformCacheAsync(); await RefreshCacheAsync(); model.Message = Strings.Get("CacheCleared"); }
            catch (Exception error) { AccessibleStatus.Update(cacheStatus, Strings.Get("CacheFailed")); model.Details = error.Message; }
            finally { ClearCacheButton.IsEnabled = true; }
        }
        var app = (App)Application.Current;
        Hint(Strings.Get("DataLocation") + ": " + app.DataDirectory + "\n" + Strings.Get("LogLocation") + ": " + System.IO.Path.Combine(app.DataDirectory, "Logs"));
        Section("DiagnosticsTitle"); Hint(Player.Core.ProductInfo.Name + " " + Player.Core.ProductInfo.Version);
        var diagnostics = new Button { Content = Strings.Get("PreviewDiagnostics"), HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(diagnostics);
        diagnostics.Click += async (_, _) =>
        {
            diagnostics.IsEnabled = false;
            try { var versions = await new Services.Audio.NativeDiagnostics().VerifyAsync(); new DiagnosticsWindow(this, model.CreateDiagnosticReport(versions)).ShowDialog(); }
            catch (Exception error) { AccessibleStatus.Update(validation, Strings.Get("DiagnosticsFailed")); model.Message = Strings.Get("DiagnosticsFailed"); model.Details = error.Message; }
            finally { diagnostics.IsEnabled = true; }
        };
        Hint(Strings.Get("LocalNotices"));
        ApplyButton.Click += (_, _) => ApplyCompletion = ApplyAsync();
        async Task ApplyAsync()
        {
            if (!ApplyButton.IsEnabled) return;
            if (!int.TryParse(CacheBudgetBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var budget) || budget is < 16 or > 2048)
            {
                var error = Strings.Get("CacheBudgetInvalid"); AccessibleStatus.Update(CacheValidationText, error);
                AutomationProperties.SetHelpText(CacheBudgetBox, error); CacheBudgetBox.Focus(); return;
            }
            ApplyButton.IsEnabled = false;
            try
            {
                var left = model.WindowSettings.DesktopPanelLeft; var top = model.WindowSettings.DesktopPanelTop;
                var preset = DesktopPanelPositionBox.SelectedIndex;
                if (preset == 1) { left = null; top = null; }
                else if (preset is >= 2 and <= 5)
                {
                    var area = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle).WorkingArea;
                    // Physical work-area edges are fitted to the panel's actual size on its next reveal.
                    left = preset is 2 or 4 ? area.Left : area.Right - 1;
                    top = preset is 2 or 3 ? area.Top : area.Bottom - 1;
                }
                await model.SavePreferencesAsync(model.WindowSettings with { Language = LanguageBox.SelectedIndex == 1 ? "ru" : "en", CloseToTray = CloseToTrayBox.IsChecked == true, DesktopPanelEnabled = DesktopPanelBox.IsChecked == true,
                    DesktopPanelLeft = left, DesktopPanelTop = top,
                    ShowAlbumSections = AlbumSectionsBox.IsChecked == true, CueCodePage = cueCodePages[Math.Clamp(CueEncodingBox.SelectedIndex, 0, cueCodePages.Length - 1)],
                    Accent = accents[Math.Clamp(AccentBox.SelectedIndex, 0, accents.Length - 1)], WaveformCacheMiB = budget,
                    WaveformStyle = WaveformStyleBox.SelectedIndex == 1 ? "peaks" : "energy", DefaultRepeat = (Player.Core.Playback.RepeatMode)RepeatDefaultBox.SelectedValue, DefaultShuffle = ShuffleDefaultBox.IsChecked == true,
                    RestoreSession = RestoreSessionBox.IsChecked == true, RestorePosition = RestorePositionBox.IsChecked == true });
                Strings.SetLanguage(model.WindowSettings.Language); model.RefreshLanguage(); model.Message = Strings.PreferencesSaved; Close();
            }
            catch (Exception error) { AccessibleStatus.Update(validation, Strings.SaveFailed); model.Message = Strings.SaveFailed; model.Details = error.Message; }
            finally { ApplyButton.IsEnabled = true; }
        }
    }
}
