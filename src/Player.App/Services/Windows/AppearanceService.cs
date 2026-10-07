using System.Windows;
using System.Windows.Media;

namespace Player.App.Services.Windows;

internal static class AppearanceService
{
    internal static void Apply(string accent)
    {
        var resources = Application.Current.Resources;
        if (SystemParameters.HighContrast) { resources.Remove("AccentBrush"); resources.Remove("AccentTextBrush"); return; }
        var color = accent switch { "blue" => "#77BCFF", "green" => "#67D6A1", "violet" => "#C5A3FF", _ => "#FFBB45" };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze();
        resources["AccentBrush"] = brush;
        var text = new SolidColorBrush(Color.FromRgb(25, 28, 34)); text.Freeze(); resources["AccentTextBrush"] = text;
        if (Application.Current.MainWindow is Views.MainWindow window && window.FindName("WaveformView") is Controls.WaveformControl waveform) waveform.InvalidateVisual();
    }
}
