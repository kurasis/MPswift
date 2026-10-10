using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;
using Player.App.Controls;
using System.Windows.Automation;

namespace Player.App.Views;

public sealed class PlaylistNameDialog : Window
{
    private readonly TextBox _name;
    internal TextBox NameBox => _name;
    internal Button SaveButton { get; }
    internal TextBlock ValidationText { get; } = AccessibleStatus.Create();
    public string PlaylistName => _name.Text.Trim();
    public PlaylistNameDialog(Window owner, string current)
    {
        Owner = owner; Title = Strings.Get("PlaylistName"); Width = 380; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(16) };
        _name = new TextBox { Text = current, MaxLength = 200, Margin = new Thickness(0, 0, 0, 12) };
        var label = new Label { Content = Strings.Get("PlaylistName"), Target = _name, Padding = new Thickness(0, 0, 0, 6) };
        label.MouseLeftButtonDown += (_, _) => _name.Focus();
        AutomationProperties.SetLabeledBy(_name, label); AutomationProperties.SetName(_name, Strings.Get("PlaylistName"));
        panel.Children.Add(label); panel.Children.Add(_name);
        ValidationText.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(ValidationText);
        SaveButton = new Button { Content = Strings.Get("SaveName"), IsDefault = true };
        SaveButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_name.Text)) { DialogResult = true; return; }
            var error = Strings.Get("PlaylistNameRequired"); AutomationProperties.SetHelpText(_name, error);
            AccessibleStatus.Update(ValidationText, error); _name.Focus();
        };
        _name.TextChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(_name.Text)) { AccessibleStatus.Update(ValidationText, ""); AutomationProperties.SetHelpText(_name, ""); } };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Strings.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false; buttons.Children.Add(cancel); buttons.Children.Add(SaveButton);
        panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
