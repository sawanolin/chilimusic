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
    private Point _dragStart; private int _dragIndex = -1;
    public MainWindow(PlayerViewModel vm)
    {
        InitializeComponent(); WindowAppearance.Attach(this); DataContext = vm;
        AllowDrop = true; PreviewDragOver += (_, e) => { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } }; PreviewDrop += (_, e) => { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop); e.Handled = true; Vm.Run(() => Vm.OpenLocalFilesAsync(files)); } };
        void LayoutRows() { TrackRow.Height = new GridLength(vm.Results.Count == 0 ? 0 : vm.Library.Count == 0 ? 1 : 2, GridUnitType.Star); LibraryRow.Height = new GridLength(vm.Library.Count == 0 ? 0 : 1, GridUnitType.Star); }
        vm.Results.CollectionChanged += (_, _) => LayoutRows(); vm.Library.CollectionChanged += (_, _) => LayoutRows(); LayoutRows();
        Loaded += (_, _) => UpdateNavigation(); vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Navigation)) UpdateNavigation(); };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); if (!string.IsNullOrWhiteSpace(SearchBox.Text)) Vm.Run(() => Vm.BrowseAsync("搜索", SearchBox.Text)); };
        Closing += (_, e) => { if (Host.Exiting) return; if (Vm.Settings.CloseToTray) { e.Cancel = true; Hide(); } else Host.Exit(); };
        var menu = new ContextMenu(); AddItem(menu, "立即播放", () => { if (TracksList.SelectedItem is Track t) Vm.Run(() => Vm.PlayTrackAsync(t)); }); AddItem(menu, "下一首播放", () => { if (TracksList.SelectedItem is Track t) Vm.Add(t, true); }); AddItem(menu, "添加到队列", () => { if (TracksList.SelectedItem is Track t) Vm.Add(t, false); }); TracksList.ContextMenu = menu;
        var qm = new ContextMenu(); AddItem(qm, "移除", () => Vm.Remove(QueueList.SelectedIndex)); AddItem(qm, "上移", () => Vm.Move(QueueList.SelectedIndex, QueueList.SelectedIndex - 1)); AddItem(qm, "下移", () => Vm.Move(QueueList.SelectedIndex, QueueList.SelectedIndex + 1)); QueueList.ContextMenu = qm;
        TracksList.PreviewMouseRightButtonDown += SelectRightClick; QueueList.PreviewMouseRightButtonDown += SelectRightClick;
    }
    private static void AddItem(ContextMenu menu, string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private static ListBoxItem? Row(DependencyObject? node) { while (node != null && node is not ListBoxItem) node = VisualTreeHelper.GetParent(node); return node as ListBoxItem; }
    private void SelectRightClick(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) is { } row) row.IsSelected = true; }
    public void FocusSearch() { SearchBox.Focus(); }
    public void FocusQueue() { QueueList.Focus(); }
    private void Navigate(object sender, RoutedEventArgs e) { _searchTimer.Stop(); Vm.Run(() => Vm.BrowseAsync(((Button)sender).Content.ToString()!)); }
    private void UpdateNavigation() { foreach (var button in NavigationStack.Children.OfType<Button>()) { bool selected = button.Content.ToString() == Vm.Navigation; button.SetResourceReference(Button.BackgroundProperty, selected ? "SelectedBrush" : "SidebarBrush"); button.SetResourceReference(Button.ForegroundProperty, selected ? "AccentBrush" : "TextBrush"); button.ApplyTemplate(); if (button.Template.FindName("Indicator", button) is FrameworkElement indicator) indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed; } }
    private void SearchChanged(object sender, TextChangedEventArgs e) { _searchTimer.Stop(); _searchTimer.Start(); }
    private void PlaySelected(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) != null && TracksList.SelectedItem is Track t) Vm.Run(() => Vm.PlayTrackAsync(t)); }
    private void OpenLibrary(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) != null && LibraryList.SelectedItem is LibraryItem item) Vm.Run(() => Vm.BrowseAsync(item.Title, item: item)); }
    private void PlayQueue(object sender, MouseButtonEventArgs e) { if (Row(e.OriginalSource as DependencyObject) != null) Vm.Run(() => Vm.PlayAsync(QueueList.SelectedIndex)); }
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
    private void PlayResults(object sender, RoutedEventArgs e) { if (Vm.Results.Count == 0) return; Vm.Stop(); Vm.Queue.Clear(); foreach (var t in Vm.Results) Vm.Queue.Add(t); Vm.Run(() => Vm.PlayAsync(0)); }
    private void QueueDown(object sender, MouseButtonEventArgs e) { _dragStart = e.GetPosition(QueueList); var row = Row(e.OriginalSource as DependencyObject); _dragIndex = row == null ? -1 : QueueList.ItemContainerGenerator.IndexFromContainer(row); }
    private void QueueMove(object sender, System.Windows.Input.MouseEventArgs e) { if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0) return; var p = e.GetPosition(QueueList); if (Math.Abs(p.Y - _dragStart.Y) > 8) { System.Windows.DragDrop.DoDragDrop(QueueList, new DataObject("QueueIndex", _dragIndex), DragDropEffects.Move); _dragIndex = -1; } }
    private void QueueDrop(object sender, System.Windows.DragEventArgs e) { if (!e.Data.GetDataPresent("QueueIndex")) return; var row = Row(e.OriginalSource as DependencyObject); int to = row == null ? Vm.Queue.Count - 1 : QueueList.ItemContainerGenerator.IndexFromContainer(row); Vm.Move((int)e.Data.GetData("QueueIndex"), to); }
}
