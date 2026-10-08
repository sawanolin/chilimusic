using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace ChiliMusic;
public partial class MainWindow : Window
{
    private App Host => (App)Application.Current; private PlayerViewModel Vm => Host.Vm;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _coverTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private Point _dragStart; private int _dragIndex = -1;
    private void ImmersiveClick(object sender, RoutedEventArgs e) => Host.ShowImmersive();
    public MainWindow(PlayerViewModel vm)
    {
        InitializeComponent(); WindowAppearance.Attach(this); DataContext = vm;
        AllowDrop = true; PreviewDragOver += (_, e) => { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } }; PreviewDrop += (_, e) => { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop); e.Handled = true; Vm.Run(() => Vm.OpenLocalFilesAsync(files)); } };
        void LayoutRows() { bool songs = vm.ShowSongs && !System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Results).IsEmpty, albums = !System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Library).IsEmpty && (vm.Section != "搜索" || vm.ShowAlbums); TrackRow.Height = new GridLength(songs ? albums ? 2 : 1 : albums ? 0 : 1, GridUnitType.Star); LibraryRow.Height = new GridLength(albums ? 1 : 0, GridUnitType.Star); EmptyPanel.Visibility = !songs && !albums ? Visibility.Visible : Visibility.Collapsed; EmptyHeading.Text = vm.IsBrowsing ? "正在加载…" : vm.Section == "本地音乐" ? "尚未添加本地音乐" : vm.Section == "离线音乐" ? "尚无离线歌曲" : "没有找到匹配的内容"; EmptyHint.Text = vm.IsBrowsing ? "" : vm.Section == "本地音乐" ? "打开音乐文件，或在设置中添加音乐目录。" : vm.Section == "离线音乐" ? "下载歌曲或专辑后，即可在断网时播放。" : "试试其他关键词，或调整筛选条件。"; }
        vm.Results.CollectionChanged += (_, _) => LayoutRows(); vm.Library.CollectionChanged += (_, _) => LayoutRows(); LayoutRows();
        System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Results).CollectionChanged += (_, _) => LayoutRows(); System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Library).CollectionChanged += (_, _) => LayoutRows();
        Loaded += (_, _) => { UpdateNavigation(); CategoryBox.SelectedIndex = FormatBox.SelectedIndex = 0; SortBox.SelectedItem = SortBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == vm.AlbumSort); };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Navigation)) UpdateNavigation(); if (e.PropertyName is nameof(vm.ShowSongs) or nameof(vm.ShowAlbums) or nameof(vm.Section) or nameof(vm.IsBrowsing)) LayoutRows(); if (e.PropertyName is nameof(vm.ArtistFilter) or nameof(vm.AlbumFilter)) { int filters = (string.IsNullOrWhiteSpace(vm.ArtistFilter) ? 0 : 1) + (string.IsNullOrWhiteSpace(vm.AlbumFilter) ? 0 : 1); FilterButton.Content = filters == 0 ? "筛选" : $"筛选 · {filters}"; if (filters > 0) FilterFields.Visibility = Visibility.Visible; } };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Vm.Run(() => string.IsNullOrWhiteSpace(SearchBox.Text) ? Vm.ClearSearchAsync() : Vm.BrowseAsync("搜索", SearchBox.Text)); };
        _coverTimer.Tick += (_, _) => { _coverTimer.Stop(); RefreshVisibleCovers(); };
        LibraryList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(BrowseScrolled)); TracksList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(BrowseScrolled));
        IsVisibleChanged += (_, _) => { if (IsVisible) { _coverTimer.Start(); } else { _coverTimer.Stop(); Vm.ReleaseBrowseImages(); } };
        Closing += (_, e) => { if (Host.Exiting) return; if (Vm.Settings.CloseToTray) { e.Cancel = true; Hide(); } else Host.Exit(); };
        var menu = new ContextMenu(); AddItem(menu, "立即播放", () => { if (TracksList.SelectedItem is Track t) Vm.Run(() => Vm.PlayBrowseTrackAsync(t)); }); AddItem(menu, "下一首播放", () => Vm.AddRange(SelectedTracks(), true)); AddItem(menu, "添加到队列", () => Vm.AddRange(SelectedTracks(), false)); TracksList.ContextMenu = menu;
        var qm = new ContextMenu(); AddItem(qm, "移除", () => Vm.Remove(QueueList.SelectedIndex)); AddItem(qm, "上移", () => Vm.Move(QueueList.SelectedIndex, QueueList.SelectedIndex - 1)); AddItem(qm, "下移", () => Vm.Move(QueueList.SelectedIndex, QueueList.SelectedIndex + 1)); QueueList.ContextMenu = qm;
        TracksList.SelectionMode = SelectionMode.Extended;
        AddItem(menu, "加入歌单…", () => Host.ShowTools("歌单", TracksList.SelectedItems.Cast<Track>().ToArray()));
        AddItem(menu, "下载到离线缓存", () => Vm.Run(() => Vm.DownloadSelectionAsync(TracksList.SelectedItems.Cast<Track>().ToArray())));
        AddItem(menu, "添加音乐文件夹…", OpenMusicFolder);
        var albumsMenu = new ContextMenu(); AddItem(albumsMenu, "打开", () => { if (LibraryList.SelectedItem is LibraryItem item) Vm.Run(() => Vm.BrowseAsync(item.Title, item: item)); }); AddItem(albumsMenu, "下载专辑", () => { if (LibraryList.SelectedItem is LibraryItem item) { Host.ShowTools("离线"); Vm.Run(() => Vm.DownloadAlbumAsync(item)); } }); LibraryList.ContextMenu = albumsMenu; LibraryList.PreviewMouseRightButtonDown += SelectRightClick;
        AddItem(qm, "保存到歌单…", () => Host.ShowTools("歌单", Vm.Queue.ToArray()));
        TracksList.PreviewMouseRightButtonDown += SelectRightClick; QueueList.PreviewMouseRightButtonDown += SelectRightClick;
        InitializeInteractions(menu, qm, albumsMenu);
    }
    private static void AddItem(ContextMenu menu, string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private static ListBoxItem? Row(DependencyObject? node) { while (node != null && node is not ListBoxItem) node = VisualTreeHelper.GetParent(node); return node as ListBoxItem; }
    private void SelectRightClick(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) is { } row && !row.IsSelected) { if (sender is ListBox list) list.SelectedItems.Clear(); row.IsSelected = true; } }
    public void FocusSearch() { SearchBox.Focus(); }
    public void FocusQueue() { QueueList.Focus(); }
    private void Navigate(object sender, RoutedEventArgs e) { _searchTimer.Stop(); Vm.Run(() => Vm.BrowseAsync(((Button)sender).Content.ToString()!)); }
    private void UpdateNavigation() { foreach (var button in NavigationStack.Children.OfType<Button>()) { bool selected = button.Content.ToString() == Vm.Navigation; button.SetResourceReference(Button.BackgroundProperty, selected ? "SelectedBrush" : "SidebarBrush"); button.SetResourceReference(Button.ForegroundProperty, selected ? "AccentBrush" : "TextBrush"); button.ApplyTemplate(); if (button.Template.FindName("Indicator", button) is FrameworkElement indicator) indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed; } }
    private void SearchChanged(object sender, TextChangedEventArgs e) { _searchTimer.Stop(); if (!_syncSearch) _searchTimer.Start(); }
    private void ClearSearchClick(object sender, RoutedEventArgs e) => SearchBox.Clear();
    private void CategoryChanged(object sender, SelectionChangedEventArgs e) { if (DataContext is PlayerViewModel vm && CategoryBox.SelectedItem is ComboBoxItem item) vm.SearchCategory = (string)item.Content; }
    private void FormatChanged(object sender, SelectionChangedEventArgs e) { if (DataContext is PlayerViewModel vm && FormatBox.SelectedItem is ComboBoxItem item) vm.FormatFilter = (string)item.Content; }
    private void SortChanged(object sender, SelectionChangedEventArgs e) { if (DataContext is PlayerViewModel vm && SortBox.SelectedItem is ComboBoxItem item) vm.AlbumSort = (string)item.Tag; }
    private void NeteaseClick(object sender, RoutedEventArgs e) => Host.ShowNetease();
    private void RemoteClick(object sender, RoutedEventArgs e) => Host.ShowRemote();
    private void LyricsClick(object sender, RoutedEventArgs e) => Host.ShowTools("歌词");
    private void FiltersClick(object sender, RoutedEventArgs e) => FilterFields.Visibility = FilterFields.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void LoadMoreClick(object sender, RoutedEventArgs e) => Vm.Run(Vm.LoadMoreAsync);
    private void OpenMusicFolder() { var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "添加音乐文件夹" }; if (dialog.ShowDialog(this) == true) Vm.Run(() => Vm.AddFolderAsync(dialog.FolderName)); }
    private void BrowseScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (!IsVisible) return; _coverTimer.Stop(); _coverTimer.Start();
        if (e.VerticalChange > 0 && e.ExtentHeight - e.ViewportHeight - e.VerticalOffset < (ReferenceEquals(sender, LibraryList) ? 360 : 5)) Vm.Run(Vm.LoadMoreAsync);
    }
    private void RefreshVisibleCovers()
    {
        if (!IsVisible) return; var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Vm.Library); int first = int.MaxValue, last = -1;
        foreach (var container in UiVerification.FindAll<ListBoxItem>(LibraryList)) { int i = LibraryList.ItemContainerGenerator.IndexFromContainer(container); var rect = container.TransformToAncestor(LibraryList).TransformBounds(new Rect(container.RenderSize)); if (i >= 0 && rect.Bottom >= 0 && rect.Top <= LibraryList.ActualHeight) { first = Math.Min(first, i); last = Math.Max(last, i); } }
        if (last >= 0) Vm.Run(() => Vm.LoadVisibleCoversAsync(first, last - first + 1));
    }
    private void PlaySelected(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) != null && TracksList.SelectedItem is Track t) Vm.Run(() => Vm.PlayBrowseTrackAsync(t)); }
    private void OpenLibrary(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) != null && LibraryList.SelectedItem is LibraryItem item) Vm.Run(() => Vm.BrowseAsync(item.Title, item: item)); }
    private void PlayQueue(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) != null) PlayQueueSelection(); }
    private void ClearQueue(object sender, RoutedEventArgs e) => Vm.ClearQueue();
    private void RandomAll(object sender, RoutedEventArgs e) => Vm.Run(() => Vm.RandomAsync(false));
    private void RandomStarred(object sender, RoutedEventArgs e) => Vm.Run(() => Vm.RandomAsync(true));
    private void SettingsClick(object sender, RoutedEventArgs e) => Host.ShowSettings();
    private void OpenFilesClick(object sender, RoutedEventArgs e) => OpenLocalFiles();
    public void OpenLocalFiles() { var dialog = new Microsoft.Win32.OpenFileDialog { Title = "打开音乐文件", Multiselect = true, Filter = "音乐文件|*.mp3;*.flac;*.ape;*.aac;*.m4a;*.alac;*.wav;*.ogg;*.opus;*.wma;*.dsf;*.dff;*.aif;*.aiff;*.wv;*.tta;*.tak;*.mpc;*.ac3;*.dts;*.cue|所有文件|*.*" }; if (dialog.ShowDialog(this) == true) Vm.Run(() => Vm.OpenLocalFilesAsync(dialog.FileNames)); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void VolumeWheel(object sender, MouseWheelEventArgs e) { Vm.Volume += Math.Sign(e.Delta) * 5; e.Handled = true; }
    private void MiniClick(object sender, RoutedEventArgs e) { Hide(); Host.ShowMini(); }
    private void InfoClick(object sender, RoutedEventArgs e) { var existing = Application.Current.Windows.Cast<Window>().OfType<InfoWindow>().FirstOrDefault(); if (existing != null) { existing.Activate(); return; } new InfoWindow(Vm) { Owner = this }.Show(); }
    private void PlayResults(object sender, RoutedEventArgs e) { var tracks = System.Windows.Data.CollectionViewSource.GetDefaultView(Vm.Results).Cast<Track>().ToList(); if (tracks.Count == 0) return; Vm.ReplaceQueue(tracks); Vm.Run(() => Vm.PlayAsync(0)); }
    private void QueueDown(object sender, MouseButtonEventArgs e) { _dragStart = e.GetPosition(QueueList); var row = Row(e.OriginalSource as DependencyObject); _dragIndex = row == null ? -1 : QueueList.ItemContainerGenerator.IndexFromContainer(row); }
    private void QueueMove(object sender, System.Windows.Input.MouseEventArgs e) { if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0) return; var p = e.GetPosition(QueueList); if (Math.Abs(p.Y - _dragStart.Y) > 8) { System.Windows.DragDrop.DoDragDrop(QueueList, new DataObject("QueueIndex", _dragIndex), DragDropEffects.Move); _dragIndex = -1; } }
    private void QueueDrop(object sender, System.Windows.DragEventArgs e) { if (!e.Data.GetDataPresent("QueueIndex")) return; var row = Row(e.OriginalSource as DependencyObject); int to = row == null ? Vm.Queue.Count - 1 : QueueList.ItemContainerGenerator.IndexFromContainer(row); Vm.Move((int)e.Data.GetData("QueueIndex"), to); }
}
