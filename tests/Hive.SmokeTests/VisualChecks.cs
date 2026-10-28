using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hive.App;
internal static class VisualChecks
{
    public static void Run(string folder)
    {
        var app = new Hive.App.App();
        app.InitializeComponent();
        var vm = PreviewFixtures.CreateViewModel(Path.Combine(folder, "preview-preferences.json"));
        ThemeManager.Apply(new AppPreferences());
        vm.FilterMode = "Running";
        if (vm.Count != 2)
            throw new Exception("Running filter failed");
        vm.FilterMode = "Attention";
        if (vm.Count != 1)
            throw new Exception("Sign-in filter failed");
        vm.FilterMode = "All";
        vm.Selected = vm.Accounts[0];
        var selected = vm.Selected;
        vm.Search = "Honey";
        if (!ReferenceEquals(vm.Selected, selected))
            throw new Exception("Search cleared the selected account");
        vm.Search = "";
        var bindings = new StringWriter();
        var listener = new TextWriterTraceListener(bindings);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        var window = new MainWindow(vm) { Width = 1100, Height = 700 };
        window.Show();
        Capture(window, "artifacts/accounts-preview.png");
        window.Width = 940;
        window.Height = 580;
        Capture(window, "artifacts/accounts-small-preview.png");
        window.Width = 1100;
        window.Height = 700;
        if (window.FindName("PasswordInput") is not null)
            throw new Exception("Permanent credential inspector returned");
        foreach (var mode in new[] { "Credentials", "Cookies", "Custom" })
        {
            var add = new AddAccountsWindow(mode, false) { Owner = window };
            add.Show();
            Capture(add, $"artifacts/add-{mode.ToLowerInvariant()}.png");
            add.Close();
            add.ForgetSecrets();
        }
        var details = new AccountDetailsWindow(vm) { Owner = window };
        details.Show();
        Capture(details, "artifacts/account-details.png");
        details.Close();
        var animated = new Border();
        UiMotion.Reveal(animated, true);
        if (!animated.HasAnimatedProperties || animated.RenderTransform is not TranslateTransform { HasAnimatedProperties: true })
            throw new Exception("Opening motion has no active animation clocks");
        UiMotion.Reveal(animated, false);
        if (animated.HasAnimatedProperties || animated.Opacity != 1)
            throw new Exception("Reduced-motion path has active animation");
        var settings = new SettingsWindow(vm) { Owner = window };
        settings.Show();
        Capture(settings, "artifacts/accounts-settings-preview.png");
        settings.Width = 880;
        settings.Height = 680;
        Capture(settings, "artifacts/settings-small-preview.png");
        settings.Width = 930;
        settings.Height = 730;
        var tabs = Find<TabControl>(settings);
        if (tabs is null || tabs.Items.Count != 4)
            throw new Exception("Settings categories are missing");
        for (var i = 1; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            settings.UpdateLayout();
            Capture(settings, $"artifacts/settings-{i}.png");
        }
        tabs.SelectedIndex = 0;
        vm.DarkTheme = true;
        vm.Accent = "Iris";
        Capture(settings, "artifacts/settings-dusk-preview.png");
        settings.Close();
        Capture(window, "artifacts/accounts-dusk-preview.png");
        vm.DarkTheme = false;
        vm.Accent = "Honey";
        var import = new RamImportWindow([new("ExampleAccount", "Example alias", "Imported description", "never-render-this-password", "never-render-this-cookie")]) { Owner = window };
        import.Show();
        Capture(import, "artifacts/import-preview.png");
        import.Close();
        PresentationTraceSources.DataBindingSource.Flush();
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        if (bindings.ToString().Length > 0)
            throw new Exception("WPF binding errors: " + bindings.ToString());
        window.Close();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        app.Shutdown();
        Console.WriteLine("PASS: running/sign-in filters, stable selection, all settings tabs, light/dusk themes, minimum-size layout, SVG rendering, import preview, no WPF binding errors.");
    }
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = Find<T>(VisualTreeHelper.GetChild(root, i));
            if (result is not null)
                return result;
        }
        return null;
    }
    private static void Capture(Window window, string path)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
    }
}
