using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Hive.App;

public static class ThemeManager
{
    public static void Apply(AppPreferences preferences)
    {
        if (Application.Current is null)
            return;
        var resources = Application.Current.Resources;
        var dark = preferences.DarkTheme;
        var animate = preferences.AnimationsEnabled && SystemParameters.ClientAreaAnimation;
        void Set(string key, string hex)
        {
            var next = (Color)ColorConverter.ConvertFromString(hex);
            if (resources[key] is SolidColorBrush current && !current.IsFrozen)
            {
                var previous = current.Color;
                current.BeginAnimation(SolidColorBrush.ColorProperty, null);
                current.Color = next;
                if (animate && previous != next)
                    current.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(previous, next, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
            }
            else
                resources[key] = new SolidColorBrush(next);
        }
        Set("CanvasBrush", dark ? "#151923" : "#F4F6FA");
        Set("SurfaceBrush", dark ? "#212838" : "#FFFFFF");
        Set("SubtleBrush", dark ? "#293347" : "#F6F8FC");
        Set("InputBrush", dark ? "#191F2D" : "#FAFBFE");
        Set("InkBrush", dark ? "#F0EDF8" : "#29313F");
        Set("MutedBrush", dark ? "#A8B4C8" : "#657186");
        Set("LineBrush", dark ? "#354258" : "#E1E6EF");
        resources["GlassBrush"] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(dark ? "#DE36425B" : "#E8FFFFFF"),
            (Color)ColorConverter.ConvertFromString(dark ? "#C2222939" : "#CDEAF0F9"), 105);
        Set("SelectedBrush", preferences.Accent switch
        {
            "Iris" => dark ? "#3A3554" : "#EFEAFB",
            "Ocean" => dark ? "#253F4B" : "#E4F3F6",
            _ => dark ? "#443B2C" : "#FFF3DC"
        });
        Set("GreenBrush", dark ? "#7ADBAA" : "#25885D");
        Set("RedBrush", dark ? "#F699A0" : "#C6505C");
        var colors = preferences.Accent switch
        {
            "Iris" => ("#9B83E9", "#7457C5", "#FFFFFF"),
            "Ocean" => ("#55B9C5", "#338692", "#FFFFFF"),
            _ => ("#F2CA76", "#DFA740", "#453012")
        };
        resources["AccentColor"] = (Color)ColorConverter.ConvertFromString(colors.Item2);
        Set("AccentBrush", colors.Item2);
        Set("AccentInkBrush", colors.Item3);
        resources["PrimaryBrush"] = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(colors.Item1), (Color)ColorConverter.ConvertFromString(colors.Item2), 90);
        resources["ControlRadius"] = new CornerRadius(8);
    }
}
