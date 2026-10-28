using System.Windows;
using Hive.Core;
namespace Hive.App;

public partial class AddAccountsWindow : Window
{
    private readonly string mode;
    public IReadOnlyList<LoginCredential> Credentials { get; private set; } = [];
    public IReadOnlyList<string> Cookies { get; private set; } = [];
    public Uri? StartUrl
    {
        get; private set;
    }
    public string Script { get; private set; } = "";
    public AddAccountsWindow(string mode, bool animations = true)
    {
        this.mode = mode;
        InitializeComponent();
        Motion.SetEnabled(this, animations);
        UiMotion.Attach(this, animations);
        CredentialsPanel.Visibility = mode == "Credentials" ? Visibility.Visible : Visibility.Collapsed;
        CookiesPanel.Visibility = mode == "Cookies" ? Visibility.Visible : Visibility.Collapsed;
        CustomPanel.Visibility = mode == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = mode switch
        {
            "Credentials" => "Add with User:Pass",
            "Cookies" => "Add with cookies",
            _ => "Custom browser flow"
        };
        Hint.Text = mode == "Credentials" ? "Roblox sign-in happens in the browser. Complete any verification when prompted." : mode == "Cookies" ? "Import verified sessions without opening a sign-in window." : "Choose a starting page and an optional script.";
        Closed += (_, _) => { PasswordInput.Clear(); BulkInput.Clear(); CookiesInput.Clear(); ScriptInput.Clear(); };
    }
    private void Continue(object sender, RoutedEventArgs e)
    {
        try
        {
            if (mode == "Credentials")
                Credentials = string.IsNullOrWhiteSpace(BulkInput.Text) ? AccountInputParser.Credentials(UsernameInput.Text + ":" + PasswordInput.Password) : AccountInputParser.Credentials(BulkInput.Text);
            else if (mode == "Cookies")
                Cookies = AccountInputParser.Cookies(CookiesInput.Text);
            else
            {
                if (!Uri.TryCreate(UrlInput.Text, UriKind.Absolute, out var url) || url.Scheme != "https")
                    throw new FormatException("Enter a valid HTTPS URL.");
                StartUrl = url;
                Script = ScriptInput.Text;
            }
            DialogResult = true;
        }
        catch (FormatException ex) { ErrorText.Text = ex.Message; }
    }
    public void ForgetSecrets()
    {
        Credentials = [];
        Cookies = [];
        Script = "";
    }
    private void CloseDialog(object sender, RoutedEventArgs e) => Close();
}
