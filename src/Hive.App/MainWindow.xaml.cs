using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Hive.Storage;
using Microsoft.Win32;
namespace Hive.App;

public partial class MainWindow : Window
{
    private readonly AccountsViewModel model;
    private readonly RamImportService? importer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer watcher = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool closing;
    private bool shutdownStarted;
    public MainWindow(AccountsViewModel model, RamImportService? importer = null)
    {
        this.model = model;
        this.importer = importer;
        InitializeComponent();
        DataContext = model;

        watcher.Tick += (_, _) => model.RefreshActivity();
        Loaded += (_, _) =>
        {
            watcher.Start();
            _ = MonitorSessionsAsync();
            if (model.EffectiveMotion)
            {
                Workspace.RenderTransform = new TranslateTransform();
                Workspace.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
                ((TranslateTransform)Workspace.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        };
        Closing += OnClosing;
        Closed += (_, _) => { watcher.Stop(); lifetime.Cancel(); };
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing)
            return;
        e.Cancel = true;
        if (shutdownStarted)
            return;
        shutdownStarted = true;
        lifetime.Cancel();
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            await model.ShutdownAsync();
            closing = true;
            Close();
        }));
    }
    private async Task MonitorSessionsAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    if (!model.IsBusy)
                        await model.CheckSessionsAsync(ct: lifetime.Token);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception) { model.Status = "Automatic check failed. Retrying in five minutes."; }
                await Task.Delay(TimeSpan.FromMinutes(5), lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    private void ShowAddMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string title, string icon, RoutedEventHandler action)
        {
            var item = new MenuItem { Header = title, Icon = new SvgIcon { NameKey = icon, Width = 16, Height = 16 } };
            item.Click += action;
            menu.Items.Add(item);
        }
        Item("Manual login", "browser", (_, _) => OpenAddDialog("Manual"));
        Item("User:Pass", "key", (_, _) => OpenAddDialog("Credentials"));
        Item("Cookie(s)", "shield", (_, _) => OpenAddDialog("Cookies"));
        menu.Items.Add(new Separator());
        Item("Custom URL + JavaScript", "external", (_, _) => OpenAddDialog("Custom"));
        ((Button)sender).ContextMenu = menu;
        menu.IsOpen = true;
    }
    private void OpenAddDialog(string mode)
    {
        var dialog = new AddAccountsWindow(mode, model.EffectiveMotion) { Owner = this };
        dialog.SubmitAsync = async () =>
        {
            if (mode == "Manual")
                await ((AsyncCommand)model.AddBrowserCommand).ExecuteAsync();
            else if (mode == "Credentials")
                await model.AddCredentialsAsync(dialog.Credentials);
            else if (mode == "Cookies")
                await model.AddCookiesAsync(dialog.Cookies);
            else
                await model.AddCustomAsync(dialog.StartUrl!, dialog.Script);
            dialog.SetProgress(model.Status);
            return model.LastAddedCount > 0;
        };
        dialog.CancelOperation = () => model.CancelLoginCommand.Execute(null);
        void UpdateProgress(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(model.Status))
                dialog.SetProgress(model.Status);
        }
        model.PropertyChanged += UpdateProgress;
        try
        {
            dialog.ShowDialog();
        }
        finally { model.PropertyChanged -= UpdateProgress; dialog.ForgetSecrets(); }
    }
    private void EditSelected(object sender, RoutedEventArgs e)
    {
        if (model.Selected is null || !model.CanEdit)
            return;
        var dialog = new AccountDetailsWindow(model) { Owner = this };
        dialog.ShowDialog();
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(model) { Owner = this };
        settings.ShowDialog();
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void CopyUsername(object sender, RoutedEventArgs e)
    {
        if (model.Selected is null)
            return;
        try
        {
            Clipboard.SetText(model.Selected.Username);
            model.Status = "Username copied.";
        }
        catch (System.Runtime.InteropServices.COMException) { model.Status = "Clipboard is busy. Try again."; }
    }
    private async void ImportRam(object sender, RoutedEventArgs e)
    {
        if (importer is null)
        {
            model.Status = "Import is unavailable.";
            return;
        }
        var picker = new OpenFileDialog { Title = "Import a decrypted RAM AccountData.json", Filter = "RAM account data (*.json)|*.json", CheckFileExists = true };
        if (picker.ShowDialog(this) != true)
            return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 16 * 1024 * 1024)
            {
                model.Status = "Choose a JSON file smaller than 16 MB.";
                return;
            }
            var items = RamImportService.Parse(await File.ReadAllTextAsync(picker.FileName));
            var preview = new RamImportWindow(items) { Owner = this };
            if (preview.ShowDialog() != true)
                return;
            await model.ImportRamAsync(importer, items);
        }
        catch (JsonException) { model.Status = "This file is not readable RAM JSON. Export/decrypt it in RAM first."; }
        catch (FormatException) { model.Status = "Unsupported RAM data. Choose a decrypted account array with valid fields."; }
        catch (Exception) { model.Status = "Import could not finish. Existing accounts have not been replaced."; }
    }
}
