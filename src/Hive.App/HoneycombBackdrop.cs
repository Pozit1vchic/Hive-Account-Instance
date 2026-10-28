using System.Windows;
using System.Windows.Media;
namespace Hive.App;

public sealed class HoneycombBackdrop : FrameworkElement
{
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        const double radius = 25;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(233, 204, 142)), 0.8);
        for (var column = 0; column < 9; column++)
            for (var row = 0; row < 4; row++)
            {
                var x = ActualWidth - 280 + column * radius * 1.5;
                var y = -28 + row * radius * 1.732 + (column % 2) * radius * .866;
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new Point(x + radius, y), true, true);
                    for (var edge = 1; edge < 6; edge++)
                    {
                        var angle = edge * Math.PI / 3;
                        path.LineTo(new Point(x + radius * Math.Cos(angle), y + radius * Math.Sin(angle)), true, false);
                    }
                }
                var fill = (column + row) % 7 == 0 ? new SolidColorBrush(Color.FromArgb(52, 239, 192, 85)) : Brushes.Transparent;
                context.DrawGeometry(fill, pen, geometry);
            }
        context.Pop();
    }
}
