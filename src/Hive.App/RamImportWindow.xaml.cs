using System.Windows;
using Hive.Storage;
namespace Hive.App;

public partial class RamImportWindow : Window
{
    public RamImportWindow(IReadOnlyList<RamImportItem> items)
    {
        InitializeComponent();
        DataContext = new
        {
            Rows = items.Select(i => new { i.Username, i.Alias, i.Description }).ToList(),
            Summary = $"{items.Count} accounts, {items.Count(i => i.Password is not null)} saved passwords and {items.Count(i => i.Cookie is not null)} sessions. Sessions remain unverified until checked."
        };
    }
    private void Confirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
