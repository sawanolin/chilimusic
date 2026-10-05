using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
namespace ChiliMusic;

internal sealed class DesktopLyricsWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetStyle(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetStyle(IntPtr hwnd, int index, IntPtr value);
    private readonly Button _lock;
    public bool Locked { get; private set; }
    public DesktopLyricsWindow(PlayerViewModel vm)
    {
        Title = "chilimusic · 桌面歌词"; Width = 620; Height = 124; MinWidth = 360; MinHeight = 100; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; DataContext = vm;
        SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontFamilyProperty, "AppFont");
        var surface = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(18, 8, 18, 12) }; surface.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); surface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new DockPanel(); var close = UiFactory.Button("×", Close); close.FontSize = 18; close.Width = 28; close.Height = 24; close.Padding = new Thickness(0); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); _lock = UiFactory.Button("锁定", ToggleLock); _lock.Height = 24; _lock.FontSize = 10; DockPanel.SetDock(_lock, Dock.Right); header.Children.Add(_lock); header.Children.Add(UiFactory.Text("拖动移动 · 可从托盘解锁", 10)); grid.Children.Add(header);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var line = new TextBlock { FontSize = 23, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; line.SetBinding(TextBlock.TextProperty, new Binding("LyricText")); line.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); words.Children.Add(line);
        var next = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }; next.SetBinding(TextBlock.TextProperty, new Binding("NextLyricText")); next.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); words.Children.Add(next); Grid.SetRow(words, 1); grid.Children.Add(words); surface.Child = grid; Content = surface; WindowAppearance.Attach(this);
        surface.MouseLeftButtonDown += (_, e) => { var node = e.OriginalSource as DependencyObject; while (node != null && node is not Button && node != surface) node = VisualTreeHelper.GetParent(node); if (!Locked && node is not Button) { try { DragMove(); } catch (InvalidOperationException) { } } };
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 24; };
    }
    public void ToggleLock()
    {
        Locked = !Locked; _lock.Content = Locked ? "已锁定" : "锁定"; var handle = new WindowInteropHelper(this).Handle; long style = GetStyle(handle, -20).ToInt64(); SetStyle(handle, -20, new IntPtr(Locked ? style | 0x20 : style & ~0x20));
    }
    public void Unlock() { if (Locked) ToggleLock(); }
}
