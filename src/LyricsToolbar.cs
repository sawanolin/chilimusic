using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ChiliMusic;

internal sealed class LyricsToolbar : WrapPanel
{
    private readonly PlayerViewModel _vm;
    private readonly ComboBox _language = new() { Height = 34, Width = 86, DisplayMemberPath = "Language", Margin = new Thickness(0, 3, 8, 4) };
    private bool _refreshing;
    private readonly bool _compact;
    internal LyricsToolbar(PlayerViewModel vm, bool compact = false)
    {
        _vm = vm; _compact = compact; DataContext = vm;
        Children.Add(_language); _language.SelectionChanged += (_, _) => { if (!_refreshing && _language.SelectedIndex >= 0) vm.SetLyricDocument(_language.SelectedIndex); };
        Loaded += (_, _) => { vm.PropertyChanged += Changed; Refresh(); }; Unloaded += (_, _) => vm.PropertyChanged -= Changed;
        if (compact) { MakeCompact(vm); return; }
        var translation = new CheckBox { Content = "翻译", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }; translation.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("ShowLyricTranslation") { Mode = BindingMode.TwoWay }); Children.Add(translation);
        Children.Add(UiFactory.Button("匹配歌词", () => ((App)Application.Current).ShowLyricsMatch()));
        Children.Add(UiFactory.Button("导入 LRC", () => { var picker = new Microsoft.Win32.OpenFileDialog { Title = "打开歌词", Filter = "歌词文件|*.lrc;*.txt" }; if (picker.ShowDialog(Window.GetWindow(this)) == true) vm.Run(() => vm.ImportLyricsAsync(picker.FileName)); }));
        var offset = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        var less = UiFactory.Button("−", () => vm.LyricOffset += .25); less.Width = 32; less.Padding = new Thickness(0); less.ToolTip = "歌词提前 0.25 秒"; var more = UiFactory.Button("+", () => vm.LyricOffset -= .25); more.Width = 32; more.Padding = new Thickness(0); more.ToolTip = "歌词延后 0.25 秒";
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), MinWidth = 62, TextAlignment = TextAlignment.Center }; value.SetBinding(TextBlock.TextProperty, new Binding("LyricOffset") { StringFormat = "{0:+0.00;-0.00;0.00} 秒" }); value.ToolTip = "双击重置歌词偏移"; value.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) vm.LyricOffset = 0; }; offset.Children.Add(less); offset.Children.Add(value); offset.Children.Add(more); Children.Add(offset);
        var desktop = UiFactory.Button("桌面歌词", () => ((App)Application.Current).ToggleDesktopLyrics()); Children.Add(desktop);
    }
    private void Import()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "打开歌词", Filter = "歌词文件|*.lrc;*.txt" }; if (picker.ShowDialog(Window.GetWindow(this)) == true) _vm.Run(() => _vm.ImportLyricsAsync(picker.FileName));
    }
    private void MakeCompact(PlayerViewModel vm)
    {
        _language.Width = 78; _language.Height = 32;
        var translation = new System.Windows.Controls.Primitives.ToggleButton { Content = "翻译", Width = 62, Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 3, 10, 4), FontSize = 12 };
        translation.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("ShowLyricTranslation") { Mode = BindingMode.TwoWay });
        var border = new FrameworkElementFactory(typeof(Border), "Pill"); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(16)); border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); border.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); var label = new FrameworkElementFactory(typeof(ContentPresenter)); label.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(label); var template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton)) { VisualTree = border }; var selected = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SelectedBrush"), "Pill")); template.Triggers.Add(selected); translation.Template = template; translation.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextBrush"); Children.Add(translation);
        Button Icon(string glyph, string tip, Action action) { var button = new Button { Content = glyph, Width = 32, Height = 32, FontSize = 17, Padding = new Thickness(0), Margin = new Thickness(0, 3, 4, 4), ToolTip = tip }; button.SetResourceReference(StyleProperty, "IconButton"); button.Click += (_, _) => action(); Children.Add(button); return button; }
        Icon("\uE70F", "匹配歌词", () => ((App)Application.Current).ShowLyricsMatch());
        var menu = new ContextMenu(); void Item(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("导入 LRC", Import); Item("桌面歌词", () => ((App)Application.Current).ToggleDesktopLyrics()); menu.Items.Add(new Separator()); var offset = new MenuItem { IsEnabled = false }; menu.Items.Add(offset); Item("提前 0.25 秒", () => vm.LyricOffset += .25); Item("延后 0.25 秒", () => vm.LyricOffset -= .25); Item("重置偏移", () => vm.LyricOffset = 0); menu.Opened += (_, _) => offset.Header = $"歌词偏移：{vm.LyricOffset:+0.00;-0.00;0.00} 秒";
        Button? more = null; more = Icon("\uE712", "更多歌词选项", () => { menu.PlacementTarget = more; menu.IsOpen = true; });
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(_vm.LyricDocuments) or nameof(_vm.LyricDocumentIndex)) Refresh(); }
    private void Refresh() { _refreshing = true; _language.ItemsSource = _vm.LyricDocuments.Count == 0 && _compact ? new List<LyricsDocument> { new("原词", []) } : _vm.LyricDocuments; _language.SelectedIndex = Math.Max(0, _vm.LyricDocumentIndex); _language.IsEnabled = _vm.LyricDocuments.Count > 1; _language.Visibility = _compact || _vm.LyricDocuments.Count > 1 ? Visibility.Visible : Visibility.Collapsed; _refreshing = false; }
}
