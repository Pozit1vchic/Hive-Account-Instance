using System.Windows;
using Hive.Core;
namespace Hive.App;

public partial class AddAccountsWindow : Window
{
    private readonly string mode;
    private bool submitting;
    public Func<Task<bool>>? SubmitAsync
    {
        get; set;
    }
    public Action? CancelOperation
    {
        get; set;
    }
    public void SetProgress(string message) => ErrorText.Text = message;
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
        ManualPanel.Visibility = mode == "Manual" ? Visibility.Visible : Visibility.Collapsed;
        CustomPanel.Visibility = mode == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = mode switch
        {
            "Manual" => "Add with browser sign-in",
            "Credentials" => "Add with User:Pass",
            "Cookies" => "Add with cookies",
            _ => "Custom browser flow"
        };
        Hint.Text = mode == "Manual" ? "Sign in to Roblox in a private browser. Hive saves the verified session locally." : mode == "Credentials" ? "Roblox sign-in happens in the browser. Complete any verification when prompted." : mode == "Cookies" ? "Import verified sessions without opening a sign-in window." : "Choose a starting page and an optional script.";
        Closing += (_, e) => { if (submitting) { e.Cancel = true; CancelOperation?.Invoke(); } };
        Closed += (_, _) => { PasswordInput.Clear(); BulkInput.Clear(); CookiesInput.Clear(); ScriptInput.Clear(); };
    }
    private async void Continue(object sender, RoutedEventArgs e)
    {
        if (submitting)
            return;
        try
        {
            if (mode == "Credentials")
                Credentials = string.IsNullOrWhiteSpace(BulkInput.Text) ? AccountInputParser.Credentials(UsernameInput.Text + ":" + PasswordInput.Password) : AccountInputParser.Credentials(BulkInput.Text);
            else if (mode == "Cookies")
                Cookies = AccountInputParser.Cookies(CookiesInput.Text);
            else if (mode == "Custom")
            {
                if (!Uri.TryCreate(UrlInput.Text, UriKind.Absolute, out var url) || url.Scheme != "https")
                    throw new FormatException("Enter a valid HTTPS URL.");
                StartUrl = url;
                Script = ScriptInput.Text;
            }
            if (SubmitAsync is null)
            {
                DialogResult = true;
                return;
            }
            submitting = true;
            ContinueButton.IsEnabled = false;
            InputPanels.IsEnabled = false;
            CancelButton.Content = "Cancel operation";
            ErrorText.Text = "Starting account addition...";
            var success = await SubmitAsync();
            submitting = false;
            if (success)
                DialogResult = true;
        }
        catch (FormatException ex) { ErrorText.Text = ex.Message; }
        catch (Exception) { ErrorText.Text = "Account addition failed. Check the browser configuration and connection, then try again."; }
        finally
        {
            submitting = false;
            ContinueButton.IsEnabled = true;
            InputPanels.IsEnabled = true;
            CancelButton.Content = "Cancel";
            ForgetSecrets();
        }
    }
    public void ForgetSecrets()
    {
        Credentials = [];
        Cookies = [];
        Script = "";
    }
    private void CloseDialog(object sender, RoutedEventArgs e)
    {
        if (submitting)
        {
            CancelOperation?.Invoke();
            return;
        }
        Close();
    }
}
