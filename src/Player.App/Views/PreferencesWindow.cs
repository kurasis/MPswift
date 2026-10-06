using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;
using Player.App.ViewModels;

namespace Player.App.Views;

public sealed class PreferencesWindow : Window
{
    public PreferencesWindow(Window owner, PlayerViewModel model)
    {
        Owner = owner; Title = Strings.PreferencesTitle; Width = 480; Height = 300; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = Strings.Language, TextWrapping = TextWrapping.Wrap });
        var language = new ComboBox { ItemsSource = new[] { "English", "Русский" }, SelectedIndex = model.WindowSettings.Language == "ru" ? 1 : 0, Margin = new Thickness(0, 8, 0, 16) };
        System.Windows.Automation.AutomationProperties.SetName(language, Strings.Language); panel.Children.Add(language);
        var tray = new CheckBox { Content = Strings.CloseToTray, IsChecked = model.WindowSettings.CloseToTray }; panel.Children.Add(tray);
        panel.Children.Add(new TextBlock { Text = Strings.CloseTrayHelp, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
        var apply = new Button { Content = Strings.Apply }; panel.Children.Add(apply);
        apply.Click += async (_, _) =>
        {
            try
            {
                model.WindowSettings = model.WindowSettings with { Language = language.SelectedIndex == 1 ? "ru" : "en", CloseToTray = tray.IsChecked == true };
                await model.SaveNowAsync(); model.Message = Strings.RestartLanguage; Close();
            }
            catch (Exception error) { model.Message = Strings.SaveFailed; model.Details = error.Message; }
        };
    }
}
