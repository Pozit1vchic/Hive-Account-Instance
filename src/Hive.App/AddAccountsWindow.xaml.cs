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
    public void SetProgress(string message, bool error = false)
    {
        ErrorText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, error ? "RedBrush" : "MutedBrush");
        ErrorText.Text = message;
    }
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
        Hint.Text = mode switch
        {
            "Credentials" => "Complete verification in the browser if asked.",
            "Cookies" => "One session cookie per line.",
            "Custom" => "Run a custom sign-in flow.",
            _ => "Sign in on Roblox."
        };

        Closing += (_, e) => { if (submitting) { e.Cancel = true; CancelOperation?.Invoke(); } };
        Closed += (_, _) => { BulkInput.Clear(); CookiesInput.Clear(); ScriptInput.Clear(); };
    }
    private async void Continue(object sender, RoutedEventArgs e)
    {
        if (submitting)
            return;
        try
        {
            if (mode == "Credentials")
                Credentials = AccountInputParser.Credentials(BulkInput.Text);
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
            SetProgress("Opening browser...");
            var success = await SubmitAsync();
            submitting = false;
            if (success)
                DialogResult = true;
        }
        catch (FormatException ex) { SetProgress(ex.Message, true); }
        catch (Exception) { SetProgress("Could not add the account. Try again.", true); }
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
