using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace ChiliMusic;

internal sealed class SettingsOptionsPanel
{
    private readonly Dictionary<string, FrameworkElement> _pages = [];
    public FrameworkElement Page(string name) => _pages[name];
    public UIElement AccentOption => _accent;
    private readonly PlayerViewModel _vm;
    private readonly ComboBox _device, _gain, _playKey, _pauseKey;
    private readonly TextBox _preamp, _width, _capacity;
    private readonly CheckBox _prefetch, _taskbar, _artist, _cover, _monitors, _hotkeys, _hardware, _accent;
    private readonly ListBox _folders;
    private readonly TextBlock _status;
    public SettingsOptionsPanel(PlayerViewModel vm)
    {
        _vm = vm; var s = vm.Settings;
        _device = new ComboBox { ItemsSource = vm.Player.AudioDevices(), DisplayMemberPath = "Description", SelectedValuePath = "Name", SelectedValue = s.AudioDevice, Height = 36, Margin = new Thickness(0, 3, 0, 7) }; if (_device.SelectedIndex < 0) _device.SelectedIndex = 0;
        _gain = UiFactory.Combo(["关闭", "按歌曲", "按专辑"], s.ReplayGain == "track" ? 1 : s.ReplayGain == "album" ? 2 : 0); _preamp = UiFactory.Input(s.ReplayGainPreamp.ToString(CultureInfo.InvariantCulture)); _prefetch = UiFactory.Check("预加载下一首", s.PrefetchNext);
        var deviceRow = new DockPanel(); var refresh = UiFactory.Button("刷新", () => { var selected = _device.SelectedValue; _device.ItemsSource = vm.Player.AudioDevices(); _device.SelectedValue = selected; if (_device.SelectedIndex < 0) _device.SelectedIndex = 0; }); refresh.Margin = new Thickness(8, 3, 0, 7); DockPanel.SetDock(refresh, Dock.Right); deviceRow.Children.Add(refresh); deviceRow.Children.Add(_device);
        _preamp.Width = 110; _preamp.HorizontalAlignment = HorizontalAlignment.Left;
        _gain.ToolTip = "ReplayGain：关闭、按歌曲或按专辑"; _preamp.ToolTip = "−12 到 12 dB";
        _pages["音频输出"] = UiFactory.Card("音频输出", UiFactory.Row("输出设备", deviceRow), UiFactory.Row("响度均衡", _gain), UiFactory.Row("额外增益（dB）", _preamp), UiFactory.Text("使用歌曲的响度标签；没有标签时保持原音量。", 11), _prefetch);
        _taskbar = UiFactory.Check("显示任务栏播放控件", s.TaskbarEnabled); _width = UiFactory.Input(s.TaskbarWidth.ToString()); _artist = UiFactory.Check("显示歌手", s.TaskbarShowArtist); _cover = UiFactory.Check("显示封面", s.TaskbarShowCover); _monitors = UiFactory.Check("在所有显示器任务栏显示", s.TaskbarAllMonitors);
        _width.Width = 110; _width.HorizontalAlignment = HorizontalAlignment.Left;
        _pages["任务栏"] = UiFactory.Card("任务栏", _taskbar, UiFactory.Text("控件宽度（240–480）"), _width, _artist, _cover, _monitors);
        _hotkeys = UiFactory.Check("启用全局播放快捷键", s.HotkeysEnabled); _playKey = UiFactory.Combo(Enumerable.Range(1, 12).Select(i => "F" + i), Math.Clamp(s.PlayKey - 0x70, 0, 11)); _pauseKey = UiFactory.Combo(Enumerable.Range(1, 12).Select(i => "F" + i), Math.Clamp(s.PauseKey - 0x70, 0, 11)); _hardware = UiFactory.Check("接管键盘的投影 / 表情功能键", s.RemapHardwareShortcuts); _hardware.ToolTip = "部分键盘使用 Win+P 和 Win+.。选中后改为播放和暂停，退出播放器后恢复。";
        _pages["快捷键"] = UiFactory.Card("快捷键", _hotkeys, UiFactory.Text("播放"), _playKey, UiFactory.Text("暂停"), _pauseKey, _hardware);
        _accent = UiFactory.Check("从专辑封面提取强调色", s.CoverAccent);
        _capacity = UiFactory.Input(s.OfflineCacheMb.ToString(), 140); _capacity.HorizontalAlignment = HorizontalAlignment.Left; _pages["离线缓存"] = UiFactory.Card("离线缓存", UiFactory.Text("容量上限（64–65536 MB）"), _capacity, UiFactory.Text("空间不足时清理最久未使用的缓存歌曲。正在播放的歌曲会保留。", 11));
        _folders = new ListBox { ItemsSource = s.MusicFolders, MaxHeight = 160, MinHeight = 120 }; _status = UiFactory.Text("", 10);
        _pages["本地音乐目录"] = UiFactory.Card("本地音乐目录", UiFactory.ListWithEmptyState(_folders, "尚未添加音乐目录", "添加后自动扫描子目录并跟踪文件变化。"), UiFactory.Buttons(UiFactory.Button("添加目录", AddFolder), UiFactory.Button("重新扫描", () => vm.Run(vm.LocalLibrary.ScanAsync)), UiFactory.Button("移除目录", () => { if (_folders.SelectedItem is string path) { vm.LocalLibrary.RemoveFolder(path); _folders.Items.Refresh(); } })), _status);
    }
    private void AddFolder()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择音乐文件夹", Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(_folders)) == true) _vm.Run(async () => { await _vm.AddFolderAsync(picker.FolderName); _folders.Items.Refresh(); _status.Text = "已添加，文件变化会自动更新"; });
    }
    public void Apply(AppSettings copy)
    {
        if (!double.TryParse(_preamp.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var preamp) || preamp is < -12 or > 12) throw new ApiException("响度增益范围为 −12 到 12 dB。");
        if (!int.TryParse(_width.Text, out var width) || width is < 240 or > 480) throw new ApiException("任务栏宽度范围为 240–480。");
        if (!int.TryParse(_capacity.Text, out var capacity) || capacity is < 64 or > 65536) throw new ApiException("离线缓存容量范围为 64–65536 MB。");
        if (_hotkeys.IsChecked == true && _playKey.SelectedIndex == _pauseKey.SelectedIndex) throw new ApiException("播放和暂停不能使用同一个快捷键。");
        if (_hotkeys.IsChecked == true && (!FunctionKeyService.KeyAvailable(0x70 + _playKey.SelectedIndex) || !FunctionKeyService.KeyAvailable(0x70 + _pauseKey.SelectedIndex))) throw new ApiException("所选快捷键已被其他程序或系统占用，请换一个键。");
        copy.AudioDevice = _device.SelectedValue as string ?? "auto"; copy.ReplayGain = _gain.SelectedIndex == 1 ? "track" : _gain.SelectedIndex == 2 ? "album" : "no"; copy.ReplayGainPreamp = preamp; copy.PrefetchNext = _prefetch.IsChecked == true;
        copy.TaskbarEnabled = _taskbar.IsChecked == true; copy.TaskbarWidth = width; copy.TaskbarShowArtist = _artist.IsChecked == true; copy.TaskbarShowCover = _cover.IsChecked == true; copy.TaskbarAllMonitors = _monitors.IsChecked == true;
        copy.HotkeysEnabled = _hotkeys.IsChecked == true; copy.PlayKey = 0x70 + _playKey.SelectedIndex; copy.PauseKey = 0x70 + _pauseKey.SelectedIndex; copy.RemapHardwareShortcuts = _hardware.IsChecked == true; copy.CoverAccent = _accent.IsChecked == true; copy.OfflineCacheMb = capacity;
    }
}
