using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hive.Core;
namespace Hive.App;

public sealed class NetworkLogWindow : ToolWindow
{
    public NetworkLogWindow(INetworkLog log) : base("Network log")
    {
        Title = "Hive — Network log";
        Width = 720;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var table = Table(("Time","At",90),("Service","Service",160),("Operation","Operation",-1),("Status","Status",70),("ms","Milliseconds",60));
        ((DataGridTextColumn)table.Columns[0]).Binding = new System.Windows.Data.Binding("At") { StringFormat = "{0:HH:mm:ss}" };
        var panel = new DockPanel();
        panel.Children.Add(table);
        SetBody(panel);
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
