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
        Content = new TextBox {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        void Refresh()
        {
            Title = Strings.Help;
            ((TextBox)Content).Text = ProductInfo.Name + " " + ProductInfo.Version + "\n\n" + Strings.HelpText + "\n\n" +
            Strings.Get("DataLocation") + ": " + dataDirectory + "\n" + Strings.Get("LogLocation") + ": " + System.IO.Path.Combine(dataDirectory, "Logs") + "\n\n" + Strings.Get("LocalNotices");
        }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(LocalizedStrings.Culture)) Refresh(); }
        LocalizedStrings.Instance.PropertyChanged += Changed;
        Closed += (_, _) => LocalizedStrings.Instance.PropertyChanged -= Changed;
        Refresh();
    }
}
