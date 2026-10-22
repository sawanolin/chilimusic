using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;
namespace ChiliMusic;

internal static class UiFactory
{
    public static TextBlock Text(string text, double size = 12) { var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 5) }; block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); return block; }
    public static TextBox Input(string text = "", double width = double.NaN) => new() { Text = text, FontSize = 12, Height = 36, Width = width, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 7) };
    public static Button Button(string text, Action action, bool primary = false) { var button = new Button { Content = text, Height = 34, Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(0, 3, 7, 4) }; if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton"); button.Click += (_, _) => action(); return button; }
    public static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 5, 0, 7) };
    public static ComboBox Combo(IEnumerable<string> options, int selected = 0) => new() { ItemsSource = options, SelectedIndex = selected, Height = 36, Margin = new Thickness(0, 3, 0, 7) };
    public static WrapPanel Buttons(params Button[] buttons) { var row = new WrapPanel(); foreach (var button in buttons) row.Children.Add(button); return row; }
    public static StackPanel Toolbar(params UIElement[] items) { var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) }; foreach (var item in items) row.Children.Add(item); return row; }
    public static Button More(params (string Text, Action Action, Func<bool>? Enabled)[] actions)
    {
        var button = Button("···", () => { }); button.Width = 34; button.Padding = new Thickness(0); button.FontSize = 18; button.ToolTip = "更多操作";
        button.Click += (_, _) => { var menu = new ContextMenu(); foreach (var entry in actions) { var item = new MenuItem { Header = entry.Text, IsEnabled = entry.Enabled?.Invoke() ?? true }; item.Click += (_, _) => entry.Action(); menu.Items.Add(item); } menu.PlacementTarget = button; menu.IsOpen = true; }; return button;
    }
    public static void MakeVertical(Slider slider)
    {
        slider.Height = 104;
        slider.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider"><Grid Width="20"><Track x:Name="PART_Track" Orientation="Vertical" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" IsDirectionReversed="False"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Width="3" Background="{DynamicResource AccentBrush}" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Width="3" Background="{DynamicResource BorderBrush}" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton><Track.Thumb><Thumb Width="10" Height="10"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="{DynamicResource SurfaceBrush}" Stroke="{DynamicResource AccentBrush}" StrokeThickness="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate>
""");
    }
    public static Grid SearchField(TextBox input, string hint)
    {
        input.Height = 32; input.FontSize = 11; input.Padding = new Thickness(10, 0, 10, 0); var field = new Grid { Margin = new Thickness(0, 0, 0, 9) }; field.Children.Add(input);
        var label = Text(hint, 10); label.Margin = new Thickness(11, 0, 8, 0); label.VerticalAlignment = VerticalAlignment.Center; label.IsHitTestVisible = false; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed)); var empty = new DataTrigger { Binding = new Binding("Text") { Source = input }, Value = "" }; empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible)); style.Triggers.Add(empty); label.Style = style; field.Children.Add(label); return field;
    }
    public static Grid ListWithEmptyState(ListBox list, string title, string description)
    {
        var grid = new Grid(); grid.Children.Add(list);
        var empty = new StackPanel { Tag = "EmptyState", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12), IsHitTestVisible = false };
        var icon = Text("\uE8B7", 24); icon.FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); empty.Children.Add(icon);
        var heading = Text(title, 12); heading.Tag = "EmptyHeading"; heading.TextAlignment = TextAlignment.Center; heading.FontWeight = FontWeights.Medium; empty.Children.Add(heading);
        var hint = Text(description, 10); hint.TextAlignment = TextAlignment.Center; hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); empty.Children.Add(hint);
        grid.SizeChanged += (_, _) => { icon.Visibility = grid.ActualHeight >= 140 ? Visibility.Visible : Visibility.Collapsed; hint.Visibility = grid.ActualHeight >= 100 ? Visibility.Visible : Visibility.Collapsed; empty.Margin = new Thickness(grid.ActualHeight < 140 ? 6 : 12); };
        var style = new Style(typeof(StackPanel)); style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var trigger = new DataTrigger { Binding = new Binding("Items.Count") { Source = list }, Value = 0 }; trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible)); style.Triggers.Add(trigger); empty.Style = style; grid.Children.Add(empty); return grid;
    }
    public static Grid Row(string label, UIElement control)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(112) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var text = Text(label); text.Margin = new Thickness(0, 0, 10, 0); text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text); Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    public static Border Card(string heading, params UIElement[] children)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = heading, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) }); foreach (var child in children) panel.Children.Add(child);
        var border = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14), Child = panel }; border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return border;
    }
}

internal class DialogShell : Window
{
    protected TextBlock HeaderTitle { get; private set; } = null!;
    protected Grid Body { get; } = new() { Margin = new Thickness(20, 12, 20, 18) };
    public DialogShell(string title, double width, double height)
    {
        Title = "chilimusic · " + title; Width = width; Height = height; MinWidth = Math.Min(width, 460); MinHeight = 400; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontFamilyProperty, "AppFont"); FontSize = 12;
        WindowChrome.SetWindowChrome(this, new() { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        var root = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) }; root.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new GridLength(44) }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new DockPanel { Margin = new Thickness(20, 0, 12, 0) }; var close = UiFactory.Button("\uE8BB", Close); close.SetResourceReference(FrameworkElement.StyleProperty, "IconButton"); close.Width = 30; close.Height = 30; close.Padding = new Thickness(0); close.Margin = new Thickness(0); close.FontSize = 14; close.ToolTip = "关闭"; WindowChrome.SetIsHitTestVisibleInChrome(close, true); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); HeaderTitle = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; header.Children.Add(HeaderTitle);
        grid.Children.Add(header); Grid.SetRow(Body, 1); grid.Children.Add(Body); root.Child = grid; Content = root; WindowAppearance.Attach(this);
    }
}
