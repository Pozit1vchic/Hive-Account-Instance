using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Hive.App;

public partial class SettingsWindow : Window
{
    private readonly AccountsViewModel model;
    public SettingsWindow(AccountsViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        UiMotion.Attach(this, model.EffectiveMotion);
    }
    private void AnimatePage(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !ReferenceEquals(e.Source, sender))
            return;
        var tabs = (TabControl)sender;
        if (tabs.Template.FindName("PART_SelectedContentHost", tabs) is FrameworkElement content)
            UiMotion.Reveal(content, model.EffectiveMotion);
    }
    private void SelectTheme(object sender, RoutedEventArgs e) => model.DarkTheme = ((Button)sender).Tag?.ToString() == "Dusk";
    private void SelectAccent(object sender, RoutedEventArgs e) => model.Accent = ((Button)sender).Tag?.ToString() ?? "Honey";
    private void ChooseBrowser(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a Chromium browser", Filter = "Browser executable (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true)
            model.BrowserPath = dialog.FileName;
    }
    private void OpenDataFolder(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
    private void CloseSettings(object sender, RoutedEventArgs e) => Close();
}
