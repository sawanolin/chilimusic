using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ListViewItem = System.Windows.Controls.ListViewItem;
using Panel = System.Windows.Controls.Panel;
using Cursors = System.Windows.Input.Cursors;

namespace ChiliMusic;

public partial class MainWindow
{
    private ListView? _details;
    private TextBlock? _albumDescription;
    private Image? _albumArt;
    private ListBox? _sidebarPlaylists;
    private TextBox? _queueSearch;
    private int _playlistRefresh;
    private System.Windows.Point _songDragStart;
    private readonly DispatcherTimer _layoutSave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private void InitializeProduct()
    {
        Width = Math.Clamp(Vm.Settings.MainWidth, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width)); Height = Math.Clamp(Vm.Settings.MainHeight, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        MainPlayerBar.ControlRow.Margin = new Thickness(0); MainPlayerBar.SeekSlider.Height = 12;
        if (MainPlayerBar.RootPanel.Children[2] is DockPanel clock) { clock.Margin = new Thickness(0); foreach (var text in clock.Children.OfType<TextBlock>()) text.FontSize = 9; }
        FilterFields.DataContext = Vm;
        FilterFields.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { FilterPopup.IsOpen = false; e.Handled = true; } };
        LocalViewBox.ItemsSource = new[] { "歌曲", "专辑", "歌手", "流派", "年份", "文件夹" }; LocalViewBox.SelectedItem = Vm.LocalView;
        LocalViewBox.SelectionChanged += (_, _) => { if (LocalViewBox.SelectedItem is string view) Vm.LocalView = view; };
        var scope = UiFactory.Combo(["当前来源", "本地音乐", "Navidrome", "网易云音乐", "全部来源"]); scope.SelectedItem = Vm.SearchScope; scope.ToolTip = "搜索范围"; scope.SelectionChanged += (_, _) => { if (scope.SelectedItem is string value) Vm.SearchScope = value; }; FilterFields.Children.Add(UiFactory.Row("搜索范围", scope));
        _albumArt = HeroCover; _albumDescription = HeroMeta;
        void DetailChanged()
        {
            bool detail = Vm.BrowseItem != null;
            double artSize = ActualWidth > 0 && ActualWidth < 1000 ? 88 : 176;
            bool compact = ActualWidth > 0 && ActualWidth < 1000;
            PlayListButton.Width = compact ? 94 : 108; ShuffleListButton.Width = compact ? 84 : 106;
            DownloadListButton.Width = compact ? 52 : 60; BrowseMoreButton.Width = compact ? 28 : 34;
            HeroCoverBorder.Width = HeroCoverBorder.Height = artSize; HeroTextGrid.Height = detail ? Math.Max(artSize, ActualWidth < 1000 ? 142 : artSize) : double.NaN; HeroDescription.Text = Vm.BrowseItem?.Kind == "local-playlist" ? Vm.Playlists.Local.FirstOrDefault(p => p.Id == Vm.BrowseItem.Id)?.Description ?? "" : ""; HeroDescription.Visibility = HeroDescription.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            HeroArtColumn.Width = detail ? new GridLength(artSize + 20) : new GridLength(0); HeroCoverBorder.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
            HeroArtist.Text = detail ? Vm.BrowseItem!.Subtitle.Split('·')[0].Trim() : Vm.CatalogName;
            HeroMeta.Text = detail ? Vm.AlbumDetail : Vm.BrowseSummary;
            BreadcrumbText.Text = detail ? Vm.CatalogName + "  ›  " + (Vm.Navigation == Vm.CatalogName ? "专辑" : Vm.Navigation) + "  ›  " + Vm.Section : Vm.CatalogName == Vm.Section ? Vm.CatalogName : Vm.CatalogName + "  ›  " + Vm.Section;
            PageHeading.FontSize = detail ? 34 : 25;
            RefreshDetailCover();
            LocalViewBox.Visibility = !detail && (Vm.IsLocalCatalog || Vm.Navigation == "本地音乐") ? Visibility.Visible : Visibility.Collapsed;
        }
        Vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(Vm.Section) or nameof(Vm.BrowseSummary) or nameof(Vm.CatalogSource) or nameof(Vm.AlbumDetail)) DetailChanged(); }; DetailChanged();
        SizeChanged += (_, _) => DetailChanged();
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshDetailCover(); else CancelDetailCover(); };
        Closed += (_, _) => CancelDetailCover();
        _details = new ListView { ItemsSource = Vm.Results, Background = Brushes.Transparent, SelectionMode = SelectionMode.Extended, Visibility = Visibility.Collapsed };
        _details.SetResourceReference(ForegroundProperty, "TextBrush"); _details.SetResourceReference(BorderBrushProperty, "BorderBrush");
        var view = new GridView { AllowsColumnReorder = true }; _details.View = view; _details.BorderThickness = new Thickness(0); _details.FontSize = 11; RowNumbering.SetEnabled(_details, true);
        var headerRoot = new FrameworkElementFactory(typeof(Grid)); headerRoot.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
        var headerText = new FrameworkElementFactory(typeof(ContentPresenter)); headerText.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 5, 0)); headerText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); headerText.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty)); headerRoot.AppendChild(headerText);
        var grip = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.Thumb), "PART_HeaderGripper"); grip.SetValue(FrameworkElement.WidthProperty, 5d); grip.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right); grip.SetValue(CursorProperty, Cursors.SizeWE); var gripSurface = new FrameworkElementFactory(typeof(Border)); gripSurface.SetValue(BackgroundProperty, Brushes.Transparent); grip.SetValue(TemplateProperty, new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = gripSurface }); headerRoot.AppendChild(grip);
        var headerStyle = new Style(typeof(GridViewColumnHeader)); headerStyle.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(GridViewColumnHeader)) { VisualTree = headerRoot })); headerStyle.Setters.Add(new Setter(HeightProperty, 28d)); headerStyle.Setters.Add(new Setter(FontSizeProperty, 10d)); headerStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("MutedBrush"))); view.ColumnHeaderContainerStyle = headerStyle;
        var numberText = new FrameworkElementFactory(typeof(TextBlock), "RowNumber"); numberText.SetValue(FrameworkElement.NameProperty, "RowNumber"); numberText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        numberText.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(RowNumbering.IndexProperty), RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1), Converter = new RowNumberConverter() });
        view.Columns.Add(new() { Header = new GridViewColumnHeader { Content = "#" }, Width = 30, CellTemplate = new DataTemplate { VisualTree = numberText } });
        _details.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        var definitions = new[] { ("歌曲", "Title", 156d), ("歌手", "Artist", 90d), ("专辑", "Album", 110d), ("时长", "DurationText", 55d), ("格式", "Format", 0d), ("码率", "BitRate", 0d), ("年份", "Year", 0d), ("碟片", "DiscNumber", 0d), ("来源", "SourceLabel", 0d) };
        foreach (var definition in definitions.OrderBy(d => Vm.Settings.TrackColumns.FirstOrDefault(c => c.Name == d.Item1)?.Order ?? Array.FindIndex(definitions, x => x.Item1 == d.Item1)))
        {
            var preference = Vm.Settings.TrackColumns.FirstOrDefault(c => c.Name == definition.Item1);
            var column = new GridViewColumn { Header = definition.Item1, DisplayMemberBinding = new Binding(definition.Item2), Width = preference is { Visible: false } ? 0 : preference == null && definition.Item3 == 0 ? 0 : Math.Clamp(preference?.Width ?? definition.Item3, 35, 600) }; view.Columns.Add(column);
            var header = new GridViewColumnHeader { Content = definition.Item1, Tag = definition.Item2 }; header.SetResourceReference(BackgroundProperty, "SurfaceBrush"); header.SetResourceReference(ForegroundProperty, "TextBrush"); header.SetResourceReference(BorderBrushProperty, "BorderBrush");
            header.Click += (_, _) => { var collection = CollectionViewSource.GetDefaultView(Vm.Results); var direction = collection.SortDescriptions.FirstOrDefault().Direction == ListSortDirection.Ascending && collection.SortDescriptions.FirstOrDefault().PropertyName == definition.Item2 ? ListSortDirection.Descending : ListSortDirection.Ascending; collection.SortDescriptions.Clear(); collection.SortDescriptions.Add(new(definition.Item2, direction)); }; column.Header = header;
            DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn)).AddValueChanged(column, (_, _) => ScheduleLayoutSave());
        }
        var columns = new ContextMenu(); foreach (var column in view.Columns) { var item = new MenuItem { Header = ((GridViewColumnHeader)column.Header).Content, IsCheckable = true, IsChecked = column.Width > 0 }; item.Click += (_, _) => { column.Width = item.IsChecked ? 120 : 0; ScheduleLayoutSave(); }; columns.Items.Add(item); } _details.ContextMenu = TracksList.ContextMenu;
        foreach (var column in view.Columns) ((GridViewColumnHeader)column.Header).ContextMenu = columns;
        _details.SelectionChanged += (_, _) => { TracksList.SelectedItems.Clear(); foreach (var track in _details.SelectedItems) TracksList.SelectedItems.Add(track); };
        _details.MouseDoubleClick += (_, e) => { if (_details.SelectedItem is Track t && Row(e.OriginalSource as DependencyObject) != null) Vm.Run(() => Vm.PlayBrowseTrackAsync(t)); };
        _details.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && _details.SelectedItem is Track track) { Vm.Run(() => Vm.PlayBrowseTrackAsync(track)); e.Handled = true; } };
        ((Grid)TracksList.Parent).Children.Add(_details);
        _details.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(BrowseScrolled));
        _details.Loaded += (_, _) => ReferenceLayout.GridViewCorner(_details);
        var songsMenu = TracksList.ContextMenu; AddItem(songsMenu, "编辑本地标签…", () => { var tracks = SelectedTracks().Where(t => t.IsLocal && t.CuePath == null).ToArray(); if (tracks.Length > 0) new TagEditorWindow(Vm, tracks) { Owner = this }.ShowDialog(); });
        AddItem(songsMenu, "在文件夹中显示", () => { if (TracksList.SelectedItem is Track { LocalPath: string file }) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); });
        AddItem(songsMenu, "重新定位文件…", RelocateSelected);
        songsMenu.Opened += (_, _) => { foreach (var item in songsMenu.Items.OfType<MenuItem>()) { string text = item.Header.ToString() ?? ""; if (text == "编辑本地标签…") item.IsEnabled = SelectedTracks().Length > 0 && SelectedTracks().All(t => t.IsLocal && t.CuePath == null); if (text is "在文件夹中显示" or "重新定位文件…") item.IsEnabled = SelectedTracks().Length == 1 && SelectedTracks()[0].IsLocal; } };
        QueueList.SelectionMode = SelectionMode.Extended;
        var queueDock = (DockPanel)((Grid)QueueList.Parent).Parent;
        var queueSearch = _queueSearch = UiFactory.Input(); queueSearch.Height = 28; queueSearch.ToolTip = "搜索播放队列"; System.Windows.Automation.AutomationProperties.SetHelpText(queueSearch, "搜索播放队列中的歌曲"); queueSearch.TextChanged += (_, _) => CollectionViewSource.GetDefaultView(Vm.Queue).Filter = row => row is Track t && SearchText.Matches(t.Title + " " + t.Artist, queueSearch.Text); DockPanel.SetDock(queueSearch, Dock.Top); var searchField = UiFactory.SearchField(queueSearch, "搜索队列中的歌曲"); DockPanel.SetDock(searchField, Dock.Top); queueDock.Children.Insert(1, searchField);
        var priority = UiFactory.Text("", 10); priority.TextTrimming = TextTrimming.CharacterEllipsis; priority.MaxHeight = 34; priority.SetBinding(TextBlock.TextProperty, new Binding(nameof(Vm.PrioritySummary))); DockPanel.SetDock(priority, Dock.Bottom); QueueBorder.ToolTip = priority;
        AddItem(QueueList.ContextMenu, "批量移除选中", () => Vm.RemoveRange(SelectedQueueIndices()));
        var resize = new GridSplitter { Width = 5, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndCurrent }; Grid.SetColumn(resize, 2); ((Grid)QueueBorder.Parent).Children.Add(resize); resize.DragCompleted += (_, _) => { Vm.Settings.QueueWidth = QueueColumn.ActualWidth; ScheduleLayoutSave(); };
        _sidebarPlaylists = new ListBox { MaxHeight = 170, MinHeight = 30, Background = Brushes.Transparent, ToolTip = "拖入歌曲加入本地歌单", AllowDrop = true };
        _sidebarPlaylists.ItemTemplate = (DataTemplate)Resources["SidebarPlaylistTemplate"];
        _sidebarPlaylists.MouseDoubleClick += (_, _) => { if (_sidebarPlaylists.SelectedItem is LibraryItem playlist) Vm.Run(() => Vm.BrowseAsync(playlist.Title, item: playlist)); };
        _sidebarPlaylists.Drop += (_, e) => { var target = ItemsControl.ContainerFromElement(_sidebarPlaylists, e.OriginalSource as DependencyObject) as ListBoxItem; if (e.Data.GetData("PlaylistTracks") is Track[] tracks && target?.DataContext is LibraryItem playlist) { e.Handled = true; Vm.Run(async () => { await Vm.AddToPlaylistAsync(playlist.Id, playlist.Kind == "playlist", tracks); RefreshSidebarPlaylists(); }); } };
        var divider = new Border { Height = 1, Margin = new Thickness(14, 10, 14, 8) }; divider.SetResourceReference(BackgroundProperty, "BorderBrush"); NavigationStack.Children.Add(divider);
        var playlistHeading = new DockPanel { Margin = new Thickness(16, 0, 10, 5) }; var addPlaylist = UiFactory.Button("＋", () => Host.ShowTools("歌单")); addPlaylist.Width = 26; addPlaylist.Height = 26; addPlaylist.Padding = new Thickness(0); addPlaylist.Margin = new Thickness(0); addPlaylist.Background = Brushes.Transparent; addPlaylist.BorderThickness = new Thickness(0); addPlaylist.ToolTip = "管理和新建歌单"; DockPanel.SetDock(addPlaylist, Dock.Right); playlistHeading.Children.Add(addPlaylist); var playlistLabel = UiFactory.Text("我的歌单", 11); playlistLabel.VerticalAlignment = VerticalAlignment.Center; playlistHeading.Children.Add(playlistLabel); NavigationStack.Children.Add(playlistHeading); NavigationStack.Children.Add(_sidebarPlaylists); Vm.PlaylistsChanged += RefreshSidebarPlaylists;
        TracksList.PreviewMouseLeftButtonDown += (_, e) => _songDragStart = e.GetPosition(TracksList);
        TracksList.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && Math.Abs(e.GetPosition(TracksList).Y - _songDragStart.Y) > 10 && SelectedTracks().Length > 0) System.Windows.DragDrop.DoDragDrop(TracksList, new DataObject("PlaylistTracks", SelectedTracks()), DragDropEffects.Copy); };
        _details.PreviewMouseRightButtonDown += SelectRightClick;
        _details.PreviewMouseLeftButtonDown += (_, e) => _songDragStart = e.GetPosition(_details);
        _details.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && Math.Abs(e.GetPosition(_details).Y - _songDragStart.Y) > 10 && SelectedTracks().Length > 0) System.Windows.DragDrop.DoDragDrop(_details, new DataObject("PlaylistTracks", SelectedTracks()), DragDropEffects.Copy); };
        var emptyActions = UiFactory.Buttons(UiFactory.Button("添加目录", OpenMusicFolder), UiFactory.Button("连接 Navidrome", () => Host.ShowSettings()), UiFactory.Button("登录网易云", Host.ShowNetease), UiFactory.Button("重新加载", () => Vm.Run(() => Vm.BrowseAsync(Vm.Section, Vm.BrowseQuery, Vm.BrowseItem)))); emptyActions.HorizontalAlignment = HorizontalAlignment.Center; EmptyPanel.Children.Add(emptyActions);
        var failure = new StackPanel { Orientation = Orientation.Horizontal }; var retry = UiFactory.Button("重试播放", () => Vm.Run(Vm.RetryPlaybackAsync)); var skip = UiFactory.Button("跳过此曲", () => Vm.Run(() => Vm.NextAsync(false))); failure.Children.Add(retry); failure.Children.Add(skip); ((StackPanel)AlbumHero.Parent).Children.Add(failure);
        Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Vm.PlaybackFailure)) failure.Visibility = Vm.PlaybackFailure.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }; failure.Visibility = Visibility.Collapsed;
        _layoutSave.Tick += (_, _) => { _layoutSave.Stop(); SaveProductLayout(); };
        SizeChanged += (_, _) => ScheduleLayoutSave(); Closing += (_, _) => { SaveProductLayout(); _layoutSave.Stop(); Vm.PlaylistsChanged -= RefreshSidebarPlaylists; };
        ApplyListMode(); ApplyQueueLayout(); RefreshSidebarPlaylists();
        Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Vm.CatalogSource)) RefreshSidebarPlaylists(); };
        Loaded += (_, _) => NameAccessibleControls(this);
    }
    private void ApplyListMode()
    {
        if (_details == null) return; TracksList.Visibility = Vm.Settings.DetailedTracks ? Visibility.Collapsed : Visibility.Visible; _details.Visibility = Vm.Settings.DetailedTracks ? Visibility.Visible : Visibility.Collapsed;
        var style = new Style(typeof(ListBoxItem), (Style)Application.Current.FindResource(typeof(ListBoxItem))); style.Setters.Add(new Setter(PaddingProperty, new Thickness(5, Vm.Settings.CompactTracks ? 3 : 7, 5, Vm.Settings.CompactTracks ? 3 : 7))); TracksList.ItemContainerStyle = style;
        var row = new FrameworkElementFactory(typeof(Border), "Row"); row.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); row.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty)); row.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cells = new FrameworkElementFactory(typeof(GridViewRowPresenter)); cells.SetBinding(GridViewRowPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent }); cells.SetBinding(GridViewRowPresenter.ColumnsProperty, new Binding("View.Columns") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListView), 1) }); row.AppendChild(cells);
        var template = new ControlTemplate(typeof(ListViewItem)) { VisualTree = row }; var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrush"), "Row")); template.Triggers.Add(hover); var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SelectedBrush"), "Row")); template.Triggers.Add(selected);
        var currentBinding = new MultiBinding { Converter = new CurrentTrackConverter() }; currentBinding.Bindings.Add(new Binding()); currentBinding.Bindings.Add(new Binding("DataContext.Current") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListView), 1) }); var current = new DataTrigger { Binding = currentBinding, Value = true }; current.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("AccentBrush"))); current.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SelectedBrush"), "Row")); template.Triggers.Add(current);
        var detailedStyle = new Style(typeof(ListViewItem)); detailedStyle.Setters.Add(new Setter(TemplateProperty, template)); detailedStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(0, Vm.Settings.CompactTracks ? 5 : 8, 0, Vm.Settings.CompactTracks ? 5 : 8))); detailedStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); detailedStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("TextBrush"))); _details.ItemContainerStyle = detailedStyle;
    }
    private void ApplyQueueLayout() { QueueColumn.Width = Vm.Settings.QueueVisible ? new GridLength(Math.Clamp(Vm.Settings.QueueWidth, 200, 420)) : new GridLength(0); QueueBorder.Visibility = Vm.Settings.QueueVisible ? Visibility.Visible : Visibility.Collapsed; }
    private void ScheduleLayoutSave() { _layoutSave.Stop(); _layoutSave.Start(); }
    private void SaveProductLayout()
    {
        if (WindowState == WindowState.Normal) { Vm.Settings.MainWidth = Width; Vm.Settings.MainHeight = Height; }
        if (_details?.View is GridView view) Vm.Settings.TrackColumns = view.Columns.Where(c => ((GridViewColumnHeader)c.Header).Content.ToString() != "#").Select((c, i) => new TrackColumnPreference { Name = ((GridViewColumnHeader)c.Header).Content.ToString()!, Width = double.IsFinite(c.Width) ? c.Width : 120, Visible = c.Width > 0, Order = i }).ToList(); Vm.SaveSettings();
    }
    private void RefreshSidebarPlaylists() => Vm.Run(async () =>
    {
        if (_sidebarPlaylists == null) return; int generation = ++_playlistRefresh;
        var list = Vm.Playlists.Local.Select(p => new LibraryItem(p.Id, p.Name, $"{p.Tracks.Count} 首", "local-playlist") { LocalCoverPath = p.CoverPath, Cover = p.CoverPath is string path ? CoverCacheService.Load(path, 64) : null }).ToList();
        if (!Vm.IsLocalCatalog && Vm.Api.Configured) try { var root = await Vm.Api.CallAsync("getPlaylists"); if (root.TryGetProperty("playlists", out var playlists)) RowsSidebar(playlists, list); } catch (ApiException) { }
        if (generation == _playlistRefresh) _sidebarPlaylists.ItemsSource = list;
    });
    private static void RowsSidebar(System.Text.Json.JsonElement parent, List<LibraryItem> items) { if (parent.TryGetProperty("playlist", out var rows)) foreach (var row in rows.EnumerateArray()) items.Add(new(NavidromeApiClient.Text(row, "id"), NavidromeApiClient.Text(row, "name"), "服务器歌单", "playlist", NavidromeApiClient.Text(row, "coverArt"))); }
    private void ShowSearchHistory() { var menu = new ContextMenu(); foreach (var query in Vm.SearchHistory.ToArray()) AddItem(menu, query, () => { SearchBox.Text = query; }); AddItem(menu, "清空搜索记录", Vm.ClearSearchHistory); menu.PlacementTarget = SearchBox; menu.IsOpen = true; }
    private void ViewMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); AddItem(menu, Vm.Settings.DetailedTracks ? "切换简洁列表" : "切换详细列表", () => { Vm.Settings.DetailedTracks = !Vm.Settings.DetailedTracks; ApplyListMode(); Vm.SaveSettings(); }); AddItem(menu, Vm.Settings.CompactTracks ? "标准行高" : "紧凑行高", () => { Vm.Settings.CompactTracks = !Vm.Settings.CompactTracks; ApplyListMode(); Vm.SaveSettings(); }); AddItem(menu, Vm.Settings.QueueVisible ? "收起队列" : "展开队列", () => { Vm.Settings.QueueVisible = !Vm.Settings.QueueVisible; ApplyQueueLayout(); Vm.SaveSettings(); }); menu.PlacementTarget = sender as UIElement; menu.IsOpen = true;
    }
    private void BrowseMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); AddItem(menu, "打开音乐文件…", OpenLocalFiles); AddItem(menu, "添加音乐目录…", OpenMusicFolder); AddItem(menu, "歌词与歌单", () => Host.ShowTools("歌词")); AddItem(menu, "搜索记录", ShowSearchHistory); AddItem(menu, "曲库整理", ShowLibraryTools); AddItem(menu, "查看听歌历史", () => new ListeningHistoryWindow(Vm) { Owner = this }.Show()); menu.PlacementTarget = sender as UIElement; menu.IsOpen = true;
    }
    private void QueueMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); AddItem(menu, "撤销上次队列修改", () => Vm.Run(Vm.UndoQueueAsync)); ((MenuItem)menu.Items[0]).IsEnabled = Vm.CanUndoQueue; AddItem(menu, "保存到歌单…", () => Host.ShowTools("歌单", Vm.Queue.ToArray())); AddItem(menu, "移除选中歌曲", () => Vm.RemoveRange(SelectedQueueIndices())); AddItem(menu, "清空队列", Vm.ClearQueue); menu.PlacementTarget = sender as UIElement; menu.IsOpen = true;
    }
    private void ShuffleBrowseClick(object sender, RoutedEventArgs e) => Vm.Run(async () => { await Vm.PlayCompleteBrowseAsync(); var tracks = Vm.Queue.ToArray(); if (tracks.Length == 0) return; Vm.Settings.Mode = PlayMode.Shuffle; Vm.Changed(nameof(Vm.ModeText)); Vm.Changed(nameof(Vm.ModeIcon)); await Vm.PlayAsync(Random.Shared.Next(tracks.Length)); Vm.SaveSettings(); });
    private void AlbumDownloadClick(object sender, RoutedEventArgs e) => Vm.Run(async () => { var tracks = Vm.Results.Where(t => !t.IsLocal && !t.IsNetease).ToArray(); if (tracks.Length == 0) { Vm.Status = "此列表没有可下载的 Navidrome 歌曲"; return; } Host.ShowTools("离线"); await Vm.DownloadSelectionAsync(tracks); });
    private void ShowLibraryTools()
    {
        var menu = new ContextMenu(); AddItem(menu, "重新扫描", () => Vm.Run(Vm.LocalLibrary.ScanAsync)); AddItem(menu, "查找重复文件", () => Vm.Run(async () => { var duplicates = await Vm.LocalLibrary.FindDuplicatesAsync(); new TextReportWindow("重复文件", duplicates.Count == 0 ? "没有发现内容完全相同的文件。" : string.Join("\n\n", duplicates.Select(g => string.Join("\n", g)))) { Owner = this }.Show(); })); AddItem(menu, "查看听歌历史（按日期）", () => new ListeningHistoryWindow(Vm) { Owner = this }.Show()); AddItem(menu, "清除本机听歌历史", Vm.ClearListeningHistory); menu.IsOpen = true;
    }
    private void RelocateSelected() { if (TracksList.SelectedItem is not Track { IsLocal: true } track) return; var picker = new Microsoft.Win32.OpenFileDialog { Title = "重新定位：" + track.Title }; if (picker.ShowDialog(this) == true) Vm.Run(() => Vm.LocalLibrary.RelocateAsync(track, picker.FileName)); }
    internal static void NameAccessibleControls(DependencyObject root)
    {
        if (root is FrameworkElement element && root is System.Windows.Controls.Primitives.ButtonBase or TextBox or ComboBox or Slider)
        { string? name = element.ToolTip as string ?? (root as ContentControl)?.Content as string; if (!string.IsNullOrWhiteSpace(name)) AutomationProperties.SetName(element, name); }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) NameAccessibleControls(VisualTreeHelper.GetChild(root, i));
    }
}

internal sealed class TextReportWindow : DialogShell
{
    public TextReportWindow(string title, string text) : base(title, 720, 480) { Body.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); }
}
