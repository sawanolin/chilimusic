using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
namespace ChiliMusic;

internal sealed class MusicToolsWindow : DialogShell
{
    private readonly PlayerViewModel _vm;
    private readonly System.Windows.Controls.TabControl _tabs = new();
    private readonly ListBox _playlistList = new(), _songs = new(), _downloads = new();
    private readonly TextBox _name = UiFactory.Input("新歌单"), _minutes = UiFactory.Input("30", 90);
    private readonly TextBlock _message = UiFactory.Text("", 11);
    private readonly List<Track> _selection;
    private List<Track> _playlistTracks = [];
    private readonly List<Action> _actionUpdates = [];
    public MusicToolsWindow(PlayerViewModel vm, string tab, IEnumerable<Track>? selection = null) : base("音乐工具", 760, 580)
    {
        MinWidth = 600; MinHeight = 460;
        _vm = vm; DataContext = vm; _selection = selection?.ToList() ?? [];
        Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto }); Body.Children.Add(_tabs); Grid.SetRow(_message, 1); Body.Children.Add(_message);
        AddTab("歌词", MakeLyrics()); AddTab("歌单", MakePlaylists()); AddTab("离线", MakeOffline()); AddTab("定时", MakeSleep()); SelectTab(tab);
        vm.PropertyChanged += OnVmChanged; vm.Queue.CollectionChanged += OnQueueChanged; Closed += (_, _) => { vm.PropertyChanged -= OnVmChanged; vm.Queue.CollectionChanged -= OnQueueChanged; };
        Loaded += (_, _) => Execute(RefreshPlaylistsAsync);
    }
    public void SelectTab(string name) { _tabs.SelectedItem = _tabs.Items.Cast<TabItem>().FirstOrDefault(t => (string)t.Header == name) ?? _tabs.Items[0]; }
    private void AddTab(string title, UIElement content) { if (content is FrameworkElement element) element.SetResourceReference(TextElement.ForegroundProperty, "TextBrush"); _tabs.Items.Add(new TabItem { Header = title, Content = content }); }
    private Button ActionButton(string text, Action action, Func<bool> enabled, bool primary = false) { var button = UiFactory.Button(text, action, primary); _actionUpdates.Add(() => button.IsEnabled = enabled()); button.IsEnabled = enabled(); return button; }
    private void UpdateActions() { foreach (var update in _actionUpdates) update(); }
    private void OnQueueChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateActions();
    private void Execute(Func<Task> action) => _vm.Run(async () => { try { await action(); _message.Text = ""; } catch (Exception e) { _message.Text = e is ApiException ? e.Message : "操作未完成，请重试。"; } });
    private UIElement MakeLyrics()
    {
        var grid = new Grid { Margin = new Thickness(12) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; var art = new System.Windows.Controls.Image { Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0) }; art.SetBinding(System.Windows.Controls.Image.SourceProperty, new Binding("Cover")); header.Children.Add(art);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var title = UiFactory.Text("", 17); title.FontWeight = FontWeights.SemiBold; title.SetBinding(TextBlock.TextProperty, new Binding("Title")); names.Children.Add(title); var artist = UiFactory.Text("", 11); artist.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); artist.SetBinding(TextBlock.TextProperty, new Binding("Artist")); names.Children.Add(artist); header.Children.Add(names); grid.Children.Add(header);
        var lyrics = new LyricsView(_vm); Grid.SetRow(lyrics, 1); grid.Children.Add(lyrics);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; footer.Children.Add(new LyricsToolbar(_vm, true) { HorizontalAlignment = HorizontalAlignment.Right }); var status = UiFactory.Text("", 10); status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); status.SetBinding(TextBlock.TextProperty, new Binding("LyricsStatus")); footer.Children.Add(status); Grid.SetRow(footer, 2); grid.Children.Add(footer); return grid;
    }
    private UIElement MakePlaylists()
    {
        var grid = new Grid { Margin = new Thickness(12) }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140, MaxWidth = 200 }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(2.6, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) }; var label = UiFactory.Text("歌单", 13); label.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(label, Dock.Top); left.Children.Add(label); var refresh = UiFactory.Button("刷新歌单", () => Execute(RefreshPlaylistsAsync)); refresh.HorizontalAlignment = HorizontalAlignment.Left; DockPanel.SetDock(refresh, Dock.Bottom); left.Children.Add(refresh); _playlistList.ItemTemplate = (DataTemplate)Application.Current.Resources["LibraryTemplate"]; _playlistList.SelectionChanged += (_, _) => Execute(LoadPlaylistAsync); left.Children.Add(UiFactory.ListWithEmptyState(_playlistList, "尚无歌单", "新建歌单或导入 M3U8。")); grid.Children.Add(left);
        var right = new DockPanel(); var top = new StackPanel(); top.Children.Add(UiFactory.Text("歌单名称", 12)); top.Children.Add(_name); top.Children.Add(UiFactory.Buttons(UiFactory.Button("新建本地歌单", () => Execute(() => CreatePlaylistAsync(false))), ActionButton("新建服务器歌单", () => Execute(() => CreatePlaylistAsync(true)), () => _vm.Api.Configured), ActionButton("重命名", () => Execute(RenamePlaylistAsync), () => _playlistList.SelectedItem != null)));
        top.Children.Add(UiFactory.Text(_selection.Count > 0 ? $"待添加：{_selection.Count} 首选中歌曲" : "添加歌曲时使用当前播放队列", 10)); DockPanel.SetDock(top, Dock.Top); right.Children.Add(top);
        var footer = UiFactory.Buttons(ActionButton("播放歌单", PlayPlaylist, () => _playlistTracks.Count > 0, true), ActionButton("添加歌曲", () => Execute(AddSongsAsync), () => _playlistList.SelectedItem != null && (_selection.Count > 0 || _vm.Queue.Count > 0)), ActionButton("移除选中", () => Execute(RemoveSongsAsync), () => _songs.SelectedItems.Count > 0), ActionButton("导出 M3U8", ExportPlaylist, () => _playlistList.SelectedItem != null), UiFactory.Button("导入 M3U8", ImportPlaylist)); DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer);
        _songs.SelectionMode = SelectionMode.Extended; _songs.ItemTemplate = (DataTemplate)Application.Current.Resources["TrackTemplate"]; _songs.SelectionChanged += (_, _) => UpdateActions(); right.Children.Add(UiFactory.ListWithEmptyState(_songs, "这里还没有歌曲", "选择歌单后，可添加选中的歌曲或当前播放队列。")); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    private async Task RefreshPlaylistsAsync()
    {
        string? selected = (_playlistList.SelectedItem as LibraryItem)?.Id; var list = _vm.Playlists.Local.Select(p => new LibraryItem(p.Id, p.Name, "本地歌单", "local-playlist")).ToList();
        if (_vm.Api.Configured) try { var root = await _vm.Api.CallAsync("getPlaylists"); if (root.TryGetProperty("playlists", out var playlists) && playlists.TryGetProperty("playlist", out var rows)) foreach (var row in rows.EnumerateArray()) list.Add(new(NavidromeApiClient.Text(row, "id"), NavidromeApiClient.Text(row, "name"), _vm.Api.SourceName + "歌单", "playlist")); } catch (ApiException e) { _message.Text = e.Message; }
        _playlistList.ItemsSource = list; _playlistList.SelectedItem = list.FirstOrDefault(p => p.Id == selected) ?? list.FirstOrDefault();
    }
    private async Task LoadPlaylistAsync()
    {
        var item = _playlistList.SelectedItem as LibraryItem; _playlistTracks = []; _songs.ItemsSource = null; UpdateActions(); if (item == null) return; _name.Text = item.Title;
        List<Track> tracks;
        if (item.Kind == "local-playlist") tracks = _vm.Playlists.Local.First(p => p.Id == item.Id).Tracks.ToList();
        else { var root = await _vm.Api.CallAsync("getPlaylist", default, ("id", item.Id)); tracks = NavidromeApiClient.Tracks(root, "playlist", "entry"); }
        if (!ReferenceEquals(item, _playlistList.SelectedItem)) return; _playlistTracks = tracks; _songs.ItemsSource = tracks; UpdateActions();
    }
    private async Task CreatePlaylistAsync(bool server)
    {
        if (string.IsNullOrWhiteSpace(_name.Text)) throw new ApiException("请输入歌单名称。");
        string id; if (server) { if (!_vm.Api.Configured) throw new ApiException("请先连接服务器。"); id = await _vm.Api.CreatePlaylistAsync(_name.Text.Trim(), []); } else id = _vm.Playlists.Create(_name.Text, []).Id; await RefreshPlaylistsAsync(); _playlistList.SelectedItem = _playlistList.Items.Cast<LibraryItem>().FirstOrDefault(p => p.Id == id);
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
        var panel = new DockPanel { Margin = new Thickness(12) }; var top = new StackPanel(); var size = UiFactory.Text("", 14); size.FontWeight = FontWeights.Medium; size.SetBinding(TextBlock.TextProperty, new Binding("OfflineSize") { StringFormat = "缓存容量：{0}" }); top.Children.Add(size); var progress = UiFactory.Text("", 11); progress.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); progress.SetBinding(TextBlock.TextProperty, new Binding("DownloadStatus")); top.Children.Add(progress); top.Children.Add(UiFactory.Buttons(ActionButton("下载当前歌曲", () => Execute(() => _vm.Offline.DownloadAsync(_vm.Current is Track track ? [track] : [])), () => !_vm.IsDownloading && _vm.Current is { IsLocal: false, IsNetease: false }, true), ActionButton("下载播放队列", () => Execute(() => _vm.Offline.DownloadAsync(_vm.Queue.Where(t => !t.IsNetease).ToArray())), () => !_vm.IsDownloading && _vm.Queue.Any(t => !t.IsLocal && !t.IsNetease)), ActionButton("取消下载", _vm.Offline.Cancel, () => _vm.IsDownloading), ActionButton("清空缓存", () => Execute(_vm.Offline.ClearAsync), () => !_vm.IsDownloading && _vm.Offline.Bytes > 0))); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var play = ActionButton("播放选中歌曲", () => Execute(async () => { if (_downloads.SelectedItem is OfflineEntry entry) { _vm.Queue.Add(entry.Track); await _vm.PlayAsync(_vm.Queue.Count - 1); } }), () => _downloads.SelectedItem != null); play.HorizontalAlignment = HorizontalAlignment.Left; DockPanel.SetDock(play, Dock.Bottom); panel.Children.Add(play); var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Track")); presenter.SetValue(ContentPresenter.ContentTemplateProperty, Application.Current.Resources["TrackTemplate"]); _downloads.ItemTemplate = new DataTemplate { VisualTree = presenter }; _downloads.SelectionChanged += (_, _) => UpdateActions(); _downloads.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("OfflineEntries")); panel.Children.Add(UiFactory.ListWithEmptyState(_downloads, "尚无离线歌曲", "下载歌曲或专辑后，即可在断网时播放。")); return panel;
    }
    private UIElement MakeSleep()
    {
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(UiFactory.Text("定时停止播放", 19)); panel.Children.Add(UiFactory.Text("分钟（1–720）")); _minutes.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(_minutes); var finish = UiFactory.Check("到时间后等当前歌曲播放完", false); panel.Children.Add(finish);
        panel.Children.Add(UiFactory.Buttons(UiFactory.Button("开始计时", () => Execute(() => { if (!int.TryParse(_minutes.Text, out var minutes) || minutes is < 1 or > 720) throw new ApiException("请输入 1–720 分钟。"); _vm.SetSleep(minutes, finish.IsChecked == true); return Task.CompletedTask; }), true), UiFactory.Button("播完当前歌曲停止", () => Execute(() => { _vm.SetSleep(0, true); return Task.CompletedTask; })), UiFactory.Button("取消定时", _vm.CancelSleep)));
        var status = UiFactory.Text("", 14); status.SetBinding(TextBlock.TextProperty, new Binding("SleepStatus")); panel.Children.Add(status); return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(_vm.Current) or nameof(_vm.IsDownloading) or nameof(_vm.OfflineEntries)) UpdateActions(); }
}
