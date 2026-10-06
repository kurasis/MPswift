using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Player.App.Controls;

public enum AppIconKind { Settings, Minimize, Maximize, Close, Previous, Stop, Play, Pause, Next, Add, More, Delete, Help, Search }

/// <summary>One 18-DIP vector canvas for action icons, independent of font glyph metrics.</summary>
public sealed class AppIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(AppIconKind), typeof(AppIcon),
        new FrameworkPropertyMetadata(AppIconKind.Play, FrameworkPropertyMetadataOptions.AffectsRender));
    public AppIconKind Kind { get => (AppIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private static readonly IReadOnlyDictionary<AppIconKind, Geometry> Shapes = new Dictionary<AppIconKind, string>
    {
        [AppIconKind.Settings] = "M7,1 L11,1 11.5,3 13,4 15,3.5 17,7 15.5,8.5 15.5,10 17,11 15,14.5 13,14 11.5,15 11,17 7,17 6.5,15 5,14 3,14.5 1,11 2.5,10 2.5,8.5 1,7 3,3.5 5,4 6.5,3 Z M12,9 A3,3 0 1 1 6,9 A3,3 0 1 1 12,9",
        [AppIconKind.Minimize] = "M2,9 L16,9",
        [AppIconKind.Maximize] = "M2,2 L16,2 16,16 2,16 Z",
        [AppIconKind.Close] = "M3,3 L15,15 M15,3 L3,15",
        [AppIconKind.Previous] = "M2,2 L4,2 4,16 2,16 Z M16,2 L6,9 16,16 Z",
        [AppIconKind.Stop] = "M2,2 L16,2 16,16 2,16 Z",
        [AppIconKind.Play] = "M3,1 L16,9 3,17 Z",
        [AppIconKind.Pause] = "M3,1 L7,1 7,17 3,17 Z M11,1 L15,1 15,17 11,17 Z",
        [AppIconKind.Next] = "M14,2 L16,2 16,16 14,16 Z M2,2 L12,9 2,16 Z",
        [AppIconKind.Add] = "M9,2 L9,16 M2,9 L16,9",
        [AppIconKind.More] = "M3,8 A1,1 0 1 1 3,10 A1,1 0 1 1 3,8 M9,8 A1,1 0 1 1 9,10 A1,1 0 1 1 9,8 M15,8 A1,1 0 1 1 15,10 A1,1 0 1 1 15,8",
        [AppIconKind.Delete] = "M2,4 L16,4 M6,4 L6,1.5 12,1.5 12,4 M4,4 L5,16 13,16 14,4 M7,7 L7.5,13 M11,7 L10.5,13",
        [AppIconKind.Help] = "M17,9 A8,8 0 1 1 1,9 A8,8 0 1 1 17,9 M6.5,6 A2.5,2.5 0 0 1 11.5,6 C11.5,8 9,8 9,10 M9,12.5 L9,13",
        [AppIconKind.Search] = "M13.5,7.5 A6,6 0 1 1 1.5,7.5 A6,6 0 1 1 13.5,7.5 M12,12 L16.5,16.5"
    }.ToDictionary(pair => pair.Key, pair => { var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry; });

    public AppIcon()
    {
        Width = Height = 18; Focusable = false; IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var filled = Kind is AppIconKind.Play or AppIconKind.Pause or AppIconKind.Stop or AppIconKind.Previous or AppIconKind.Next or AppIconKind.More;
        drawingContext.DrawGeometry(filled ? Foreground : null, filled ? null : new Pen(Foreground, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Shapes[Kind]);
    }
}
