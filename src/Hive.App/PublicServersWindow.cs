using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Hive.Core;
using Hive.Roblox;

namespace Hive.App;

public sealed class PublicServersWindow : ToolWindow
{
    private readonly PublicServerClient client;
    private readonly long placeId;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ObservableCollection<PublicServer> servers = [];
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid table = Table(("Server ID", "Id", -1), ("Players", "Playing", 75), ("Capacity", "Capacity", 80), ("FPS", "Fps", 65), ("Ping", "Ping", 65));
    private readonly Button next = new() { Content = "Next page", Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
    private readonly Button refresh = new() { Content = "Refresh" };
    private string? cursor;
    public PublicServer? SelectedServer { get; private set; }
    public PublicServersWindow(long placeId, INetworkLog? networkLog = null) : base("Public servers")
    {
        this.placeId = placeId; client = new PublicServerClient(networkLog: networkLog); Width = 760; Height = 520;
        table.ItemsSource = servers;
        var panel = new DockPanel(); var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var select = new Button { Content = "Use server", Margin = new Thickness(8, 0, 0, 0), Style = (Style)Application.Current.FindResource("PrimaryButton") };
        select.Click += (_, _) => { if (table.SelectedItem is PublicServer server) { SelectedServer = server; DialogResult = true; } };
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is PublicServer server) { SelectedServer = server; DialogResult = true; } };
        refresh.Click += async (_, _) => await LoadPageAsync(null);
        next.Click += async (_, _) => await LoadPageAsync(cursor);
        actions.Children.Add(refresh); actions.Children.Add(next); actions.Children.Add(select); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(status); panel.Children.Add(footer); panel.Children.Add(table); SetBody(panel);
        Loaded += async (_, _) => await LoadPageAsync(null); Closed += (_, _) => lifetime.Cancel();
    }
    private async Task LoadPageAsync(string? pageCursor)
    {
        refresh.IsEnabled = false; next.IsEnabled = false; status.Text = "Loading...";
        try
        {
            var page = await client.GetPageAsync(placeId, pageCursor, lifetime.Token);
            servers.Clear(); foreach (var server in page.Servers) servers.Add(server);
            cursor = page.NextCursor; status.Text = servers.Count == 0 ? "No servers found." : $"{servers.Count} servers";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (RobloxLaunchException error) { cursor = null; status.Text = error.Message; }
        catch (Exception) { cursor = null; status.Text = "Could not load servers. Check your connection."; }
        finally { if (!lifetime.IsCancellationRequested) { refresh.IsEnabled = true; next.IsEnabled = !string.IsNullOrEmpty(cursor); } }
    }
}
