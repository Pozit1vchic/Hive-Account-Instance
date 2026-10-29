using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;
using System.Globalization;
namespace Hive.App;
// The bundled SVGs use a deliberately small, auditable subset: a 24x24 viewBox and stroked paths.
public sealed class SvgIcon : FrameworkElement
{
    private static readonly Dictionary<string, Geometry[]> Cache = [];
    public static readonly DependencyProperty NameKeyProperty = DependencyProperty.Register(nameof(NameKey), typeof(string), typeof(SvgIcon), new FrameworkPropertyMetadata("hex", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(SvgIcon), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public string NameKey
    {
        get => (string)GetValue(NameKeyProperty); set => SetValue(NameKeyProperty, value);
    }
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value);
    }
    private static Geometry? ParseShape(XElement shape)
    {
        double N(string name, double fallback = 0) => double.TryParse(shape.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        return shape.Name.LocalName switch
        {
            "path" => Geometry.Parse(shape.Attribute("d")!.Value),
            "circle" => new EllipseGeometry(new Point(N("cx"), N("cy")), N("r"), N("r")),
            "ellipse" => new EllipseGeometry(new Point(N("cx"), N("cy")), N("rx"), N("ry")),
            "rect" => new RectangleGeometry(new Rect(N("x"), N("y"), N("width"), N("height")), N("rx"), N("ry", N("rx"))),
            "line" => new LineGeometry(new Point(N("x1"), N("y1")), new Point(N("x2"), N("y2"))),
            "polyline" or "polygon" => Geometry.Parse("M" + shape.Attribute("points")!.Value + (shape.Name.LocalName == "polygon" ? "Z" : "")),
            _ => null
        };
    }
    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(18, availableSize.Width), Math.Min(18, availableSize.Height));
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        if (!Cache.TryGetValue(NameKey, out var geometries))
        {
            var resource = Application.GetResourceStream(new Uri($"/Hive.App;component/Assets/Icons/{NameKey}.svg", UriKind.Relative)) ?? throw new InvalidOperationException("Unknown SVG icon.");
            using var stream = resource.Stream;
            var document = XDocument.Load(stream);
            geometries = document.Descendants().Select(ParseShape).OfType<Geometry>().ToArray();
            foreach (var geometry in geometries)
                geometry.Freeze();
            Cache.Add(NameKey, geometries);
        }
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        var pen = new Pen(Foreground, Math.Min(ActualWidth, ActualHeight) >= 18 ? 1.7 : 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var matrix = new Matrix(scale, 0, 0, scale, (ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2);
        foreach (var geometry in geometries)
        {
            var drawing = geometry.Clone(); drawing.Transform = new MatrixTransform(matrix);
            context.DrawGeometry(null, pen, drawing);
        }
    }
}
public static class Motion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);
}
