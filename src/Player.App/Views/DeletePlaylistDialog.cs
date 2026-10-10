using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;

namespace Player.App.Views;

internal sealed class DeletePlaylistDialog : Window
{
    internal Button CancelButton { get; }
    internal Button DeleteButton { get; }

    internal DeletePlaylistDialog(Window owner, string name, int count)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Title = Strings.Get("DeletePlaylist"); Width = 440; MinWidth = 340;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = string.Format(Strings.Culture, Strings.Get("DeletePlaylistPrompt"), name, count),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        CancelButton = new Button { Content = Strings.Get("Cancel"), IsDefault = true, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DeleteButton = new Button { Content = Strings.Get("DeletePlaylist") };
        buttons.Children.Add(CancelButton); buttons.Children.Add(DeleteButton);
        CancelButton.Click += (_, _) => DialogResult = false;
        DeleteButton.Click += (_, _) => DialogResult = true;
        Loaded += (_, _) => CancelButton.Focus();
    }
}
