using System.Windows;
using System.Windows.Media;
namespace Hive.App;

public static class ThemeManager
{
    public static void Apply(AppPreferences preferences)
    {
        if (Application.Current is null)
            return;
        var resources = Application.Current.Resources;
        var dark = preferences.DarkTheme;
        void Set(string key, string hex) => resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        Set("CanvasBrush", dark ? "#151620" : "#F4F5F8");
        Set("SurfaceBrush", dark ? "#222331" : "#FFFFFF");
        Set("SubtleBrush", dark ? "#2A2C3B" : "#F8F9FC");
        Set("InputBrush", dark ? "#1B1C29" : "#FCFCFE");
        Set("InkBrush", dark ? "#F0EDF8" : "#29313F");
        Set("MutedBrush", dark ? "#A8A7BD" : "#828896");
        Set("LineBrush", dark ? "#383A4D" : "#E7E9F0");
        Set("SelectedBrush", dark ? "#3C3442" : "#FFF4DD");
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
