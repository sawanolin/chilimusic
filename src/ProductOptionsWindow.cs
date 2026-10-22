using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabControl = System.Windows.Controls.TabControl;

namespace ChiliMusic;

internal sealed class ProductOptionsWindow : DialogShell
{
    internal ProductOptionsWindow(PlayerViewModel vm, bool lyrics = false) : base(lyrics ? "歌词外观" : "音频与操作", 680, 650)
    {
        MinWidth = 580; MinHeight = 490;
        Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var status = UiFactory.Text("", 11); var tabs = new TabControl(); ReferenceLayout.Tabs(tabs);
        var audioPanel = new StackPanel(); var operationPanel = new StackPanel(); var lyricPanel = new StackPanel();
        UIElement Scroll(UIElement element) => new ScrollViewer { Content = element, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(12) };
        var eqEnabled = UiFactory.Check("启用均衡器", vm.Settings.EqualizerEnabled); var eqSliders = new List<Slider>(); var disconnect = UiFactory.Check("设备断开后暂停等待", vm.Settings.DeviceDisconnectPause);
        var keys = new Dictionary<string, ComboBox>(); var fileAction = UiFactory.Combo(["立即播放", "追加队列"], vm.Settings.FileOpenAction == "append" ? 1 : 0);
        var font = new Slider { Minimum = 14, Maximum = 38, Value = vm.Settings.LyricFontSize, TickFrequency = 1, IsSnapToTickEnabled = true, ToolTip = "歌词字号" };
        var spacing = new Slider { Minimum = 0, Maximum = 24, Value = vm.Settings.LyricSpacing, TickFrequency = 1, IsSnapToTickEnabled = true, ToolTip = "歌词行距" };
        var opacity = new Slider { Minimum = .2, Maximum = 1, Value = vm.Settings.LyricBackgroundOpacity, TickFrequency = .1, IsSnapToTickEnabled = true, ToolTip = "桌面歌词背景透明度" };
        var color = UiFactory.Input(vm.Settings.DesktopLyricColor); color.ToolTip = "留空跟随主题，或输入 #RRGGBB";
        UIElement NumericSlider(Slider slider, string suffix, string format = "0")
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 8) }; var value = UiFactory.Text(slider.Value.ToString(format) + suffix, 11); value.Width = 65; value.TextAlignment = TextAlignment.Right; DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); slider.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(slider); slider.ValueChanged += (_, _) => value.Text = slider.Value.ToString(format) + suffix; return row;
        }
        if (lyrics)
        {
            var preview = new Border { Height = 160, CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 18), ClipToBounds = true }; preview.SetResourceReference(BackgroundProperty, "SelectedBrush");
            var previewLines = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var first = UiFactory.Text(vm.LyricText is "暂无歌词" or "尚未播放" ? "夜色轻轻落在窗前" : vm.LyricText, font.Value); first.TextAlignment = TextAlignment.Center; first.FontWeight = FontWeights.SemiBold; first.MaxHeight = 85; previewLines.Children.Add(first); var next = UiFactory.Text("而你的身影还在我心里面", 13); next.TextAlignment = TextAlignment.Center; next.SetResourceReference(ForegroundProperty, "MutedBrush"); previewLines.Children.Add(next); preview.Child = previewLines; lyricPanel.Children.Add(preview);
            font.ValueChanged += (_, _) => first.FontSize = font.Value; spacing.ValueChanged += (_, _) => next.Margin = new Thickness(0, spacing.Value, 0, 0);
            lyricPanel.Children.Add(UiFactory.Card("文字与间距", UiFactory.Row("字号", NumericSlider(font, " px")), UiFactory.Row("行距", NumericSlider(spacing, " px"))));
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
            foreach (string hex in new[] { "", "#FFFFFF", "#263443", "#3478F6", "#B17BEB", "#F59E7C" }) { var swatch = UiFactory.Button(hex.Length == 0 ? "跟随主题" : "●", () => color.Text = hex); swatch.Width = hex.Length == 0 ? 85 : 34; swatch.Padding = new Thickness(0); if (hex.Length > 0) swatch.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); swatches.Children.Add(swatch); }
            lyricPanel.Children.Add(UiFactory.Card("桌面歌词", UiFactory.Row("背景透明度", NumericSlider(opacity, "", "0.0")), swatches, UiFactory.Row("文字颜色", color))); Body.Children.Add(Scroll(lyricPanel));
        }
        else
        {
            audioPanel.Children.Add(UiFactory.Card("播放设备", disconnect, UiFactory.Text("关闭时，设备断开会从原进度切到系统默认输出。", 11)));
            var presets = UiFactory.Combo(["平直", "低音增强", "人声", "明亮"]); presets.Width = 130;
            var equalizer = new Grid { Height = 155, Margin = new Thickness(0, 6, 0, 0) }; var names = new[] { "31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };
            for (int i = 0; i < 10; i++)
            {
                equalizer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); var band = new DockPanel { Margin = new Thickness(3, 0, 3, 0) };
                var value = UiFactory.Text(vm.Settings.Equalizer.ElementAtOrDefault(i).ToString("+0;-0;0"), 10); value.TextAlignment = TextAlignment.Center; DockPanel.SetDock(value, Dock.Top); band.Children.Add(value);
                var label = UiFactory.Text(names[i], 10); label.TextAlignment = TextAlignment.Center; DockPanel.SetDock(label, Dock.Bottom); band.Children.Add(label);
                var slider = new Slider { Minimum = -12, Maximum = 12, Value = vm.Settings.Equalizer.ElementAtOrDefault(i), TickFrequency = 1, IsSnapToTickEnabled = true, Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, Width = 24, ToolTip = names[i] + " Hz · −12 至 +12 dB" }; UiFactory.MakeVertical(slider); slider.ValueChanged += (_, _) => value.Text = slider.Value.ToString("+0;-0;0"); eqSliders.Add(slider); band.Children.Add(slider); Grid.SetColumn(band, i); equalizer.Children.Add(band);
            }
            presets.SelectionChanged += (_, _) => { double[][] values = [new double[10], [6,5,3,1,0,0,0,0,0,0], [-2,-1,0,2,3,3,2,0,-1,-2], [0,0,-1,-1,0,1,2,3,4,4]]; if (presets.SelectedIndex >= 0) for (int i = 0; i < 10; i++) eqSliders[i].Value = values[presets.SelectedIndex][i]; };
            var profile = UiFactory.More(("保存此设备配置", () => { vm.Settings.AudioProfiles[vm.Settings.AudioDevice] = new() { Exclusive = vm.Settings.Exclusive, ReplayGain = vm.Settings.ReplayGain, Preamp = vm.Settings.ReplayGainPreamp, EqualizerEnabled = eqEnabled.IsChecked == true, Equalizer = eqSliders.Select(s => s.Value).ToArray() }; vm.SaveSettings(); status.Text = "已保存此设备配置"; }, null), ("应用此设备配置", () => { if (vm.Settings.AudioProfiles.TryGetValue(vm.Settings.AudioDevice, out var saved)) { var copy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(vm.Settings))!; copy.Exclusive = saved.Exclusive; copy.ReplayGain = saved.ReplayGain; copy.ReplayGainPreamp = saved.Preamp; copy.EqualizerEnabled = saved.EqualizerEnabled; copy.Equalizer = saved.Equalizer.ToArray(); vm.UpdateSettings(copy, vm.Password); eqEnabled.IsChecked = saved.EqualizerEnabled; for (int i = 0; i < 10; i++) eqSliders[i].Value = saved.Equalizer.ElementAtOrDefault(i); status.Text = "已应用此设备配置"; } }, () => vm.Settings.AudioProfiles.ContainsKey(vm.Settings.AudioDevice)));
            audioPanel.Children.Add(UiFactory.Card("均衡器", UiFactory.Toolbar(eqEnabled, presets, profile), equalizer, UiFactory.Text("均衡器增益后启用限幅保护，防止削波。", 10)));
            audioPanel.Children.Add(UiFactory.Card("实际输出", UiFactory.Text(vm.DetailedOutput, 11)));
            foreach (var (label, key) in new[] { ("下一首", vm.Settings.NextKey), ("上一首", vm.Settings.PreviousKey), ("音量增加", vm.Settings.VolumeUpKey), ("音量减少", vm.Settings.VolumeDownKey) }) { var picker = UiFactory.Combo(new[] { "关闭" }.Concat(Enumerable.Range(1, 12).Select(i => "F" + i)), key == 0 ? 0 : Math.Clamp(key - 0x6f, 0, 12)); keys[label] = picker; operationPanel.Children.Add(UiFactory.Row(label, picker)); }
            operationPanel.Children.Add(UiFactory.Text("以上按键在启用全局快捷键时生效。", 11)); operationPanel.Children.Add(UiFactory.Card("文件打开", UiFactory.Row("打开方式", fileAction), UiFactory.Button("注册 Windows 文件打开入口", () => { SystemIntegration.Register(); status.Text = "已注册，可在 Windows 默认应用中选择 chilimusic"; })));
            tabs.Items.Add(new TabItem { Header = "音频输出", Content = Scroll(audioPanel) }); tabs.Items.Add(new TabItem { Header = "快捷键与文件", Content = Scroll(operationPanel) }); Body.Children.Add(tabs);
        }
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) }; footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.Children.Add(status);
        var buttons = UiFactory.Toolbar(UiFactory.Button("取消", Close), UiFactory.Button("保存", () =>
        {
            try
            {
                if (lyrics) { if (color.Text.Trim().Length > 0) _ = ColorConverter.ConvertFromString(color.Text.Trim()); vm.Settings.LyricFontSize = font.Value; vm.Settings.LyricSpacing = spacing.Value; vm.Settings.LyricBackgroundOpacity = opacity.Value; vm.Settings.DesktopLyricColor = color.Text.Trim(); vm.ApplyLyricAppearance(); }
                else
                {
                    int Key(string label) => keys[label].SelectedIndex == 0 ? 0 : 0x6f + keys[label].SelectedIndex; var all = new[] { vm.Settings.PlayKey, vm.Settings.PauseKey, Key("下一首"), Key("上一首"), Key("音量增加"), Key("音量减少") }.Where(k => k != 0).ToArray(); if (all.Distinct().Count() != all.Length) throw new ApiException("全局快捷键不能重复。"); if (all.Any(k => !FunctionKeyService.KeyAvailable(k))) throw new ApiException("某个快捷键已被系统或其他程序占用。");
                    vm.Settings.DeviceDisconnectPause = disconnect.IsChecked == true; vm.Settings.EqualizerEnabled = eqEnabled.IsChecked == true; vm.Settings.Equalizer = eqSliders.Select(s => s.Value).ToArray(); vm.Settings.NextKey = Key("下一首"); vm.Settings.PreviousKey = Key("上一首"); vm.Settings.VolumeUpKey = Key("音量增加"); vm.Settings.VolumeDownKey = Key("音量减少"); vm.Settings.FileOpenAction = fileAction.SelectedIndex == 1 ? "append" : "play"; vm.ApplyAudioProcessing();
                }
                vm.SaveSettings(); Close();
            }
            catch (Exception error) { status.Text = error is ApiException ? error.Message : "设置格式无效，请检查颜色或音频参数。"; }
        }, true)); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons); Grid.SetRow(footer, 1); Body.Children.Add(footer); Loaded += (_, _) => MainWindow.NameAccessibleControls(this);
    }
}
