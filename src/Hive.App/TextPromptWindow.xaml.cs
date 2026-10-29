using System.Windows;
namespace Hive.App;

public partial class TextPromptWindow : Window
{
    public string Value => Input.Text.Trim();
    public TextPromptWindow(string title, string label)
    {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = label;
        Loaded += (_, _) => Input.Focus();
    }
    private void Confirm(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Value))
            DialogResult = true;
    }
}
