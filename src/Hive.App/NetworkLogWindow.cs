using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hive.Core;
namespace Hive.App;

public sealed class NetworkLogWindow : Window
{
    public NetworkLogWindow(INetworkLog log)
    {
        Title = "Hive — Network log";
        Width = 720;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, BorderThickness = new Thickness(0), RowHeight = 32 };
        table.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        table.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        foreach (var column in new[] { ("Time", "At", 140d), ("Service", "Service", 160d), ("Operation", "Operation", 175d), ("Status", "Status", 70d), ("ms", "Milliseconds", 65d) })
            table.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new System.Windows.Data.Binding(column.Item2) { StringFormat = column.Item2 == "At" ? "{0:HH:mm:ss}" : null }, Width = column.Item3 });
        var panel = new DockPanel { Margin = new Thickness(18) };
        var hint = new TextBlock { Text = "Recent requests · no cookies, passwords or tickets", Margin = new Thickness(0, 0, 0, 14) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(hint);
        panel.Children.Add(table);
        Content = panel;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        DateTimeOffset latest = default;
        void Update()
        {
            var entries = log.Snapshot();
            if (entries.Count > 0 && entries[0].At != latest)
            {
                latest = entries[0].At;
                table.ItemsSource = entries;
            }
        }
        timer.Tick += (_, _) => Update();
        Loaded += (_, _) => { Update(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
    }
}
