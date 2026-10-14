using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Player.App.Controls;

internal sealed class AlbumInsertionAdorner(UIElement element, double y, Brush brush) : Adorner(element)
{
    protected override void OnRender(DrawingContext context)
    {
        var edge = Math.Clamp(y, 1, Math.Max(1, AdornedElement.RenderSize.Height - 1));
        context.DrawLine(new Pen(brush, 2), new Point(8, edge), new Point(Math.Max(8, AdornedElement.RenderSize.Width - 24), edge));
    }
}
