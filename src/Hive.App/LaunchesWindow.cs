using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hive.Core;
namespace Hive.App;

public sealed class LaunchesWindow : ToolWindow
{
    private sealed record Row(string AccountId, string Account, string State, string Process, int Attempts, string Detail);
    public LaunchesWindow(AccountsViewModel model) : base("Launches", model)
    {
        Width = 880;
        var table = Table(("Account","Account",150),("State","State",140),("PID","Process",80),("Retries","Attempts",65),("Details","Detail",-1));
        var panel = new DockPanel(); var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,16) }; DockPanel.SetDock(actions,Dock.Top);
        actions.Children.Add(ActionButton("Cancel selected", (_, _) => { if (table.SelectedItem is Row row) model.CancelLaunch(row.AccountId); }));
        actions.Children.Add(ActionButton("Cancel pending", (_, _) => { foreach (var launch in model.Launches.Where(x => x.State is not InstanceState.Running)) model.CancelLaunch(launch.AccountId); }));
        panel.Children.Add(actions); panel.Children.Add(table); SetBody(panel);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        Row[] last = [];
        void Update()
        {
            var rows = model.Launches.Select(x => new Row(x.AccountId,model.AccountName(x.AccountId),x.State.ToString(),x.ProcessId?.ToString() ?? "—",x.RejoinAttempts,x.Detail ?? "")).OrderBy(x => x.Account).ToArray();
            if (last.SequenceEqual(rows)) return; var selected = (table.SelectedItem as Row)?.AccountId; last = rows; table.ItemsSource = rows; table.SelectedItem = rows.FirstOrDefault(x => x.AccountId == selected);
        }
        timer.Tick += (_, _) => Update(); Loaded += (_, _) => { Update(); timer.Start(); }; Closed += (_, _) => timer.Stop();
    }
}
