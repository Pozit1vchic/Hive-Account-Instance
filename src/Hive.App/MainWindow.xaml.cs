using System.ComponentModel;
using System.IO;
using System.Net.Http;
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
    private readonly Hive.Core.INetworkLog? networkLog;
    private readonly HiveDatabase? database;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer watcher = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool closing;
    private bool shutdownStarted;
    public MainWindow(AccountsViewModel model, RamImportService? importer = null, Hive.Core.INetworkLog? networkLog = null, HiveDatabase? database = null)
    {
        this.model = model;
        this.importer = importer;
        this.networkLog = networkLog;
        this.database = database;
        InitializeComponent();
        DataContext = model;
        AccountsTable.PreviewMouseRightButtonDown += (_, e) =>
        {
            var element = e.OriginalSource as DependencyObject;
            while (element is not null && element is not DataGridRow)
                element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            if (element is DataGridRow { Item: AccountRow account })
            {
                if (!Equals(AccountsTable.SelectedItem, account)) AccountsTable.SelectedItem = account;
                model.Selected = account;
            }
        };

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
            await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token);
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
    private void ShowTools(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry("Browser profiles", "browser", (_, _) => new ProfilesWindow(model) { Owner = this }.Show()));
        menu.Items.Add(MenuEntry("Launches", "queue", (_, _) => new LaunchesWindow(model) { Owner = this }.Show()));
        menu.Items.Add(MenuEntry("Account settings on Roblox", "settings", async (_, _) => await model.OpenAccountSettingsAsync()));
        menu.Items.Add(MenuEntry("Password and security", "shield", async (_, _) => await model.OpenAccountSettingsAsync("security")));
        menu.Items.Add(MenuEntry("Account privacy", "eye", async (_, _) => await model.OpenAccountSettingsAsync("privacy")));
        menu.Items.Add(MenuEntry("Import RAM", "import", ImportRam));
        menu.Items.Add(MenuEntry("Check sessions", "refresh", (_, _) => model.CheckCommand.Execute(null)));
        menu.Items.Add(MenuEntry("Check Roblox presence", "users", CheckPresence));
        menu.Items.Add(MenuEntry("Follow user by username", "users", FollowByUsername));
        menu.Items.Add(MenuEntry("Network log", "queue", (_, _) => { if (networkLog is not null) new NetworkLogWindow(networkLog) { Owner = this }.Show(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Export visible accounts (metadata)", "save", ExportMetadata));
        menu.Items.Add(MenuEntry("Back up account database", "shield", BackupAccounts));
        menu.Items.Add(MenuEntry("Close selected games", "stop", async (_, _) => await model.CloseGamesAsync(SelectedAccountIds())));
        OpenMenu(menu, (FrameworkElement)sender);
    }
    private async void CheckPresence(object? sender, RoutedEventArgs e)
    {
        var account = model.Selected;
        if (account?.Data.RobloxUserId is not > 0) { model.Status = "Select an account with a verified Roblox user ID."; return; }
        try
        {
            var presence = await new Hive.Roblox.PresenceClient(networkLog: networkLog).GetPresenceAsync(account.Data.RobloxUserId.Value, lifetime.Token);
            model.Status = $"{account.Username}: {presence}";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { model.Status = "Could not check Roblox presence."; }
    }
    private async void FollowByUsername(object? sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("Follow user", "Roblox username", "Find") { Owner = this };
        if (prompt.ShowDialog() != true) return;
        try
        {
            var id = await new Hive.Roblox.UserLookupClient(networkLog: networkLog).FindIdAsync(prompt.Value, lifetime.Token);
            if (id is null) { model.Status = "Roblox user not found."; return; }
            model.LaunchMode = Hive.Core.LaunchKind.FollowUser;
            model.LaunchTargetId = id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            model.Status = "User found. Select an account and launch.";
        }
        catch (Hive.Core.RobloxLaunchException ex) { model.Status = ex.Message; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or ArgumentException) { model.Status = "Could not find the Roblox user."; }
    }
    private async void BackupAccounts(object sender, RoutedEventArgs e)
    {
        if (database is null)
            return;
        var picker = new SaveFileDialog { Title = "Back up encrypted account database", FileName = $"Hive-backup-{DateTime.Now:yyyyMMdd}.db", Filter = "SQLite backup (*.db)|*.db" };
        if (picker.ShowDialog(this) != true)
            return;
        try
        {
            await database.BackupAsync(picker.FileName);
            model.Status = "Encrypted account backup saved.";
        }
        catch (Exception) { model.Status = "Could not save the backup. Choose another file."; }
    }
    private async void ExportMetadata(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = "Export account metadata", FileName = "Hive-accounts.json", Filter = "JSON (*.json)|*.json" };
        if (picker.ShowDialog(this) != true)
            return;
        try
        {
            var items = model.Accounts.Select(row => new { row.Username, Alias = row.Data.DisplayName, Description = row.Notes, row.Data.RobloxUserId });
            await File.WriteAllTextAsync(picker.FileName, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
            model.Status = "Account metadata exported.";
        }
        catch (Exception) { model.Status = "Could not export account metadata."; }
    }
    private IReadOnlyList<string> SelectedAccountIds() => AccountsTable.SelectedItems.OfType<AccountRow>().Select(a => a.Id).ToList();
    private async void LaunchSelectedAccounts(object sender, RoutedEventArgs e) => await model.LaunchAccountsAsync(SelectedAccountIds());
    private void ShowFavorites(object sender, RoutedEventArgs e) => model.FilterMode = "Favorites";
    private async void ToggleFavorite(object sender, RoutedEventArgs e) => await model.ToggleFavoriteAsync();
    private static MenuItem MenuEntry(string text, string icon, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = text, Icon = new SvgIcon { NameKey = icon, Width = 16, Height = 16 } };
        item.Click += click;
        return item;
    }
    private void OpenMenu(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private void ShowLaunches(object sender, RoutedEventArgs e) => new LaunchesWindow(model) { Owner = this }.Show();
    private void ShowLaunchMode(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<Hive.Core.LaunchKind>())
        {
            var title = kind switch { Hive.Core.LaunchKind.PrivateServer => "Private server", Hive.Core.LaunchKind.FollowUser => "Follow user", _ => kind.ToString() };
            menu.Items.Add(MenuEntry(title, "play", (_, _) => model.LaunchMode = kind));
        }
        OpenMenu(menu, (FrameworkElement)sender);
    }
    private void ShowSavedPlaces(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry("Save current place", "save", (_, _) => model.SaveCurrentPlace()));
        menu.Items.Add(MenuEntry("Save target for selected accounts", "save", async (_, _) => await model.SetAccountTargetAsync(SelectedAccountIds(), false)));
        menu.Items.Add(MenuEntry("Clear selected account targets", "trash", async (_, _) => await model.SetAccountTargetAsync(SelectedAccountIds(), true)));
        if (model.RecentPlaceIds.Count > 0)
        {
            var recent = new MenuItem { Header = "Recent places" };
            foreach (var id in model.RecentPlaceIds.ToList())
                recent.Items.Add(MenuEntry(id.ToString(System.Globalization.CultureInfo.InvariantCulture), "play", (_, _) => model.LoadRecentPlace(id)));
            recent.Items.Add(new Separator());
            recent.Items.Add(MenuEntry("Clear history", "trash", (_, _) => model.ClearRecentPlaces()));
            menu.Items.Add(recent);
        }
        if (model.SavedPlaces.Count > 0)
            menu.Items.Add(new Separator());
        foreach (var saved in model.SavedPlaces.ToList())
            menu.Items.Add(MenuEntry(saved.Kind + " / " + saved.PlaceId, "play", (_, _) => model.LoadSavedPlace(saved)));
        if (model.SavedPlaces.Count > 0)
        {
            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Remove saved target" };
            foreach (var saved in model.SavedPlaces.ToList())
                remove.Items.Add(MenuEntry(saved.Kind + " / " + saved.PlaceId, "trash", (_, _) => model.RemoveSavedPlace(saved)));
            menu.Items.Add(remove);
        }
        OpenMenu(menu, (FrameworkElement)sender);
    }
    private async void CreateGroup(object? sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("New group", "Group name") { Owner = this };
        if (prompt.ShowDialog() == true)
            await model.CreateGroupAsync(prompt.Value);
    }
    private void ShowGroups(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry("All groups", "users", (_, _) => model.SelectedGroup = null));
        foreach (var group in model.Groups.ToList())
            menu.Items.Add(MenuEntry(group.Name, "group", (_, _) => model.SelectedGroup = group));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("New group", "plus", CreateGroup));
        if (model.SelectedGroup is not null)
            menu.Items.Add(MenuEntry("Delete group", "trash", async (_, _) => await model.DeleteSelectedGroupAsync()));
        OpenMenu(menu, (FrameworkElement)sender);
    }
    private void ShowMoveGroups(object sender, RoutedEventArgs e)
    {
        var ids = SelectedAccountIds();
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry("Ungrouped", "users", async (_, _) => await model.MoveToGroupAsync(null, ids)));
        foreach (var group in model.Groups.ToList())
            menu.Items.Add(MenuEntry(group.Name, "group", async (_, _) => await model.MoveToGroupAsync(group.Id, ids)));
        if (model.Groups.Count == 0)
            menu.Items.Add(MenuEntry("New group", "plus", CreateGroup));
        OpenMenu(menu, AccountsTable);
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
    private async void OpenAddDialog(string mode)
    {
        if (model.IsBusy)
            return;
        if (mode == "Manual")
        {
            await ((AsyncCommand)model.AddBrowserCommand).ExecuteAsync();
            return;
        }
        var dialog = new AddAccountsWindow(mode, model.EffectiveMotion) { Owner = this };
        dialog.SubmitAsync = async () =>
        {
            if (mode == "Manual")
                await ((AsyncCommand)model.AddBrowserCommand).ExecuteAsync();
            else if (mode == "Credentials")
                await model.AddDirectCredentialsAsync(dialog.Credentials);
            else if (mode == "Cookies")
                await model.AddCookiesAsync(dialog.Cookies);
            else
                await model.AddCustomAsync(dialog.StartUrl!, dialog.Script);
            dialog.SetProgress(model.Status, model.LastAddedCount == 0);
            if (mode == "Credentials")
            {
                var complete = model.LastDirectCompleted.Count == dialog.Credentials.Count;
                if (!complete) dialog.RemoveCompletedCredentials(model.LastDirectCompleted);
                return complete && dialog.Credentials.Count > 0;
            }
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
    private void EditAccountFields(object sender, RoutedEventArgs e)
    {
        if (model.Selected is null || database is null) return;
        new AccountFieldsWindow(new AccountFieldsStore(database, new Hive.Infrastructure.DpapiSecretProtector()), model.Selected.Id) { Owner = this }.ShowDialog();
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(model) { Owner = this };
        settings.ShowDialog();
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private async void CopyUsername(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("username");
    private async void CopyPassword(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("password");
    private async void CopyCombo(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("combo");
    private async void CopyCookie(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("cookie");
    private async void CopyAlias(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("alias");
    private async void CopyUserId(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("userId");
    private async void CopyProfile(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("profile");
    private async void CopyDescription(object sender, RoutedEventArgs e) => await CopyAccountValueAsync("description");
    private async Task CopyAccountValueAsync(string kind)
    {
        var account = model.Selected;
        if (account is null)
            return;
        try
        {
            var value = await model.ReadCopyValueAsync(account, kind);
            if (string.IsNullOrEmpty(value)) { model.Status = "No saved value for this account."; return; }
            Clipboard.SetText(value);
            model.Status = "Copied to clipboard.";
        }
        catch (System.Runtime.InteropServices.COMException) { model.Status = "Clipboard is busy. Try again."; }
        catch (Exception) { model.Status = "Could not read the saved value."; }
    }
    private async void ImportRam(object sender, RoutedEventArgs e)
    {
        if (importer is null)
        {
            model.Status = "Import is unavailable.";
            return;
        }
        var picker = new OpenFileDialog { Title = "Import RAM account data", Filter = "RAM account data (*.json;*.backup)|*.json;*.backup|All files (*.*)|*.*", CheckFileExists = true };
        if (picker.ShowDialog(this) != true)
            return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 16 * 1024 * 1024)
            {
                model.Status = "Choose a JSON file smaller than 16 MB.";
                return;
            }
            var data = await File.ReadAllBytesAsync(picker.FileName);
            string json;
            if (Hive.Infrastructure.RamDataDecoder.IsPasswordLocked(data))
            {
                var prompt = new RamPasswordWindow { Owner = this };
                if (prompt.ShowDialog() != true) { prompt.Forget(); return; }
                var password = prompt.Password;
                prompt.Forget();
                try { json = await Task.Run(() => Hive.Infrastructure.RamDataDecoder.Decode(data, password)); }
                finally { password = ""; }
            }
            else json = Hive.Infrastructure.RamDataDecoder.Decode(data);
            var items = RamImportService.Parse(json);
            var preview = new RamImportWindow(items) { Owner = this };
            if (preview.ShowDialog() != true)
                return;
            await model.ImportRamAsync(importer, items);
        }
        catch (JsonException) { model.Status = "This file is not readable RAM JSON. Export/decrypt it in RAM first."; }
        catch (FormatException error) { model.Status = error.Message; }
        catch (Exception) { model.Status = "Import could not finish. Existing accounts have not been replaced."; }
    }
}
