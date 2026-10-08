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
    private Track[] SelectedTracks() => CollectionViewSource.GetDefaultView(Vm.Results).Cast<Track>().Where(TracksList.SelectedItems.Contains).ToArray();
    private void BackClick(object sender, RoutedEventArgs e) { _searchTimer.Stop(); Vm.Run(Vm.GoBackAsync); }
    private void QueueFollowClick(object sender, RoutedEventArgs e)
    {
        if (Vm.Index < 0 || Vm.Index >= QueueList.Items.Count) return;
        QueueList.SelectedIndex = Vm.Index; QueueList.ScrollIntoView(Vm.Current); QueueList.Focus();
    }
    private void PlayQueueSelection()
    {
        if (QueueList.SelectedIndex < 0) return;
        Vm.Run(() => QueueList.SelectedIndex == Vm.Index ? Vm.ResumePlaybackAsync() : Vm.PlayAsync(QueueList.SelectedIndex));
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
            int index = QueueList.SelectedIndex;
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
        void RefreshActions() { PlayListButton.IsEnabled = TracksList.Items.Count > 0; QueueFollowButton.IsEnabled = Vm.Current != null; ClearQueueButton.IsEnabled = Vm.Queue.Count > 0; }
        CollectionViewSource.GetDefaultView(Vm.Results).CollectionChanged += (_, _) => RefreshActions();
        Vm.Queue.CollectionChanged += (_, _) => RefreshActions(); Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Vm.Current)) RefreshActions(); }; RefreshActions();
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
            else if (QueueList.IsKeyboardFocusWithin && e.Key == Key.Delete) { int index = QueueList.SelectedIndex; Vm.Remove(index); if (QueueList.Items.Count > 0) QueueList.SelectedIndex = Math.Min(index, QueueList.Items.Count - 1); e.Handled = true; }
            else if (QueueList.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.Alt && e.Key is Key.Up or Key.Down) { int from = QueueList.SelectedIndex; int to = from + (e.Key == Key.Up ? -1 : 1); Vm.Move(from, to); if (to >= 0 && to < QueueList.Items.Count) QueueList.SelectedIndex = to; e.Handled = true; }
        };
    }
}
