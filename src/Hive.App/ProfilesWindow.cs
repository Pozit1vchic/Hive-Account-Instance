using System.Windows;
using System.Windows.Controls;
using Hive.Core;
namespace Hive.App;

public sealed class ProfilesWindow : ToolWindow
{
    private sealed record Row(BrowserProfile Profile, string Name, string Account, string LastUsed);
    public ProfilesWindow(AccountsViewModel model) : base("Browser profiles", model)
    {
        var table = Table(("Profile","Name",-1),("Account","Account",180),("Last used","LastUsed",150));
        void Update() { var id = (table.SelectedItem as Row)?.Profile.Id; var rows = model.BrowserProfiles.Select(p => new Row(p,p.Name,p.AccountId is null ? "Unassigned" : model.AccountName(p.AccountId),p.LastUsedAt?.ToLocalTime().ToString("dd MMM, HH:mm",System.Globalization.CultureInfo.InvariantCulture) ?? "Never")).ToArray(); table.ItemsSource = rows; table.SelectedItem = rows.FirstOrDefault(p => p.Profile.Id == id); }
        async void Open(object? sender, RoutedEventArgs e) { if (table.SelectedItem is Row row) await model.OpenProfileAsync(row.Profile); Update(); }
        var actions = new WrapPanel { Margin = new Thickness(0,0,0,16) };
        actions.Children.Add(ActionButton("New profile", async (_, _) => { var prompt = new TextPromptWindow("New profile", "Profile name") { Owner = this }; if (prompt.ShowDialog() == true) await model.CreateProfileAsync(prompt.Value); Update(); }));
        actions.Children.Add(ActionButton("Open", Open));
        actions.Children.Add(ActionButton("Rename", async (_, _) => { if (table.SelectedItem is not Row row) return; var prompt = new TextPromptWindow("Rename profile","Profile name") { Owner = this }; if (prompt.ShowDialog() == true) await model.RenameProfileAsync(row.Profile,prompt.Value); Update(); }));
        actions.Children.Add(ActionButton("Reset cache", async (_, _) => { if (table.SelectedItem is Row row && MessageBox.Show("Archive this profile's browser cache? The saved account session is kept.","Hive",MessageBoxButton.YesNo) == MessageBoxResult.Yes) await model.RemoveProfileAsync(row.Profile,true); Update(); }));
        actions.Children.Add(ActionButton("Remove", async (_, _) => { if (table.SelectedItem is Row row && MessageBox.Show("Remove this browser profile? The account is kept.","Hive",MessageBoxButton.YesNo) == MessageBoxResult.Yes) await model.RemoveProfileAsync(row.Profile,false); Update(); }));
        table.MouseDoubleClick += Open; var panel = new DockPanel(); DockPanel.SetDock(actions,Dock.Top); panel.Children.Add(actions); panel.Children.Add(table); SetBody(panel); Loaded += (_, _) => Update();
    }
}
