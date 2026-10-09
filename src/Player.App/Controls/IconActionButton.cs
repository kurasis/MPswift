using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Player.App.Resources;

namespace Player.App.Controls;

/// <summary>Consistent icon actions for code-built dialogs, with live localized labels.</summary>
internal static class IconActionButton
{
    internal static Button Create(string key, AppIconKind kind)
    {
        var button = new Button { Content = new AppIcon { Kind = kind }, Margin = new Thickness(3) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
        LocalizedStrings.Bind(button, FrameworkElement.ToolTipProperty, key);
        LocalizedStrings.Bind(button, AutomationProperties.NameProperty, key);
        return button;
    }
}
