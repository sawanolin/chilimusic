using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace ChiliMusic;

public partial class MainWindow
{
    private bool _syncSearch;
    private int QueueSourceIndex(int visibleIndex)
    {
        if (visibleIndex < 0) return -1;
        var filter = CollectionViewSource.GetDefaultView(Vm.Queue).Filter;
        int visible = 0;
        for (int i = 0; i < Vm.Queue.Count; i++)
            if (filter == null || filter(Vm.Queue[i])) { if (visible++ == visibleIndex) return i; }
        return -1;
    }
    private int SelectedQueueIndex => QueueSourceIndex(QueueList.SelectedIndex);
    private int[] SelectedQueueIndices() => QueueList.SelectedItems.Count == 1 ? [SelectedQueueIndex] : Enumerable.Range(0, QueueList.Items.Count)
        .Where(i => QueueList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem row ? row.IsSelected : QueueList.SelectedItems.Contains(QueueList.Items[i]))
        .Select(QueueSourceIndex).Where(i => i >= 0).ToArray();
    private Track[] SelectedTracks() => CollectionViewSource.GetDefaultView(Vm.Results).Cast<Track>().Where(TracksList.SelectedItems.Contains).ToArray();
    private void BackClick(object sender, RoutedEventArgs e) { _searchTimer.Stop(); Vm.Run(Vm.GoBackAsync); }
    private void ForwardClick(object sender, RoutedEventArgs e) { _searchTimer.Stop(); Vm.Run(Vm.GoForwardAsync); }
    internal void RemoveSelectedQueueRows()
    {
        int visible = QueueList.SelectedIndex; var indices = SelectedQueueIndices(); if (indices.Length == 0) return;
        Vm.RemoveRange(indices);
        if (QueueList.Items.Count > 0) QueueList.SelectedIndex = Math.Clamp(visible, 0, QueueList.Items.Count - 1);
    }
    private void QueueFollowClick(object sender, RoutedEventArgs e)
    {
        if (Vm.Index < 0 || Vm.Index >= Vm.Queue.Count) return;
        _queueSearch?.Clear(); QueueList.SelectedIndex = Vm.Index; QueueList.ScrollIntoView(Vm.Current); QueueList.Focus();
    }
    private void PlayQueueSelection()
    {
        int index = SelectedQueueIndex; if (index < 0) return;
        Vm.Run(() => index == Vm.Index ? Vm.ResumePlaybackAsync() : Vm.PlayAsync(index));
    }
    private void InitializeInteractions(ContextMenu songs, ContextMenu queue, ContextMenu albums)
    {
        AddItem(albums, "播放", () => { if (LibraryList.SelectedItem is LibraryItem item) Vm.Run(() => Vm.PlayLibraryAsync(item)); });
        AddItem(albums, "添加到队列", () => { if (LibraryList.SelectedItem is LibraryItem item) Vm.Run(() => Vm.PlayLibraryAsync(item, true)); });
        songs.Opened += (_, _) =>
        {
            var selected = SelectedTracks();
            foreach (var item in songs.Items.OfType<MenuItem>()) item.IsEnabled = item.Header.ToString() == "添加音乐文件夹…" || selected.Length > 0;
            ((MenuItem)songs.Items[4]).IsEnabled = selected.Length > 0 && selected.All(t => !t.IsLocal && !t.IsNetease);
        };
        queue.Opened += (_, _) =>
        {
            int index = SelectedQueueIndex;
            ((MenuItem)queue.Items[0]).IsEnabled = index >= 0;
            ((MenuItem)queue.Items[1]).IsEnabled = index > 0;
            ((MenuItem)queue.Items[2]).IsEnabled = index >= 0 && index < Vm.Queue.Count - 1;
            ((MenuItem)queue.Items[3]).IsEnabled = Vm.Queue.Count > 0;
        };
        albums.Opened += (_, _) =>
        {
            var selected = LibraryList.SelectedItem as LibraryItem;
            foreach (var item in albums.Items.OfType<MenuItem>()) item.IsEnabled = selected != null;
            ((MenuItem)albums.Items[1]).IsEnabled = selected?.Kind == "album" && !selected.Id.StartsWith("ncm:", StringComparison.Ordinal);
            foreach (var item in albums.Items.OfType<MenuItem>().Skip(2)) item.IsEnabled = selected?.Kind is "album" or "playlist" or "local-album" or "local-artist" or "local-playlist";
        };
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Vm.BrowseQuery))
            {
                _searchTimer.Stop(); _syncSearch = true; SearchBox.Text = Vm.Section == "搜索" ? Vm.BrowseQuery : ""; _syncSearch = false;
            }
            if (e.PropertyName == nameof(Vm.Current) && Vm.Current != null && !QueueList.IsKeyboardFocusWithin && !QueueList.IsMouseOver) QueueList.ScrollIntoView(Vm.Current);
        };
        void RefreshActions()
        {
            PlayListButton.IsEnabled = TracksList.Items.Count > 0;
            bool same = Vm.BrowseQueueMatches;
            PlayListButton.Content = same ? Vm.IsProgressMoving ? "Ⅱ  暂停" : "▶  继续播放" : "▶  播放全部";
            PlayListButton.ToolTip = same ? "控制当前列表，保留歌曲和播放位置" : "从当前列表开始播放";
            QueueFollowButton.IsEnabled = Vm.Current != null; ClearQueueButton.IsEnabled = Vm.Queue.Count > 0;
        }
        CollectionViewSource.GetDefaultView(Vm.Results).CollectionChanged += (_, _) => RefreshActions();
        Vm.Queue.CollectionChanged += (_, _) => RefreshActions(); Vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(Vm.Current) or nameof(Vm.IsProgressMoving) or nameof(Vm.HasMore)) RefreshActions(); }; RefreshActions();
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) { BackClick(this, new()); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.XButton2) { ForwardClick(this, new()); e.Handled = true; }
        };
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.Left or Key.Right)) return;
            if (key == Key.Left) BackClick(this, new()); else ForwardClick(this, new()); e.Handled = true;
        };
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { _searchTimer.Stop(); string query = SearchBox.Text.Trim(); Vm.Run(() => query.Length == 0 ? Vm.ClearSearchAsync() : Vm.BrowseAsync("搜索", query)); e.Handled = true; }
            else if (e.Key == Key.Escape) { SearchBox.Clear(); e.Handled = true; }
        };
        KeyDown += (_, e) =>
        {
            if (e.Handled) return;
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { FocusSearch(); SearchBox.SelectAll(); e.Handled = true; return; }
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { OpenLocalFiles(); e.Handled = true; return; }
            if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { BackClick(this, new()); e.Handled = true; return; }
            if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox or Slider or Button) return;
            if (e.Key == Key.Enter)
            {
                if (TracksList.IsKeyboardFocusWithin && TracksList.SelectedItem is Track track) Vm.Run(() => Vm.PlayBrowseTrackAsync(track));
                else if (QueueList.IsKeyboardFocusWithin) PlayQueueSelection();
                else if (LibraryList.IsKeyboardFocusWithin && LibraryList.SelectedItem is LibraryItem item) Vm.Run(() => Vm.BrowseAsync(item.Title, item: item));
                else return; e.Handled = true;
            }
            else if (e.Key == Key.Space) { Vm.Run(Vm.ToggleAsync); e.Handled = true; }
            else if (QueueList.IsKeyboardFocusWithin && e.Key == Key.Delete) { RemoveSelectedQueueRows(); e.Handled = true; }
            else if (QueueList.IsKeyboardFocusWithin && e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Vm.Run(Vm.UndoQueueAsync); e.Handled = true; }
            else if (QueueList.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.Alt && e.Key is Key.Up or Key.Down) { int from = SelectedQueueIndex; int to = from + (e.Key == Key.Up ? -1 : 1); Vm.Move(from, to); _queueSearch?.Clear(); if (to >= 0 && to < QueueList.Items.Count) QueueList.SelectedIndex = to; e.Handled = true; }
        };
    }
}
