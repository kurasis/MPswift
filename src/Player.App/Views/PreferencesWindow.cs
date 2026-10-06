using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Player.App.Resources;
using Player.App.ViewModels;

namespace Player.App.Views;

public sealed class PreferencesWindow : Window
{
    internal ComboBox LanguageBox { get; }
    internal CheckBox AlbumSectionsBox { get; }
    internal CheckBox CloseToTrayBox { get; }
    internal ComboBox CueEncodingBox { get; }
    internal Button ApplyButton { get; }
    internal Task ApplyCompletion { get; private set; } = Task.CompletedTask;

    public PreferencesWindow(Window owner, PlayerViewModel model)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Title = Strings.Settings; Width = 520; Height = 590; MinWidth = 460; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var surface = new Border { Child = root };
        surface.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); Content = surface;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var cancel = new Button { Content = Strings.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        ApplyButton = new Button { Content = Strings.Apply, IsDefault = true, Style = (Style)FindResource("AccentButton") }; footer.Children.Add(ApplyButton);
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        panel.Children.Add(new TextBlock { Text = Strings.Settings, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        void Section(string title) => panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
        void Hint(string text)
        {
            var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8), FontSize = 12 };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); panel.Children.Add(hint);
        }
        Section(Strings.InterfaceSection);
        panel.Children.Add(new TextBlock { Text = Strings.Language, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) });
        LanguageBox = new ComboBox { ItemsSource = new[] { "English", "Русский" }, SelectedIndex = model.WindowSettings.Language == "ru" ? 1 : 0, MinHeight = 32 };
        AutomationProperties.SetName(LanguageBox, Strings.Language); panel.Children.Add(LanguageBox);
        Section(Strings.PlaylistSection);
        AlbumSectionsBox = new CheckBox { Content = Strings.ShowAlbumSections, IsChecked = model.WindowSettings.ShowAlbumSections }; panel.Children.Add(AlbumSectionsBox);
        Hint(Strings.AlbumSectionsHelp);
        panel.Children.Add(new TextBlock { Text = Strings.Get("CueEncoding"), Margin = new Thickness(0, 10, 0, 7) });
        var cueCodePages = new[] { 1251, 1252, 866, 0 };
        CueEncodingBox = new ComboBox { ItemsSource = new[] { Strings.Get("Encoding1251"), Strings.Get("Encoding1252"), Strings.Get("Encoding866"), "UTF-8 / Unicode" },
            SelectedIndex = Array.IndexOf(cueCodePages, model.WindowSettings.CueCodePage), MinHeight = 32 };
        AutomationProperties.SetName(CueEncodingBox, Strings.Get("CueEncoding")); panel.Children.Add(CueEncodingBox);
        Hint(Strings.Get("CueEncodingHelp"));
        Section(Strings.BehaviorSection);
        CloseToTrayBox = new CheckBox { Content = Strings.CloseToTray, IsChecked = model.WindowSettings.CloseToTray }; panel.Children.Add(CloseToTrayBox);
        Hint(Strings.CloseTrayHelp);
        Section(Strings.AudioSection);
        Hint(Strings.AudioSettingsHelp);
        var audio = new Button { Content = Strings.Get("AudioSettings"), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(audio);
        audio.Click += async (_, _) =>
        {
            audio.IsEnabled = false;
            try { new AudioSettingsWindow(this, model, await model.GetDevicesAsync()).ShowDialog(); }
            catch (Exception error) { model.Message = Strings.Get("DevicesUnavailable"); model.Details = error.Message; }
            finally { audio.IsEnabled = true; }
        };
        ApplyButton.Click += (_, _) => ApplyCompletion = ApplyAsync();
        async Task ApplyAsync()
        {
            if (!ApplyButton.IsEnabled) return;
            var previous = model.WindowSettings;
            ApplyButton.IsEnabled = false;
            try
            {
                model.WindowSettings = previous with { Language = LanguageBox.SelectedIndex == 1 ? "ru" : "en", CloseToTray = CloseToTrayBox.IsChecked == true,
                    ShowAlbumSections = AlbumSectionsBox.IsChecked == true, CueCodePage = cueCodePages[Math.Clamp(CueEncodingBox.SelectedIndex, 0, cueCodePages.Length - 1)] };
                await model.SaveNowAsync();
                Strings.SetLanguage(model.WindowSettings.Language);
                model.RefreshLanguage();
                model.Message = Strings.PreferencesSaved;
                Close();
            }
            catch (Exception error)
            {
                model.WindowSettings = previous;
                model.Message = Strings.SaveFailed; model.Details = error.Message;
                MessageBox.Show(this, Strings.SaveFailed, Strings.Settings, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { ApplyButton.IsEnabled = true; }
        }
    }
}
