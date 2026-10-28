using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;
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
    protected override Size MeasureOverride(Size availableSize) => new(18, 18);
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
            geometries = document.Descendants().Where(e => e.Name.LocalName == "path").Select(e => Geometry.Parse(e.Attribute("d")!.Value)).ToArray();
            foreach (var geometry in geometries)
                geometry.Freeze();
            Cache.Add(NameKey, geometries);
        }
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        var pen = new Pen(Foreground, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        context.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        context.PushTransform(new ScaleTransform(scale, scale));
        foreach (var geometry in geometries)
            context.DrawGeometry(null, pen, geometry);
        context.Pop();
        context.Pop();
    }
}
public static class Motion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);
}
