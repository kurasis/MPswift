using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;
using Player.Core;

namespace Player.App.Views;

public sealed class HelpWindow : Window
{
    public HelpWindow(Window owner, string dataDirectory)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Title = Strings.Help; Width = 660; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TextBox { Text = ProductInfo.Name + " " + ProductInfo.Version + "\n\n" + Strings.HelpText + "\n\n" +
            Strings.Get("DataLocation") + ": " + dataDirectory + "\n" + Strings.Get("LogLocation") + ": " + System.IO.Path.Combine(dataDirectory, "Logs") + "\n\n" + Strings.Get("LocalNotices"),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
    }
}
