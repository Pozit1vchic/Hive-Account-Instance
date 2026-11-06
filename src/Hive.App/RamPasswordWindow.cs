using System.Windows;
using System.Windows.Controls;

namespace Hive.App;

public sealed class RamPasswordWindow : ToolWindow
{
    private readonly PasswordBox input = new() { MaxLength = 1024, Height = 36 };
    public string Password => input.Password;
    public void Forget() => input.Clear();
    public RamPasswordWindow() : base("Unlock RAM file")
    {
        Width = 400; Height = 230; MinWidth = 360; MinHeight = 220; ResizeMode = ResizeMode.NoResize;
        var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(new TextBlock { Text = "RAM file password", Margin = new Thickness(0, 0, 0, 8) }); Grid.SetRow(input, 1); panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Content = "Unlock", IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        confirm.Click += (_, _) => { if (input.Password.Length > 0) DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); Grid.SetRow(buttons, 2); panel.Children.Add(buttons);
        SetBody(panel); Loaded += (_, _) => input.Focus();
    }
}
