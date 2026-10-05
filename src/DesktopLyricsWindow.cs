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
        Title = "chilimusic · 桌面歌词"; Width = 620; Height = 156; MinWidth = 360; MinHeight = 156; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; DataContext = vm;
        SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontFamilyProperty, "AppFont");
        var surface = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(18, 8, 18, 12) }; surface.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); surface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new DockPanel(); var close = UiFactory.Button("\uE8BB", Close); close.SetResourceReference(StyleProperty, "IconButton"); close.FontSize = 13; close.Width = 32; close.Height = 28; close.Margin = new Thickness(4, 0, 0, 0); close.Padding = new Thickness(0); close.ToolTip = "关闭桌面歌词"; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); _lock = UiFactory.Button("锁定", ToggleLock); _lock.Height = 28; _lock.FontSize = 11; _lock.ToolTip = "锁定后点击穿透，可从托盘解锁"; DockPanel.SetDock(_lock, Dock.Right); header.Children.Add(_lock); var heading = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = "拖动移动" }; heading.SetBinding(TextBlock.TextProperty, new Binding("Title")); heading.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); header.Children.Add(heading); grid.Children.Add(header);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var line = new TextBlock { FontSize = 23, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }; line.SetBinding(TextBlock.TextProperty, new Binding("LyricText")); line.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); words.Children.Add(line);
        var next = new TextBlock { FontSize = 13, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap }; next.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); words.Children.Add(next); Grid.SetRow(words, 1); grid.Children.Add(words); surface.Child = grid; Content = surface; WindowAppearance.Attach(this);
        void Fit()
        {
            next.Text = vm.ShowLyricTranslation && vm.TranslationText.Length > 0 ? vm.TranslationText : vm.NextLyricText;
            double width = Math.Max(260, ActualWidth - 38); var size = new FormattedText(vm.LyricText, System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 23, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip); line.FontSize = size.Width <= width * 1.8 ? 23 : size.Width <= width * 2.8 ? 18 : 14;
        }
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(vm.LyricText) or nameof(vm.NextLyricText) or nameof(vm.TranslationText) or nameof(vm.ShowLyricTranslation)) Fit(); }; vm.PropertyChanged += changed; SizeChanged += (_, _) => Fit(); Closed += (_, _) => vm.PropertyChanged -= changed;
        surface.MouseLeftButtonDown += (_, e) => { var node = e.OriginalSource as DependencyObject; while (node != null && node is not Button && node != surface) node = VisualTreeHelper.GetParent(node); if (!Locked && node is not Button) { try { DragMove(); } catch (InvalidOperationException) { } } };
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 24; };
    }
    public void ToggleLock()
    {
        Locked = !Locked; _lock.Content = Locked ? "已锁定" : "锁定"; var handle = new WindowInteropHelper(this).Handle; long style = GetStyle(handle, -20).ToInt64(); SetStyle(handle, -20, new IntPtr(Locked ? style | 0x20 : style & ~0x20));
    }
    public void Unlock() { if (Locked) ToggleLock(); }
}
