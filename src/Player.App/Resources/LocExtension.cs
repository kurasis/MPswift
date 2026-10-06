using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows;

namespace Player.App.Resources;

public sealed class LocalizedStrings : INotifyPropertyChanged
{
    public static LocalizedStrings Instance { get; } = new();
    public string this[string key] => Strings.Get(key);
    public CultureInfo Culture => Strings.Culture;
    public event PropertyChangedEventHandler? PropertyChanged;
    public static void Bind(DependencyObject target, DependencyProperty property, string key) =>
        BindingOperations.SetBinding(target, property, new Binding("[" + key + "]") { Source = Instance, Mode = BindingMode.OneWay });
    internal void Refresh()
    {
        PropertyChanged?.Invoke(this, new("Item[]"));
        PropertyChanged?.Invoke(this, new(nameof(Culture)));
    }
}

/// <summary>Localized bindings also update templates and detached context-menu popups.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding("[" + key + "]") { Source = LocalizedStrings.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
