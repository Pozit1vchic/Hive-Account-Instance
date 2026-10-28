using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Hive.App;

public static class UiMotion
{
    public static void Reveal(FrameworkElement element, bool enabled)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        var shift = new TranslateTransform();
        element.RenderTransform = shift;
        if (!enabled)
        {
            element.Opacity = 1;
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    public static void Attach(Window window, bool enabled) => window.Loaded += (_, _) => Reveal((FrameworkElement)window.Content, enabled);
}
