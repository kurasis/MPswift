using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Player.App.Controls;

internal sealed class PlaylistInsertionAdorner(UIElement adornedElement, double x, Brush brush) : Adorner(adornedElement)
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        var edge = Math.Clamp(x, 1, Math.Max(1, AdornedElement.RenderSize.Width - 1));
        drawingContext.DrawLine(new Pen(brush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            new Point(edge, 3), new Point(edge, Math.Max(3, AdornedElement.RenderSize.Height - 3)));
    }
}
