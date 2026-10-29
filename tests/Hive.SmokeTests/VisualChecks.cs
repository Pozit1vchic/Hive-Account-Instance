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
        var app = new Hive.App.App(false);
        app.InitializeComponent();
        var vm = PreviewFixtures.CreateViewModel(Path.Combine(folder, "preview-preferences.json"));
        ThemeManager.Apply(new AppPreferences { AnimationsEnabled = false });
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
        var resizeProbe = new Window { Width = 1200, Height = 800, Title = "Hive window sizing check", Owner = window };
        resizeProbe.Show();
        if (!Hive.Infrastructure.RobloxWindowSizer.ApplyCompactBounds(new System.Windows.Interop.WindowInteropHelper(resizeProbe).Handle))
            throw new Exception("Compact native window bounds failed");
        resizeProbe.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var dpi = VisualTreeHelper.GetDpi(resizeProbe);
        if (Math.Abs(resizeProbe.ActualWidth * dpi.DpiScaleX - 800) > 2 || Math.Abs(resizeProbe.ActualHeight * dpi.DpiScaleY - 500) > 2)
            throw new Exception("Native compact bounds have the wrong dimensions");
        if (!Hive.Infrastructure.RobloxWindowSizer.ApplyCompactBounds(new System.Windows.Interop.WindowInteropHelper(resizeProbe).Handle, 640, 480)) throw new Exception("Custom window size failed");
        resizeProbe.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        if (Math.Abs(resizeProbe.ActualWidth * dpi.DpiScaleX - 640) > 2 || Math.Abs(resizeProbe.ActualHeight * dpi.DpiScaleY - 480) > 2) throw new Exception("Custom window size was not applied");
        resizeProbe.Close();
        if (((SolidColorBrush)app.Resources["SurfaceBrush"]).Color != Colors.White)
            throw new Exception("Light theme resource is not white");
        Capture(window, "artifacts/accounts-preview.png");
        window.Width = 860;
        window.Height = 540;
        Capture(window, "artifacts/accounts-small-preview.png");
        foreach (var mode in new[] { Hive.Core.LaunchKind.Server, Hive.Core.LaunchKind.PrivateServer, Hive.Core.LaunchKind.FollowUser })
        {
            vm.LaunchMode = mode;
            Capture(window, $"artifacts/target-{mode.ToString().ToLowerInvariant()}.png");
        }
        vm.LaunchMode = Hive.Core.LaunchKind.Place;
        window.Width = 1100;
        window.Height = 700;
        if (window.FindName("PasswordInput") is not null)
            throw new Exception("Permanent credential inspector returned");
        foreach (var mode in new[] { "Manual", "Credentials", "Cookies", "Custom" })
        {
            var add = new AddAccountsWindow(mode, false) { Owner = window };
            add.Show();
            if (mode == "Credentials")
            {
                if (add.FindName("UsernameInput") is not null || add.FindName("PasswordInput") is not null) throw new Exception("Separate credential fields returned");
                var input = (TextBox)add.FindName("BulkInput");
                input.Text = "first:test\nsecond:test";
                add.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                if (input.VerticalContentAlignment != VerticalAlignment.Top || input.GetRectFromCharacterIndex(0).Top > 25) throw new Exception("Multiline credentials are not top aligned");
                add.SubmitAsync = () => Task.FromResult(false);
                ((Button)add.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Continue clears its transient parsed data after the operation; validate parsing separately.
                if (Hive.Core.AccountInputParser.Credentials(input.Text).Count != 2) throw new Exception("Multiline credential parsing failed");
                input.Clear(); add.SetProgress("");
            }
            Capture(add, $"artifacts/add-{mode.ToLowerInvariant()}.png");
            add.Close();
            add.ForgetSecrets();
        }
        var retry = new AddAccountsWindow("Cookies", false) { Owner = window };
        var submitted = 0;
        var completion = new TaskCompletionSource<bool>();
        retry.SubmitAsync = () => { submitted++; return completion.Task; };
        retry.Show();
        ((TextBox)retry.FindName("CookiesInput")).Text = "test-local-cookie";
        var continueButton = (Button)retry.FindName("ContinueButton");
        continueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (submitted != 1 || continueButton.IsEnabled || !retry.IsVisible)
            throw new Exception("Continue did not start the operation or wait for completion");
        completion.SetResult(false);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        if (!retry.IsVisible || !continueButton.IsEnabled)
            throw new Exception("Failed addition closed the dialog or blocked retry");
        var cancelled = false;
        retry.CancelOperation = () => cancelled = true;
        completion = new TaskCompletionSource<bool>();
        continueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)retry.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!cancelled || !retry.IsVisible)
            throw new Exception("In-progress cancellation did not reach onboarding");
        completion.SetResult(false);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        retry.Close();
        vm.BrowserProfiles.Add(new("preview-profile-a","Default profile",Path.Combine(folder,"profiles","a"),"preview-a",DateTimeOffset.UtcNow));
        vm.BrowserProfiles.Add(new("preview-profile-b","Trading profile",Path.Combine(folder,"profiles","b"),"preview-b"));
        var profilesWindow = new ProfilesWindow(vm) { Owner = window }; profilesWindow.Show(); Capture(profilesWindow,"artifacts/profiles-preview.png"); profilesWindow.Close();
        var prompt = new TextPromptWindow("New group", "Group name") { Owner = window }; prompt.Show(); Capture(prompt,"artifacts/text-prompt.png"); prompt.Close();
        var launchesWindow = new LaunchesWindow(vm) { Owner = window }; launchesWindow.Show(); Capture(launchesWindow,"artifacts/launches-preview.png"); launchesWindow.Close();
        var modal = new AddAccountsWindow("Cookies", false) { Owner = window };
        modal.SubmitAsync = () => Task.FromResult(true);
        modal.Loaded += (_, _) => modal.Dispatcher.BeginInvoke(new Action(() => { ((TextBox)modal.FindName("CookiesInput")).Text = "local-confirmation-test"; ((Button)modal.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
        if (modal.ShowDialog() != true)
            throw new Exception("Successful addition did not close with a success result");
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
        settings.Width = 760;
        settings.Height = 540;
        Capture(settings, "artifacts/settings-small-preview.png");
        settings.Width = 800;
        settings.Height = 600;
        var tabs = Find<TabControl>(settings);
        if (tabs is null || tabs.Items.Count != 3)
            throw new Exception("Settings categories are missing");
        for (var i = 1; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            settings.UpdateLayout();
            if (((TabItem)tabs.Items[0]).IsSelected || !((TabItem)tabs.Items[i]).IsSelected)
                throw new Exception("Inactive settings tab remained selected");
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
