using System.Windows;
using System.Windows.Controls;
namespace Hive.App;

public sealed class TextPromptWindow : ToolWindow
{
    private readonly TextBox input = new() { MaxLength = 64, Height = 36 };
    public string Value => input.Text.Trim();
    public TextPromptWindow(string title, string label) : base(title)
    {
        Width = 380; Height = 230; MinWidth = 340; MinHeight = 220; ResizeMode = ResizeMode.NoResize;
        var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,0,0,8) }); Grid.SetRow(input,1); panel.Children.Add(input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0,0,8,0) };
        var confirm = new Button { Content = "Save", IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        confirm.Click += (_, _) => { if (Value.Length > 0) DialogResult = true; }; actions.Children.Add(cancel); actions.Children.Add(confirm); Grid.SetRow(actions,2); panel.Children.Add(actions);
        SetBody(panel); Loaded += (_, _) => input.Focus();
    }
}
