using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
namespace Hive.App;

public class ToolWindow : Window
{
    private readonly ContentControl body = new();
    protected ToolWindow(string title, AccountsViewModel? model = null)
    {
        Title = title; Width = 800; Height = 520; MinWidth = 620; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 48, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(12) });
        var frame = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(20,0,8,0) };
        var close = new Button { Style = (Style)Application.Current.FindResource("IconButton"), Content = new SvgIcon { NameKey = "close", Width = 18, Height = 18 } };
        close.Click += (_, _) => Close(); WindowChrome.SetIsHitTestVisibleInChrome(close, true); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(header); body.Margin = new Thickness(20,16,20,20); Grid.SetRow(body,1); grid.Children.Add(body);
        if (model is not null)
        {
            DataContext = model;
            var status = new TextBlock { FontSize = 12, Margin = new Thickness(20,0,20,14), TextTrimming = TextTrimming.CharacterEllipsis };
            status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); status.SetBinding(TextBlock.TextProperty, "Status"); Grid.SetRow(status,2); grid.Children.Add(status);
        }
        frame.Child = grid; Content = frame;
        if (model is not null) SetBinding(Motion.EnabledProperty, new System.Windows.Data.Binding(nameof(AccountsViewModel.EffectiveMotion)) { Source = model });
        Loaded += (_, _) =>
        {
            var enabled = model?.EffectiveMotion ?? (SystemParameters.ClientAreaAnimation && (Owner is null || Motion.GetEnabled(Owner)));
            Motion.SetEnabled(this, enabled);
            UiMotion.Reveal(frame, enabled);
        };
    }
    protected void SetBody(UIElement element) => body.Content = element;
    protected static DataGrid Table(params (string Header, string Binding, double Width)[] columns)
    {
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserResizeRows = false, RowHeight = 38, ColumnHeaderHeight = 34, BorderThickness = new Thickness(0), HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, SelectionMode = DataGridSelectionMode.Single };
        table.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); table.SetResourceReference(Control.ForegroundProperty, "InkBrush"); table.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "LineBrush");
        foreach (var column in columns) table.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = new System.Windows.Data.Binding(column.Binding), Width = column.Width < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(column.Width) });
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.FindResource("SubtleBrush"))); header.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.FindResource("MutedBrush"))); header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12,0,12,0))); header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        table.ColumnHeaderStyle = header;
        var row = new Style(typeof(DataGridRow)); row.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.FindResource("SurfaceBrush"))); row.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.FindResource("InkBrush")));
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.FindResource("SelectedBrush"))); row.Triggers.Add(selected); table.RowStyle = row;
        table.CellStyle = (Style)Application.Current.FindResource("ToolTableCell");
        return table;
    }
    protected static Button ActionButton(string text, RoutedEventHandler action)
    {
        var icon = text switch { "New profile" => "plus", "Open" => "browser", "Rename" => "edit", "Reset cache" => "refresh", "Remove" => "trash", _ => "stop" };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new SvgIcon { NameKey = icon, Width = 18, Height = 18 }); content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8,0,0,0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Margin = new Thickness(0,0,8,0) }; button.Click += action; return button;
    }
}
