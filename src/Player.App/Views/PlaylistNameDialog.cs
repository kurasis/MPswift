using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;

namespace Player.App.Views;

public sealed class PlaylistNameDialog : Window
{
    private readonly TextBox _name;
    public string PlaylistName => _name.Text.Trim();
    public PlaylistNameDialog(Window owner, string current)
    {
        Owner = owner; Title = Strings.Get("PlaylistName"); Width = 380; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(16) };
        _name = new TextBox { Text = current, MaxLength = 200, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(_name);
        var ok = new Button { Content = Strings.Get("SaveName"), IsDefault = true };
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_name.Text)) DialogResult = true; };
        panel.Children.Add(ok); Content = panel;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
