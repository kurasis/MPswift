using Player.App.Controls;
using System.Windows;
using System.Windows.Controls;
using Player.App.Resources;

namespace Player.App.Views;

public sealed class DiagnosticsWindow : Window
{
    internal TextBox Preview { get; }
    internal Button CopyButton { get; }
    public DiagnosticsWindow(Window owner, string redactedReport)
    {
        SetResourceReference(StyleProperty, typeof(Window)); Owner = owner;
        Title = Strings.Get("DiagnosticsTitle"); Width = 700; Height = 580; MinWidth = 460; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var hint = new TextBlock { Text = Strings.Get("DiagnosticsPreviewHelp"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        CopyButton = IconActionButton.Create("CopyDiagnostics", AppIconKind.Copy); buttons.Children.Add(CopyButton);
        var close = IconActionButton.Create("Close", AppIconKind.Close); close.IsCancel = true; buttons.Children.Add(close);
        close.Click += (_, _) => Close();
        Preview = new TextBox { Text = redactedReport, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(Preview, Strings.Get("DiagnosticsTitle")); root.Children.Add(Preview);
        CopyButton.Click += (_, _) =>
        {
            try { Clipboard.SetText(Preview.Text); CopyButton.ToolTip = Strings.Get("DiagnosticsCopied"); System.Windows.Automation.AutomationProperties.SetHelpText(CopyButton, Strings.Get("DiagnosticsCopied")); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, Strings.Get("ClipboardUnavailable"), Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }
}
