using System.Windows;
using System.Windows.Controls;
namespace Hive.App;

public partial class AccountDetailsWindow : Window
{
    private readonly AccountsViewModel model;
    public AccountDetailsWindow(AccountsViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        UiMotion.Attach(this, model.EffectiveMotion);
        model.ClearSensitiveInputs += Clear;
        Closed += (_, _) => { model.ClearSensitiveInputs -= Clear; Clear(); model.Selected = model.Selected; };
    }
    private void Clear()
    {
        PasswordInput.Clear();
        CookieInput.Clear();
    }
    private void PasswordChanged(object sender, RoutedEventArgs e) => model.PasswordDraft = ((PasswordBox)sender).Password;
    private void CookieChanged(object sender, RoutedEventArgs e) => model.CookieDraft = ((PasswordBox)sender).Password;
    private async void Save(object sender, RoutedEventArgs e)
    {
        await ((AsyncCommand)model.SaveCommand).ExecuteAsync();
        if (model.Status == "Account saved locally.")
            Close();
    }
    private void CloseDialog(object sender, RoutedEventArgs e) => Close();
}
