using System.Windows;
using System.Windows.Controls;

namespace ChiliMusic;

internal sealed class EqualizerSettingsPanel : StackPanel
{
    private readonly CheckBox _enabled;
    private readonly CheckBox _wait;
    private readonly List<Slider> _bands = [];
    internal EqualizerSettingsPanel(PlayerViewModel vm)
    {
        _enabled = UiFactory.Check("启用均衡器", vm.Settings.EqualizerEnabled); _wait = UiFactory.Check("设备断开后暂停等待", vm.Settings.DeviceDisconnectPause);
        var preset = UiFactory.Combo(["平直", "低音增强", "人声", "明亮"]); preset.Width = 120;
        var sliders = new Grid { Height = 150 }; string[] names = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
        for (int i = 0; i < 10; i++)
        {
            sliders.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); var column = new DockPanel(); var value = UiFactory.Text(vm.Settings.Equalizer.ElementAtOrDefault(i).ToString("+0;-0;0"), 9); value.TextAlignment = TextAlignment.Center; DockPanel.SetDock(value, Dock.Top); column.Children.Add(value); var label = UiFactory.Text(names[i], 9); label.TextAlignment = TextAlignment.Center; DockPanel.SetDock(label, Dock.Bottom); column.Children.Add(label);
            var band = new Slider { Minimum = -12, Maximum = 12, Value = vm.Settings.Equalizer.ElementAtOrDefault(i), TickFrequency = 1, IsSnapToTickEnabled = true, Orientation = Orientation.Vertical, Width = 22, ToolTip = names[i] + " Hz · −12 至 +12 dB" }; UiFactory.MakeVertical(band); band.ValueChanged += (_, _) => value.Text = band.Value.ToString("+0;-0;0"); _bands.Add(band); column.Children.Add(band); Grid.SetColumn(column, i); sliders.Children.Add(column);
        }
        preset.SelectionChanged += (_, _) => { double[][] presets = [new double[10], [6,5,3,1,0,0,0,0,0,0], [-2,-1,0,2,3,3,2,0,-1,-2], [0,0,-1,-1,0,1,2,3,4,4]]; if (preset.SelectedIndex >= 0) for (int i = 0; i < 10; i++) _bands[i].Value = presets[preset.SelectedIndex][i]; };
        Children.Add(UiFactory.Card("均衡器", UiFactory.Toolbar(_enabled, preset), sliders)); Children.Add(UiFactory.Card("设备恢复", _wait, UiFactory.Text("启用后，输出设备断开时暂停并保留进度；关闭时切换到系统默认输出。", 10)));
    }
    internal void Apply(AppSettings copy) { copy.EqualizerEnabled = _enabled.IsChecked == true; copy.Equalizer = _bands.Select(b => b.Value).ToArray(); copy.DeviceDisconnectPause = _wait.IsChecked == true; }
}
