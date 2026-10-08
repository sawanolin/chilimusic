using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Control = System.Windows.Controls.Control;

namespace ChiliMusic;

internal sealed class LyricsView : Grid
{
    private readonly PlayerViewModel _vm;
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private DateTime _manualUntil;
    private ScrollViewer? _scroll;
    private bool _centerPending;
    private readonly DispatcherTimer _resume = new() { Interval = TimeSpan.FromSeconds(5) };
    private static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register("ScrollOffset", typeof(double), typeof(LyricsView), new PropertyMetadata(0d, (owner, args) => ((LyricsView)owner)._scroll?.ScrollToVerticalOffset((double)args.NewValue)));
    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(nameof(ContentPadding), typeof(Thickness), typeof(LyricsView), new PropertyMetadata(new Thickness(0)));
    public Thickness ContentPadding { get => (Thickness)GetValue(ContentPaddingProperty); private set => SetValue(ContentPaddingProperty, value); }
    public LyricsView(PlayerViewModel vm, bool immersive = false)
    {
        _vm = vm; DataContext = vm;
        _list.ItemsSource = vm.LyricLines; _list.ItemTemplate = Template(immersive);
        var row = new FrameworkElementFactory(typeof(Border)); row.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty)); row.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var rowStyle = new Style(typeof(ListBoxItem)); rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, immersive ? 15 : 8, 12, immersive ? 15 : 8))); rowStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = row })); _list.ItemContainerStyle = rowStyle;
        _list.SetValue(ScrollViewer.CanContentScrollProperty, false); _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled); _list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(ContentPadding)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(LyricsView), 1) }); _list.ItemsPanel = new ItemsPanelTemplate(panel);
        var mask = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) }; mask.GradientStops.Add(new(Colors.Transparent, 0)); mask.GradientStops.Add(new(Colors.White, .12)); mask.GradientStops.Add(new(Colors.White, .88)); mask.GradientStops.Add(new(Colors.Transparent, 1)); _list.OpacityMask = mask;
        Children.Add(UiFactory.ListWithEmptyState(_list, "暂无歌词", "可匹配网易云歌词，或导入 LRC 文件。"));
        _list.PreviewMouseWheel += (_, _) => PauseFollowing();
        _list.PreviewMouseDown += (_, _) => PauseFollowing();
        _resume.Tick += (_, _) => { _resume.Stop(); _manualUntil = DateTime.MinValue; Center(); };
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItem is LyricLine { Time: double time }) { vm.Seek(Math.Max(0, time - vm.LyricOffset)); _manualUntil = DateTime.MinValue; Center(); } };
        SizeChanged += (_, _) => { ContentPadding = new Thickness(0, Math.Max(30, ActualHeight / 2 - 40), 0, Math.Max(30, ActualHeight / 2 - 40)); Center(); };
        Loaded += (_, _) => { vm.ActiveLyricChanged += OnActive; vm.PropertyChanged += OnChanged; Center(); };
        Unloaded += (_, _) => { vm.ActiveLyricChanged -= OnActive; vm.PropertyChanged -= OnChanged; _resume.Stop(); StopAnimation(); _scroll = null; };
    }
    private void StopAnimation() { double current = _scroll?.VerticalOffset ?? 0; BeginAnimation(ScrollOffsetProperty, null); SetCurrentValue(ScrollOffsetProperty, current); }
    private void PauseFollowing() { StopAnimation(); _manualUntil = DateTime.UtcNow.AddSeconds(5); _resume.Stop(); _resume.Start(); }
    private void OnActive(LyricLine? line) { if (DateTime.UtcNow >= _manualUntil) Center(); }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(_vm.LyricDocuments) or nameof(_vm.ShowLyricTranslation)) { _manualUntil = DateTime.MinValue; Dispatcher.BeginInvoke(Center, DispatcherPriority.Loaded); } }
    internal void Center()
    {
        if (!IsVisible || DateTime.UtcNow < _manualUntil || _centerPending) return;
        _centerPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _centerPending = false; if (!IsVisible || DateTime.UtcNow < _manualUntil) return;
            var active = _vm.LyricLines.FirstOrDefault(l => l.IsActive) ?? _vm.LyricLines.FirstOrDefault(); if (active == null) return;
            var scroll = _scroll ??= FindScroll(_list); var row = _list.ItemContainerGenerator.ContainerFromItem(active) as FrameworkElement;
            if (scroll == null || row == null) return;
            var point = row.TranslatePoint(new Point(0, row.ActualHeight / 2), scroll); double target = Math.Clamp(scroll.VerticalOffset + point.Y - scroll.ViewportHeight / 2, 0, scroll.ScrollableHeight);
            if (Math.Abs(target - scroll.VerticalOffset) < .5) return;
            if (!SystemParameters.ClientAreaAnimation) { StopAnimation(); SetCurrentValue(ScrollOffsetProperty, target); return; }
            BeginAnimation(ScrollOffsetProperty, new DoubleAnimation(scroll.VerticalOffset, target, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
        }, DispatcherPriority.Loaded);
    }
    private static ScrollViewer? FindScroll(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) { var found = FindScroll(VisualTreeHelper.GetChild(element, i)); if (found != null) return found; }
        return null;
    }
    private static DataTemplate Template(bool immersive)
    {
        var stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 9, 12, 9));
        var text = new FrameworkElementFactory(typeof(TextBlock), "Words"); text.SetBinding(TextBlock.TextProperty, new Binding("Text")); text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(TextBlock.FontSizeProperty, immersive ? 24d : 21d); text.SetValue(UIElement.OpacityProperty, .55); text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); stack.AppendChild(text);
        var translation = new FrameworkElementFactory(typeof(TextBlock), "Translation"); translation.SetBinding(TextBlock.TextProperty, new Binding("Translation")); translation.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center); translation.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); translation.SetValue(TextBlock.FontSizeProperty, 13d); translation.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0)); translation.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); stack.AppendChild(translation);
        var template = new DataTemplate { VisualTree = stack };
        var active = new DataTrigger { Binding = new Binding("IsActive"), Value = true }; active.Setters.Add(new Setter(UIElement.OpacityProperty, 1d, "Words")); active.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold, "Words")); active.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush"), "Translation")); template.Triggers.Add(active);
        var empty = new DataTrigger { Binding = new Binding("Translation"), Value = "" }; empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "Translation")); template.Triggers.Add(empty);
        var hidden = new DataTrigger { Binding = new Binding("DataContext.ShowLyricTranslation") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(LyricsView), 1) }, Value = false }; hidden.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "Translation")); template.Triggers.Add(hidden); return template;
    }
}
