using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;
using Player.App.ViewModels;

namespace Player.App.Views;

internal sealed class RecycleFilesWindow : Window
{
    internal Button ConfirmButton { get; }
    internal Button CancelButton { get; }
    internal string[] Paths { get; }
    public RecycleFilesWindow(Window owner, IReadOnlyList<PlaylistRowViewModel> rows)
    {
        Paths = rows.Select(row => row.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        SetResourceReference(StyleProperty, typeof(Window)); Owner = owner;
        Title = Strings.Get("RecycleFiles"); Width = 640; Height = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var hint = new TextBlock { Text = string.Format(Strings.Culture, Strings.Get("RecycleFilesConfirm"), Paths.Length), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        if (rows.Any(row => row.Entry.Track.Segment is not null)) hint.Text += "\n\n" + Strings.Get("RecycleCueWarning");
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        CancelButton = new Button { Content = Strings.Get("Cancel"), IsCancel = true, IsDefault = true, Margin = new Thickness(6) };
        ConfirmButton = new Button { Content = Strings.Get("RecycleFiles"), Margin = new Thickness(6) };
        CancelButton.Click += (_, _) => { Close(); };
        ConfirmButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(CancelButton); buttons.Children.Add(ConfirmButton);
        var files = new ListBox { ItemsSource = Paths }; System.Windows.Automation.AutomationProperties.SetName(files, Strings.Get("SelectedFiles")); root.Children.Add(files);
    }
}
