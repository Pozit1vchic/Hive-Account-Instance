using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hive.Storage;

namespace Hive.App;

public sealed class AccountFieldsWindow : ToolWindow
{
    public sealed class FieldRow { public string Name { get; set; } = ""; public string Value { get; set; } = ""; }
    private readonly ObservableCollection<FieldRow> rows = [];
    private readonly AccountFieldsStore store;
    private readonly string accountId;
    private readonly DataGrid table = new() { AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    public AccountFieldsWindow(AccountFieldsStore store, string accountId) : base("Account fields")
    {
        this.store = store; this.accountId = accountId; Width = 620; Height = 470; MinWidth = 500; MinHeight = 350;
        table.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding("Value") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        table.ItemsSource = rows;
        var panel = new DockPanel(); var bottom = new DockPanel(); DockPanel.SetDock(bottom, Dock.Bottom);
        var save = new Button { Content = "Save fields", Style = (Style)Application.Current.FindResource("PrimaryButton") }; save.Click += Save;
        DockPanel.SetDock(save, Dock.Right); bottom.Children.Add(save); bottom.Children.Add(status); panel.Children.Add(bottom); panel.Children.Add(table); SetBody(panel);
        Loaded += async (_, _) => { try { foreach (var item in await store.ReadAsync(accountId)) rows.Add(new() { Name = item.Key, Value = item.Value }); } catch (Exception) { status.Text = "Could not read account fields."; table.IsEnabled = false; save.IsEnabled = false; } };
    }
    private async void Save(object sender, RoutedEventArgs e)
    {
        table.CommitEdit(DataGridEditingUnit.Cell, true); table.CommitEdit(DataGridEditingUnit.Row, true);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
            if (!fields.TryAdd(row.Name ?? "", row.Value ?? "")) { status.Text = "Field names must be unique."; return; }
        try { await store.SaveAsync(accountId, fields); Close(); }
        catch (ArgumentException error) { status.Text = error.Message; }
        catch (Exception) { status.Text = "Could not save account fields."; }
    }
}
