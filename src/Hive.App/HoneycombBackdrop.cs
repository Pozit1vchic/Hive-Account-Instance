using System.Windows;
using System.Windows.Media;
namespace Hive.App;

public sealed class HoneycombBackdrop : FrameworkElement
{
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        const double radius = 32;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
            for (var column = -1; column * radius * 1.5 < ActualWidth + radius; column++)
                for (var row = -1; row * radius * Math.Sqrt(3) < ActualHeight + radius; row++)
                {
                    var x = column * radius * 1.5;
                    var y = row * radius * Math.Sqrt(3) + (column & 1) * radius * Math.Sqrt(3) / 2;
                    path.BeginFigure(new Point(x + radius, y), false, true);
                    for (var edge = 1; edge < 6; edge++)
                    {
                        var angle = edge * Math.PI / 3;
                        path.LineTo(new Point(x + radius * Math.Cos(angle), y + radius * Math.Sin(angle)), true, false);
                    }
                }
        geometry.Freeze();
        context.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(217, 167, 76)), .7), geometry);
        context.Pop();
    }
}
