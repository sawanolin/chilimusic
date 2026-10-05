using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace ChiliMusic;

internal sealed class MusicToolsWindow : DialogShell
{
    private readonly PlayerViewModel _vm;
    private readonly System.Windows.Controls.TabControl _tabs = new();
    private readonly ListBox _lyrics = new(), _playlistList = new(), _songs = new(), _downloads = new();
    private readonly TextBox _name = UiFactory.Input("新歌单"), _minutes = UiFactory.Input("30", 90);
    private readonly ComboBox _language = new() { Height = 36, Width = 120, DisplayMemberPath = "Language" };
    private readonly TextBlock _message = UiFactory.Text("", 11);
    private readonly List<Track> _selection;
    private List<Track> _playlistTracks = [];
    public MusicToolsWindow(PlayerViewModel vm, string tab, IEnumerable<Track>? selection = null) : base("音乐工具", 780, 650)
    {
        _vm = vm; DataContext = vm; _selection = selection?.ToList() ?? [];
        Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto }); Body.Children.Add(_tabs); Grid.SetRow(_message, 1); Body.Children.Add(_message);
        AddTab("歌词", MakeLyrics()); AddTab("歌单", MakePlaylists()); AddTab("离线", MakeOffline()); AddTab("定时", MakeSleep()); SelectTab(tab);
        vm.ActiveLyricChanged += OnActiveLyric; vm.PropertyChanged += OnVmChanged; Closed += (_, _) => { vm.ActiveLyricChanged -= OnActiveLyric; vm.PropertyChanged -= OnVmChanged; };
        Loaded += (_, _) => Execute(RefreshPlaylistsAsync);
    }
    public void SelectTab(string name) { _tabs.SelectedItem = _tabs.Items.Cast<TabItem>().FirstOrDefault(t => (string)t.Header == name) ?? _tabs.Items[0]; }
    private void AddTab(string title, UIElement content) => _tabs.Items.Add(new TabItem { Header = title, Content = content });
    private void Execute(Func<Task> action) => _vm.Run(async () => { try { await action(); _message.Text = ""; } catch (Exception e) { _message.Text = e is ApiException ? e.Message : "操作未完成，请重试。"; } });
    private UIElement MakeLyrics()
    {
        var grid = new Grid { Margin = new Thickness(12) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel(); _language.ItemsSource = _vm.LyricDocuments; _language.SelectedIndex = 0; _language.SelectionChanged += (_, _) => _vm.SetLyricDocument(_language.SelectedIndex);
        DockPanel.SetDock(_language, Dock.Right); header.Children.Add(_language); var status = UiFactory.Text("", 11); status.SetBinding(TextBlock.TextProperty, new Binding("LyricsStatus")); header.Children.Add(status); grid.Children.Add(header);
        _lyrics.ItemsSource = _vm.LyricLines; _lyrics.BorderThickness = new Thickness(0); _lyrics.ItemTemplate = LyricTemplate(); _lyrics.MouseDoubleClick += (_, _) => { if (_lyrics.SelectedItem is LyricLine { Time: double time }) _vm.Seek(Math.Max(0, time - _vm.LyricOffset)); }; Grid.SetRow(_lyrics, 1); grid.Children.Add(_lyrics);
        var footer = new StackPanel(); footer.Children.Add(UiFactory.Buttons(UiFactory.Button("桌面歌词", () => ((App)Application.Current).ToggleDesktopLyrics()), UiFactory.Button("导入 LRC", ImportLyrics), UiFactory.Button("提前 0.25 秒", () => _vm.LyricOffset += .25), UiFactory.Button("延后 0.25 秒", () => _vm.LyricOffset -= .25), UiFactory.Button("重置偏移", () => _vm.LyricOffset = 0)));
        var offset = UiFactory.Text("", 10); offset.SetBinding(TextBlock.TextProperty, new Binding("LyricOffset") { StringFormat = "歌词偏移：{0:+0.00;-0.00;0.00} 秒" }); footer.Children.Add(offset); Grid.SetRow(footer, 2); grid.Children.Add(footer); return grid;
    }
    private static DataTemplate LyricTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock), "LyricText"); text.SetBinding(TextBlock.TextProperty, new Binding("Text")); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(TextBlock.FontSizeProperty, 18d); text.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 10, 8, 10)); text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var template = new DataTemplate { VisualTree = text }; var trigger = new DataTrigger { Binding = new Binding("IsActive"), Value = true }; trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("AccentBrush"), "LyricText")); trigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold, "LyricText")); template.Triggers.Add(trigger); return template;
    }
    private void ImportLyrics() { var picker = new Microsoft.Win32.OpenFileDialog { Title = "打开歌词", Filter = "歌词文件|*.lrc;*.txt" }; if (picker.ShowDialog(this) == true) Execute(() => _vm.ImportLyricsAsync(picker.FileName)); }
    private UIElement MakePlaylists()
    {
        var grid = new Grid { Margin = new Thickness(12) }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(190) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) }; var refresh = UiFactory.Button("刷新歌单", () => Execute(RefreshPlaylistsAsync)); DockPanel.SetDock(refresh, Dock.Bottom); left.Children.Add(refresh); _playlistList.DisplayMemberPath = "Title"; _playlistList.SelectionChanged += (_, _) => Execute(LoadPlaylistAsync); left.Children.Add(_playlistList); grid.Children.Add(left);
        var right = new DockPanel(); var top = new StackPanel(); top.Children.Add(_name); top.Children.Add(UiFactory.Buttons(UiFactory.Button("新建本地歌单", () => Execute(() => CreatePlaylistAsync(false))), UiFactory.Button("新建服务器歌单", () => Execute(() => CreatePlaylistAsync(true))), UiFactory.Button("重命名", () => Execute(RenamePlaylistAsync))));
        top.Children.Add(UiFactory.Text(_selection.Count > 0 ? $"待添加：{_selection.Count} 首选中歌曲" : "添加歌曲时使用当前播放队列", 10)); DockPanel.SetDock(top, Dock.Top); right.Children.Add(top);
        var footer = UiFactory.Buttons(UiFactory.Button("播放歌单", PlayPlaylist), UiFactory.Button("添加歌曲", () => Execute(AddSongsAsync)), UiFactory.Button("移除选中", () => Execute(RemoveSongsAsync)), UiFactory.Button("导出 M3U8", ExportPlaylist), UiFactory.Button("导入 M3U8", ImportPlaylist)); DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer);
        _songs.SelectionMode = SelectionMode.Extended; _songs.ItemTemplate = (DataTemplate)Application.Current.Resources["TrackTemplate"]; right.Children.Add(_songs); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    private async Task RefreshPlaylistsAsync()
    {
        string? selected = (_playlistList.SelectedItem as LibraryItem)?.Id; var list = _vm.Playlists.Local.Select(p => new LibraryItem(p.Id, p.Name, "本地歌单", "local-playlist")).ToList();
        if (_vm.Api.Configured) try { var root = await _vm.Api.CallAsync("getPlaylists"); if (root.TryGetProperty("playlists", out var playlists) && playlists.TryGetProperty("playlist", out var rows)) foreach (var row in rows.EnumerateArray()) list.Add(new(NavidromeApiClient.Text(row, "id"), NavidromeApiClient.Text(row, "name"), "服务器歌单", "playlist")); } catch (ApiException e) { _message.Text = e.Message; }
        _playlistList.ItemsSource = list; _playlistList.SelectedItem = list.FirstOrDefault(p => p.Id == selected) ?? list.FirstOrDefault();
    }
    private async Task LoadPlaylistAsync()
    {
        var item = _playlistList.SelectedItem as LibraryItem; if (item == null) { _songs.ItemsSource = null; return; } _name.Text = item.Title;
        List<Track> tracks;
        if (item.Kind == "local-playlist") tracks = _vm.Playlists.Local.First(p => p.Id == item.Id).Tracks.ToList();
        else { var root = await _vm.Api.CallAsync("getPlaylist", default, ("id", item.Id)); tracks = NavidromeApiClient.Tracks(root, "playlist", "entry"); }
        if (!ReferenceEquals(item, _playlistList.SelectedItem)) return; _playlistTracks = tracks; _songs.ItemsSource = tracks;
    }
    private async Task CreatePlaylistAsync(bool server)
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) throw new ApiException("请输入歌单名称。");
        if (server) { if (!_vm.Api.Configured) throw new ApiException("请先连接服务器。"); await _vm.Api.CreatePlaylistAsync(_name.Text.Trim(), []); } else _vm.Playlists.Create(_name.Text, []); await RefreshPlaylistsAsync();
    }
    private async Task RenamePlaylistAsync()
    {
        if (_playlistList.SelectedItem is not LibraryItem item || string.IsNullOrWhiteSpace(_name.Text)) throw new ApiException("请选择歌单并填写名称。");
        if (item.Kind == "playlist") await _vm.Api.UpdatePlaylistAsync(item.Id, _name.Text.Trim()); else { _vm.Playlists.Local.First(p => p.Id == item.Id).Name = _name.Text.Trim(); _vm.Playlists.Save(); } await RefreshPlaylistsAsync();
    }
    private async Task AddSongsAsync() { if (_playlistList.SelectedItem is not LibraryItem item) throw new ApiException("请先选择歌单。"); var tracks = _selection.Count > 0 ? _selection : _vm.Queue.ToList(); if (tracks.Count == 0) throw new ApiException("先将歌曲加入播放队列。"); await _vm.AddToPlaylistAsync(item.Id, item.Kind == "playlist", tracks); await LoadPlaylistAsync(); }
    private async Task RemoveSongsAsync()
    {
        if (_playlistList.SelectedItem is not LibraryItem item) return; var indexes = _songs.SelectedItems.Cast<Track>().Select(t => _playlistTracks.IndexOf(t)).Where(i => i >= 0).Order().ToList(); if (indexes.Count == 0) return;
        if (item.Kind == "playlist") await _vm.Api.UpdatePlaylistAsync(item.Id, remove: indexes);
        else { var playlist = _vm.Playlists.Local.First(p => p.Id == item.Id); foreach (var index in indexes.OrderDescending()) playlist.Tracks.RemoveAt(index); _vm.Playlists.Save(); } await LoadPlaylistAsync();
    }
    private void PlayPlaylist() => Execute(async () => { if (_playlistTracks.Count == 0) throw new ApiException("歌单中没有歌曲。"); _vm.ReplaceQueue(_playlistTracks); await _vm.PlayAsync(0); });
    private void ExportPlaylist() { if (_playlistList.SelectedItem is not LibraryItem item) return; var picker = new Microsoft.Win32.SaveFileDialog { Title = "导出播放列表", Filter = "M3U8 播放列表|*.m3u8", FileName = "playlist.m3u8" }; if (picker.ShowDialog(this) == true) Execute(() => _vm.Playlists.ExportAsync(new() { Name = item.Title, Tracks = _playlistTracks }, picker.FileName, _vm.Offline)); }
    private void ImportPlaylist()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "导入播放列表", Filter = "播放列表|*.m3u8;*.m3u" }; if (picker.ShowDialog(this) != true) return;
        Execute(async () => { var paths = await PlaylistService.ImportPathsAsync(picker.FileName); await _vm.LocalLibrary.ImportAsync(paths); var set = paths.ToHashSet(StringComparer.OrdinalIgnoreCase); _vm.Playlists.Create(Path.GetFileNameWithoutExtension(picker.FileName), _vm.LocalLibrary.Snapshot.Where(t => t.LocalPath != null && set.Contains(t.LocalPath))); await RefreshPlaylistsAsync(); });
    }
    private UIElement MakeOffline()
    {
        var panel = new DockPanel { Margin = new Thickness(12) }; var top = new StackPanel(); var size = UiFactory.Text("", 12); size.SetBinding(TextBlock.TextProperty, new Binding("OfflineSize")); top.Children.Add(size); var progress = UiFactory.Text("", 11); progress.SetBinding(TextBlock.TextProperty, new Binding("DownloadStatus")); top.Children.Add(progress); top.Children.Add(UiFactory.Buttons(UiFactory.Button("下载当前歌曲", () => Execute(() => _vm.Offline.DownloadAsync(_vm.Current is Track track ? [track] : []))), UiFactory.Button("下载播放队列", () => Execute(() => _vm.Offline.DownloadAsync(_vm.Queue.ToArray()))), UiFactory.Button("取消下载", _vm.Offline.Cancel), UiFactory.Button("清空缓存", () => Execute(_vm.Offline.ClearAsync)))); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var play = UiFactory.Button("播放选中缓存歌曲", () => Execute(async () => { if (_downloads.SelectedItem is OfflineEntry entry) { _vm.Queue.Add(entry.Track); await _vm.PlayAsync(_vm.Queue.Count - 1); } })); DockPanel.SetDock(play, Dock.Bottom); panel.Children.Add(play); _downloads.DisplayMemberPath = "Track.Title"; _downloads.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("OfflineEntries")); panel.Children.Add(_downloads); return panel;
    }
    private UIElement MakeSleep()
    {
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(UiFactory.Text("定时停止播放", 19)); panel.Children.Add(UiFactory.Text("分钟（1–720）")); _minutes.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(_minutes); var finish = UiFactory.Check("到时间后等当前歌曲播放完", false); panel.Children.Add(finish);
        panel.Children.Add(UiFactory.Buttons(UiFactory.Button("开始计时", () => Execute(() => { if (!int.TryParse(_minutes.Text, out var minutes) || minutes <= 0) throw new ApiException("请输入 1–720 分钟。"); _vm.SetSleep(minutes, finish.IsChecked == true); return Task.CompletedTask; })), UiFactory.Button("播完当前歌曲停止", () => Execute(() => { _vm.SetSleep(0, true); return Task.CompletedTask; })), UiFactory.Button("取消定时", _vm.CancelSleep)));
        var status = UiFactory.Text("", 14); status.SetBinding(TextBlock.TextProperty, new Binding("SleepStatus")); panel.Children.Add(status); return panel;
    }
    private void OnActiveLyric(LyricLine? line) { if (line != null && IsVisible && _tabs.SelectedIndex == 0) _lyrics.ScrollIntoView(line); }
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(_vm.LyricDocuments)) { _language.ItemsSource = _vm.LyricDocuments; _language.SelectedIndex = 0; } }
}
