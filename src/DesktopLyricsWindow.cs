using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
namespace ChiliMusic;

internal sealed class DesktopLyricsWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetStyle(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetStyle(IntPtr hwnd, int index, IntPtr value);
    private readonly PlayerViewModel _vm;
    private readonly Button _lock;
    private readonly Border _toolbar;
    private readonly Grid _root;
    private readonly Thumb _grip;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(400) };
    public bool Locked { get; private set; }
    public DesktopLyricsWindow(PlayerViewModel vm)
    {
        _vm = vm;
        Title = "chilimusic · 桌面歌词";
        MinWidth = 320; MinHeight = 130;
        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(vm.Settings.DesktopLyricWidth, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(vm.Settings.DesktopLyricHeight, MinHeight, Math.Max(MinHeight, area.Height));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; DataContext = vm;
        SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(FontFamilyProperty, "AppFont");
        _root = new Grid { Background = Brushes.Transparent };
        _root.RowDefinitions.Add(new() { Height = new GridLength(36) });
        _root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new() { Height = new GridLength(18) });
        _toolbar = new Border { HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 0, 4, 0), Visibility = Visibility.Hidden };
        _toolbar.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        Button Control(string text, Action action, string tooltip)
        {
            var button = UiFactory.Button(text, action); button.Width = 32; button.Height = 28; button.Padding = new Thickness(0); button.Margin = new Thickness(1, 2, 1, 2); button.ToolTip = tooltip; button.FocusVisualStyle = null;
            controls.Children.Add(button); return button;
        }
        Control("A−", () => AdjustFont(-2), "缩小歌词 · 滚轮向下");
        Control("A+", () => AdjustFont(2), "放大歌词 · 滚轮向上");
        _lock = Control("锁定", ToggleLock, "锁定后点击穿透，可从托盘解锁"); _lock.Width = 48; _lock.FontSize = 11;
        var close = Control("\uE8BB", Close, "关闭桌面歌词"); close.SetResourceReference(StyleProperty, "WindowControlButton"); close.FontSize = 13;
        _toolbar.Child = controls; _root.Children.Add(_toolbar);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(12, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        TextBlock Line(string resource, string binding)
        {
            var text = new TextBlock { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = .9 } };
            text.SetResourceReference(TextBlock.ForegroundProperty, "DesktopLyricBrush");
            text.SetResourceReference(TextBlock.FontSizeProperty, resource);
            text.SetBinding(TextBlock.TextProperty, new Binding(binding)); words.Children.Add(text); return text;
        }
        var line = Line("DesktopLyricFontSize", "LyricText"); line.FontWeight = FontWeights.SemiBold;
        var next = Line("DesktopLyricSecondaryFontSize", "NextLyricText"); next.Margin = new Thickness(0, 7, 0, 0); next.Opacity = .8;
        void UpdateNext() => next.Text = vm.ShowLyricTranslation && vm.TranslationText.Length > 0 ? vm.TranslationText : vm.NextLyricText;
        UpdateNext();
        scroll.Content = words; Grid.SetRow(scroll, 1); _root.Children.Add(scroll);
        _grip = new Thumb { Width = 24, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, Cursor = System.Windows.Input.Cursors.SizeNWSE, Visibility = Visibility.Hidden, ToolTip = "拖动调整歌词区域；滚轮调整字号" };
        var gripText = new FrameworkElementFactory(typeof(TextBlock)); gripText.SetValue(TextBlock.TextProperty, "◢"); gripText.SetValue(TextBlock.FontSizeProperty, 16d); gripText.SetResourceReference(TextBlock.ForegroundProperty, "DesktopLyricBrush");
        var gripSurface = new FrameworkElementFactory(typeof(Border)); gripSurface.SetValue(Border.BackgroundProperty, Brushes.Transparent); gripSurface.AppendChild(gripText);
        _grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripSurface };
        _grip.DragDelta += (_, e) => { if (!Locked) { Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, Math.Max(MinWidth, area.Width)); Height = Math.Clamp(Height + e.VerticalChange, MinHeight, Math.Max(MinHeight, area.Height)); } };
        _grip.DragCompleted += (_, _) => SaveSoon(); Grid.SetRow(_grip, 2); _root.Children.Add(_grip);
        Content = _root; WindowAppearance.Attach(this);
        void Editing(bool editing)
        {
            bool visible = editing && !Locked;
            _toolbar.Visibility = _grip.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            // A nonzero alpha while editing keeps the transparent gaps draggable.
            _root.Background = visible ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        }
        MouseEnter += (_, _) => Editing(true); MouseLeave += (_, _) => { if (!_grip.IsDragging) Editing(false); };
        PreviewMouseWheel += (_, e) => { if (!Locked) { AdjustFont(Math.Sign(e.Delta) * 2); e.Handled = true; } };
        _root.MouseLeftButtonDown += (_, e) =>
        {
            var node = e.OriginalSource as DependencyObject;
            while (node != null && node is not Button && node is not Thumb && node != _root) node = VisualTreeHelper.GetParent(node);
            if (!Locked && node is not Button && node is not Thumb) { try { DragMove(); } catch (InvalidOperationException) { } }
        };
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(vm.NextLyricText) or nameof(vm.TranslationText) or nameof(vm.ShowLyricTranslation)) UpdateNext(); };
        vm.PropertyChanged += changed;
        _save.Tick += (_, _) => { _save.Stop(); SaveSize(); };
        Closed += (_, _) => { vm.PropertyChanged -= changed; _save.Stop(); SaveSize(); };
        Loaded += (_, _) => { Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 24; };
    }
    private void AdjustFont(double delta)
    {
        _vm.Settings.DesktopLyricFontSize = Math.Clamp(_vm.Settings.DesktopLyricFontSize + delta, 14, 72);
        _vm.ApplyLyricAppearance(); SaveSoon();
    }
    private void SaveSoon() { _save.Stop(); _save.Start(); }
    private void SaveSize() { _vm.Settings.DesktopLyricWidth = Width; _vm.Settings.DesktopLyricHeight = Height; _vm.SaveSettings(); }
    public void ToggleLock()
    {
        Locked = !Locked; _lock.Content = Locked ? "已锁定" : "锁定";
        if (Locked) { _toolbar.Visibility = _grip.Visibility = Visibility.Hidden; _root.Background = Brushes.Transparent; }
        var handle = new WindowInteropHelper(this).Handle; long style = GetStyle(handle, -20).ToInt64(); SetStyle(handle, -20, new IntPtr(Locked ? style | 0x20 : style & ~0x20));
    }
    public void Unlock() { if (Locked) ToggleLock(); }
}
