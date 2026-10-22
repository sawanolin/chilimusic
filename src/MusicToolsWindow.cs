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
    private TextBlock? _playlistHeading, _playlistCaption;
    private Image? _playlistCover;
    private System.Windows.Controls.ProgressBar? _capacityBar;
    private readonly TextBox _description = UiFactory.Input();
    private readonly TextBlock _message = UiFactory.Text("", 11);
    private readonly List<Track> _selection;
    private List<Track> _playlistTracks = [];
    private readonly List<Action> _actionUpdates = [];
    public MusicToolsWindow(PlayerViewModel vm, string tab, IEnumerable<Track>? selection = null) : base("音乐工具", 760, 580)
    {
        MinWidth = 600; MinHeight = 460; ReferenceLayout.Tabs(_tabs);
        _vm = vm; DataContext = vm; _selection = selection?.ToList() ?? [];
        Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto }); Body.Children.Add(_tabs); Grid.SetRow(_message, 1); Body.Children.Add(_message);
        _tabs.SelectionChanged += (_, _) => { if (_tabs.SelectedItem is TabItem selected) HeaderTitle.Text = (string)selected.Header switch { "歌单" => "歌单管理", "离线" => "离线下载", "定时" => "定时停止", _ => "同步歌词" }; };
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
        var grid = new Grid { Margin = new Thickness(10) }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(168) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; var add = UiFactory.More(("新建本地歌单", () => EditName(true, false), null), ("新建服务器歌单", () => EditName(true, true), () => _vm.Api.Configured && !_vm.IsLocalCatalog), ("导入 M3U8…", ImportPlaylist, null), ("刷新歌单", () => Execute(RefreshPlaylistsAsync), null)); DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add); heading.Children.Add(UiFactory.Text("我的歌单", 12)); DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading);
        _playlistList.ItemTemplate = (DataTemplate)Application.Current.Resources["LibraryTemplate"]; _playlistList.SelectionChanged += (_, _) => Execute(LoadPlaylistAsync); left.Children.Add(UiFactory.ListWithEmptyState(_playlistList, "尚无歌单", "点击上方菜单创建歌单。")); grid.Children.Add(left);
        var right = new DockPanel(); var top = new DockPanel(); var names = new StackPanel(); var art = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 14, 12) }; art.SetResourceReference(BackgroundProperty, "SelectedBrush"); var coverGrid = new Grid(); var note = UiFactory.Text("♪", 30); note.HorizontalAlignment = HorizontalAlignment.Center; note.VerticalAlignment = VerticalAlignment.Center; coverGrid.Children.Add(note); _playlistCover = new Image { Stretch = System.Windows.Media.Stretch.UniformToFill }; coverGrid.Children.Add(_playlistCover); art.Child = coverGrid; top.Children.Add(art); top.Children.Add(names); _playlistHeading = UiFactory.Text("选择歌单", 20); _playlistHeading.FontWeight = FontWeights.SemiBold; names.Children.Add(_playlistHeading); _playlistCaption = UiFactory.Text("", 11); _playlistCaption.SetResourceReference(ForegroundProperty, "MutedBrush"); names.Children.Add(_playlistCaption);
        var more = UiFactory.More(("重命名…", () => EditName(false, false), () => _playlistList.SelectedItem != null), ("编辑说明…", EditDescription, () => (_playlistList.SelectedItem as LibraryItem)?.Kind == "local-playlist"), ("设置封面…", SelectPlaylistCover, () => (_playlistList.SelectedItem as LibraryItem)?.Kind == "local-playlist"), ("导出 M3U8…", ExportPlaylist, () => _playlistList.SelectedItem != null));
        names.Children.Add(UiFactory.Toolbar(ActionButton("播放", PlayPlaylist, () => _playlistTracks.Count > 0, true), ActionButton("添加歌曲", () => Execute(AddSongsAsync), () => _playlistList.SelectedItem != null && (_selection.Count > 0 || _vm.Queue.Count > 0)), more)); DockPanel.SetDock(top, Dock.Top); right.Children.Add(top);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; var actions = UiFactory.More(("移除选中歌曲", () => Execute(RemoveSongsAsync), () => _songs.SelectedItems.Count > 0), ("上移", () => Execute(() => MoveSongAsync(-1)), () => _songs.SelectedIndex > 0 && (_playlistList.SelectedItem as LibraryItem)?.Kind == "local-playlist"), ("下移", () => Execute(() => MoveSongAsync(1)), () => _songs.SelectedIndex >= 0 && _songs.SelectedIndex < _playlistTracks.Count - 1 && (_playlistList.SelectedItem as LibraryItem)?.Kind == "local-playlist")); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        var selected = UiFactory.Text("选择歌曲后可移除或调整顺序", 10); selected.SetResourceReference(ForegroundProperty, "MutedBrush"); footer.Children.Add(selected); DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer);
        _songs.SelectionMode = SelectionMode.Extended; _songs.ItemTemplate = (DataTemplate)Application.Current.Resources["PlaylistTrackTemplate"]; _songs.SelectionChanged += (_, _) => { UpdateActions(); selected.Text = _songs.SelectedItems.Count == 0 ? "选择歌曲后可移除或调整顺序" : $"已选择 {_songs.SelectedItems.Count} 首歌曲"; };
        _songs.AllowDrop = true; int dragIndex = -1; System.Windows.Point dragStart = default;
        _songs.PreviewMouseLeftButtonDown += (_, e) => { var row = ItemsControl.ContainerFromElement(_songs, e.OriginalSource as DependencyObject) as ListBoxItem; dragIndex = row == null ? -1 : _songs.ItemContainerGenerator.IndexFromContainer(row); dragStart = e.GetPosition(_songs); };
        _songs.PreviewMouseMove += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && dragIndex >= 0 && Math.Abs(e.GetPosition(_songs).Y - dragStart.Y) > 10) { System.Windows.DragDrop.DoDragDrop(_songs, new DataObject("PlaylistIndex", dragIndex), DragDropEffects.Move); dragIndex = -1; } };
        _songs.Drop += (_, e) => { if (e.Data.GetData("PlaylistIndex") is not int from) return; var row = ItemsControl.ContainerFromElement(_songs, e.OriginalSource as DependencyObject) as ListBoxItem; int to = row == null ? _songs.Items.Count - 1 : _songs.ItemContainerGenerator.IndexFromContainer(row); e.Handled = true; Execute(() => ReorderSongAsync(from, to)); };
        right.Children.Add(UiFactory.ListWithEmptyState(_songs, "这里还没有歌曲", "从曲库选择歌曲后加入，或添加当前队列。")); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    private void EditName(bool create, bool server)
    {
        var item = _playlistList.SelectedItem as LibraryItem; var dialog = new PlaylistEditWindow(create ? "新建歌单" : "重命名歌单", create ? "新歌单" : item?.Title ?? "") { Owner = this }; if (dialog.ShowDialog() != true) return; _name.Text = dialog.PlaylistName; Execute(() => create ? CreatePlaylistAsync(server) : RenamePlaylistAsync());
    }
    private void EditDescription()
    {
        if (_playlistList.SelectedItem is not LibraryItem { Kind: "local-playlist" } item) return; var playlist = _vm.Playlists.Local.First(p => p.Id == item.Id); var dialog = new PlaylistEditWindow("编辑歌单", playlist.Name, playlist.Description, true) { Owner = this }; if (dialog.ShowDialog() == true) { playlist.Name = dialog.PlaylistName; playlist.Description = dialog.Description; _vm.Playlists.Save(); _vm.NotifyPlaylists(); Execute(RefreshPlaylistsAsync); }
    }
    private void SelectPlaylistCover()
    {
        if (_playlistList.SelectedItem is not LibraryItem { Kind: "local-playlist" } item) return; var picker = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.jpg;*.png;*.jpeg" }; if (picker.ShowDialog(this) != true) return; if (new FileInfo(picker.FileName).Length > 10 * 1024 * 1024) { _message.Text = "封面不能超过 10 MB。"; return; }
        var folder = Path.Combine(Store.Root, "playlist-covers"); Directory.CreateDirectory(folder); string destination = Path.Combine(folder, item.Id + Path.GetExtension(picker.FileName)); File.Copy(picker.FileName, destination, true); _vm.Playlists.Local.First(p => p.Id == item.Id).CoverPath = destination; _vm.Playlists.Save(); _vm.NotifyPlaylists();
    }
    private async Task RefreshPlaylistsAsync()
    {
        _vm.NotifyPlaylists();
        string? selected = (_playlistList.SelectedItem as LibraryItem)?.Id; var list = _vm.Playlists.Local.Select(p => new LibraryItem(p.Id, p.Name, "本地歌单", "local-playlist")).ToList();
        if (_vm.Api.Configured) try { var root = await _vm.Api.CallAsync("getPlaylists"); if (root.TryGetProperty("playlists", out var playlists) && playlists.TryGetProperty("playlist", out var rows)) foreach (var row in rows.EnumerateArray()) list.Add(new(NavidromeApiClient.Text(row, "id"), NavidromeApiClient.Text(row, "name"), _vm.Api.SourceName + "歌单", "playlist")); } catch (ApiException e) { _message.Text = e.Message; }
        _playlistList.ItemsSource = list; _playlistList.SelectedItem = list.FirstOrDefault(p => p.Id == selected) ?? list.FirstOrDefault();
    }
    private async Task LoadPlaylistAsync()
    {
        var item = _playlistList.SelectedItem as LibraryItem; _playlistTracks = []; _songs.ItemsSource = null; UpdateActions(); if (item == null) return; _name.Text = item.Title; _description.Text = item.Kind == "local-playlist" ? _vm.Playlists.Local.First(p => p.Id == item.Id).Description : "服务器歌单：名称、添加与移除由账号权限决定；排序请在服务端管理";
        List<Track> tracks;
        if (item.Kind == "local-playlist") tracks = _vm.Playlists.Local.First(p => p.Id == item.Id).Tracks.ToList();
        else { var root = await _vm.Api.CallAsync("getPlaylist", default, ("id", item.Id)); tracks = NavidromeApiClient.Tracks(root, "playlist", "entry"); }
        if (!ReferenceEquals(item, _playlistList.SelectedItem)) return; _playlistTracks = tracks; _songs.ItemsSource = tracks; if (_playlistCover != null) _playlistCover.Source = item.Kind == "local-playlist" ? CoverCacheService.Load(_vm.Playlists.Local.First(p => p.Id == item.Id).CoverPath, 160) : null; if (_playlistHeading != null) _playlistHeading.Text = item.Title; if (_playlistCaption != null) _playlistCaption.Text = $"{(item.Kind == "local-playlist" ? "本地歌单" : "服务器歌单")} · {tracks.Count} 首 · {TimeSpan.FromSeconds(tracks.Sum(t => t.Duration)):h\\:mm\\:ss}"; UpdateActions();
    }
    private Task MoveSongAsync(int delta) => ReorderSongAsync(_songs.SelectedIndex, _songs.SelectedIndex + delta);
    private async Task ReorderSongAsync(int from, int to)
    {
        if (_playlistList.SelectedItem is not LibraryItem { Kind: "local-playlist" } item || from < 0 || to < 0 || from >= _playlistTracks.Count || to >= _playlistTracks.Count) return;
        var list = _vm.Playlists.Local.First(p => p.Id == item.Id).Tracks; var track = list[from]; list.RemoveAt(from); list.Insert(to, track); _vm.Playlists.Save(); _vm.NotifyPlaylists(); await LoadPlaylistAsync(); _songs.SelectedIndex = to;
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
        var grid = new Grid { Margin = new Thickness(12) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new StackPanel(); var size = UiFactory.Text("", 16); size.FontWeight = FontWeights.SemiBold; size.SetBinding(TextBlock.TextProperty, new Binding("OfflineSize") { StringFormat = "已使用 {0}" }); header.Children.Add(size); _capacityBar = new System.Windows.Controls.ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(100d * _vm.Offline.Bytes / Math.Max(1, _vm.Settings.OfflineCacheMb * 1048576L), 0, 100), Height = 4, Margin = new Thickness(0, 7, 0, 9) }; _capacityBar.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentBrush"); header.Children.Add(_capacityBar);
        var progress = UiFactory.Text("", 10); progress.SetResourceReference(ForegroundProperty, "MutedBrush"); progress.SetBinding(TextBlock.TextProperty, new Binding("DownloadStatus")); header.Children.Add(progress);
        var more = UiFactory.More(("暂停全部任务", _vm.Offline.Cancel, () => _vm.IsDownloading), ("继续 / 重试未完成", _vm.Offline.RetryFailed, null), ("清理未保留缓存", () => Execute(_vm.Offline.ClearAsync), () => !_vm.IsDownloading));
        header.Children.Add(UiFactory.Toolbar(ActionButton("下载当前歌曲", () => Execute(() => _vm.Offline.DownloadAsync(_vm.Current is Track t ? [t] : [])), () => _vm.Current is { IsLocal: false, IsNetease: false }, true), ActionButton("下载队列", () => Execute(() => _vm.Offline.DownloadAsync(_vm.Queue.Where(t => !t.IsNetease).ToArray())), () => _vm.Queue.Any(t => !t.IsLocal && !t.IsNetease)), more)); grid.Children.Add(header);
        var tabs = new System.Windows.Controls.TabControl(); ReferenceLayout.Tabs(tabs); var jobs = new ListBox { Name = "DownloadTaskList", ItemsSource = _vm.Offline.Tasks, ItemTemplate = DownloadTemplate() }; tabs.Items.Add(new TabItem { Header = "下载任务", Content = UiFactory.ListWithEmptyState(jobs, "没有下载任务", "下载歌曲或队列后，可在这里暂停、继续或重试。") });
        var cached = new DockPanel(); var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; var actions = UiFactory.More(("播放选中歌曲", () => Execute(async () => { if (_downloads.SelectedItem is OfflineEntry entry) await _vm.PlayTrackAsync(entry.Track); }), () => _downloads.SelectedItem != null), ("始终保留", () => { if (_downloads.SelectedItem is OfflineEntry entry) _vm.Offline.SetPinned(entry.Track, true); }, () => _downloads.SelectedItem is OfflineEntry { Pinned: false }), ("取消始终保留", () => { if (_downloads.SelectedItem is OfflineEntry entry) _vm.Offline.SetPinned(entry.Track, false); }, () => _downloads.SelectedItem is OfflineEntry { Pinned: true })); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(UiFactory.Text("始终保留的歌曲不会自动清理", 10)); DockPanel.SetDock(footer, Dock.Bottom); cached.Children.Add(footer);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Track")); presenter.SetValue(ContentPresenter.ContentTemplateProperty, Application.Current.Resources["TrackTemplate"]); _downloads.ItemTemplate = new DataTemplate { VisualTree = presenter }; _downloads.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("OfflineEntries")); cached.Children.Add(UiFactory.ListWithEmptyState(_downloads, "尚无离线歌曲", "主动下载默认保留，可在歌曲菜单中更改。")); tabs.Items.Add(new TabItem { Header = "已下载", Content = cached }); Grid.SetRow(tabs, 1); grid.Children.Add(tabs); return grid;
    }
    private DataTemplate DownloadTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel)); row.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 8, 6, 8));
        var action = new FrameworkElementFactory(typeof(Button)); action.SetValue(DockPanel.DockProperty, Dock.Right); action.SetValue(ContentControl.ContentProperty, "···"); action.SetValue(FrameworkElement.WidthProperty, 32d); action.SetValue(FrameworkElement.HeightProperty, 30d); action.SetValue(System.Windows.Controls.Control.PaddingProperty, new Thickness(0)); action.SetValue(FrameworkElement.ToolTipProperty, "任务操作"); action.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) => { if (sender is Button { DataContext: DownloadTask job } button) { var menu = new ContextMenu(); foreach (var (title, run, enabled) in new (string, Action, bool)[] { ("暂停", () => _vm.Offline.Pause(job), job.State is "下载中" or "等待"), ("继续 / 重试", () => _vm.Offline.Resume(job), job.State is "已暂停" or "失败" or "已取消"), ("取消任务", () => _vm.Offline.Cancel(job), job.State is not ("完成" or "已取消")) }) { var item = new MenuItem { Header = title, IsEnabled = enabled }; item.Click += (_, _) => run(); menu.Items.Add(item); } menu.PlacementTarget = button; menu.IsOpen = true; } })); row.AppendChild(action);
        var pause = new FrameworkElementFactory(typeof(Button)); pause.SetValue(DockPanel.DockProperty, Dock.Right); pause.SetValue(FrameworkElement.WidthProperty, 32d); pause.SetValue(FrameworkElement.HeightProperty, 30d); pause.SetValue(System.Windows.Controls.Control.PaddingProperty, new Thickness(0)); pause.SetValue(System.Windows.Controls.Control.BackgroundProperty, Brushes.Transparent); pause.SetValue(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0)); pause.SetBinding(ContentControl.ContentProperty, new Binding("ActionGlyph")); pause.SetBinding(UIElement.IsEnabledProperty, new Binding("ActionEnabled")); pause.SetBinding(FrameworkElement.ToolTipProperty, new Binding("ActionName")); pause.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) => { if (sender is Button { DataContext: DownloadTask job }) { if (job.State is "下载中" or "等待") _vm.Offline.Pause(job); else _vm.Offline.Resume(job); } })); row.AppendChild(pause);
        var cover = new FrameworkElementFactory(typeof(Border)); cover.SetValue(DockPanel.DockProperty, Dock.Left); cover.SetValue(FrameworkElement.WidthProperty, 36d); cover.SetValue(FrameworkElement.HeightProperty, 36d); cover.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0)); cover.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); cover.SetResourceReference(Border.BackgroundProperty, "SelectedBrush"); var note = new FrameworkElementFactory(typeof(TextBlock)); note.SetValue(TextBlock.TextProperty, "♪"); note.SetValue(TextBlock.FontSizeProperty, 19d); note.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); note.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); note.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); cover.AppendChild(note); row.AppendChild(cover);
        var stack = new FrameworkElementFactory(typeof(StackPanel)); var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding("Track.Title")); title.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium); stack.AppendChild(title); var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding("Detail")); label.SetValue(TextBlock.FontSizeProperty, 10d); label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 5)); label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); stack.AppendChild(label);
        var bar = new FrameworkElementFactory(typeof(System.Windows.Controls.ProgressBar)); bar.SetValue(FrameworkElement.HeightProperty, 3d); bar.SetValue(System.Windows.Controls.Primitives.RangeBase.MaximumProperty, 100d); bar.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding("Percent") { Mode = BindingMode.OneWay }); bar.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentBrush"); stack.AppendChild(bar); row.AppendChild(stack); return new DataTemplate { VisualTree = row };
    }
    private UIElement MakeSleep()
    {
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(UiFactory.Text("定时停止播放", 19)); panel.Children.Add(UiFactory.Text("分钟（1–720）")); _minutes.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(_minutes); var finish = UiFactory.Check("到时间后等当前歌曲播放完", false); panel.Children.Add(finish);
        panel.Children.Add(UiFactory.Buttons(UiFactory.Button("开始计时", () => Execute(() => { if (!int.TryParse(_minutes.Text, out var minutes) || minutes is < 1 or > 720) throw new ApiException("请输入 1–720 分钟。"); _vm.SetSleep(minutes, finish.IsChecked == true); return Task.CompletedTask; }), true), UiFactory.Button("播完当前歌曲停止", () => Execute(() => { _vm.SetSleep(0, true); return Task.CompletedTask; })), UiFactory.Button("取消定时", _vm.CancelSleep)));
        var status = UiFactory.Text("", 14); status.SetBinding(TextBlock.TextProperty, new Binding("SleepStatus")); panel.Children.Add(status); return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(_vm.OfflineSize) && _capacityBar != null) _capacityBar.Value = Math.Clamp(100d * _vm.Offline.Bytes / Math.Max(1, _vm.Settings.OfflineCacheMb * 1048576L), 0, 100); if (e.PropertyName is nameof(_vm.Current) or nameof(_vm.IsDownloading) or nameof(_vm.OfflineEntries)) UpdateActions(); }
}
