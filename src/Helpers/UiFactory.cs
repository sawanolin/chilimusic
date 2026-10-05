using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
namespace ChiliMusic;

internal static class UiFactory
{
    public static TextBlock Text(string text, double size = 12) { var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 5) }; block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); return block; }
    public static TextBox Input(string text = "", double width = double.NaN) => new() { Text = text, Height = 36, Width = width, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 7) };
    public static Button Button(string text, Action action) { var button = new Button { Content = text, Height = 34, Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(0, 3, 7, 4) }; button.Click += (_, _) => action(); return button; }
    public static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 5, 0, 7) };
    public static ComboBox Combo(IEnumerable<string> options, int selected = 0) => new() { ItemsSource = options, SelectedIndex = selected, Height = 36, Margin = new Thickness(0, 3, 0, 7) };
    public static WrapPanel Buttons(params Button[] buttons) { var row = new WrapPanel(); foreach (var button in buttons) row.Children.Add(button); return row; }
    public static Border Card(string heading, params UIElement[] children)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = heading, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) }); foreach (var child in children) panel.Children.Add(child);
        var border = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14), Child = panel }; border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return border;
    }
}

internal class DialogShell : Window
{
    protected Grid Body { get; } = new() { Margin = new Thickness(20, 12, 20, 18) };
    public DialogShell(string title, double width, double height)
    {
        Title = "chilimusic · " + title; Width = width; Height = height; MinWidth = Math.Min(width, 460); MinHeight = 400; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontFamilyProperty, "AppFont"); FontSize = 12;
        WindowChrome.SetWindowChrome(this, new() { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        var root = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) }; root.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new GridLength(44) }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new DockPanel { Margin = new Thickness(20, 0, 12, 0) }; var close = UiFactory.Button("×", Close); close.Width = 32; close.Height = 28; close.Padding = new Thickness(0); close.Margin = new Thickness(0); close.FontSize = 22; WindowChrome.SetIsHitTestVisibleInChrome(close, true); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(header); Grid.SetRow(Body, 1); grid.Children.Add(Body); root.Child = grid; Content = root; WindowAppearance.Attach(this);
    }
}
